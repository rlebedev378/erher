using System;
using System.Text;

namespace Varta.Core
{
    public sealed class Notice
    {
        public AlertEventKind Kind;
        public AlertLevel Level = AlertLevel.Unknown;
        public string Title = "";
        public string Body = "";
        public string Emoji = "";
        public string Source = "";
        public DateTime LocalTime;
        public bool IsTest;
        public bool IsTechnical;
        public bool IsInfo;
    }

    public static class Lang
    {
        public static string LevelTitle(AlertLevel l)
        {
            switch (l)
            {
                case AlertLevel.Red: return "ЧЕРВОНИЙ РІВЕНЬ";
                case AlertLevel.Yellow: return "ЖОВТИЙ РІВЕНЬ";
                case AlertLevel.Clear: return "ТРИВОГИ НЕМАЄ";
                default: return "СТАН НЕВІДОМИЙ";
            }
        }

        public static string LevelSubtitle(AlertLevel l)
        {
            switch (l)
            {
                case AlertLevel.Red: return "Повітряна тривога — пройдіть в укриття";
                case AlertLevel.Yellow: return "Підвищена загроза — будьте уважні та готові до укриття";
                case AlertLevel.Clear: return "Зелений рівень — відбій, працюємо у звичайному режимі";
                default: return "Немає свіжих даних від джерел — перевірте з'єднання";
            }
        }

        public static string LevelShort(AlertLevel l)
        {
            switch (l)
            {
                case AlertLevel.Red: return "червоний";
                case AlertLevel.Yellow: return "жовтий";
                case AlertLevel.Clear: return "зелений";
                default: return "невідомо";
            }
        }
    }

    public static class Messages
    {
        public static Notice Compose(EngineEvent e, AppSettings s, DateTime localTime, string pcName)
        {
            var n = new Notice { Kind = e.Kind, LocalTime = localTime, Source = e.Source ?? "", IsTest = e.IsTest };
            switch (e.Kind)
            {
                case AlertEventKind.RedStarted:
                    Alert(n, AlertLevel.Red, "🔴", s.TextRedTitle, s.TextRedBody, s, e); break;
                case AlertEventKind.RedFromYellow:
                    Alert(n, AlertLevel.Red, "🔴", s.TextRedFromYellowTitle, s.TextRedFromYellowBody, s, e); break;
                case AlertEventKind.YellowStarted:
                    Alert(n, AlertLevel.Yellow, "🟡", s.TextYellowTitle, s.TextYellowBody, s, e); break;
                case AlertEventKind.YellowFromRed:
                    Alert(n, AlertLevel.Yellow, "🟡", s.TextYellowFromRedTitle, s.TextYellowFromRedBody, s, e); break;
                case AlertEventKind.AllClear:
                    Alert(n, AlertLevel.Clear, "🟢", s.TextClearTitle, s.TextClearBody, s, e); break;
                case AlertEventKind.RedReminder:
                    Alert(n, AlertLevel.Red, "🔴", s.TextRedTitle + " (нагадування)", s.TextRedBody, s, e); break;
                case AlertEventKind.ConnectionLost:
                    Tech(n, "⚠️", "Втрачено зв'язок із джерелом даних", e.Message); break;
                case AlertEventKind.ConnectionRestored:
                    Tech(n, "✅", "Зв'язок із джерелом відновлено", "Перерва тривала " + TextUtil.FormatDuration(e.Downtime) + "."); n.IsTechnical = false; n.IsInfo = true; break;
                case AlertEventKind.AudioProblem:
                    Tech(n, "🔇", "Проблема зі звуком", e.Message); break;
                case AlertEventKind.ConfigProblem:
                    Tech(n, "⚙️", "Проблема з налаштуваннями", e.Message); break;
                case AlertEventKind.AppStarted:
                    Tech(n, "ℹ️", "ВАРТУ запущено", e.Message); n.IsTechnical = false; n.IsInfo = true; break;
                case AlertEventKind.MonitoringPaused:
                    Tech(n, "⏸", "Моніторинг призупинено", e.Message); break;
                case AlertEventKind.MonitoringResumed:
                    Tech(n, "▶️", "Моніторинг відновлено", e.Message); n.IsTechnical = false; n.IsInfo = true; break;
                case AlertEventKind.DailyReport:
                    Tech(n, "📋", "Щоденний звіт: усе працює", e.Message); n.IsTechnical = false; n.IsInfo = true; break;
            }
            if (e.IsInitial && n.Level != AlertLevel.Unknown && n.Level != AlertLevel.Clear)
                n.Body += " (стан на момент запуску програми)";
            if (e.IsTest) n.Title = "ТЕСТ: " + n.Title;
            return n;
        }

        static void Alert(Notice n, AlertLevel lvl, string emoji, string title, string body, AppSettings s, EngineEvent e)
        {
            n.Level = lvl;
            n.Emoji = emoji;
            n.Title = Fill(title, s, e, n.LocalTime);
            n.Body = Fill(body, s, e, n.LocalTime);
        }

        static void Tech(Notice n, string emoji, string title, string body)
        {
            n.IsTechnical = true;
            n.Emoji = emoji;
            n.Title = title;
            n.Body = body ?? "";
        }

        public static string Fill(string tpl, AppSettings s, EngineEvent e, DateTime local)
        {
            if (string.IsNullOrEmpty(tpl)) return "";
            return tpl.Replace("{city}", s.CityDisplayName ?? "")
                      .Replace("{time}", local.ToString("HH:mm"))
                      .Replace("{detail}", e != null ? (e.Detail ?? "") : "")
                      .Replace("{org}", s.OrganizationName ?? "");
        }

        /// <summary>Текст для Telegram-повідомлення адміністратору.</summary>
        public static string AdminText(Notice n, string pcName)
        {
            var sb = new StringBuilder();
            if (n.IsTest) sb.Append("🧪 ");
            sb.Append(n.Emoji).Append(' ').Append(n.Title);
            if (!string.IsNullOrEmpty(n.Body)) sb.Append('\n').Append(n.Body);
            sb.Append("\n🕒 ").Append(n.LocalTime.ToString("dd.MM HH:mm:ss"));
            if (!string.IsNullOrEmpty(pcName)) sb.Append(" • ").Append(pcName);
            if (!string.IsNullOrEmpty(n.Source)) sb.Append(" • ").Append(n.Source);
            return sb.ToString();
        }
    }
}
