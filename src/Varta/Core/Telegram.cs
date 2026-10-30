using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Varta.Core
{
    public sealed class TgMessage
    {
        public string Channel = "";
        public long Id;
        public DateTime TimeUtc;
        public string Text = "";
    }

    public enum TgScope { None, City, Raion, Oblast }
    public enum TgKind { None, AlarmStart, AlarmClear, ThreatStart, ThreatClear }

    public sealed class TgEvent
    {
        public string Channel = "";
        public long Id;
        public DateTime TimeUtc;
        public TgScope Scope;
        public TgKind Kind;
        public string Text = "";
    }

    public sealed class ChannelStatus
    {
        public string Channel = "";
        public bool Ok;
        public bool Stale;
        public string Error = "";
        public DateTime? NewestMessageUtc;
        public int Events;
    }

    /// <summary>Розбір HTML-сторінки публічного веб-перегляду каналу (https://t.me/s/канал).</summary>
    public static class TelegramPageParser
    {
        static readonly Regex PostRe = new Regex("data-post=\"([^\"/]+)/(\\d+)\"", RegexOptions.Compiled);
        static readonly Regex TimeRe = new Regex("<time[^>]*datetime=\"([^\"]+)\"", RegexOptions.Compiled);
        static readonly Regex TextRe = new Regex("<div class=\"tgme_widget_message_text js-message_text[^\"]*\"[^>]*>(.*?)</div>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        public static List<TgMessage> Parse(string html, string channel)
        {
            var res = new List<TgMessage>();
            if (string.IsNullOrEmpty(html)) return res;
            string[] parts = html.Split(new[] { "tgme_widget_message_wrap" }, StringSplitOptions.None);
            for (int i = 1; i < parts.Length; i++)
            {
                string b = parts[i];
                Match pm = PostRe.Match(b);
                Match tm = TimeRe.Match(b);
                if (!pm.Success || !tm.Success) continue;
                DateTime? t = TextUtil.ParseIsoUtc(tm.Groups[1].Value);
                long id;
                if (!t.HasValue || !long.TryParse(pm.Groups[2].Value, out id)) continue;
                Match xm = TextRe.Match(b);
                res.Add(new TgMessage
                {
                    Channel = pm.Groups[1].Value,
                    Id = id,
                    TimeUtc = t.Value,
                    Text = xm.Success ? TextUtil.HtmlToText(xm.Groups[1].Value) : ""
                });
            }
            return res;
        }
    }

    /// <summary>Визначає, чи стосується повідомлення нашого міста та що воно означає.</summary>
    public static class TelegramClassifier
    {
        static readonly Regex AlarmClear = new Regex(@"відбій\s+(повітряної\s+)?тривоги", RegexOptions.Compiled);
        static readonly Regex ThreatClear = new Regex(@"відбій\s+(загрози|небезпеки)", RegexOptions.Compiled);
        static readonly Regex AlarmStart = new Regex(@"повітряна\s+тривога", RegexOptions.Compiled);
        static readonly Regex ThreatStart = new Regex(@"(^|[^\p{L}])загроза\s", RegexOptions.Compiled);

        public static TgEvent Classify(TgMessage m, AppSettings s)
        {
            string n = TextUtil.Norm(m.Text);
            if (n.Length == 0) return null;

            TgKind kind;
            if (AlarmClear.IsMatch(n)) kind = TgKind.AlarmClear;
            else if (ThreatClear.IsMatch(n)) kind = TgKind.ThreatClear;
            else if (AlarmStart.IsMatch(n)) kind = TgKind.AlarmStart;
            else if (ThreatStart.IsMatch(n)) kind = TgKind.ThreatStart;
            else return null;

            TgScope scope;
            if (TextUtil.ContainsAnyPhrase(n, s.CityKeywords)) scope = TgScope.City;
            else if (TextUtil.ContainsAnyPhrase(n, s.RaionKeywords)) scope = TgScope.Raion;
            else if (TextUtil.ContainsAnyPhrase(n, s.OblastKeywords)) scope = TgScope.Oblast;
            else return null;

            bool threat = kind == TgKind.ThreatStart || kind == TgKind.ThreatClear;
            if (threat && scope != TgScope.City) return null;

            return new TgEvent { Channel = m.Channel, Id = m.Id, TimeUtc = m.TimeUtc, Scope = scope, Kind = kind, Text = m.Text };
        }
    }

    public sealed class TelegramState
    {
        public AlertLevel Level = AlertLevel.Clear;
        public DateTime? SinceUtc;
        public string Detail = "";
    }

    public static class TelegramStateMachine
    {
        public static TelegramState Evaluate(IEnumerable<TgEvent> events, AppSettings s, DateTime nowUtc)
        {
            TimeSpan maxAge = TimeSpan.FromHours(s.TelegramEventMaxAgeHours);
            List<TgEvent> list = events.Where(e => nowUtc - e.TimeUtc <= maxAge)
                                       .OrderBy(e => e.TimeUtc).ThenBy(e => e.Id).ToList();

            bool city = false, raion = false, oblast = false, threat = false;
            DateTime? cityT = null, raionT = null, oblastT = null, threatT = null;
            string cityX = "", raionX = "", oblastX = "", threatX = "";

            foreach (TgEvent e in list)
            {
                switch (e.Kind)
                {
                    case TgKind.AlarmStart:
                        if (e.Scope == TgScope.City) { if (!city) cityT = e.TimeUtc; city = true; cityX = e.Text; }
                        else if (e.Scope == TgScope.Raion) { if (!raion) raionT = e.TimeUtc; raion = true; raionX = e.Text; }
                        else { if (!oblast) oblastT = e.TimeUtc; oblast = true; oblastX = e.Text; }
                        break;
                    case TgKind.AlarmClear:
                        if (e.Scope == TgScope.City) { city = false; threat = false; }
                        else if (e.Scope == TgScope.Raion) raion = false;
                        else { oblast = false; threat = false; }
                        break;
                    case TgKind.ThreatStart:
                        if (!threat) threatT = e.TimeUtc;
                        threat = true; threatX = e.Text;
                        break;
                    case TgKind.ThreatClear:
                        threat = false;
                        break;
                }
            }

            var st = new TelegramState();
            if (city || (oblast && s.CountOblastAlert))
            {
                st.Level = AlertLevel.Red;
                st.SinceUtc = city ? cityT : oblastT;
                st.Detail = TextUtil.ShortDetail(city ? cityX : oblastX);
            }
            else if ((raion && s.RaionAlertIsYellow) || threat)
            {
                st.Level = AlertLevel.Yellow;
                bool useThreat = threat;
                st.SinceUtc = useThreat ? threatT : raionT;
                st.Detail = TextUtil.ShortDetail(useThreat ? threatX : raionX);
            }
            return st;
        }
    }

    /// <summary>Джерело без ключа: читає публічні Telegram-канали з оголошеннями про тривогу.</summary>
    public sealed class TelegramSource : IAlertSource
    {
        public const string SourceName = "Telegram-канали";
        const int MaxBackfillPages = 8;

        sealed class ChannelCache
        {
            public bool Initialized;
            public DateTime NewestUtc = DateTime.MinValue;
            public readonly Dictionary<long, TgEvent> Events = new Dictionary<long, TgEvent>();
        }

        readonly Func<AppSettings> _cfg;
        readonly IHttp _http;
        readonly Func<DateTime> _now;
        readonly Dictionary<string, ChannelCache> _cache = new Dictionary<string, ChannelCache>(StringComparer.OrdinalIgnoreCase);
        readonly object _lock = new object();
        List<ChannelStatus> _status = new List<ChannelStatus>();

        public TelegramSource(Func<AppSettings> cfg, IHttp http, Func<DateTime> utcNow)
        {
            _cfg = cfg;
            _http = http;
            _now = utcNow ?? (() => DateTime.UtcNow);
        }

        public string Name { get { return SourceName; } }

        public void Reset()
        {
            lock (_lock) { _cache.Clear(); _status = new List<ChannelStatus>(); }
        }

        public List<ChannelStatus> GetChannelStatus()
        {
            lock (_lock) { return new List<ChannelStatus>(_status); }
        }

        static string PageUrl(string ch, long before)
        {
            return "https://t.me/s/" + ch + (before > 0 ? "?before=" + before : "");
        }

        static readonly Dictionary<string, string> Headers = new Dictionary<string, string>
        {
            { "Accept", "text/html" },
            { "Accept-Language", "uk,en;q=0.8" }
        };

        public SourceReading Fetch()
        {
            AppSettings s = _cfg();
            DateTime localNow = _now();
            List<string> channels = TextUtil.SplitChannels(s.TelegramChannels);
            if (channels.Count == 0)
                return SourceReading.Fail(SourceName, ErrorKind.NotConfigured, "не вказано жодного Telegram-каналу", localNow);

            var statuses = new List<ChannelStatus>();
            var all = new List<TgEvent>();
            int okCount = 0;
            SourceReading firstFail = null;
            DateTime? serverDate = null;
            int latency = 0;

            foreach (string ch in channels)
            {
                var st = new ChannelStatus { Channel = ch };
                statuses.Add(st);

                ChannelCache cache;
                lock (_lock)
                {
                    if (!_cache.TryGetValue(ch, out cache)) { cache = new ChannelCache(); _cache[ch] = cache; }
                }

                HttpResult res = _http.Get(PageUrl(ch, 0), Headers, 12000);
                latency = Math.Max(latency, res.ElapsedMs);
                if (res.ServerDateUtc.HasValue) serverDate = res.ServerDateUtc;
                if (!res.Ok)
                {
                    SourceReading f = SourceHelpers.FromHttpFailure(SourceName, res, localNow);
                    st.Error = f.Error;
                    if (firstFail == null) firstFail = f;
                    continue;
                }

                List<TgMessage> msgs = TelegramPageParser.Parse(res.Body, ch);
                if (msgs.Count == 0)
                {
                    st.Error = "сторінку каналу не вдалося розібрати (канал не існує або веб-перегляд вимкнено)";
                    if (firstFail == null) firstFail = SourceReading.Fail(SourceName, ErrorKind.BadData, st.Error, localNow);
                    continue;
                }

                DateTime now = res.ServerDateUtc ?? localNow;
                DateTime prevNewest = cache.NewestUtc;
                DateTime target = cache.Initialized ? prevNewest : now - TimeSpan.FromHours(s.TelegramBackfillHours);
                Merge(cache, msgs, s);

                DateTime oldest = msgs.Min(m => m.TimeUtc);
                long minId = msgs.Min(m => m.Id);
                for (int page = 0; page < MaxBackfillPages && oldest > target && minId > 1; page++)
                {
                    HttpResult older = _http.Get(PageUrl(ch, minId), Headers, 12000);
                    if (!older.Ok) break;
                    List<TgMessage> prev = TelegramPageParser.Parse(older.Body, ch);
                    if (prev.Count == 0) break;
                    Merge(cache, prev, s);
                    DateTime po = prev.Min(m => m.TimeUtc);
                    long pid = prev.Min(m => m.Id);
                    if (pid >= minId) break;
                    oldest = po;
                    minId = pid;
                }
                cache.Initialized = true;

                st.NewestMessageUtc = cache.NewestUtc;
                st.Events = cache.Events.Count;
                if (now - cache.NewestUtc > TimeSpan.FromHours(s.ChannelStaleHours))
                {
                    st.Stale = true;
                    st.Error = "канал не оновлювався " + TextUtil.FormatDuration(now - cache.NewestUtc) + " — дані ігноруються";
                    if (firstFail == null) firstFail = SourceReading.Fail(SourceName, ErrorKind.Stale, st.Error, localNow);
                    continue;
                }

                st.Ok = true;
                okCount++;
                all.AddRange(cache.Events.Values);
            }

            lock (_lock) { _status = statuses; }

            if (okCount == 0)
            {
                SourceReading f = firstFail ?? SourceReading.Fail(SourceName, ErrorKind.Network, "немає даних", localNow);
                f.LatencyMs = latency;
                f.ServerDateUtc = serverDate;
                return f;
            }

            DateTime evalNow = serverDate ?? localNow;
            TelegramState state = TelegramStateMachine.Evaluate(all, s, evalNow);
            return new SourceReading
            {
                Ok = true,
                Level = state.Level,
                Detail = state.Detail,
                SinceUtc = state.SinceUtc,
                SourceName = SourceName,
                FetchedUtc = localNow,
                ServerDateUtc = serverDate,
                LatencyMs = latency
            };
        }

        static void Merge(ChannelCache cache, List<TgMessage> msgs, AppSettings s)
        {
            foreach (TgMessage m in msgs)
            {
                if (m.TimeUtc > cache.NewestUtc) cache.NewestUtc = m.TimeUtc;
                TgEvent e = TelegramClassifier.Classify(m, s);
                if (e != null) cache.Events[m.Id] = e;
            }
            DateTime keepFrom = cache.NewestUtc - TimeSpan.FromHours(Math.Max(s.TelegramEventMaxAgeHours, s.TelegramBackfillHours) + 1);
            var old = cache.Events.Where(kv => kv.Value.TimeUtc < keepFrom).Select(kv => kv.Key).ToList();
            foreach (long id in old) cache.Events.Remove(id);
        }
    }
}
