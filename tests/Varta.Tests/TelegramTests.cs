using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Varta.Core;
using Xunit;

namespace Varta.Tests
{
    sealed class FakeHttp : IHttp
    {
        public Func<string, HttpResult> OnGet = u => new HttpResult { Status = 404, Body = "" };
        public readonly List<string> Requests = new List<string>();
        public HttpResult Get(string url, IDictionary<string, string> headers, int timeoutMs) { Requests.Add(url); return OnGet(url); }
        public HttpResult PostForm(string url, IDictionary<string, string> form, int timeoutMs) { return new HttpResult { Status = 200, Body = "{}" }; }
    }

    public class TelegramTests
    {
        static string Fixture(string name)
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        }

        static DateTime Utc(string s) { return DateTime.Parse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal); }

        [Fact]
        public void Parser_reads_real_kharkivlife_page()
        {
            List<TgMessage> m = TelegramPageParser.Parse(Fixture("kharkivlife_sample.html"), "kharkivlife");
            Assert.Equal(18, m.Count);
            Assert.Equal(156494, m.First().Id);
            Assert.Equal(156511, m.Last().Id);
            TgMessage alarm = m.Single(x => x.Id == 156495);
            Assert.Equal(Utc("2026-10-15T08:32:34Z"), alarm.TimeUtc);
            Assert.Contains("ОГОЛОШЕНА ПОВІТРЯНА ТРИВОГА", alarm.Text);
        }

        [Fact]
        public void Classifier_finds_city_alarm_and_clear_in_kharkivlife()
        {
            var s = new AppSettings();
            List<TgEvent> ev = TelegramPageParser.Parse(Fixture("kharkivlife_sample.html"), "kharkivlife")
                .Select(x => TelegramClassifier.Classify(x, s)).Where(x => x != null).ToList();
            Assert.Equal(new long[] { 156495, 156500, 156501, 156508, 156510 }, ev.Select(x => x.Id).ToArray());
            Assert.Equal(new[] { TgKind.AlarmStart, TgKind.AlarmClear, TgKind.AlarmStart, TgKind.AlarmClear, TgKind.AlarmStart }, ev.Select(x => x.Kind).ToArray());
            Assert.All(ev, e => Assert.Equal(TgScope.City, e.Scope));
        }

        [Fact]
        public void State_follows_events_in_time_order()
        {
            var s = new AppSettings();
            List<TgEvent> ev = TelegramPageParser.Parse(Fixture("kharkivlife_sample.html"), "kharkivlife")
                .Select(x => TelegramClassifier.Classify(x, s)).Where(x => x != null).ToList();

            DateTime t1 = Utc("2026-10-15T09:35:00Z");
            TelegramState clear = TelegramStateMachine.Evaluate(ev.Where(e => e.TimeUtc <= t1), s, t1);
            Assert.Equal(AlertLevel.Clear, clear.Level);

            DateTime t2 = Utc("2026-10-15T09:40:00Z");
            TelegramState red = TelegramStateMachine.Evaluate(ev, s, t2);
            Assert.Equal(AlertLevel.Red, red.Level);
            Assert.Equal(Utc("2026-10-15T09:36:54Z"), red.SinceUtc);
            Assert.Contains("ПОВІТРЯНА ТРИВОГА", red.Detail);
        }

        [Fact]
        public void Old_alarm_without_clear_expires()
        {
            var s = new AppSettings { TelegramEventMaxAgeHours = 12 };
            var ev = new List<TgEvent> { new TgEvent { Id = 1, TimeUtc = Utc("2026-10-15T00:00:00Z"), Scope = TgScope.City, Kind = TgKind.AlarmStart, Text = "тривога" } };
            Assert.Equal(AlertLevel.Red, TelegramStateMachine.Evaluate(ev, s, Utc("2026-10-15T11:00:00Z")).Level);
            Assert.Equal(AlertLevel.Clear, TelegramStateMachine.Evaluate(ev, s, Utc("2026-10-15T13:00:00Z")).Level);
        }

        [Fact]
        public void Air_alert_channel_city_raion_and_threat_messages()
        {
            var s = new AppSettings();
            List<TgEvent> ev = TelegramPageParser.Parse(Fixture("air_alert_ua_sample.html"), "air_alert_ua")
                .Select(x => TelegramClassifier.Classify(x, s)).Where(x => x != null).ToList();
            Assert.Contains(ev, e => e.Scope == TgScope.Raion && e.Kind == TgKind.AlarmStart);
            Assert.Contains(ev, e => e.Scope == TgScope.City && e.Kind == TgKind.AlarmStart);
            Assert.Contains(ev, e => e.Scope == TgScope.City && e.Kind == TgKind.ThreatStart);
            Assert.DoesNotContain(ev, e => e.Text.Contains("Запоріж"));

            TelegramState st = TelegramStateMachine.Evaluate(ev, s, Utc("2026-09-07T06:10:00Z"));
            Assert.Equal(AlertLevel.Red, st.Level);
            Assert.Equal(Utc("2026-09-07T05:49:00Z").Date, st.SinceUtc.Value.Date);
        }

        [Fact]
        public void Raion_alarm_alone_is_yellow_and_can_be_disabled()
        {
            var s = new AppSettings();
            var ev = new List<TgEvent> { new TgEvent { Id = 1, TimeUtc = Utc("2026-10-15T09:00:00Z"), Scope = TgScope.Raion, Kind = TgKind.AlarmStart, Text = "Повітряна тривога в Харківський район" } };
            DateTime now = Utc("2026-10-15T09:05:00Z");
            Assert.Equal(AlertLevel.Yellow, TelegramStateMachine.Evaluate(ev, s, now).Level);
            s.RaionAlertIsYellow = false;
            Assert.Equal(AlertLevel.Clear, TelegramStateMachine.Evaluate(ev, s, now).Level);
        }

        [Fact]
        public void Threat_message_gives_yellow_until_clear()
        {
            var s = new AppSettings();
            var ev = new List<TgEvent>
            {
                new TgEvent { Id = 1, TimeUtc = Utc("2026-10-15T09:00:00Z"), Scope = TgScope.City, Kind = TgKind.ThreatStart, Text = "Загроза застосування КАБ" }
            };
            DateTime now = Utc("2026-10-15T09:05:00Z");
            Assert.Equal(AlertLevel.Yellow, TelegramStateMachine.Evaluate(ev, s, now).Level);
            ev.Add(new TgEvent { Id = 2, TimeUtc = Utc("2026-10-15T09:10:00Z"), Scope = TgScope.City, Kind = TgKind.ThreatClear, Text = "Відбій загрози застосування КАБ" });
            Assert.Equal(AlertLevel.Clear, TelegramStateMachine.Evaluate(ev, s, Utc("2026-10-15T09:11:00Z")).Level);
        }

        [Fact]
        public void Oblast_wide_alarm_counts_as_red_for_city()
        {
            var s = new AppSettings();
            var m = new TgMessage { Id = 5, TimeUtc = Utc("2026-10-15T09:00:00Z"), Text = "🔴 00:05 Повітряна тривога в Харківська область Слідкуйте за подальшими повідомленнями. #Харківська_область" };
            TgEvent e = TelegramClassifier.Classify(m, s);
            Assert.Equal(TgScope.Oblast, e.Scope);
            Assert.Equal(AlertLevel.Red, TelegramStateMachine.Evaluate(new[] { e }, s, Utc("2026-10-15T09:01:00Z")).Level);
            s.CountOblastAlert = false;
            Assert.Equal(AlertLevel.Clear, TelegramStateMachine.Evaluate(new[] { e }, s, Utc("2026-10-15T09:01:00Z")).Level);
        }

        [Fact]
        public void Messages_about_other_places_are_ignored()
        {
            var s = new AppSettings();
            foreach (string t in new[]
            {
                "🔴 08:54 Повітряна тривога в • Кальміуський район • Краматорський район",
                "🔴 Загроза пусків «Бандероль»",
                "БПЛА на Лісне",
                "Повітряна тривога в Сумський район #Сумський_район"
            })
                Assert.Null(TelegramClassifier.Classify(new TgMessage { Id = 1, TimeUtc = DateTime.UtcNow, Text = t }, s));
        }

        static FakeHttp ServeKharkivlife(DateTime serverDate)
        {
            string page = Fixture("kharkivlife_sample.html");
            var http = new FakeHttp();
            http.OnGet = u =>
            {
                if (u.Contains("?before=")) return new HttpResult { Status = 200, Body = "<html></html>", ServerDateUtc = serverDate };
                return new HttpResult { Status = 200, Body = page, ServerDateUtc = serverDate, ElapsedMs = 120 };
            };
            return http;
        }

        [Fact]
        public void Source_reads_state_and_backfills_only_once()
        {
            DateTime now = Utc("2026-10-15T09:40:00Z");
            FakeHttp http = ServeKharkivlife(now);
            var src = new TelegramSource(() => new AppSettings(), http, () => now);

            SourceReading r = src.Fetch();
            Assert.True(r.Ok);
            Assert.Equal(AlertLevel.Red, r.Level);
            Assert.Equal(Utc("2026-10-15T09:36:54Z"), r.SinceUtc);
            Assert.Equal(2, http.Requests.Count);
            Assert.Contains("?before=156494", http.Requests[1]);

            src.Fetch();
            Assert.Equal(3, http.Requests.Count);

            List<ChannelStatus> st = src.GetChannelStatus();
            Assert.Single(st);
            Assert.True(st[0].Ok);
            Assert.Equal(5, st[0].Events);
        }

        [Fact]
        public void Source_reports_stale_channel_instead_of_green()
        {
            DateTime now = Utc("2026-10-20T09:40:00Z");
            var src = new TelegramSource(() => new AppSettings(), ServeKharkivlife(now), () => now);
            SourceReading r = src.Fetch();
            Assert.False(r.Ok);
            Assert.Equal(ErrorKind.Stale, r.ErrorKind);
        }

        [Fact]
        public void Source_reports_unparseable_page_and_http_errors()
        {
            DateTime now = Utc("2026-10-15T09:40:00Z");
            var http = new FakeHttp { OnGet = u => new HttpResult { Status = 200, Body = "<html>no messages</html>" } };
            SourceReading r = new TelegramSource(() => new AppSettings(), http, () => now).Fetch();
            Assert.False(r.Ok);
            Assert.Equal(ErrorKind.BadData, r.ErrorKind);

            http.OnGet = u => new HttpResult { Status = 0, Error = "dns", Body = "" };
            r = new TelegramSource(() => new AppSettings(), http, () => now).Fetch();
            Assert.False(r.Ok);
            Assert.Equal(ErrorKind.Network, r.ErrorKind);

            http.OnGet = u => new HttpResult { Status = 0, TimedOut = true, Error = "timeout", Body = "" };
            Assert.Equal(ErrorKind.Timeout, new TelegramSource(() => new AppSettings(), http, () => now).Fetch().ErrorKind);
        }

        [Fact]
        public void Source_with_one_dead_and_one_live_channel_still_works()
        {
            DateTime now = Utc("2026-10-15T09:40:00Z");
            string page = Fixture("kharkivlife_sample.html");
            var http = new FakeHttp();
            http.OnGet = u => u.Contains("/s/deadchannel") ? new HttpResult { Status = 404, Body = "" }
                            : u.Contains("?before=") ? new HttpResult { Status = 200, Body = "<html></html>", ServerDateUtc = now }
                            : new HttpResult { Status = 200, Body = page, ServerDateUtc = now };
            var s = new AppSettings { TelegramChannels = "deadchannel kharkivlife" };
            var src = new TelegramSource(() => s, http, () => now);
            SourceReading r = src.Fetch();
            Assert.True(r.Ok);
            Assert.Equal(AlertLevel.Red, r.Level);
            Assert.Equal(2, src.GetChannelStatus().Count);
            Assert.False(src.GetChannelStatus().Single(c => c.Channel == "deadchannel").Ok);
        }
    }
}
