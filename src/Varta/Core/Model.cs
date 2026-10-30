using System;

namespace Varta.Core
{
    public static class AppInfo
    {
        public const string Name = "ВАРТА";
        public const string Version = "1.0.2";
        public const string Tagline = "Сповіщення про повітряну тривогу";
    }

    /// <summary>Рівень небезпеки для міста: зелений / жовтий / червоний.</summary>
    public enum AlertLevel { Unknown = 0, Clear = 1, Yellow = 2, Red = 3 }

    public enum ErrorKind { None = 0, Network, Timeout, Unauthorized, RateLimited, ServerError, BadData, Stale, NotConfigured }

    public enum AlertEventKind
    {
        RedStarted,
        RedFromYellow,
        YellowStarted,
        YellowFromRed,
        AllClear,
        RedReminder,
        ConnectionLost,
        ConnectionRestored,
        AudioProblem,
        ConfigProblem,
        AppStarted,
        MonitoringPaused,
        MonitoringResumed,
        DailyReport
    }

    [Flags]
    public enum Channel { None = 0, Voice = 1, Banner = 2, Tray = 4, Admin = 8 }

    /// <summary>Один результат опитування джерела даних.</summary>
    public sealed class SourceReading
    {
        public bool Ok;
        public AlertLevel Level = AlertLevel.Unknown;
        public string Detail = "";
        public DateTime? SinceUtc;
        public string SourceName = "";
        public string Error = "";
        public ErrorKind ErrorKind;
        public DateTime FetchedUtc;
        public DateTime? ServerDateUtc;
        public int LatencyMs;

        public static SourceReading Fail(string source, ErrorKind kind, string error, DateTime nowUtc)
        {
            return new SourceReading { Ok = false, SourceName = source, ErrorKind = kind, Error = error, FetchedUtc = nowUtc };
        }
    }

    /// <summary>Подія, яку рушій передає в систему сповіщень.</summary>
    public sealed class EngineEvent
    {
        public AlertEventKind Kind;
        public AlertLevel From;
        public AlertLevel To;
        public DateTime AtUtc;
        public string Detail = "";
        public string Source = "";
        public string Message = "";
        public bool IsInitial;
        public bool IsTest;
        public TimeSpan Downtime;
    }

    public interface IAlertSource
    {
        string Name { get; }
        SourceReading Fetch();
        void Reset();
    }

    public sealed class HttpResult
    {
        public int Status;
        public string Body = "";
        public string Error;
        public bool TimedOut;
        public DateTime? ServerDateUtc;
        public int ElapsedMs;

        public bool Ok { get { return Error == null && Status >= 200 && Status < 300; } }
    }

    public interface IHttp
    {
        HttpResult Get(string url, System.Collections.Generic.IDictionary<string, string> headers, int timeoutMs);
        HttpResult PostForm(string url, System.Collections.Generic.IDictionary<string, string> form, int timeoutMs);
    }
}
