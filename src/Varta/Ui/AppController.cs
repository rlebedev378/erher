using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Varta.Core;
using Varta.Platform;

namespace Varta.Ui
{
    /// <summary>Кореневий об'єкт програми: з'єднує джерела даних, звук, банер, трей і адміністратора.</summary>
    public sealed class AppController : IDisposable
    {
        readonly Control _marshal;
        readonly System.Windows.Forms.Timer _tick = new System.Windows.Forms.Timer { Interval = 1000 };
        DateTime _lastDailyReport = DateTime.MinValue;
        DateTime _alarmDay = DateTime.Today;
        int _alarmsToday;
        bool _disposed;

        public AppSettings Settings { get; private set; }
        public readonly IHttp Http = new HttpFetcher();
        public readonly MonitorService Monitor;
        public readonly AudioService Audio;
        public readonly AdminNotifier Admin;
        public readonly BannerManager Banners = new BannerManager();
        public TrayController Tray;
        public readonly List<string> StartupWarnings;
        public readonly bool FirstRun;
        public PlayReport LastPlay;
        /// <summary>Показувати вікно, якщо голос у ТЕСТІ не відтворився (вимикається лише в автоматичних перевірках).</summary>
        public static bool ShowTestFailureDialog = true;

        public event Action SettingsApplied;
        public event Action StateChanged;

        public AppController(AppSettings settings, List<string> warnings, bool settingsExisted)
        {
            _marshal = new Control();
            _marshal.CreateControl();
            IntPtr force = _marshal.Handle;

            Settings = settings;
            StartupWarnings = warnings;
            FirstRun = !settingsExisted || !settings.FirstRunDone;

            Audio = new AudioService();
            Audio.Problem += msg => OnUi(delegate { RaiseTech(AlertEventKind.AudioProblem, msg); });
            Audio.Reported += rep => OnUi(delegate { OnPlayReport(rep); });
            Admin = new AdminNotifier(Http, () => Settings);
            Monitor = new MonitorService(() => Settings, Http);
            Monitor.EventRaised += e => OnUi(delegate { Dispatch(e); });
            Monitor.Updated += () => OnUi(delegate { var h = StateChanged; if (h != null) h(); });
            _tick.Tick += delegate { OnTick(); };
        }

        void OnUi(Action a)
        {
            if (_disposed) return;
            try
            {
                if (_marshal.InvokeRequired) _marshal.BeginInvoke(a);
                else a();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        public void Start()
        {
            Log.Info("ВАРТА " + AppInfo.Version + " запущено. Папка: " + Paths.AppDir);
            foreach (string w in StartupWarnings) Log.Warn(w);
            Log.Cleanup(Settings.LogRetentionDays);
            Monitor.Start();
            _tick.Start();
            PreloadSounds();
            RaiseTech(AlertEventKind.AppStarted, "Комп'ютер: " + PcName() + ". Версія " + AppInfo.Version + ". Опитування кожні " + Settings.PollIntervalSeconds + " с.");
        }

        public string PcName()
        {
            return string.IsNullOrWhiteSpace(Settings.AdminPcName) ? Environment.MachineName : Settings.AdminPcName;
        }

        // ---------- Сповіщення ----------

        public void Dispatch(EngineEvent e)
        {
            AppSettings s = Settings;
            Notice n = Messages.Compose(e, s, e.AtUtc.ToLocalTime(), PcName());
            Channel route = RoutePolicy.Get(s, e.Kind);

            string line = (n.IsTest ? "[ТЕСТ] " : "") + n.Title + (string.IsNullOrEmpty(n.Body) ? "" : " — " + n.Body);
            if (n.IsTechnical) Log.Warn(line);
            else if (n.Level == AlertLevel.Red || n.Level == AlertLevel.Yellow) Log.Alert(line);
            else Log.Info(line);

            if (e.Kind == AlertEventKind.RedStarted && !e.IsTest)
            {
                if (_alarmDay != DateTime.Today) { _alarmDay = DateTime.Today; _alarmsToday = 0; }
                _alarmsToday++;
            }

            try
            {
                if (e.Kind == AlertEventKind.AllClear) Banners.Hide();
                if ((route & Channel.Banner) != 0) Banners.Show(n, s);
                if ((route & Channel.Tray) != 0 && Tray != null) Tray.Balloon(n);
                if ((route & Channel.Voice) != 0) PlayVoice(e.Kind, s, e.IsTest);
                if ((route & Channel.Admin) != 0 && (!e.IsTest || s.TestsGoToAdmin)) Admin.Send(Messages.AdminText(n, PcName()));
            }
            catch (Exception ex) { Log.Error("Збій під час показу сповіщення", ex); }

            var h = StateChanged;
            if (h != null) h();
        }

        void PlayVoice(AlertEventKind k, AppSettings s, bool isTest)
        {
            if (!Schedule.VoiceAllowed(s, DateTime.Now))
            {
                Log.Info("Голосове оголошення пропущено: голос вимкнено або зараз поза робочим часом.");
                return;
            }
            string stored;
            int repeat;
            switch (k)
            {
                case AlertEventKind.RedStarted:
                case AlertEventKind.RedFromYellow: stored = s.SoundRed; repeat = s.RepeatRed; break;
                case AlertEventKind.YellowStarted: stored = s.SoundYellow; repeat = s.RepeatOther; break;
                case AlertEventKind.YellowFromRed: stored = s.SoundYellowFromRed; repeat = s.RepeatOther; break;
                case AlertEventKind.AllClear: stored = s.SoundClear; repeat = s.RepeatOther; break;
                case AlertEventKind.RedReminder: stored = string.IsNullOrWhiteSpace(s.SoundReminder) ? s.SoundRed : s.SoundReminder; repeat = 1; break;
                default: return;
            }
            string file = Paths.Resolve(stored);
            Log.Info("Голос: " + RoutePolicy.Title(k) + " → " + Path.GetFileName(file) +
                     (File.Exists(file) ? "" : " (ФАЙЛ НЕ ЗНАЙДЕНО: " + file + ")") + ", повторів: " + repeat);
            AnnounceRequest req = MakeRequest(file, repeat, s, true, k.ToString());
            req.IsTest = isTest;
            Audio.Announce(req);
        }

        void PreloadSounds()
        {
            AppSettings s = Settings;
            var files = new List<string>();
            if (s.VoiceEnabled)
                foreach (string st in new[] { s.SoundRed, s.SoundYellow, s.SoundYellowFromRed, s.SoundClear, s.SoundReminder })
                    if (!string.IsNullOrWhiteSpace(st)) files.Add(Paths.Resolve(st));
            Audio.Preload(files);
        }

        void OnPlayReport(PlayReport rep)
        {
            if (rep.Cancelled) return;
            LastPlay = rep;
            string kind = rep.Preview ? "прослуховування" : (rep.IsTest ? "тест" : "оголошення");
            if (rep.Ok) Log.Info("Озвучено (" + kind + "): «" + rep.FileName + "» — " + rep.Backend + ", " + rep.Seconds);
            else if (rep.Preview) Log.Warn("Не вдалося відтворити «" + rep.FileName + "»: " + rep.Error);

            if (!rep.Ok && rep.IsTest && ShowTestFailureDialog)
                MessageBox.Show("Тест: голос не вдалося відтворити — прозвучав лише резервний сигнал.\n\nФайл: " + rep.FileName + "\nПричина: " + rep.Error +
                                "\n\nПеревірте файл у вкладці «Звук» (кнопка «▶ Прослухати») або запустіть «Система → Діагностика».",
                    "ВАРТА", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            var h = StateChanged;
            if (h != null) h();
        }

        static AnnounceRequest MakeRequest(string file, int repeat, AppSettings s, bool duck, string tag)
        {
            return new AnnounceRequest
            {
                File = file,
                Repeat = repeat,
                GapSeconds = s.RepeatGapSeconds,
                VolumePercent = s.AnnounceVolumePercent,
                Duck = duck && s.DuckMusic,
                KeepRatio = s.MusicDuckPercent / 100f,
                Boost = duck && s.BoostMasterVolume,
                MinMaster = s.MinMasterVolumePercent / 100f,
                Unmute = s.UnmuteMaster,
                Tag = tag
            };
        }

        public void RaiseTest(AlertEventKind kind)
        {
            var e = new EngineEvent
            {
                Kind = kind,
                AtUtc = DateTime.UtcNow,
                IsTest = true,
                Source = "тест",
                Detail = "Це перевірка системи оповіщення"
            };
            Dispatch(e);
        }

        public void RaiseTech(AlertEventKind kind, string message)
        {
            Dispatch(new EngineEvent { Kind = kind, AtUtc = DateTime.UtcNow, Message = message, Source = "" });
        }

        public void PreviewSound(string stored, Action<PlayReport> done)
        {
            AnnounceRequest req = MakeRequest(Paths.Resolve(stored), 1, Settings, false, "preview");
            req.Preview = true;
            if (done != null) req.Done = rep => OnUi(delegate { done(rep); });
            Audio.Announce(req);
        }

        public void StopSound() { Audio.Stop(); }

        // ---------- Налаштування ----------

        public string ApplySettings(AppSettings s)
        {
            s.Normalize();
            try { SettingsStore.Save(s); }
            catch (Exception ex)
            {
                Log.Error("Не вдалося зберегти налаштування", ex);
                return ex.Message;
            }
            Settings = s;
            Monitor.ApplySettings(s);
            PreloadSounds();
            Log.Info("Налаштування збережено.");
            var h = SettingsApplied;
            if (h != null) h();
            var h2 = StateChanged;
            if (h2 != null) h2();
            return null;
        }

        public string SetAutostart(bool on)
        {
            string err = Autostart.Set(on, Paths.ExePath);
            Log.Info(err == null ? (on ? "Автозапуск увімкнено." : "Автозапуск вимкнено.") : "Автозапуск: помилка — " + err);
            return err;
        }

        public List<HealthItem> Health()
        {
            return HealthService.Evaluate(new HealthContext
            {
                Settings = Settings,
                Monitor = Monitor,
                Admin = Admin,
                Autostart = Autostart.Query(Paths.ExePath),
                LastPlay = LastPlay
            });
        }

        void OnTick()
        {
            if (Schedule.DailyReportDue(Settings, DateTime.Now, _lastDailyReport))
            {
                _lastDailyReport = DateTime.Now;
                RaiseTech(AlertEventKind.DailyReport, BuildReport());
            }
        }

        string BuildReport()
        {
            var st = Monitor.Engine.State;
            List<HealthItem> hl = Health();
            var problems = new List<string>();
            foreach (HealthItem i in hl) if (i.State == HealthState.Error || i.State == HealthState.Warn) problems.Add(i.Title + ": " + i.Message);
            var sb = new StringBuilder();
            sb.Append("Поточний рівень: ").Append(Lang.LevelShort(Monitor.Engine.EffectiveLevel())).Append(". ");
            sb.Append("Тривог (червоний рівень) сьогодні: ").Append(_alarmDay == DateTime.Today ? _alarmsToday : 0).Append(". ");
            if (st.LastOkUtc.HasValue) sb.Append("Остання успішна перевірка: ").Append(HealthService.Ago(st.LastOkUtc.Value)).Append(". ");
            sb.Append(problems.Count == 0 ? "Проблем не виявлено." : "Є зауваження: " + string.Join("; ", problems.ToArray()));
            return sb.ToString();
        }

        // ---------- Перевірки з вкладок налаштувань (у фоні) ----------

        void Background(Func<string> work, Action<string> done)
        {
            Task.Run(work).ContinueWith(t =>
            {
                string res = t.IsFaulted ? "Помилка: " + t.Exception.GetBaseException().Message : t.Result;
                OnUi(delegate { done(res); });
            });
        }

        AppSettings With(Action<AppSettings> tweak)
        {
            AppSettings c = SettingsJson.Clone(Settings);
            tweak(c);
            return c;
        }

        public void TestAlertsInUa(string token, Action<string> done)
        {
            Background(delegate
            {
                AppSettings c = With(s => s.AlertsInUaToken = token);
                SourceReading r = new AlertsInUaSource(() => c, Http, () => DateTime.UtcNow).Fetch();
                if (!r.Ok) return "✗ " + r.Error;
                return "✓ Ключ працює (відповідь за " + r.LatencyMs + " мс). Зараз для міста: " + Lang.LevelShort(r.Level) +
                       (string.IsNullOrEmpty(r.Detail) ? "" : " — " + r.Detail);
            }, done);
        }

        public void TestTelegram(string channels, Action<string> done)
        {
            Background(delegate
            {
                AppSettings c = With(s => s.TelegramChannels = channels);
                var src = new TelegramSource(() => c, Http, () => DateTime.UtcNow);
                SourceReading r = src.Fetch();
                var sb = new StringBuilder();
                foreach (ChannelStatus ch in src.GetChannelStatus())
                {
                    sb.Append(ch.Ok ? "✓ @" : "✗ @").Append(ch.Channel).Append(": ");
                    sb.Append(ch.Ok
                        ? "доступний, остання публікація " + (ch.NewestMessageUtc.HasValue ? HealthService.Ago(ch.NewestMessageUtc.Value) : "—") + ", подій про місто в історії: " + ch.Events
                        : ch.Error);
                    sb.AppendLine();
                }
                if (r.Ok) sb.Append("Зараз для міста: " + Lang.LevelShort(r.Level) + (string.IsNullOrEmpty(r.Detail) ? "" : " — " + r.Detail));
                return sb.ToString().Trim();
            }, done);
        }

        public void TestAdmin(string token, string chatId, Action<string> done)
        {
            Background(delegate
            {
                string err = Admin.SendNow(token, chatId, "✅ ВАРТА: тестове повідомлення від комп'ютера «" + PcName() + "». Якщо ви це бачите — адміністратор підключений.");
                return err == null ? "✓ Повідомлення надіслано — перевірте Telegram." : "✗ " + err;
            }, done);
        }

        public void RunDiagnostics(Action<string> done)
        {
            Background(delegate { return Diagnostics(); }, done);
        }

        string Diagnostics()
        {
            AppSettings s = Settings;
            var sb = new StringBuilder();
            sb.AppendLine("ВАРТА " + AppInfo.Version + " — діагностика " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
            sb.AppendLine("ОС: " + Environment.OSVersion + "; .NET: " + Environment.Version + "; 64-бітний процес: " + Environment.Is64BitProcess);
            sb.AppendLine("Програма: " + Paths.ExePath);
            sb.AppendLine("Папка даних: " + Paths.DataDir + (Paths.IsPortableFolder ? " (переносна)" : " (папка програми недоступна для запису)"));
            sb.AppendLine();

            AudioStatus a = AudioDucker.Query();
            sb.AppendLine("ЗВУК: пристроїв виводу: " + a.DeviceCount + "; типовий пристрій: " + (a.DeviceAvailable ? "є" : "НЕМАЄ") +
                          (a.DeviceAvailable ? "; гучність " + (int)Math.Round(a.MasterVolume * 100) + "%" + (a.Muted ? ", mute" : "") : "") +
                          (string.IsNullOrEmpty(a.Error) ? "" : "; помилка: " + a.Error));
            var files = new Dictionary<string, string>
            {
                { "Червоний рівень", s.SoundRed }, { "Жовтий рівень", s.SoundYellow },
                { "Зниження до жовтого", s.SoundYellowFromRed }, { "Відбій", s.SoundClear }
            };
            foreach (var kv in files)
            {
                string full = Paths.Resolve(kv.Value);
                ProbeResult pr = AudioProbe.Check(full);
                sb.AppendLine("  • " + kv.Key + ": " + kv.Value + " → " +
                              (pr.Ok ? "OK, " + (pr.Format.Length > 0 ? pr.Format + ", " : "") + (pr.DurationMs / 1000.0).ToString("0.0") + " с (" + pr.Backend + ")" : "ПРОБЛЕМА: " + pr.Error));
            }
            int mciLen;
            string mciErr = Mci.Probe(Paths.Resolve(s.SoundRed), out mciLen);
            sb.AppendLine("  • Запасний спосіб MCI: " + (mciErr == null ? "працює" : "не працює на цьому ПК — " + mciErr + " (це нормально: основний спосіб його не використовує)"));
            if (LastPlay != null && !LastPlay.Cancelled)
                sb.AppendLine("  • Останнє озвучення " + LastPlay.TimeLocal.ToString("HH:mm:ss") + ": «" + LastPlay.FileName + "» — " + (LastPlay.Ok ? "OK, " + LastPlay.Backend : "ПОМИЛКА: " + LastPlay.Error));
            sb.AppendLine();

            AutostartInfo au = Autostart.Query(Paths.ExePath);
            sb.AppendLine("АВТОЗАПУСК: " + (au.Enabled ? "увімкнено" : "вимкнено") + (au.Enabled && !au.PathMatches ? " (шлях застарів)" : "") +
                          (au.DisabledInTaskManager ? " (вимкнено в Диспетчері завдань)" : "") + (string.IsNullOrEmpty(au.Error) ? "" : "; помилка: " + au.Error));
            sb.AppendLine();

            sb.AppendLine("ДЖЕРЕЛА ДАНИХ:");
            if (s.UseAlertsInUa)
            {
                SourceReading r = new AlertsInUaSource(() => Settings, Http, () => DateTime.UtcNow).Fetch();
                sb.AppendLine("  • alerts.in.ua: " + (r.Ok ? "OK, рівень " + Lang.LevelShort(r.Level) + ", " + r.LatencyMs + " мс" : "ПРОБЛЕМА: " + r.Error));
            }
            if (s.UseTelegram)
            {
                var src = new TelegramSource(() => Settings, Http, () => DateTime.UtcNow);
                SourceReading r = src.Fetch();
                foreach (ChannelStatus ch in src.GetChannelStatus())
                    sb.AppendLine("  • @" + ch.Channel + ": " + (ch.Ok ? "OK, подій про місто: " + ch.Events : "ПРОБЛЕМА: " + ch.Error));
                sb.AppendLine("  • Telegram → рівень: " + (r.Ok ? Lang.LevelShort(r.Level) : "н/д (" + r.Error + ")"));
            }
            sb.AppendLine();
            sb.AppendLine("АДМІНІСТРАТОР: " + (s.AdminEnabled ? (Admin.IsConfigured(s) ? "налаштовано" : "увімкнено, але не заповнено") : "вимкнено"));
            sb.AppendLine("МОНІТОРИНГ: " + (Monitor.IsPaused ? "призупинено" : "працює") + "; стан рушія: " + Lang.LevelShort(Monitor.Engine.State.Level));
            return sb.ToString();
        }

        public void Dispose()
        {
            _disposed = true;
            _tick.Stop();
            try { Banners.Dispose(); } catch { }
            try { Monitor.Dispose(); } catch { }
            try { Admin.Dispose(); } catch { }
            try { Audio.Dispose(); } catch { }
            try { _marshal.Dispose(); } catch { }
        }
    }
}
