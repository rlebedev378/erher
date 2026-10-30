using System;
using System.Collections.Generic;

namespace Varta.Core
{
    public sealed class EngineState
    {
        public AlertLevel Level = AlertLevel.Unknown;
        public DateTime? SinceUtc;
        public string Detail = "";
        public string Source = "";
        public DateTime? LastOkUtc;
        public DateTime? LastReadingUtc;
        public int ConsecutiveFailures;
        public bool ConnectionLost;
        public string LastError = "";
        public ErrorKind LastErrorKind;
        public DateTime? LastReminderUtc;
    }

    /// <summary>
    /// Логіка рівнів: підвищення рівня — миттєво, зниження — після кількох підряд підтверджень,
    /// втрата зв'язку — після кількох невдалих опитувань. Нічого не знає про Windows/UI.
    /// Перший відомий стан після запуску лише запам'ятовується: оповіщення — тільки при зміні стану.
    /// </summary>
    public sealed class AlertEngine
    {
        /// <summary>Скільки після запуску чекати, поки ПК підключиться до мережі, перш ніж повідомляти про втрату зв'язку.</summary>
        public static readonly TimeSpan StartupGrace = TimeSpan.FromMinutes(2);

        readonly Func<DateTime> _now;
        readonly DateTime _startedUtc;
        int _lowerCount;
        AlertLevel _lowerMax = AlertLevel.Unknown;
        DateTime? _firstFailureUtc;

        public AppSettings Settings;
        public readonly EngineState State = new EngineState();

        public AlertEngine(AppSettings settings, Func<DateTime> utcNow)
        {
            Settings = settings;
            _now = utcNow ?? (() => DateTime.UtcNow);
            _startedUtc = _now();
        }

        /// <summary>Рівень для показу: Unknown, якщо даних давно немає.</summary>
        public AlertLevel EffectiveLevel()
        {
            if (State.Level == AlertLevel.Unknown || !State.LastOkUtc.HasValue) return AlertLevel.Unknown;
            return IsStale() ? AlertLevel.Unknown : State.Level;
        }

        public bool IsStale()
        {
            if (!State.LastOkUtc.HasValue) return true;
            return (_now() - State.LastOkUtc.Value).TotalSeconds > EffectiveStaleSeconds(Settings);
        }

        /// <summary>Не менше двох інтервалів опитування — інакше при рідкому опитуванні дані «застарівали» б між перевірками.</summary>
        public static int EffectiveStaleSeconds(AppSettings s)
        {
            return Math.Max(s.StaleAfterSeconds, 2 * s.PollIntervalSeconds + 15);
        }

        public List<EngineEvent> OnReading(SourceReading r)
        {
            var ev = new List<EngineEvent>();
            DateTime now = _now();
            State.LastReadingUtc = now;

            if (!r.Ok)
            {
                if (State.ConsecutiveFailures == 0) _firstFailureUtc = now;
                State.ConsecutiveFailures++;
                State.LastError = r.Error ?? "";
                State.LastErrorKind = r.ErrorKind;
                bool startingUp = !State.LastOkUtc.HasValue && now - _startedUtc < StartupGrace;
                if (!State.ConnectionLost && !startingUp && State.ConsecutiveFailures >= Settings.FailuresBeforeLost)
                {
                    State.ConnectionLost = true;
                    TimeSpan down = now - (_firstFailureUtc ?? now);
                    ev.Add(new EngineEvent
                    {
                        Kind = AlertEventKind.ConnectionLost,
                        AtUtc = now,
                        Source = r.SourceName,
                        Message = "Немає даних від джерел уже " + TextUtil.FormatDuration(down) +
                                  ". Стан тривоги НЕВІДОМИЙ — перевірте інтернет. Остання помилка: " + (r.Error ?? "невідома") + "."
                    });
                }
                return ev;
            }

            bool wasLost = State.ConnectionLost;
            TimeSpan downtime = wasLost && _firstFailureUtc.HasValue ? now - _firstFailureUtc.Value : TimeSpan.Zero;
            State.ConsecutiveFailures = 0;
            State.ConnectionLost = false;
            State.LastError = "";
            State.LastErrorKind = ErrorKind.None;
            _firstFailureUtc = null;
            State.LastOkUtc = now;
            State.Source = r.SourceName;

            if (wasLost)
                ev.Add(new EngineEvent { Kind = AlertEventKind.ConnectionRestored, AtUtc = now, Source = r.SourceName, Downtime = downtime });

            ApplyLevel(r, now, ev, wasLost);
            return ev;
        }

        void ApplyLevel(SourceReading r, DateTime now, List<EngineEvent> ev, bool afterReportedOutage)
        {
            AlertLevel cur = State.Level;
            AlertLevel target = r.Level;
            if (target == AlertLevel.Unknown) return;

            if (cur == AlertLevel.Unknown)
            {
                // Перший стан після запуску — без оповіщення. Але якщо вже повідомили про втрату зв'язку («стан невідомий»),
                // відомий тепер рівень — зміна стану, про яку треба сказати.
                SetLevel(target, r, now);
                if (!afterReportedOutage) return;
                if (target == AlertLevel.Red) ev.Add(Make(AlertEventKind.RedStarted, cur, target, r, now));
                else if (target == AlertLevel.Yellow) ev.Add(Make(AlertEventKind.YellowStarted, cur, target, r, now));
                return;
            }

            if (target > cur)
            {
                _lowerCount = 0;
                SetLevel(target, r, now);
                if (target == AlertLevel.Red)
                    ev.Add(Make(cur == AlertLevel.Yellow ? AlertEventKind.RedFromYellow : AlertEventKind.RedStarted, cur, target, r, now));
                else
                    ev.Add(Make(AlertEventKind.YellowStarted, cur, target, r, now));
                return;
            }

            if (target == cur)
            {
                _lowerCount = 0;
                State.Detail = r.Detail ?? "";
                return;
            }

            _lowerCount++;
            if (_lowerCount == 1 || target > _lowerMax) _lowerMax = target;
            if (_lowerCount < Settings.ClearConfirmPolls) return;

            AlertLevel newLevel = _lowerMax;
            _lowerCount = 0;
            SetLevel(newLevel, r, now);
            State.SinceUtc = now;
            ev.Add(Make(newLevel == AlertLevel.Clear ? AlertEventKind.AllClear : AlertEventKind.YellowFromRed, cur, newLevel, r, now));
        }

        void SetLevel(AlertLevel lvl, SourceReading r, DateTime now)
        {
            State.Level = lvl;
            State.SinceUtc = r.SinceUtc ?? now;
            State.Detail = r.Detail ?? "";
            State.LastReminderUtc = now;
        }

        EngineEvent Make(AlertEventKind k, AlertLevel from, AlertLevel to, SourceReading r, DateTime now)
        {
            return new EngineEvent { Kind = k, From = from, To = to, AtUtc = now, Detail = r.Detail ?? "", Source = r.SourceName };
        }

        /// <summary>Періодичні події (нагадування під час тривоги).</summary>
        public List<EngineEvent> OnTick()
        {
            var ev = new List<EngineEvent>();
            DateTime now = _now();
            if (State.Level == AlertLevel.Red && Settings.ReminderMinutes > 0 && !IsStale())
            {
                DateTime last = State.LastReminderUtc ?? State.SinceUtc ?? now;
                if (now - last >= TimeSpan.FromMinutes(Settings.ReminderMinutes))
                {
                    State.LastReminderUtc = now;
                    ev.Add(new EngineEvent
                    {
                        Kind = AlertEventKind.RedReminder,
                        From = AlertLevel.Red,
                        To = AlertLevel.Red,
                        AtUtc = now,
                        Detail = State.Detail,
                        Source = State.Source
                    });
                }
            }
            return ev;
        }
    }
}
