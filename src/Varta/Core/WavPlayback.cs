using System;
using System.Diagnostics;
using System.Threading;

namespace Varta.Core
{
    /// <summary>Приймач готового WAV (у Windows — System.Media.SoundPlayer, у тестах — підмінка).</summary>
    public interface IWavSink : IDisposable
    {
        string Label { get; }
        /// <summary>Готує звук; кидає виняток, якщо дані не можна відтворити.</summary>
        void Load(byte[] wav);
        /// <summary>Блокує потік, доки звук не закінчиться або не буде викликано Stop.</summary>
        void PlaySync();
        /// <summary>Безпечно викликається з іншого потоку.</summary>
        void Stop();
    }

    /// <summary>
    /// Керування відтворенням: окремий потік для блокуючого PlaySync, опитування скасування, захист від «зависання»
    /// і від звуку, що «закінчився» миттєво (немає пристрою виведення).
    /// </summary>
    public static class WavPlayback
    {
        public const int DefaultTimeoutExtraMs = 15000;

        public static BackendResult Play(string backend, IWavSink sink, byte[] wav, int durationMs,
            Func<bool> cancelled, Action tick, int timeoutExtraMs)
        {
            try { sink.Load(wav); }
            catch (Exception ex)
            {
                SafeDispose(sink);
                return BackendResult.Fail(backend, sink.Label + ": " + ex.Message);
            }

            string error = null;
            var done = new ManualResetEvent(false);
            var worker = new Thread(delegate ()
            {
                try { sink.PlaySync(); }
                catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
                finally { done.Set(); }
            }) { IsBackground = true, Name = "Varta.WavSink" };

            var sw = Stopwatch.StartNew();
            bool wasCancelled = false;
            worker.Start();
            while (!done.WaitOne(80))
            {
                if (cancelled != null && cancelled()) { wasCancelled = true; SafeStop(sink); break; }
                if (tick != null) tick();
                if (sw.ElapsedMilliseconds > durationMs + timeoutExtraMs)
                {
                    error = "відтворення не завершилось вчасно";
                    SafeStop(sink);
                    break;
                }
            }
            bool finished = done.WaitOne(3000);
            sw.Stop();
            if (finished) { SafeDispose(sink); done.Close(); }

            if (wasCancelled) return BackendResult.Cancel(backend);
            if (error != null) return BackendResult.Fail(backend, error);
            if (!finished) return BackendResult.Fail(backend, "відтворення не вдалося зупинити");
            if (durationMs > 1000 && sw.ElapsedMilliseconds < durationMs * 0.6)
                return BackendResult.Fail(backend, "звук завершився одразу (" + sw.ElapsedMilliseconds + " мс із " + durationMs + " мс) — можливо, немає пристрою виведення звуку");
            return BackendResult.Success(backend, durationMs);
        }

        static void SafeStop(IWavSink s) { try { s.Stop(); } catch { } }
        static void SafeDispose(IWavSink s) { try { s.Dispose(); } catch { } }
    }
}
