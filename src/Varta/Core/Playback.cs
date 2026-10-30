using System;
using System.Collections.Generic;
using System.IO;

namespace Varta.Core
{
    public sealed class BackendResult
    {
        public bool Ok;
        public bool Cancelled;
        public string Backend = "";
        public string Error = "";
        public int DurationMs;

        public static BackendResult Success(string backend, int ms) { return new BackendResult { Ok = true, Backend = backend, DurationMs = ms }; }
        public static BackendResult Fail(string backend, string error) { return new BackendResult { Backend = backend, Error = error ?? "" }; }
        public static BackendResult Cancel(string backend) { return new BackendResult { Cancelled = true, Backend = backend }; }
    }

    /// <summary>Один зі способів відтворення файлу. Play блокує потік до кінця звуку або скасування.</summary>
    public interface IPlayBackend
    {
        string Name { get; }
        BackendResult Play(string file, int volumePercent, Func<bool> cancelled, Action tick);
    }

    /// <summary>Підсумок одного оголошення (для журналу, сторінки «Стан» і рядка під файлом у «Звук»).</summary>
    public sealed class PlayReport
    {
        public string File = "";
        public string Tag = "";
        public bool Preview;
        public bool IsTest;
        public bool Ok;
        public bool Cancelled;
        public string Backend = "";
        public string Error = "";
        public int DurationMs;
        public DateTime TimeLocal;

        public string FileName { get { return string.IsNullOrEmpty(File) ? "(не вказано)" : Path.GetFileName(File); } }
        public string Seconds { get { return (DurationMs / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " с"; } }
    }

    /// <summary>Пробує способи відтворення по черзі, поки один не спрацює; збирає помилки всіх невдалих.</summary>
    public static class PlaybackChain
    {
        public static BackendResult Play(IList<IPlayBackend> backends, string file, int volumePercent, Func<bool> cancelled, Action tick)
        {
            var errors = new List<string>();
            foreach (IPlayBackend b in backends)
            {
                BackendResult r;
                try { r = b.Play(file, volumePercent, cancelled, tick); }
                catch (Exception ex) { r = BackendResult.Fail(b.Name, ex.GetType().Name + ": " + ex.Message); }
                if (r.Ok || r.Cancelled) return r;
                errors.Add(b.Name + ": " + r.Error);
                if (cancelled != null && cancelled()) return BackendResult.Cancel(b.Name);
            }
            return BackendResult.Fail("—", string.Join("; ", errors.ToArray()));
        }
    }

    /// <summary>Кеш розкодованих файлів: тривога не чекає на розкодування. Змінений файл підхоплюється автоматично.</summary>
    public sealed class DecodeCache
    {
        const int Max = 8;
        readonly object _l = new object();
        readonly Dictionary<string, DecodedAudio> _items = new Dictionary<string, DecodedAudio>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> _order = new List<string>();
        readonly Func<string, DecodedAudio> _decode;

        public DecodeCache(Func<string, DecodedAudio> decode) { _decode = decode; }

        public int Count { get { lock (_l) return _items.Count; } }

        public DecodedAudio Get(string path)
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) throw new AudioDecodeException("файл не знайдено");
            string key = path + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            lock (_l)
            {
                DecodedAudio hit;
                if (_items.TryGetValue(key, out hit)) return hit;
            }
            DecodedAudio d = _decode(path);
            lock (_l)
            {
                // старі версії того самого файлу більше не потрібні
                string prefix = path + "|";
                foreach (string old in _order.FindAll(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && k != key))
                {
                    _items.Remove(old);
                    _order.Remove(old);
                }
                _items[key] = d;
                _order.Remove(key);
                _order.Add(key);
                while (_order.Count > Max)
                {
                    _items.Remove(_order[0]);
                    _order.RemoveAt(0);
                }
            }
            return d;
        }
    }
}
