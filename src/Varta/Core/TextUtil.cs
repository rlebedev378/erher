using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Varta.Core
{
    public static class TextUtil
    {
        public static bool ContainsIC(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>true, якщо haystack містить будь-яке зі слів needles (через «|»).</summary>
        public static bool ContainsAnyIC(string haystack, string needles)
        {
            foreach (string n in SplitKeywords(needles)) if (ContainsIC(haystack, n)) return true;
            return false;
        }

        public static List<string> SplitKeywords(string s)
        {
            var res = new List<string>();
            if (string.IsNullOrWhiteSpace(s)) return res;
            foreach (string p in s.Split(new[] { '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = p.Trim();
                if (t.Length > 0) res.Add(t);
            }
            return res;
        }

        /// <summary>Список каналів: допускаються @name, t.me/name, https://t.me/s/name; роздільники — кома, пробіл, новий рядок.</summary>
        public static List<string> SplitChannels(string s)
        {
            var res = new List<string>();
            if (string.IsNullOrWhiteSpace(s)) return res;
            foreach (string p in s.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = p.Trim();
                t = Regex.Replace(t, @"^(https?://)?(www\.)?t\.me/(s/)?", "", RegexOptions.IgnoreCase);
                t = t.TrimStart('@').Trim('/');
                int q = t.IndexOfAny(new[] { '?', '/' });
                if (q > 0) t = t.Substring(0, q);
                if (Regex.IsMatch(t, @"^[A-Za-z0-9_]{3,64}$") && !res.Contains(t, StringComparer.OrdinalIgnoreCase)) res.Add(t);
            }
            return res;
        }

        static bool Contains(this List<string> l, string v, StringComparer c)
        {
            foreach (string x in l) if (c.Equals(x, v)) return true;
            return false;
        }

        /// <summary>Нормалізація для пошуку: нижній регістр, апострофи, «м. », підкреслення з хештегів, зайві пробіли.</summary>
        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = s.ToLowerInvariant();
            t = t.Replace('’', '\'').Replace('ʼ', '\'').Replace('`', '\'').Replace('\u00a0', ' ').Replace('\u3164', ' ').Replace('_', ' ');
            t = Regex.Replace(t, @"м\.\s*", "м.");
            t = Regex.Replace(t, @"\s+", " ").Trim();
            return t;
        }

        /// <summary>Пошук цілого виразу (з межами слів) у нормалізованому тексті.</summary>
        public static bool ContainsPhrase(string normText, string keyword)
        {
            string k = Norm(keyword);
            if (k.Length == 0 || string.IsNullOrEmpty(normText)) return false;
            int from = 0;
            while (true)
            {
                int i = normText.IndexOf(k, from, StringComparison.Ordinal);
                if (i < 0) return false;
                bool leftOk = i == 0 || !char.IsLetterOrDigit(normText[i - 1]);
                int end = i + k.Length;
                bool rightOk = end >= normText.Length || !char.IsLetterOrDigit(normText[end]);
                if (leftOk && rightOk) return true;
                from = i + 1;
            }
        }

        public static bool ContainsAnyPhrase(string normText, string keywords)
        {
            foreach (string k in SplitKeywords(keywords)) if (ContainsPhrase(normText, k)) return true;
            return false;
        }

        public static string HtmlToText(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            string t = Regex.Replace(html, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"<[^>]+>", " ");
            t = WebUtility.HtmlDecode(t);
            t = t.Replace('\u3164', ' ').Replace('\u00a0', ' ');
            t = Regex.Replace(t, @"\s+", " ").Trim();
            return t;
        }

        /// <summary>Короткий опис події з тексту повідомлення каналу.</summary>
        public static string ShortDetail(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string t = text;
            int cut = t.IndexOf("Надіслати новину", StringComparison.OrdinalIgnoreCase);
            if (cut >= 0) t = t.Substring(0, cut);
            t = Regex.Replace(t, @"#\S+", " ");
            t = t.Replace("Слідкуйте за подальшими повідомленнями.", " ");
            t = StripSymbols(t);
            t = Regex.Replace(t, @"^\W*\d{1,2}:\d{2}\s*", "");
            t = Regex.Replace(t, @"\s+", " ").Trim();
            return Truncate(t, 140);
        }

        /// <summary>Прибирає емодзі та піктограми, які не відображаються у звичайних елементах Windows.</summary>
        public static string StripSymbols(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsSurrogate(c) || c == '\uFE0F' || c == '\u200D') continue;
                UnicodeCategory cat = char.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.OtherSymbol || cat == UnicodeCategory.ModifierSymbol) continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, max - 1).TrimEnd() + "…";
        }

        public static string FormatDuration(TimeSpan d)
        {
            if (d < TimeSpan.Zero) d = TimeSpan.Zero;
            if (d.TotalSeconds < 60) return "менше хвилини";
            if (d.TotalHours < 1) return ((int)d.TotalMinutes) + " хв";
            if (d.TotalDays < 1) return ((int)d.TotalHours) + " год " + d.Minutes.ToString("00") + " хв";
            return ((int)d.TotalDays) + " д " + d.Hours + " год";
        }

        public static DateTime? ParseIsoUtc(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            DateTimeOffset dto;
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dto))
                return dto.UtcDateTime;
            return null;
        }
    }
}
