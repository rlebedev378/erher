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
    /// </summary>
    public sealed class AlertEngine
    {
        readonly Func<DateTime> _now;
        int _lowerCount;
        AlertLevel _lowerMax = AlertLevel.Unknown;
        DateTime? _firstFailureUtc;

        public AppSettings Settings;
        public readonly EngineState State = new EngineState();

        public AlertEngine(AppSettings settings, Func<DateTime> utcNow)
        {
            Settings = settings;
            _now = utcNow ?? (() => DateTime.UtcNow);
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
            return (_now() - State.LastOkUtc.Value).TotalSeconds > Settings.StaleAfterSeconds;
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
                if (!State.ConnectionLost && State.ConsecutiveFailures >= Settings.FailuresBeforeLost)
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

            ApplyLevel(r, now, ev);
            return ev;
        }

        void ApplyLevel(SourceReading r, DateTime now, List<EngineEvent> ev)
        {
            AlertLevel cur = State.Level;
            AlertLevel target = r.Level;
            if (target == AlertLevel.Unknown) return;

            if (cur == AlertLevel.Unknown)
            {
                SetLevel(target, r, now);
                if (target == AlertLevel.Red) ev.Add(Make(AlertEventKind.RedStarted, cur, target, r, now, true));
                else if (target == AlertLevel.Yellow) ev.Add(Make(AlertEventKind.YellowStarted, cur, target, r, now, true));
                return;
            }

            if (target > cur)
            {
                _lowerCount = 0;
                SetLevel(target, r, now);
                if (target == AlertLevel.Red)
                    ev.Add(Make(cur == AlertLevel.Yellow ? AlertEventKind.RedFromYellow : AlertEventKind.RedStarted, cur, target, r, now, false));
                else
                    ev.Add(Make(AlertEventKind.YellowStarted, cur, target, r, now, false));
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
            ev.Add(Make(newLevel == AlertLevel.Clear ? AlertEventKind.AllClear : AlertEventKind.YellowFromRed, cur, newLevel, r, now, false));
        }

        void SetLevel(AlertLevel lvl, SourceReading r, DateTime now)
        {
            State.Level = lvl;
            State.SinceUtc = r.SinceUtc ?? now;
            State.Detail = r.Detail ?? "";
            State.LastReminderUtc = now;
        }

        EngineEvent Make(AlertEventKind k, AlertLevel from, AlertLevel to, SourceReading r, DateTime now, bool initial)
        {
            return new EngineEvent { Kind = k, From = from, To = to, AtUtc = now, Detail = r.Detail ?? "", Source = r.SourceName, IsInitial = initial };
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
