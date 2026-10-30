using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Varta.Core
{
    /// <summary>Нове повідомлення каналу про рух цілей, у якому згадано наш район.</summary>
    public sealed class NearbyMatch
    {
        public string Channel = "";
        public long Id;
        public DateTime TimeUtc;
        public string Text = "";
        public string Headline = "";
        public string Keyword = "";
        /// <summary>false — повтор протягом паузи між оповіщеннями: лише запис у журнал.</summary>
        public bool Announce;
    }

    public sealed class NearbyChannelStatus
    {
        public string Channel = "";
        public bool Ok;
        public string Error = "";
        public DateTime? NewestMessageUtc;
    }

    /// <summary>Чи згадує коротке повідомлення про рух цілі наш район.</summary>
    public static class NearbyMatcher
    {
        /// <summary>Довші дописи — зведення новин, а не повідомлення про рух цілі.</summary>
        public const int MaxLength = 250;

        /// <summary>Слово, що збіглося, або null. Слова — через «|»; «*» у кінці слова — будь-яке закінчення.</summary>
        public static string Match(string text, string keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength) return null;
            string n = TextUtil.Norm(text);
            foreach (string k in TextUtil.SplitKeywords(keywords))
                if (TextUtil.ContainsPhrase(n, k)) return k;
            return null;
        }

        /// <summary>Суть повідомлення без «хвоста» з посиланнями каналу: до першого ⚠️ включно, інакше початок тексту.</summary>
        public static string Headline(string text)
        {
            string t = (text ?? "").Trim();
            int w = t.IndexOf('\u26A0');
            if (w > 0)
            {
                int end = w + 1;
                if (end < t.Length && t[end] == '\uFE0F') end++;
                t = t.Substring(0, end);
            }
            return t.Length > 140 ? t.Substring(0, 137).TrimEnd() + "…" : t;
        }
    }

    /// <summary>
    /// Стежить за публічними Telegram-каналами про рух цілей (наприклад, @tlknewsua): нове коротке повідомлення зі згадкою
    /// нашого району — особисте сповіщення «Ціль летить у наш бік» (за замовчуванням лише адміністратору в Telegram).
    /// Те, що вже було в каналі на момент запуску, не надсилається.
    /// </summary>
    public sealed class NearbyWatcher
    {
        /// <summary>Старіші повідомлення вже неактуальні (наприклад, після сну ПК чи перерви зв'язку).</summary>
        public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

        static readonly Dictionary<string, string> Headers = new Dictionary<string, string>
        {
            { "Accept", "text/html" },
            { "Accept-Language", "uk,en;q=0.8" }
        };

        readonly Func<AppSettings> _cfg;
        readonly IHttp _http;
        readonly Func<DateTime> _now;
        readonly Dictionary<string, long> _lastSeen = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        readonly object _lock = new object();
        List<NearbyChannelStatus> _status = new List<NearbyChannelStatus>();
        DateTime _lastAnnouncedUtc = DateTime.MinValue;

        public NearbyWatcher(Func<AppSettings> cfg, IHttp http, Func<DateTime> utcNow)
        {
            _cfg = cfg;
            _http = http;
            _now = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>Останній збіг — для вкладки «Стан».</summary>
        public NearbyMatch LastMatch { get; private set; }

        public List<NearbyChannelStatus> GetStatus()
        {
            lock (_lock) { return new List<NearbyChannelStatus>(_status); }
        }

        /// <summary>Чи є куди надіслати: банер чи вікно Windows або підключений Telegram-адміністратор. Інакше канали не читаються.</summary>
        public static bool CanDeliver(AppSettings s)
        {
            Channel c = RoutePolicy.Get(s, AlertEventKind.ThreatNearby);
            if ((c & (Channel.Banner | Channel.Tray)) != 0) return true;
            return (c & Channel.Admin) != 0 && s.AdminConfigured;
        }

        /// <summary>Читає канали й повертає нові збіги (з позначкою, чи оголошувати). Викликається з потоку опитування.</summary>
        public List<NearbyMatch> Poll()
        {
            var found = new List<NearbyMatch>();
            var statuses = new List<NearbyChannelStatus>();
            AppSettings s = _cfg();
            if (!s.NearbyEnabled || !CanDeliver(s))
            {
                // Після повторного ввімкнення перше читання знову лише запам'ятовує, що вже є в каналі.
                _lastSeen.Clear();
            }
            else
            {
                foreach (string ch in TextUtil.SplitChannels(s.NearbyChannels))
                {
                    var st = new NearbyChannelStatus { Channel = ch };
                    statuses.Add(st);
                    DateTime now;
                    string err;
                    List<TgMessage> msgs = Read(_http, ch, _now(), out now, out err);
                    if (msgs == null) { st.Error = err; continue; }
                    st.Ok = true;
                    st.NewestMessageUtc = msgs.Max(m => m.TimeUtc);
                    long newest = msgs.Max(m => m.Id);

                    long last;
                    if (!_lastSeen.TryGetValue(ch, out last))
                    {
                        _lastSeen[ch] = newest;   // перше читання після запуску: лише запам'ятовуємо
                        continue;
                    }
                    foreach (TgMessage m in msgs.Where(x => x.Id > last).OrderBy(x => x.Id))
                    {
                        if (now - m.TimeUtc > MaxAge) continue;
                        string kw = NearbyMatcher.Match(m.Text, s.NearbyKeywords);
                        if (kw == null) continue;
                        bool announce = now - _lastAnnouncedUtc >= TimeSpan.FromMinutes(s.NearbyRepeatMinutes);
                        if (announce) _lastAnnouncedUtc = now;
                        var hit = new NearbyMatch
                        {
                            Channel = ch, Id = m.Id, TimeUtc = m.TimeUtc, Text = m.Text,
                            Headline = NearbyMatcher.Headline(m.Text), Keyword = kw, Announce = announce
                        };
                        LastMatch = hit;
                        found.Add(hit);
                    }
                    if (newest > last) _lastSeen[ch] = newest;
                }
            }
            lock (_lock) { _status = statuses; }
            return found;
        }

        /// <summary>Повідомлення зі сторінки каналу або null і опис помилки; now — час сервера Telegram, якщо відомий.</summary>
        static List<TgMessage> Read(IHttp http, string ch, DateTime localNow, out DateTime now, out string error)
        {
            now = localNow;
            error = "";
            HttpResult res = http.Get("https://t.me/s/" + ch, Headers, 10000);
            if (res.ServerDateUtc.HasValue) now = res.ServerDateUtc.Value;
            if (!res.Ok) { error = SourceHelpers.FromHttpFailure("Telegram", res, localNow).Error; return null; }
            List<TgMessage> msgs = TelegramPageParser.Parse(res.Body, ch);
            if (msgs.Count == 0) { error = "сторінку каналу не вдалося розібрати (канал не існує або веб-перегляд вимкнено)"; return null; }
            return msgs;
        }

        /// <summary>Перевірка з вкладки налаштувань: які з останніх повідомлень збігаються зі словами (без оповіщень і без зміни стану).</summary>
        public static string Preview(IHttp http, string channels, string keywords, DateTime utcNow)
        {
            List<string> list = TextUtil.SplitChannels(channels);
            if (list.Count == 0) return "✗ Не вказано жодного коректного каналу.";
            if (TextUtil.SplitKeywords(keywords).Count == 0) return "✗ Не вказано жодного слова для вашого району.";
            var sb = new StringBuilder();
            bool allOk = true;
            foreach (string ch in list)
            {
                DateTime now;
                string err;
                List<TgMessage> msgs = Read(http, ch, utcNow, out now, out err);
                if (msgs == null) { allOk = false; sb.Append("✗ @").Append(ch).Append(": ").Append(err).Append('\n'); continue; }
                List<TgMessage> hits = msgs.Where(m => NearbyMatcher.Match(m.Text, keywords) != null).OrderByDescending(m => m.Id).ToList();
                sb.Append('@').Append(ch).Append(": прочитано ").Append(msgs.Count).Append(" останніх повідомлень, збігів із вашим районом: ").Append(hits.Count).Append('\n');
                foreach (TgMessage m in hits.Take(3))
                    sb.Append("   ").Append(m.TimeUtc.ToLocalTime().ToString("dd.MM HH:mm")).Append(" — ").Append(NearbyMatcher.Headline(m.Text)).Append('\n');
            }
            return (allOk ? "✓ " : "") + sb.ToString().Trim();
        }
    }
}
