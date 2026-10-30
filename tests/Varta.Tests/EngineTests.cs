using System;
using System.Collections.Generic;
using System.Linq;
using Varta.Core;
using Xunit;

namespace Varta.Tests
{
    public class AlertsInUaTests
    {
        static readonly DateTime Now = new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

        static string Json(params string[] alerts) { return "{\"alerts\":[" + string.Join(",", alerts) + "],\"meta\":{}}"; }

        static string A(string title, string locType, string type = "air_raid", string level = "red", string extra = "", string finished = "null")
        {
            return "{\"id\":1,\"location_title\":\"" + title + "\",\"location_type\":\"" + locType + "\",\"started_at\":\"2026-10-15T07:30:00.000Z\"," +
                   "\"finished_at\":" + finished + ",\"alert_type\":\"" + type + "\",\"location_oblast\":\"Харківська область\",\"alert_level\":\"" + level + "\"" + extra + "}";
        }

        [Fact]
        public void City_red_alert_is_red()
        {
            var r = AlertsInUaParser.Parse(Json(A("м. Харків та Харківська територіальна громада", "hromada")), new AppSettings(), Now);
            Assert.True(r.Ok);
            Assert.Equal(AlertLevel.Red, r.Level);
            Assert.Equal(new DateTime(2026, 10, 15, 7, 30, 0, DateTimeKind.Utc), r.SinceUtc);
            Assert.Contains("Повітряна тривога", r.Detail);
        }

        [Fact]
        public void City_yellow_level_with_threats_is_yellow_and_described()
        {
            string threats = ",\"threats\":[{\"threat_type\":\"drones\",\"level\":\"yellow\"},{\"threat_type\":\"guided_aerial_bombs\",\"level\":\"red\"}]";
            var r = AlertsInUaParser.Parse(Json(A("м. Харків та Харківська територіальна громада", "hromada", level: "yellow", extra: threats)), new AppSettings(), Now);
            Assert.Equal(AlertLevel.Yellow, r.Level);
            Assert.Contains("БпЛА", r.Detail);
            Assert.Contains("КАБ", r.Detail);
        }

        [Fact]
        public void Raion_alert_alone_is_yellow_and_configurable()
        {
            var s = new AppSettings();
            string json = Json(A("Харківський район", "raion"));
            Assert.Equal(AlertLevel.Yellow, AlertsInUaParser.Parse(json, s, Now).Level);
            s.RaionAlertIsYellow = false;
            Assert.Equal(AlertLevel.Clear, AlertsInUaParser.Parse(json, s, Now).Level);
        }

        [Fact]
        public void Oblast_wide_alert_counts_for_city_by_default()
        {
            var s = new AppSettings();
            string json = Json(A("Харківська область", "oblast"));
            Assert.Equal(AlertLevel.Red, AlertsInUaParser.Parse(json, s, Now).Level);
            s.CountOblastAlert = false;
            Assert.Equal(AlertLevel.Clear, AlertsInUaParser.Parse(json, s, Now).Level);
        }

        [Fact]
        public void Red_city_alert_beats_yellow_raion_alert()
        {
            var r = AlertsInUaParser.Parse(Json(A("Харківський район", "raion"), A("м. Харків та Харківська територіальна громада", "city")), new AppSettings(), Now);
            Assert.Equal(AlertLevel.Red, r.Level);
        }

        [Fact]
        public void Other_places_and_finished_alerts_are_ignored()
        {
            string json = Json(
                A("Сумська область", "oblast"),
                A("Богодухівський район", "raion"),
                A("м. Харків та Харківська територіальна громада", "hromada", finished: "\"2026-10-15T08:00:00.000Z\""));
            var r = AlertsInUaParser.Parse(json, new AppSettings(), Now);
            Assert.True(r.Ok);
            Assert.Equal(AlertLevel.Clear, r.Level);
        }

        [Fact]
        public void Other_alert_types_in_city_are_yellow_unless_disabled()
        {
            var s = new AppSettings();
            string json = Json(A("м. Харків та Харківська територіальна громада", "hromada", type: "artillery_shelling", level: ""));
            Assert.Equal(AlertLevel.Yellow, AlertsInUaParser.Parse(json, s, Now).Level);
            s.OtherThreatTypesAreYellow = false;
            Assert.Equal(AlertLevel.Clear, AlertsInUaParser.Parse(json, s, Now).Level);
        }

        [Fact]
        public void Empty_list_is_clear_and_garbage_is_error()
        {
            Assert.Equal(AlertLevel.Clear, AlertsInUaParser.Parse(Json(), new AppSettings(), Now).Level);
            var bad = AlertsInUaParser.Parse("<html>oops</html>", new AppSettings(), Now);
            Assert.False(bad.Ok);
            Assert.Equal(ErrorKind.BadData, bad.ErrorKind);
            Assert.False(AlertsInUaParser.Parse("{\"x\":1}", new AppSettings(), Now).Ok);
        }

        [Fact]
        public void Source_sends_bearer_token_and_maps_errors()
        {
            var http = new FakeHttp();
            IDictionary<string, string> seen = null;
            var h2 = new CapturingHttp(r => seen = r);
            h2.Result = new HttpResult { Status = 200, Body = Json() };
            var s = new AppSettings { AlertsInUaToken = " secret " };
            var src = new AlertsInUaSource(() => s, h2, () => Now);
            Assert.True(src.Fetch().Ok);
            Assert.Equal("Bearer secret", seen["Authorization"]);

            h2.Result = new HttpResult { Status = 401, Body = "" };
            Assert.Equal(ErrorKind.Unauthorized, src.Fetch().ErrorKind);
            h2.Result = new HttpResult { Status = 429, Body = "" };
            Assert.Equal(ErrorKind.RateLimited, src.Fetch().ErrorKind);
            h2.Result = new HttpResult { Status = 503, Body = "" };
            Assert.Equal(ErrorKind.ServerError, src.Fetch().ErrorKind);

            s.AlertsInUaToken = "";
            Assert.Equal(ErrorKind.NotConfigured, src.Fetch().ErrorKind);
        }

        sealed class CapturingHttp : IHttp
        {
            readonly Action<IDictionary<string, string>> _cap;
            public HttpResult Result;
            public CapturingHttp(Action<IDictionary<string, string>> cap) { _cap = cap; }
            public HttpResult Get(string url, IDictionary<string, string> headers, int timeoutMs) { _cap(headers); return Result; }
            public HttpResult PostForm(string url, IDictionary<string, string> form, int timeoutMs) { return Result; }
        }
    }

    public class EngineTests
    {
        sealed class Clock
        {
            public DateTime Now = new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);
            public void Advance(double minutes) { Now = Now.AddMinutes(minutes); }
        }

        static SourceReading Ok(AlertLevel l, string detail = "")
        {
            return new SourceReading { Ok = true, Level = l, Detail = detail, SourceName = "test" };
        }

        static SourceReading Fail()
        {
            return SourceReading.Fail("test", ErrorKind.Network, "немає з'єднання", DateTime.UtcNow);
        }

        static AlertEngine Make(Clock c, Action<AppSettings> tweak = null)
        {
            var s = new AppSettings { ClearConfirmPolls = 2, FailuresBeforeLost = 4, StaleAfterSeconds = 120 };
            if (tweak != null) tweak(s);
            return new AlertEngine(s, () => c.Now);
        }

        static AlertEventKind[] Kinds(List<EngineEvent> ev) { return ev.Select(e => e.Kind).ToArray(); }

        [Fact]
        public void First_clear_reading_is_silent_but_sets_state()
        {
            var c = new Clock(); var e = Make(c);
            Assert.Empty(e.OnReading(Ok(AlertLevel.Clear)));
            Assert.Equal(AlertLevel.Clear, e.State.Level);
            Assert.Equal(AlertLevel.Clear, e.EffectiveLevel());
        }

        [Fact]
        public void Starting_during_alarm_announces_it_as_initial()
        {
            var c = new Clock(); var e = Make(c);
            List<EngineEvent> ev = e.OnReading(Ok(AlertLevel.Red, "тривога"));
            Assert.Equal(new[] { AlertEventKind.RedStarted }, Kinds(ev));
            Assert.True(ev[0].IsInitial);
        }

        [Fact]
        public void Escalation_is_immediate_and_downgrade_needs_confirmation()
        {
            var c = new Clock(); var e = Make(c);
            e.OnReading(Ok(AlertLevel.Clear));
            Assert.Equal(new[] { AlertEventKind.YellowStarted }, Kinds(e.OnReading(Ok(AlertLevel.Yellow))));
            Assert.Equal(new[] { AlertEventKind.RedFromYellow }, Kinds(e.OnReading(Ok(AlertLevel.Red))));

            Assert.Empty(e.OnReading(Ok(AlertLevel.Yellow)));
            Assert.Equal(new[] { AlertEventKind.YellowFromRed }, Kinds(e.OnReading(Ok(AlertLevel.Yellow))));
            Assert.Empty(e.OnReading(Ok(AlertLevel.Clear)));
            Assert.Equal(new[] { AlertEventKind.AllClear }, Kinds(e.OnReading(Ok(AlertLevel.Clear))));
            Assert.Equal(AlertLevel.Clear, e.State.Level);
        }

        [Fact]
        public void Direct_red_from_clear_is_RedStarted()
        {
            var c = new Clock(); var e = Make(c);
            e.OnReading(Ok(AlertLevel.Clear));
            Assert.Equal(new[] { AlertEventKind.RedStarted }, Kinds(e.OnReading(Ok(AlertLevel.Red))));
        }

        [Fact]
        public void Flapping_reading_does_not_clear_alarm()
        {
            var c = new Clock(); var e = Make(c);
            e.OnReading(Ok(AlertLevel.Red));
            Assert.Empty(e.OnReading(Ok(AlertLevel.Clear)));
            Assert.Empty(e.OnReading(Ok(AlertLevel.Red)));
            Assert.Empty(e.OnReading(Ok(AlertLevel.Clear)));
            Assert.Equal(AlertLevel.Red, e.State.Level);
        }

        [Fact]
        public void Downgrade_settles_on_highest_pending_level()
        {
            var c = new Clock(); var e = Make(c);
            e.OnReading(Ok(AlertLevel.Red));
            e.OnReading(Ok(AlertLevel.Yellow));
            Assert.Equal(new[] { AlertEventKind.YellowFromRed }, Kinds(e.OnReading(Ok(AlertLevel.Clear))));
            Assert.Equal(AlertLevel.Yellow, e.State.Level);
        }

        [Fact]
        public void Single_poll_confirmation_gives_instant_clear()
        {
            var c = new Clock(); var e = Make(c, s => s.ClearConfirmPolls = 1);
            e.OnReading(Ok(AlertLevel.Red));
            Assert.Equal(new[] { AlertEventKind.AllClear }, Kinds(e.OnReading(Ok(AlertLevel.Clear))));
        }

        [Fact]
        public void Connection_lost_once_then_restored_with_downtime()
        {
            var c = new Clock(); var e = Make(c);
            e.OnReading(Ok(AlertLevel.Clear));
            for (int i = 0; i < 3; i++) { c.Advance(0.25); Assert.Empty(e.OnReading(Fail())); }
            c.Advance(0.25);
            List<EngineEvent> lost = e.OnReading(Fail());
            Assert.Equal(new[] { AlertEventKind.ConnectionLost }, Kinds(lost));
            Assert.Contains("НЕВІДОМИЙ", lost[0].Message);
            c.Advance(0.25);
            Assert.Empty(e.OnReading(Fail()));

            c.Advance(2);
            List<EngineEvent> back = e.OnReading(Ok(AlertLevel.Clear));
            Assert.Equal(new[] { AlertEventKind.ConnectionRestored }, Kinds(back));
            Assert.True(back[0].Downtime > TimeSpan.FromMinutes(2));
            Assert.False(e.State.ConnectionLost);
        }

        [Fact]
        public void Alarm_that_started_during_outage_is_announced_after_restore()
        {
            var c = new Clock(); var e = Make(c, s => s.FailuresBeforeLost = 2);
            e.OnReading(Ok(AlertLevel.Clear));
            e.OnReading(Fail()); e.OnReading(Fail());
            List<EngineEvent> ev = e.OnReading(Ok(AlertLevel.Red));
            Assert.Equal(new[] { AlertEventKind.ConnectionRestored, AlertEventKind.RedStarted }, Kinds(ev));
        }

        [Fact]
        public void Alarm_is_kept_but_marked_unknown_when_data_goes_stale()
        {
            var c = new Clock(); var e = Make(c);
            e.OnReading(Ok(AlertLevel.Red));
            Assert.Equal(AlertLevel.Red, e.EffectiveLevel());
            c.Advance(3);
            Assert.Equal(AlertLevel.Unknown, e.EffectiveLevel());
            Assert.Equal(AlertLevel.Red, e.State.Level);
        }

        [Fact]
        public void Reminders_fire_on_interval_only_during_red()
        {
            var c = new Clock(); var e = Make(c, s => s.ReminderMinutes = 5);
            e.OnReading(Ok(AlertLevel.Red));
            c.Advance(4); e.OnReading(Ok(AlertLevel.Red));
            Assert.Empty(e.OnTick());
            c.Advance(1.5); e.OnReading(Ok(AlertLevel.Red));
            Assert.Equal(new[] { AlertEventKind.RedReminder }, Kinds(e.OnTick()));
            Assert.Empty(e.OnTick());
            c.Advance(5); e.OnReading(Ok(AlertLevel.Red));
            Assert.Single(e.OnTick());
            e.OnReading(Ok(AlertLevel.Clear)); e.OnReading(Ok(AlertLevel.Clear));
            c.Advance(30);
            Assert.Empty(e.OnTick());
        }

        [Fact]
        public void Reminders_disabled_by_default()
        {
            var c = new Clock(); var e = Make(c);
            e.OnReading(Ok(AlertLevel.Red));
            c.Advance(60); e.OnReading(Ok(AlertLevel.Red));
            Assert.Empty(e.OnTick());
        }
    }
}
