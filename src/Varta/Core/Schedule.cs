using System;
using System.Text.RegularExpressions;

namespace Varta.Core
{
    public static class Schedule
    {
        public static readonly string[] AllDays = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        public static bool TryParseTime(string s, out TimeSpan t)
        {
            t = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(s)) return false;
            Match m = Regex.Match(s.Trim(), @"^([01]?\d|2[0-3]):([0-5]\d)$");
            if (!m.Success) return false;
            t = new TimeSpan(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), 0);
            return true;
        }

        public static bool IsTime(string s) { TimeSpan t; return TryParseTime(s, out t); }

        public static bool DayEnabled(string days, DayOfWeek d)
        {
            if (string.IsNullOrEmpty(days)) return false;
            string abbr = d.ToString().Substring(0, 3);
            foreach (string tok in days.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (string.Equals(tok.Trim(), abbr, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Чи можна зараз озвучувати оголошення (головний перемикач + розклад).</summary>
        public static bool VoiceAllowed(AppSettings s, DateTime localNow)
        {
            if (!s.VoiceEnabled) return false;
            if (!s.VoiceOnlyInWorkHours) return true;
            bool today = DayEnabled(s.WorkDays, localNow.DayOfWeek);
            TimeSpan start, end;
            if (!TryParseTime(s.WorkStart, out start) || !TryParseTime(s.WorkEnd, out end) || start == end) return today;
            TimeSpan t = localNow.TimeOfDay;
            if (start < end) return today && t >= start && t < end;
            // Зміна через північ: час після півночі належить до зміни, що почалася напередодні.
            if (t >= start) return today;
            return t < end && DayEnabled(s.WorkDays, localNow.AddDays(-1).DayOfWeek);
        }

        public static bool DailyReportDue(AppSettings s, DateTime localNow, DateTime lastSentDate)
        {
            if (!s.AdminEnabled || !s.AdminDailyReport) return false;
            TimeSpan at;
            if (!TryParseTime(s.AdminDailyReportTime, out at)) return false;
            return localNow.TimeOfDay >= at && lastSentDate.Date != localNow.Date;
        }
    }
}
