using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Varta.Core;

namespace Varta.Platform
{
    /// <summary>Системний MCI (winmm). Запасний спосіб: на деяких ПК драйвер MP3 не завантажується («помилка при завантаженні драйвера пристрою»).</summary>
    internal static class Mci
    {
        [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode)]
        static extern int mciSendString(string command, StringBuilder returnString, int returnLength, IntPtr callback);

        [DllImport("winmm.dll", EntryPoint = "mciGetErrorStringW", CharSet = CharSet.Unicode)]
        static extern bool mciGetErrorString(int error, StringBuilder buffer, int length);

        static int _n;

        static int Send(string cmd, out string ret)
        {
            var sb = new StringBuilder(256);
            int err = mciSendString(cmd, sb, sb.Capacity, IntPtr.Zero);
            ret = sb.ToString();
            return err;
        }

        static string ErrorText(int err)
        {
            var sb = new StringBuilder(256);
            return (mciGetErrorString(err, sb, sb.Capacity) ? sb.ToString() : "код") + " [" + err + "]";
        }

        static string TypeFor(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".mp3" || ext == ".mp2") return " type mpegvideo";
            if (ext == ".wav") return " type waveaudio";
            return "";
        }

        public static string Probe(string file, out int lengthMs)
        {
            lengthMs = 0;
            string alias = "vprobe" + Interlocked.Increment(ref _n);
            string r;
            int err;
            try { err = Send("open \"" + file + "\"" + TypeFor(file) + " alias " + alias, out r); }
            catch (Exception ex) { return "звукова підсистема недоступна (" + ex.Message + ")"; }
            if (err != 0) return "файл не відкривається (" + ErrorText(err) + ")";
            try
            {
                Send("set " + alias + " time format milliseconds", out r);
                if (Send("status " + alias + " length", out r) == 0) int.TryParse(r, out lengthMs);
                return null;
            }
            finally { Send("close " + alias, out r); }
        }

        public static string PlayBlocking(string file, int volume1000, Func<bool> cancelled, Action tick,
            out int length, out bool wasCancelled, out long elapsedMs)
        {
            length = 0;
            wasCancelled = false;
            elapsedMs = 0;
            string alias = "varta" + Interlocked.Increment(ref _n);
            string r;
            int err;
            try { err = Send("open \"" + file + "\"" + TypeFor(file) + " alias " + alias, out r); }
            catch (Exception ex) { return "звукова підсистема недоступна (" + ex.Message + ")"; }
            if (err != 0) return "не вдалося відкрити файл (" + ErrorText(err) + ")";
            try
            {
                Send("set " + alias + " time format milliseconds", out r);
                Send("setaudio " + alias + " volume to " + volume1000, out r);
                if (Send("status " + alias + " length", out r) == 0) int.TryParse(r, out length);
                err = Send("play " + alias, out r);
                if (err != 0) return "не вдалося почати відтворення (" + ErrorText(err) + ")";

                var sw = Stopwatch.StartNew();
                while (true)
                {
                    if (cancelled()) { wasCancelled = true; break; }
                    Thread.Sleep(80);
                    if (tick != null) tick();
                    string mode;
                    if (Send("status " + alias + " mode", out mode) == 0 && mode == "stopped" && sw.ElapsedMilliseconds > 400) break;
                    if (length > 0 && sw.ElapsedMilliseconds > length + 5000) break;
                    if (sw.ElapsedMilliseconds > 15 * 60 * 1000) break;
                }
                elapsedMs = sw.ElapsedMilliseconds;
                return null;
            }
            finally
            {
                Send("stop " + alias, out r);
                Send("close " + alias, out r);
            }
        }
    }

    internal sealed class MciBackend : IPlayBackend
    {
        public string Name { get { return "MCI"; } }

        public BackendResult Play(string file, int volumePercent, Func<bool> cancelled, Action tick)
        {
            int length;
            bool wasCancelled;
            long elapsed;
            string err = Mci.PlayBlocking(file, Math.Max(0, Math.Min(1000, volumePercent * 10)), cancelled, tick, out length, out wasCancelled, out elapsed);
            if (err != null) return BackendResult.Fail(Name, err);
            if (wasCancelled) return BackendResult.Cancel(Name);
            if (length > 1000 && elapsed < length * 0.6) return BackendResult.Fail(Name, "відтворення зупинилось завчасно");
            return BackendResult.Success(Name, length);
        }
    }

    /// <summary>
    /// Основний спосіб: MP3/WAV розкодовується власним декодером у PCM і відтворюється через winmm PlaySound (System.Media.SoundPlayer) —
    /// так само, як системні звуки Windows. Не залежить від MCI, DirectShow, кодеків Windows і від символів у шляху до файлу.
    /// </summary>
    internal sealed class ManagedBackend : IPlayBackend
    {
        public const string BackendName = "NLayer+SoundPlayer";
        public static readonly DecodeCache Cache = new DecodeCache(AudioDecoder.Decode);

        public string Name { get { return BackendName; } }

        public BackendResult Play(string file, int volumePercent, Func<bool> cancelled, Action tick)
        {
            DecodedAudio audio;
            try { audio = Cache.Get(file); }
            catch (AudioDecodeException ex) { return BackendResult.Fail(Name, ex.Message); }
            catch (Exception ex) { return BackendResult.Fail(Name, ex.GetType().Name + ": " + ex.Message); }
            return PlayAudio(Name, audio, Math.Max(0f, Math.Min(1f, volumePercent / 100f)), cancelled, tick);
        }

        public static BackendResult PlayAudio(string name, DecodedAudio audio, float gain, Func<bool> cancelled, Action tick)
        {
            return WavPlayback.Play(name, new SoundPlayerSink(), Pcm.ToWav(audio, gain), audio.DurationMs, cancelled, tick, WavPlayback.DefaultTimeoutExtraMs);
        }
    }

    /// <summary>System.Media.SoundPlayer (winmm PlaySound з пам'яті) — той самий механізм, що й для звуків Windows.</summary>
    internal sealed class SoundPlayerSink : IWavSink
    {
        SoundPlayer _player;
        MemoryStream _ms;

        public string Label { get { return "SoundPlayer"; } }

        public void Load(byte[] wav)
        {
            _ms = new MemoryStream(wav, false);
            _player = new SoundPlayer(_ms);
            _player.Load();
        }

        public void PlaySync() { _player.PlaySync(); }

        public void Stop()
        {
            SoundPlayer p = _player;
            if (p != null) p.Stop();
        }

        public void Dispose()
        {
            if (_player != null) _player.Dispose();
            if (_ms != null) _ms.Dispose();
        }
    }

    public sealed class ProbeResult
    {
        public bool Ok;
        public int DurationMs;
        public string Backend = "";
        public string Format = "";
        public string Error = "";
    }

    /// <summary>Перевірка, що файл можна відтворити (без звуку): спершу власним декодером, потім MCI.</summary>
    public static class AudioProbe
    {
        public static ProbeResult Check(string fullPath)
        {
            var r = new ProbeResult();
            if (string.IsNullOrWhiteSpace(fullPath)) { r.Error = "файл не вказано"; return r; }
            if (!File.Exists(fullPath)) { r.Error = "файл не знайдено"; return r; }
            try { if (new FileInfo(fullPath).Length == 0) { r.Error = "файл порожній"; return r; } } catch { }

            var errors = new List<string>();
            try
            {
                DecodedAudio a = ManagedBackend.Cache.Get(fullPath);
                r.Ok = true;
                r.DurationMs = a.DurationMs;
                r.Format = a.Format;
                r.Backend = ManagedBackend.BackendName;
                return r;
            }
            catch (AudioDecodeException ex) { errors.Add(ManagedBackend.BackendName + ": " + ex.Message); }
            catch (Exception ex) { errors.Add(ManagedBackend.BackendName + ": " + ex.GetType().Name + ": " + ex.Message); }

            int len;
            string mci = Mci.Probe(fullPath, out len);
            if (mci == null) { r.Ok = true; r.DurationMs = len; r.Backend = "MCI"; return r; }
            errors.Add("MCI: " + mci);
            r.Error = string.Join("; ", errors.ToArray());
            return r;
        }
    }

    public sealed class AnnounceRequest
    {
        public string File = "";
        public int Repeat = 1;
        public int GapSeconds = 3;
        public int VolumePercent = 100;
        public bool Duck = true;
        public float KeepRatio = 0.1f;
        public bool Boost = true;
        public float MinMaster = 0.7f;
        public bool Unmute = true;
        public string Tag = "";
        public bool Preview;
        public bool IsTest;
        public Action<PlayReport> Done;
    }

    /// <summary>
    /// Черга оголошень на окремому потоці: нове оголошення перериває попереднє (завжди актуальне найновіше),
    /// на час відтворення музика приглушується, після — гучність повертається.
    /// </summary>
    public sealed class AudioService : IDisposable
    {
        readonly object _l = new object();
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        readonly AudioDucker _ducker = new AudioDucker();
        readonly IList<IPlayBackend> _backends = new IPlayBackend[] { new ManagedBackend(), new MciBackend() };
        readonly Thread _thread;
        AnnounceRequest _pending;
        bool _cancel;
        volatile bool _stop;

        /// <summary>Проблема, про яку варто повідомити (не вдалося приглушити музику, не відтворився файл тривоги).</summary>
        public event Action<string> Problem;
        /// <summary>Підсумок кожного оголошення (для журналу та сторінки «Стан»). Викликається з потоку звуку.</summary>
        public event Action<PlayReport> Reported;
        public bool IsPlaying { get; private set; }

        public AudioService()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "Varta.Audio" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        public void Announce(AnnounceRequest r)
        {
            lock (_l) { _pending = r; _cancel = true; }
            _wake.Set();
        }

        public void Stop()
        {
            lock (_l) { _pending = null; _cancel = true; }
        }

        /// <summary>Розкодовує файли наперед, щоб тривога не чекала.</summary>
        public void Preload(IEnumerable<string> files)
        {
            var list = new List<string>(files);
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (string f in list)
                {
                    try { if (!string.IsNullOrEmpty(f) && File.Exists(f)) ManagedBackend.Cache.Get(f); }
                    catch { }
                }
            });
        }

        bool Cancelled() { lock (_l) return _cancel || _stop; }
        bool HasPending() { lock (_l) return _pending != null; }

        void Raise(string msg)
        {
            Log.Warn(msg);
            try { var h = Problem; if (h != null) h(msg); } catch { }
        }

        void Loop()
        {
            _ducker.RecoverIfNeeded();
            while (!_stop)
            {
                _wake.WaitOne(1000);
                if (_stop) break;
                AnnounceRequest r;
                lock (_l) { r = _pending; _pending = null; _cancel = false; }
                if (r == null) continue;
                try { Run(r); }
                catch (Exception ex) { Log.Error("Збій відтворення", ex); try { _ducker.Restore(); } catch { } }
                finally { IsPlaying = false; }
            }
            try { _ducker.Restore(); } catch { }
        }

        void Run(AnnounceRequest r)
        {
            IsPlaying = true;
            var report = new PlayReport { File = r.File ?? "", Tag = r.Tag, Preview = r.Preview, IsTest = r.IsTest, TimeLocal = DateTime.Now };

            if (r.Duck)
            {
                string e = _ducker.Duck(r.KeepRatio, r.Boost, r.MinMaster, r.Unmute);
                if (e != null) Raise("Не вдалося приглушити музику: " + e);
            }

            DateTime lastDuck = DateTime.UtcNow;
            Action tick = delegate
            {
                if (r.Duck && (DateTime.UtcNow - lastDuck).TotalMilliseconds > 1000)
                {
                    lastDuck = DateTime.UtcNow;
                    _ducker.Duck(r.KeepRatio, r.Boost, r.MinMaster, r.Unmute);
                }
            };

            string path = r.File;
            if (string.IsNullOrWhiteSpace(path)) report.Error = "файл не вказано";
            else if (!File.Exists(path)) report.Error = "файл не знайдено: " + path;
            else
            {
                for (int i = 0; i < Math.Max(1, r.Repeat) && !Cancelled(); i++)
                {
                    if (i > 0 && !SleepCancellable(r.GapSeconds * 1000, tick)) break;
                    BackendResult br = PlaybackChain.Play(_backends, path, r.VolumePercent, Cancelled, tick);
                    if (br.Cancelled) { report.Cancelled = true; break; }
                    if (!br.Ok) { report.Ok = false; report.Error = br.Error; break; }
                    report.Ok = true;
                    report.Backend = br.Backend;
                    report.DurationMs = br.DurationMs;
                }
                if (!report.Ok && report.Error.Length == 0) report.Cancelled = true;
            }

            if (!report.Ok && !report.Cancelled && !r.Preview)
            {
                Raise("Не вдалося озвучити «" + report.FileName + "»: " + report.Error + ". Прозвучить резервний сигнал.");
                PlayFallbackTone(r.VolumePercent);
            }

            try { var h = Reported; if (h != null) h(report); } catch { }
            try { if (r.Done != null) r.Done(report); } catch { }

            if (!HasPending())
            {
                Thread.Sleep(350);
                _ducker.Restore();
            }
        }

        bool SleepCancellable(int ms, Action tick)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (Cancelled()) return false;
                Thread.Sleep(100);
                if (tick != null) tick();
            }
            return true;
        }

        /// <summary>Різкий сигнал замість голосу, коли файл не відтворився; не схожий на звичайний дзвінок сповіщення Windows.</summary>
        void PlayFallbackTone(int volumePercent)
        {
            try
            {
                BackendResult br = ManagedBackend.PlayAudio("Сигнал", Tones.AlertBeeps(), Math.Max(0.4f, volumePercent / 100f), Cancelled, null);
                if (br.Ok || br.Cancelled) return;
            }
            catch { }
            try
            {
                for (int i = 0; i < 3 && !Cancelled(); i++)
                {
                    SystemSounds.Exclamation.Play();
                    if (!SleepCancellable(700, null)) break;
                }
            }
            catch { }
        }

        public void Dispose()
        {
            _stop = true;
            lock (_l) { _cancel = true; }
            _wake.Set();
            try { _thread.Join(2000); } catch { }
        }
    }
}
