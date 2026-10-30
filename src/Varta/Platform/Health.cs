using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using Varta.Core;

namespace Varta.Platform
{
    public enum HealthState { Ok, Info, Warn, Error }

    public sealed class HealthItem
    {
        public string Id = "";
        public string Title = "";
        public string Message = "";
        public HealthState State;
        public string ActionId = "";
        public string ActionText = "";
    }

    public sealed class HealthContext
    {
        public AppSettings Settings;
        public MonitorService Monitor;
        public AdminNotifier Admin;
        public AutostartInfo Autostart;
        public PlayReport LastPlay;
    }

    /// <summary>Збирає перелік перевірок стану системи для вкладки «Стан».</summary>
    public static class HealthService
    {
        static HealthItem Item(string id, string title, HealthState st, string msg, string action = "", string actionText = "")
        {
            return new HealthItem { Id = id, Title = title, State = st, Message = msg, ActionId = action, ActionText = actionText };
        }

        public static string Ago(DateTime utc)
        {
            TimeSpan d = DateTime.UtcNow - utc;
            if (d.TotalSeconds < 60) return Math.Max(0, (int)d.TotalSeconds) + " с тому";
            return TextUtil.FormatDuration(d) + " тому";
        }

        public static HealthState Worst(IEnumerable<HealthItem> items)
        {
            HealthState w = HealthState.Ok;
            foreach (HealthItem i in items) if (i.State > w) w = i.State;
            return w;
        }

        public static List<HealthItem> Evaluate(HealthContext c)
        {
            var list = new List<HealthItem>();
            AppSettings s = c.Settings;
            MonitorService m = c.Monitor;
            EngineState st = m.Engine.State;
            DateTime now = DateTime.UtcNow;

            // 1. Моніторинг
            if (m.IsPaused)
                list.Add(Item("monitor", "Моніторинг", HealthState.Warn,
                    "Призупинено до " + m.PausedUntilUtc.Value.ToLocalTime().ToString("HH:mm") + " — тривоги не відстежуються!", "resume", "Відновити"));
            else if (m.LastCycleUtc == DateTime.MinValue)
                list.Add(Item("monitor", "Моніторинг", HealthState.Info, "Запускається…"));
            else if ((now - m.LastCycleUtc).TotalSeconds > s.PollIntervalSeconds * 3 + 30)
                list.Add(Item("monitor", "Моніторинг", HealthState.Error,
                    "Опитування не відповідає вже " + TextUtil.FormatDuration(now - m.LastCycleUtc) + ". Спробуйте «Перевірити зараз» або перезапустіть програму.", "refresh", "Перевірити зараз"));
            else
                list.Add(Item("monitor", "Моніторинг", HealthState.Ok, "Працює: перевірка кожні " + s.PollIntervalSeconds + " с."));

            // 2. Мережа
            bool net = true;
            try { net = NetworkInterface.GetIsNetworkAvailable(); } catch { }
            list.Add(net
                ? Item("network", "Інтернет", HealthState.Ok, "Мережеве підключення є.")
                : Item("network", "Інтернет", HealthState.Error, "Немає мережевого підключення — перевірте кабель/Wi-Fi."));

            // 3. Дані
            if (!m.IsPaused)
            {
                if (st.ConnectionLost)
                    list.Add(Item("data", "Дані про тривоги", HealthState.Error,
                        "Немає зв'язку з джерелами: " + st.LastError + ". Стан тривоги НЕВІДОМИЙ.", "refresh", "Перевірити зараз"));
                else if (st.ConsecutiveFailures > 0)
                    list.Add(Item("data", "Дані про тривоги", HealthState.Warn,
                        "Остання спроба невдала: " + st.LastError + " (" + st.ConsecutiveFailures + " з " + s.FailuresBeforeLost + ").", "refresh", "Перевірити зараз"));
                else if (!st.LastOkUtc.HasValue)
                    list.Add(Item("data", "Дані про тривоги", HealthState.Info, "Очікування першої відповіді від джерела…"));
                else if (m.Engine.IsStale())
                    list.Add(Item("data", "Дані про тривоги", HealthState.Warn, "Дані застаріли: остання успішна перевірка " + Ago(st.LastOkUtc.Value) + "."));
                else
                    list.Add(Item("data", "Дані про тривоги", HealthState.Ok,
                        "Джерело «" + st.Source + "», оновлено " + Ago(st.LastOkUtc.Value) + "."));
            }

            // 4. Джерела
            Dictionary<string, SourceReading> rd = m.GetSourceReadings();
            SourceReading aiu;
            rd.TryGetValue(AlertsInUaParser.SourceName, out aiu);
            if (!s.UseAlertsInUa)
                list.Add(Item("src-aiu", "alerts.in.ua", HealthState.Info, "Вимкнено в налаштуваннях."));
            else if (string.IsNullOrWhiteSpace(s.AlertsInUaToken))
                list.Add(Item("src-aiu", "alerts.in.ua", HealthState.Info,
                    "Ключ не вказано: рівні «жовтий/червоний» недоступні, використовуються Telegram-канали. Безкоштовний ключ: alerts.in.ua/api-request",
                    "goto-sources", "Вказати ключ"));
            else if (aiu == null)
                list.Add(Item("src-aiu", "alerts.in.ua", HealthState.Info, "Ще не опитувалось."));
            else if (aiu.Ok)
                list.Add(Item("src-aiu", "alerts.in.ua", HealthState.Ok, "Підключено, відповідь за " + aiu.LatencyMs + " мс."));
            else
                list.Add(Item("src-aiu", "alerts.in.ua", aiu.ErrorKind == ErrorKind.Unauthorized ? HealthState.Error : HealthState.Warn,
                    (aiu.ErrorKind == ErrorKind.Unauthorized ? "Ключ недійсний або прострочений. " : "Недоступне: ") + aiu.Error, "goto-sources", "Налаштувати"));

            if (!s.UseTelegram)
                list.Add(Item("src-tg", "Telegram-канали", HealthState.Info, "Вимкнено в налаштуваннях."));
            else
            {
                List<ChannelStatus> chs = m.Telegram.GetChannelStatus();
                if (chs.Count == 0)
                    list.Add(Item("src-tg", "Telegram-канали", HealthState.Info,
                        aiu != null && aiu.Ok ? "Резервне джерело: не потрібне, поки працює alerts.in.ua." : "Ще не опитувалось."));
                else
                {
                    int ok = 0;
                    var parts = new List<string>();
                    foreach (ChannelStatus ch in chs)
                    {
                        if (ch.Ok) ok++;
                        parts.Add("@" + ch.Channel + ": " + (ch.Ok
                            ? "остання публікація " + (ch.NewestMessageUtc.HasValue ? Ago(ch.NewestMessageUtc.Value) : "—")
                            : ch.Error));
                    }
                    HealthState hs = ok == chs.Count ? HealthState.Ok : (ok > 0 ? HealthState.Warn : HealthState.Error);
                    list.Add(Item("src-tg", "Telegram-канали", hs, string.Join("; ", parts.ToArray()), hs == HealthState.Ok ? "" : "goto-sources", hs == HealthState.Ok ? "" : "Налаштувати"));
                }
            }

            // 5. Годинник
            SourceReading any = m.LastReading;
            if (any != null && any.ServerDateUtc.HasValue)
            {
                double drift = (any.ServerDateUtc.Value - any.FetchedUtc).TotalSeconds;
                if (Math.Abs(drift) > 120)
                    list.Add(Item("clock", "Годинник ПК", HealthState.Warn,
                        "Час на комп'ютері " + (drift > 0 ? "відстає" : "поспішає") + " приблизно на " + TextUtil.FormatDuration(TimeSpan.FromSeconds(Math.Abs(drift))) +
                        ". Синхронізуйте час у налаштуваннях Windows."));
                else
                    list.Add(Item("clock", "Годинник ПК", HealthState.Ok, "Час синхронізовано (відхилення " + (int)Math.Abs(drift) + " с)."));
            }

            // 6. Звук
            list.AddRange(AudioItems(s, m.Audio));
            PlayReport lp = c.LastPlay;
            if (lp != null && !lp.Cancelled)
            {
                string when = lp.TimeLocal.ToString("HH:mm:ss");
                string what = lp.Preview ? "прослуховування" : (lp.IsTest ? "тест" : "оголошення");
                if (lp.Ok)
                    list.Add(Item("audio-last", "Останнє озвучення", HealthState.Ok, when + " • " + what + " • «" + lp.FileName + "» • " + lp.Seconds + " • " + lp.Backend));
                else
                    list.Add(Item("audio-last", "Останнє озвучення", HealthState.Error,
                        when + " • " + what + " • «" + lp.FileName + "» не відтворено: " + lp.Error, "goto-sound", "Перевірити звук"));
            }

            // 7. Автозапуск
            AutostartInfo a = c.Autostart;
            if (a != null)
            {
                if (!string.IsNullOrEmpty(a.Error))
                    list.Add(Item("autostart", "Автозапуск", HealthState.Warn, "Не вдалося перевірити: " + a.Error));
                else if (!a.Enabled)
                    list.Add(Item("autostart", "Автозапуск", HealthState.Warn,
                        "Вимкнено — після перезавантаження ПК програма не запуститься сама.", "autostart-on", "Увімкнути"));
                else if (a.DisabledInTaskManager)
                    list.Add(Item("autostart", "Автозапуск", HealthState.Warn,
                        "Увімкнено, але вимкнено у «Диспетчері завдань → Автозавантаження».", "autostart-on", "Виправити"));
                else if (!a.PathMatches)
                    list.Add(Item("autostart", "Автозапуск", HealthState.Warn,
                        "Шлях у запису автозапуску застарів (папку переносили).", "autostart-on", "Виправити"));
                else
                    list.Add(Item("autostart", "Автозапуск", HealthState.Ok, "Програма запускається разом із Windows."));
            }

            // 8. Адміністратор
            if (!s.AdminEnabled)
                list.Add(Item("admin", "Telegram-адміністратор", HealthState.Info, "Не підключено (необов'язково)."));
            else if (!c.Admin.IsConfigured(s))
                list.Add(Item("admin", "Telegram-адміністратор", HealthState.Warn, "Увімкнено, але не вказано токен бота або ID чату.", "goto-admin", "Налаштувати"));
            else if (c.Admin.LastAttemptUtc.HasValue && !c.Admin.LastOk)
                list.Add(Item("admin", "Telegram-адміністратор", HealthState.Warn, "Останнє повідомлення не надіслано: " + c.Admin.LastResult, "goto-admin", "Налаштувати"));
            else
                list.Add(Item("admin", "Telegram-адміністратор", HealthState.Ok,
                    c.Admin.LastAttemptUtc.HasValue ? "Останнє повідомлення надіслано " + Ago(c.Admin.LastAttemptUtc.Value) + "." : "Підключено."));

            // 9. Папка даних
            if (!Paths.IsPortableFolder)
                list.Add(Item("data-dir", "Папка даних", HealthState.Info,
                    "Папка програми захищена від запису — налаштування та журнал зберігаються в " + Paths.DataDir));

            return list;
        }

        static IEnumerable<HealthItem> AudioItems(AppSettings s, AudioStatus a)
        {
            var res = new List<HealthItem>();
            if (!s.VoiceEnabled)
            {
                res.Add(Item("audio", "Голосові оголошення", HealthState.Info, "Вимкнено в налаштуваннях — діють лише банер і текстові сповіщення.", "goto-sound", "Увімкнути"));
                return res;
            }

            var missing = new List<string>();
            var used = new List<string>();
            AddFile(s, AlertEventKind.RedStarted, "Червоний", s.SoundRed, used, missing);
            AddFile(s, AlertEventKind.YellowStarted, "Жовтий", s.SoundYellow, used, missing);
            AddFile(s, AlertEventKind.YellowFromRed, "Зниження", s.SoundYellowFromRed, used, missing);
            AddFile(s, AlertEventKind.AllClear, "Відбій", s.SoundClear, used, missing);
            if (missing.Count > 0)
                res.Add(Item("audio-files", "Файли оголошень", HealthState.Error,
                    "Не знайдено: " + string.Join(", ", missing.ToArray()) + ". Замість голосу прозвучить резервний сигнал.", "goto-sound", "Вказати файли"));
            else
                res.Add(Item("audio-files", "Файли оголошень", HealthState.Ok, JoinTwoPerLine(used)));

            if (!string.IsNullOrEmpty(a.Error) && !a.DeviceAvailable)
                res.Add(Item("audio-dev", "Пристрій звуку", HealthState.Warn, "Не вдалося опитати звукову підсистему: " + a.Error));
            else if (!a.DeviceAvailable)
                res.Add(Item("audio-dev", "Пристрій звуку", HealthState.Error, "Не знайдено активного пристрою виводу звуку (колонки/навушники)."));
            else
            {
                string vol = "Гучність Windows " + (int)Math.Round(a.MasterVolume * 100) + "%" + (a.Muted ? ", звук вимкнено (mute)" : "") + ".";
                bool low = a.Muted || a.MasterVolume * 100 < 10;
                if (low && !s.BoostMasterVolume)
                    res.Add(Item("audio-dev", "Пристрій звуку", HealthState.Warn, vol + " Оголошення можуть бути не чутні — увімкніть автопідняття гучності.", "goto-sound", "Налаштувати"));
                else if (low)
                    res.Add(Item("audio-dev", "Пристрій звуку", HealthState.Info, vol + " Під час тривоги гучність буде піднято автоматично."));
                else
                    res.Add(Item("audio-dev", "Пристрій звуку", HealthState.Ok, vol));
            }
            return res;
        }

        /// <summary>По два файли на рядок — щоб довгі назви не обрізались.</summary>
        static string JoinTwoPerLine(List<string> items)
        {
            var lines = new List<string>();
            for (int i = 0; i < items.Count; i += 2)
                lines.Add(i + 1 < items.Count ? items[i] + " • " + items[i + 1] : items[i]);
            return string.Join("\n", lines.ToArray());
        }

        static void AddFile(AppSettings s, AlertEventKind k, string title, string stored, List<string> used, List<string> missing)
        {
            if ((RoutePolicy.Get(s, k) & Channel.Voice) == 0) return;
            string full = Paths.Resolve(stored);
            string name = string.IsNullOrWhiteSpace(stored) ? "(не вказано)" : Path.GetFileName(full);
            used.Add(title + " — " + name);
            if (string.IsNullOrWhiteSpace(stored) || !File.Exists(full)) missing.Add(title + " — " + name);
        }
    }
}
