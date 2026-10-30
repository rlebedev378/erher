using System;
using System.Collections.Generic;
using System.IO;
using Varta.Core;
using Xunit;

namespace Varta.Tests
{
    public class CoreTests
    {
        [Fact]
        public void MiniJson_parses_nested_values_and_unicode()
        {
            var o = MiniJson.Obj(MiniJson.Parse("{\"a\":1,\"b\":[true,null,\"\\u0416\\n\"],\"c\":{\"d\":1.5},\"e\":\"Харків\"}"));
            Assert.Equal(1L, o["a"]);
            var b = MiniJson.Arr(o["b"]);
            Assert.Equal(true, b[0]);
            Assert.Null(b[1]);
            Assert.Equal("Ж\n", b[2]);
            Assert.Equal(1.5, MiniJson.Obj(o["c"])["d"]);
            Assert.Equal("Харків", o["e"]);
        }

        [Fact]
        public void MiniJson_roundtrip_keeps_cyrillic_and_escapes()
        {
            var d = new Dictionary<string, object> { { "t", "Привіт \"світе\"\n\\" }, { "n", 5 }, { "l", new List<object> { 1, "x" } } };
            string json = MiniJson.Serialize(d, true);
            var back = MiniJson.Obj(MiniJson.Parse(json));
            Assert.Equal("Привіт \"світе\"\n\\", back["t"]);
            Assert.Equal(5L, back["n"]);
        }

        [Fact]
        public void MiniJson_rejects_garbage()
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\":}"));
            Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\":1} x"));
        }

        [Fact]
        public void Settings_roundtrip_preserves_values()
        {
            var s = new AppSettings();
            s.OrganizationName = "Автосалон «Тест»";
            s.PollIntervalSeconds = 20;
            RoutePolicy.Set(s, AlertEventKind.AllClear, Channel.Tray);
            AppSettings back = SettingsJson.FromJson(SettingsJson.ToJson(s), null);
            Assert.Equal("Автосалон «Тест»", back.OrganizationName);
            Assert.Equal(20, back.PollIntervalSeconds);
            Assert.Equal(Channel.Tray, RoutePolicy.Get(back, AlertEventKind.AllClear));
        }

        [Fact]
        public void Settings_partial_json_keeps_defaults_and_clamps()
        {
            var w = new List<string>();
            AppSettings s = SettingsJson.FromJson("{\"PollIntervalSeconds\":3,\"VoiceEnabled\":false,\"Routes\":{\"AllClear\":\"tray\",\"ConnectionLost\":\"voice,tray\"}}", w);
            Assert.Equal(10, s.PollIntervalSeconds);
            Assert.False(s.VoiceEnabled);
            Assert.Equal(Channel.Tray, RoutePolicy.Get(s, AlertEventKind.AllClear));
            Assert.Equal(Channel.Tray, RoutePolicy.Get(s, AlertEventKind.ConnectionLost));
            Assert.Equal(RoutePolicy.DefaultFor(AlertEventKind.RedStarted), RoutePolicy.Get(s, AlertEventKind.RedStarted));
            Assert.Empty(w);
        }

        [Fact]
        public void Settings_wrong_type_is_reported_and_default_kept()
        {
            var w = new List<string>();
            AppSettings s = SettingsJson.FromJson("{\"PollIntervalSeconds\":\"abc\",\"VoiceEnabled\":\"yes\"}", w);
            Assert.Equal(15, s.PollIntervalSeconds);
            Assert.True(s.VoiceEnabled);
            Assert.Equal(2, w.Count);
        }

        [Fact]
        public void Default_routes_have_no_voice_for_technical_events()
        {
            foreach (AlertEventKind k in RoutePolicy.Order)
            {
                Channel c = RoutePolicy.DefaultFor(k);
                if (!RoutePolicy.VoiceCapable(k)) Assert.Equal(Channel.None, c & Channel.Voice);
            }
            Assert.Equal(Channel.None, RoutePolicy.DefaultFor(AlertEventKind.ConnectionLost) & Channel.Voice);
            Assert.NotEqual(Channel.None, RoutePolicy.DefaultFor(AlertEventKind.RedStarted) & Channel.Voice);
        }

        [Fact]
        public void SplitChannels_handles_links_and_duplicates()
        {
            var l = TextUtil.SplitChannels("@kharkivlife, https://t.me/s/air_alert_ua  t.me/foo_bar?x=1 kharkivlife ab");
            Assert.Equal(new[] { "kharkivlife", "air_alert_ua", "foo_bar" }, l.ToArray());
        }

        [Fact]
        public void ContainsPhrase_respects_word_boundaries()
        {
            string n = TextUtil.Norm("Повітряна тривога в Харківський район #Харківський_район");
            Assert.False(TextUtil.ContainsPhrase(n, "Харків"));
            Assert.True(TextUtil.ContainsPhrase(n, "Харківський район"));
            Assert.True(TextUtil.ContainsPhrase(TextUtil.Norm("#м_Харків_та_Харківська_територіальна_громада"), "Харків"));
            Assert.True(TextUtil.ContainsPhrase(TextUtil.Norm("М. ХАРКІВ - ВІДБІЙ"), "м. Харків"));
        }

        [Fact]
        public void FormatDuration_is_human_readable()
        {
            Assert.Equal("менше хвилини", TextUtil.FormatDuration(TimeSpan.FromSeconds(30)));
            Assert.Equal("5 хв", TextUtil.FormatDuration(TimeSpan.FromMinutes(5)));
            Assert.Equal("1 год 05 хв", TextUtil.FormatDuration(TimeSpan.FromMinutes(65)));
            Assert.Equal("2 д 3 год", TextUtil.FormatDuration(TimeSpan.FromHours(51)));
        }

        [Fact]
        public void ShortDetail_strips_channel_boilerplate()
        {
            Assert.Equal("М. ХАРКІВ - ОГОЛОШЕНА ПОВІТРЯНА ТРИВОГА",
                TextUtil.ShortDetail("🚨 М. ХАРКІВ - ОГОЛОШЕНА ПОВІТРЯНА ТРИВОГА Надіслати новину @Kharkiv_Lifebot 👉 ПІДПИСАТИСЯ"));
        }

        [Fact]
        public void Schedule_work_hours_including_overnight()
        {
            var s = new AppSettings { VoiceOnlyInWorkHours = true, WorkStart = "08:30", WorkEnd = "20:00", WorkDays = "Mon,Tue,Wed,Thu,Fri" };
            var mon10 = new DateTime(2026, 10, 12, 10, 0, 0);
            Assert.Equal(DayOfWeek.Monday, mon10.DayOfWeek);
            Assert.True(Schedule.VoiceAllowed(s, mon10));
            Assert.False(Schedule.VoiceAllowed(s, new DateTime(2026, 10, 12, 21, 0, 0)));
            Assert.False(Schedule.VoiceAllowed(s, new DateTime(2026, 10, 17, 10, 0, 0))); // Saturday
            s.WorkStart = "22:00"; s.WorkEnd = "06:00"; s.WorkDays = "Mon,Tue,Wed,Thu,Fri,Sat,Sun";
            Assert.True(Schedule.VoiceAllowed(s, new DateTime(2026, 10, 12, 23, 30, 0)));
            Assert.True(Schedule.VoiceAllowed(s, new DateTime(2026, 10, 12, 5, 0, 0)));
            Assert.False(Schedule.VoiceAllowed(s, new DateTime(2026, 10, 12, 12, 0, 0)));
            s.VoiceEnabled = false;
            Assert.False(Schedule.VoiceAllowed(s, new DateTime(2026, 10, 12, 23, 30, 0)));
        }

        [Fact]
        public void Daily_report_due_once_per_day()
        {
            var s = new AppSettings { AdminEnabled = true, AdminDailyReport = true, AdminDailyReportTime = "09:00" };
            Assert.False(Schedule.DailyReportDue(s, new DateTime(2026, 10, 15, 8, 59, 0), DateTime.MinValue));
            Assert.True(Schedule.DailyReportDue(s, new DateTime(2026, 10, 15, 9, 1, 0), DateTime.MinValue));
            Assert.False(Schedule.DailyReportDue(s, new DateTime(2026, 10, 15, 9, 1, 0), new DateTime(2026, 10, 15)));
        }

        [Fact]
        public void Messages_fill_placeholders_and_mark_tests()
        {
            var s = new AppSettings { TextRedBody = "{city}: {time} {detail}" };
            var e = new EngineEvent { Kind = AlertEventKind.RedStarted, Detail = "БпЛА", IsTest = true };
            Notice n = Messages.Compose(e, s, new DateTime(2026, 10, 15, 9, 5, 0), "PC1");
            Assert.StartsWith("ТЕСТ: ", n.Title);
            Assert.Equal("Харків: 09:05 БпЛА", n.Body);
            Assert.Equal(AlertLevel.Red, n.Level);
            string adm = Messages.AdminText(n, "PC1");
            Assert.Contains("PC1", adm);
            Assert.StartsWith("🧪", adm);
        }
    }
}
