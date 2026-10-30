using System;
using System.Collections.Generic;

namespace Varta.Core
{
    /// <summary>Яка подія якими каналами сповіщається (голос / банер / спливаюче / Telegram-адміну).</summary>
    public static class RoutePolicy
    {
        public static readonly AlertEventKind[] Order = new[]
        {
            AlertEventKind.RedStarted, AlertEventKind.RedFromYellow, AlertEventKind.YellowStarted,
            AlertEventKind.YellowFromRed, AlertEventKind.AllClear, AlertEventKind.RedReminder,
            AlertEventKind.ConnectionLost, AlertEventKind.ConnectionRestored, AlertEventKind.AudioProblem,
            AlertEventKind.ConfigProblem, AlertEventKind.AppStarted, AlertEventKind.MonitoringPaused,
            AlertEventKind.MonitoringResumed, AlertEventKind.DailyReport
        };

        public static bool VoiceCapable(AlertEventKind k)
        {
            switch (k)
            {
                case AlertEventKind.RedStarted:
                case AlertEventKind.RedFromYellow:
                case AlertEventKind.YellowStarted:
                case AlertEventKind.YellowFromRed:
                case AlertEventKind.AllClear:
                case AlertEventKind.RedReminder:
                    return true;
                default:
                    return false;
            }
        }

        public static Channel DefaultFor(AlertEventKind k)
        {
            const Channel all = Channel.Voice | Channel.Banner | Channel.Tray | Channel.Admin;
            switch (k)
            {
                case AlertEventKind.RedStarted:
                case AlertEventKind.RedFromYellow:
                case AlertEventKind.YellowStarted:
                case AlertEventKind.YellowFromRed:
                case AlertEventKind.AllClear:
                    return all;
                case AlertEventKind.RedReminder: return Channel.Voice | Channel.Tray;
                case AlertEventKind.ConnectionLost:
                case AlertEventKind.ConnectionRestored:
                case AlertEventKind.AudioProblem:
                case AlertEventKind.ConfigProblem:
                case AlertEventKind.MonitoringPaused:
                case AlertEventKind.MonitoringResumed:
                    return Channel.Tray | Channel.Admin;
                case AlertEventKind.AppStarted:
                case AlertEventKind.DailyReport:
                    return Channel.Admin;
                default:
                    return Channel.None;
            }
        }

        public static Dictionary<string, string> Defaults()
        {
            var d = new Dictionary<string, string>();
            foreach (AlertEventKind k in Order) d[k.ToString()] = ToText(DefaultFor(k));
            return d;
        }

        public static Channel Get(AppSettings s, AlertEventKind k)
        {
            string t;
            if (s.Routes != null && s.Routes.TryGetValue(k.ToString(), out t)) return FromText(t);
            return DefaultFor(k);
        }

        public static void Set(AppSettings s, AlertEventKind k, Channel c)
        {
            if (!VoiceCapable(k)) c &= ~Channel.Voice;
            s.Routes[k.ToString()] = ToText(c);
        }

        public static string ToText(Channel c)
        {
            var parts = new List<string>();
            if ((c & Channel.Voice) != 0) parts.Add("voice");
            if ((c & Channel.Banner) != 0) parts.Add("banner");
            if ((c & Channel.Tray) != 0) parts.Add("tray");
            if ((c & Channel.Admin) != 0) parts.Add("admin");
            return string.Join(",", parts.ToArray());
        }

        public static Channel FromText(string t)
        {
            Channel c = Channel.None;
            if (string.IsNullOrEmpty(t)) return c;
            foreach (string p in t.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                switch (p.Trim().ToLowerInvariant())
                {
                    case "voice": c |= Channel.Voice; break;
                    case "banner": c |= Channel.Banner; break;
                    case "tray": c |= Channel.Tray; break;
                    case "admin": c |= Channel.Admin; break;
                }
            }
            return c;
        }

        public static string Title(AlertEventKind k)
        {
            switch (k)
            {
                case AlertEventKind.RedStarted: return "Червоний рівень: повітряна тривога розпочалась";
                case AlertEventKind.RedFromYellow: return "Підвищення рівня: жовтий → червоний";
                case AlertEventKind.YellowStarted: return "Жовтий рівень: підвищена загроза";
                case AlertEventKind.YellowFromRed: return "Зниження рівня: червоний → жовтий";
                case AlertEventKind.AllClear: return "Відбій тривоги (зелений рівень)";
                case AlertEventKind.RedReminder: return "Нагадування, поки триває тривога";
                case AlertEventKind.ConnectionLost: return "Втрачено зв'язок із джерелом даних";
                case AlertEventKind.ConnectionRestored: return "Зв'язок із джерелом відновлено";
                case AlertEventKind.AudioProblem: return "Проблема зі звуком або файлами";
                case AlertEventKind.ConfigProblem: return "Проблема з налаштуваннями";
                case AlertEventKind.AppStarted: return "Програму запущено";
                case AlertEventKind.MonitoringPaused: return "Моніторинг призупинено";
                case AlertEventKind.MonitoringResumed: return "Моніторинг відновлено";
                case AlertEventKind.DailyReport: return "Щоденний звіт «усе працює»";
                default: return k.ToString();
            }
        }
    }
}
