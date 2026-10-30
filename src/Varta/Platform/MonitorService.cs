using System;
using System.Collections.Generic;
using System.Threading;
using Varta.Core;

namespace Varta.Platform
{
    /// <summary>Фоновий опитувач: бере дані з першого справного джерела, передає їх рушію рівнів, піднімає події.</summary>
    public sealed class MonitorService : IDisposable
    {
        readonly Func<AppSettings> _cfg;
        readonly AlertsInUaSource _aiu;
        readonly TelegramSource _tg;
        readonly object _lock = new object();
        readonly Dictionary<string, SourceReading> _bySource = new Dictionary<string, SourceReading>();
        readonly ManualResetEvent _wake = new ManualResetEvent(false);
        Thread _thread;
        volatile bool _stop;
        DateTime _aiuBackoffUntil = DateTime.MinValue;
        DateTime _lastForcedUtc = DateTime.MinValue;
        int _cycle;

        public readonly AlertEngine Engine;
        public event Action<EngineEvent> EventRaised;
        public event Action Updated;

        public DateTime LastCycleUtc = DateTime.MinValue;
        public DateTime? PausedUntilUtc;
        public SourceReading LastReading;
        public AudioStatus Audio = new AudioStatus();

        public TelegramSource Telegram { get { return _tg; } }
        public bool IsPaused { get { return PausedUntilUtc.HasValue; } }

        public MonitorService(Func<AppSettings> cfg, IHttp http)
        {
            _cfg = cfg;
            Engine = new AlertEngine(cfg(), () => DateTime.UtcNow);
            _aiu = new AlertsInUaSource(cfg, http, () => DateTime.UtcNow);
            _tg = new TelegramSource(cfg, http, () => DateTime.UtcNow);
        }

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Run) { IsBackground = true, Name = "Varta.Monitor" };
            _thread.Start();
        }

        public void ApplySettings(AppSettings s)
        {
            Engine.Settings = s;
            _tg.Reset();
            _aiuBackoffUntil = DateTime.MinValue;
            _wake.Set();
        }

        /// <summary>Негайна перевірка (не частіше ніж раз на 5 с — щоб не порушити ліміти API).</summary>
        public void RefreshNow()
        {
            if ((DateTime.UtcNow - _lastForcedUtc).TotalSeconds < 5) return;
            _lastForcedUtc = DateTime.UtcNow;
            _wake.Set();
        }

        public Dictionary<string, SourceReading> GetSourceReadings()
        {
            lock (_lock) { return new Dictionary<string, SourceReading>(_bySource); }
        }

        public void PauseFor(TimeSpan d)
        {
            DateTime until = DateTime.UtcNow + d;
            PausedUntilUtc = until;
            Raise(new EngineEvent
            {
                Kind = AlertEventKind.MonitoringPaused,
                AtUtc = DateTime.UtcNow,
                Message = "Моніторинг призупинено до " + until.ToLocalTime().ToString("HH:mm") + ". Сповіщення про тривоги НЕ надходитимуть до відновлення."
            });
            Fire();
        }

        public void Resume(string reason)
        {
            bool was = PausedUntilUtc.HasValue;
            PausedUntilUtc = null;
            if (was)
                Raise(new EngineEvent { Kind = AlertEventKind.MonitoringResumed, AtUtc = DateTime.UtcNow, Message = "Моніторинг відновлено (" + reason + ")." });
            _wake.Set();
            Fire();
        }

        void Run()
        {
            while (!_stop)
            {
                try { Cycle(); }
                catch (Exception ex) { Log.Error("Збій циклу моніторингу", ex); }
                int wait = Math.Max(10, _cfg().PollIntervalSeconds) * 1000;
                _wake.WaitOne(wait);
                _wake.Reset();
            }
        }

        void Cycle()
        {
            AppSettings s = _cfg();
            LastCycleUtc = DateTime.UtcNow;
            _cycle++;
            if (_cycle % 4 == 1)
            {
                try { Audio = AudioDucker.Query(); } catch (Exception ex) { Audio = new AudioStatus { Error = ex.Message }; }
            }

            DateTime? until = PausedUntilUtc;
            if (until.HasValue)
            {
                if (DateTime.UtcNow >= until.Value) Resume("час паузи минув");
                else { Fire(); return; }
            }

            var sources = new List<IAlertSource>();
            if (s.UseAlertsInUa && !string.IsNullOrWhiteSpace(s.AlertsInUaToken)) sources.Add(_aiu);
            if (s.UseTelegram) sources.Add(_tg);

            SourceReading chosen = null;
            SourceReading lastFail = null;
            foreach (IAlertSource src in sources)
            {
                SourceReading r;
                if (src == _aiu && DateTime.UtcNow < _aiuBackoffUntil)
                {
                    r = SourceReading.Fail(src.Name, ErrorKind.RateLimited, "пауза після перевищення ліміту запитів", DateTime.UtcNow);
                }
                else
                {
                    try { r = src.Fetch(); }
                    catch (Exception ex)
                    {
                        Log.Error("Збій джерела " + src.Name, ex);
                        r = SourceReading.Fail(src.Name, ErrorKind.BadData, "внутрішня помилка: " + ex.Message, DateTime.UtcNow);
                    }
                    if (!r.Ok && r.ErrorKind == ErrorKind.RateLimited && src == _aiu)
                        _aiuBackoffUntil = DateTime.UtcNow.AddSeconds(60);
                }
                lock (_lock) { _bySource[src.Name] = r; }
                if (r.Ok) { chosen = r; break; }
                lastFail = r;
            }

            SourceReading reading = chosen ??
                lastFail ??
                SourceReading.Fail("—", ErrorKind.NotConfigured, "не ввімкнено жодного джерела даних (вкладка «Джерела даних»)", DateTime.UtcNow);
            LastReading = reading;

            List<EngineEvent> events = Engine.OnReading(reading);
            events.AddRange(Engine.OnTick());
            foreach (EngineEvent e in events)
            {
                Log.Info("Подія: " + e.Kind + (string.IsNullOrEmpty(e.Detail) ? "" : " — " + e.Detail));
                Raise(e);
            }
            Fire();
        }

        void Raise(EngineEvent e)
        {
            try { var h = EventRaised; if (h != null) h(e); }
            catch (Exception ex) { Log.Error("Збій обробки події " + e.Kind, ex); }
        }

        void Fire()
        {
            try { var h = Updated; if (h != null) h(); } catch { }
        }

        public void Dispose()
        {
            _stop = true;
            _wake.Set();
        }
    }
}
