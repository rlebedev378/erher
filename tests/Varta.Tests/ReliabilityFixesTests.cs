using System;
using System.IO;
using System.Linq;
using Varta.Core;
using Xunit;

namespace Varta.Tests
{
    /// <summary>Виправлення за підсумками перевірки 1.0.3: хибний відбій із Telegram, нічний графік, поріг застарілих даних.</summary>
    public sealed class ReliabilityFixesTests
    {
        static DateTime Utc(string s) { return DateTime.Parse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal); }

        [Fact]
        public void Interrupted_telegram_backfill_is_a_failure_not_an_all_clear_and_is_retried()
        {
            DateTime now = Utc("2026-10-15T09:40:00Z");
            string page = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "kharkivlife_sample.html"));
            bool limited = true;
            var http = new FakeHttp
            {
                OnGet = u => !u.Contains("?before=") ? new HttpResult { Status = 200, Body = page, ServerDateUtc = now }
                           : limited ? new HttpResult { Status = 429, Body = "" }
                           : new HttpResult { Status = 200, Body = "<html></html>", ServerDateUtc = now }
            };
            var src = new TelegramSource(() => new AppSettings(), http, () => now);

            SourceReading r1 = src.Fetch();
            Assert.False(r1.Ok);
            Assert.Equal(ErrorKind.RateLimited, r1.ErrorKind);
            Assert.False(src.GetChannelStatus().Single().Ok);

            limited = false;
            SourceReading r2 = src.Fetch();
            Assert.True(r2.Ok);
            Assert.Equal(AlertLevel.Red, r2.Level);
            Assert.Equal(2, http.Requests.Count(u => u.Contains("?before=")));
        }

        [Fact]
        public void Telegram_history_covers_the_whole_lifetime_of_an_alarm_without_clear()
        {
            DateTime now = Utc("2026-10-15T09:40:00Z");
            string page = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "kharkivlife_sample.html"));
            var http = new FakeHttp
            {
                OnGet = u => u.Contains("?before=") ? new HttpResult { Status = 200, Body = "<html></html>", ServerDateUtc = now }
                                                    : new HttpResult { Status = 200, Body = page, ServerDateUtc = now }
            };
            // Сторінка охоплює 08:28–09:37. Історії задано 1 годину, але тривога без відбою діє 12 годин — старіші сторінки все одно потрібні.
            var s = new AppSettings { TelegramBackfillHours = 1, TelegramEventMaxAgeHours = 12 };
            Assert.True(new TelegramSource(() => s, http, () => now).Fetch().Ok);
            Assert.Contains(http.Requests, u => u.Contains("?before="));
        }

        [Fact]
        public void Overnight_work_hours_use_the_day_the_shift_started()
        {
            var s = new AppSettings { VoiceOnlyInWorkHours = true, WorkStart = "22:00", WorkEnd = "06:00", WorkDays = "Mon,Tue,Wed,Thu,Fri" };
            Assert.True(Schedule.VoiceAllowed(s, new DateTime(2026, 10, 16, 23, 0, 0)));   // пт, початок зміни
            Assert.True(Schedule.VoiceAllowed(s, new DateTime(2026, 10, 17, 2, 0, 0)));    // сб 02:00 — ще зміна п'ятниці
            Assert.False(Schedule.VoiceAllowed(s, new DateTime(2026, 10, 17, 23, 0, 0)));  // сб — вихідний
            Assert.False(Schedule.VoiceAllowed(s, new DateTime(2026, 10, 12, 2, 0, 0)));   // пн 02:00 — зміна неділі
            Assert.False(Schedule.VoiceAllowed(s, new DateTime(2026, 10, 13, 12, 0, 0)));  // вт удень — поза зміною
        }

        [Fact]
        public void Data_is_not_stale_between_two_slow_polls()
        {
            var s = new AppSettings { PollIntervalSeconds = 300, StaleAfterSeconds = 120 };
            s.Normalize();
            Assert.Equal(615, AlertEngine.EffectiveStaleSeconds(s));
            Assert.Equal(120, AlertEngine.EffectiveStaleSeconds(new AppSettings()));

            DateTime clock = Utc("2026-10-15T09:00:00Z");
            var engine = new AlertEngine(s, () => clock);
            engine.OnReading(new SourceReading { Ok = true, Level = AlertLevel.Red, SourceName = "test", FetchedUtc = clock });
            clock = clock.AddSeconds(200);
            Assert.Equal(AlertLevel.Red, engine.EffectiveLevel());
            clock = clock.AddSeconds(500);
            Assert.Equal(AlertLevel.Unknown, engine.EffectiveLevel());
        }
    }
}
