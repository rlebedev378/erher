using System;
using System.Collections.Generic;
using System.Text;

namespace Varta.Core
{
    /// <summary>Розбір відповіді alerts.in.ua (/v1/alerts/active.json) у рівень для нашого міста.</summary>
    public static class AlertsInUaParser
    {
        public const string SourceName = "alerts.in.ua";

        enum Scope { None, City, Raion, Oblast }

        public static SourceReading Parse(string json, AppSettings s, DateTime nowUtc)
        {
            List<object> arr;
            try
            {
                var root = MiniJson.Obj(MiniJson.Parse(json));
                arr = MiniJson.Arr(MiniJson.Get(root, "alerts"));
            }
            catch (Exception ex)
            {
                return SourceReading.Fail(SourceName, ErrorKind.BadData, "Некоректна відповідь API: " + ex.Message, nowUtc);
            }
            if (arr == null)
                return SourceReading.Fail(SourceName, ErrorKind.BadData, "У відповіді API немає списку alerts", nowUtc);

            AlertLevel best = AlertLevel.Clear;
            string bestDetail = "";
            DateTime? bestSince = null;

            foreach (object o in arr)
            {
                var a = MiniJson.Obj(o);
                if (a == null) continue;
                if (!string.IsNullOrEmpty(MiniJson.Str(a, "finished_at"))) continue;

                string type = MiniJson.Str(a, "alert_type");
                if (type.Length == 0) type = "air_raid";
                string locType = MiniJson.Str(a, "location_type");
                string title = MiniJson.Str(a, "location_title");
                string nTitle = TextUtil.Norm(title);
                string lvlStr = MiniJson.Str(a, "alert_level");
                AlertLevel own = lvlStr == "yellow" ? AlertLevel.Yellow
                               : lvlStr == "red" ? AlertLevel.Red
                               : (type == "air_raid" ? AlertLevel.Red : AlertLevel.Yellow);

                Scope scope = Scope.None;
                if ((locType == "city" || locType == "hromada") && TextUtil.ContainsAnyPhrase(nTitle, s.CityKeywords)) scope = Scope.City;
                else if (locType == "raion" && TextUtil.ContainsAnyPhrase(nTitle, s.RaionKeywords)) scope = Scope.Raion;
                else if (locType == "oblast" && TextUtil.ContainsAnyPhrase(nTitle, s.OblastKeywords)) scope = Scope.Oblast;
                if (scope == Scope.None) continue;

                AlertLevel lvl;
                if (scope == Scope.City)
                {
                    if (type != "air_raid" && !s.OtherThreatTypesAreYellow) continue;
                    lvl = own;
                }
                else if (scope == Scope.Oblast)
                {
                    if (!s.CountOblastAlert) continue;
                    lvl = own;
                }
                else
                {
                    if (!s.RaionAlertIsYellow) continue;
                    lvl = AlertLevel.Yellow;
                }

                DateTime? since = TextUtil.ParseIsoUtc(MiniJson.Str(a, "started_at"));
                if (lvl > best)
                {
                    best = lvl;
                    bestDetail = Describe(a, scope, title, type);
                    bestSince = since;
                }
                else if (lvl == best && lvl != AlertLevel.Clear && since.HasValue && (!bestSince.HasValue || since < bestSince))
                {
                    bestSince = since;
                }
            }

            return new SourceReading
            {
                Ok = true,
                Level = best,
                Detail = best == AlertLevel.Clear ? "" : bestDetail,
                SinceUtc = best == AlertLevel.Clear ? (DateTime?)null : bestSince,
                SourceName = SourceName,
                FetchedUtc = nowUtc
            };
        }

        static string Describe(Dictionary<string, object> a, Scope scope, string title, string type)
        {
            var sb = new StringBuilder();
            if (scope == Scope.City) sb.Append(TypeLabel(type)).Append(": ").Append(title);
            else if (scope == Scope.Raion) sb.Append("Тривога в районі: ").Append(title);
            else sb.Append("Тривога по області: ").Append(title);

            var threats = MiniJson.Arr(MiniJson.Get(a, "threats"));
            if (threats != null && threats.Count > 0)
            {
                var labels = new List<string>();
                foreach (object t in threats)
                {
                    string l = ThreatLabel(MiniJson.Str(MiniJson.Obj(t), "threat_type"));
                    if (l.Length > 0 && !labels.Contains(l)) labels.Add(l);
                }
                if (labels.Count > 0) sb.Append(" • Загрози: ").Append(string.Join(", ", labels.ToArray()));
            }
            return sb.ToString();
        }

        static string TypeLabel(string type)
        {
            switch (type)
            {
                case "air_raid": return "Повітряна тривога";
                case "artillery_shelling": return "Загроза артилерійських обстрілів";
                case "urban_fights": return "Вуличні бої";
                case "chemical": return "Хімічна загроза";
                case "nuclear": return "Радіаційна/ядерна загроза";
                default: return "Тривога";
            }
        }

        static string ThreatLabel(string t)
        {
            switch (t)
            {
                case "tactic_aircraft_activity": return "тактична авіація";
                case "strategic_aircraft_activity": return "стратегічна авіація";
                case "mig31k_departure": return "зліт МіГ-31К";
                case "ballistic_missiles": return "балістика";
                case "cruise_missiles": return "крилаті ракети";
                case "unspecified_missiles": return "ракети";
                case "drones": return "БпЛА";
                case "guided_aerial_bombs": return "КАБ";
                case "air_defense": return "робота ППО";
                case "unknown": return "невизначена загроза";
                default: return "";
            }
        }
    }

    public static class SourceHelpers
    {
        public static SourceReading FromHttpFailure(string source, HttpResult r, DateTime nowUtc)
        {
            ErrorKind kind;
            string msg;
            if (r.TimedOut) { kind = ErrorKind.Timeout; msg = "час очікування відповіді вичерпано"; }
            else if (r.Status == 401 || r.Status == 403) { kind = ErrorKind.Unauthorized; msg = "ключ недійсний або доступ заборонено (HTTP " + r.Status + ")"; }
            else if (r.Status == 429) { kind = ErrorKind.RateLimited; msg = "перевищено ліміт запитів (HTTP 429)"; }
            else if (r.Status >= 500) { kind = ErrorKind.ServerError; msg = "помилка сервера (HTTP " + r.Status + ")"; }
            else if (r.Status > 0) { kind = ErrorKind.Network; msg = "несподівана відповідь (HTTP " + r.Status + ")"; }
            else { kind = ErrorKind.Network; msg = "немає з'єднання" + (string.IsNullOrEmpty(r.Error) ? "" : ": " + r.Error); }
            var f = SourceReading.Fail(source, kind, msg, nowUtc);
            f.LatencyMs = r.ElapsedMs;
            f.ServerDateUtc = r.ServerDateUtc;
            return f;
        }
    }

    public sealed class AlertsInUaSource : IAlertSource
    {
        const string Url = "https://api.alerts.in.ua/v1/alerts/active.json";
        readonly Func<AppSettings> _cfg;
        readonly IHttp _http;
        readonly Func<DateTime> _now;

        public AlertsInUaSource(Func<AppSettings> cfg, IHttp http, Func<DateTime> utcNow)
        {
            _cfg = cfg;
            _http = http;
            _now = utcNow ?? (() => DateTime.UtcNow);
        }

        public string Name { get { return AlertsInUaParser.SourceName; } }
        public void Reset() { }

        public SourceReading Fetch()
        {
            AppSettings s = _cfg();
            DateTime now = _now();
            if (string.IsNullOrWhiteSpace(s.AlertsInUaToken))
                return SourceReading.Fail(Name, ErrorKind.NotConfigured, "не вказано ключ alerts.in.ua", now);

            var headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer " + s.AlertsInUaToken.Trim() },
                { "Accept", "application/json" }
            };
            HttpResult res = _http.Get(Url, headers, 10000);
            if (!res.Ok) return SourceHelpers.FromHttpFailure(Name, res, now);

            SourceReading r = AlertsInUaParser.Parse(res.Body, s, now);
            r.LatencyMs = res.ElapsedMs;
            r.ServerDateUtc = res.ServerDateUtc;
            return r;
        }
    }
}
