using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Varta.Core;

namespace Varta.Platform
{
    /// <summary>
    /// Службова перевірка звуку з командного рядка: Varta.exe --audio-test=файл [--save-wav=результат.wav].
    /// Результат записується в audio-test.txt поруч із програмою (корисно для підтримки й автоматичних перевірок).
    /// </summary>
    internal static class AudioSelfTest
    {
        public static int Run(string file, string saveWav)
        {
            var sb = new StringBuilder();
            int code = 0;
            sb.AppendLine("ВАРТА " + AppInfo.Version + " — перевірка звуку " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
            sb.AppendLine("ОС: " + Environment.OSVersion + "; 64-бітний процес: " + Environment.Is64BitProcess + "; .NET: " + Environment.Version);
            string full = Paths.Resolve(file);
            sb.AppendLine("Файл: " + full);
            try
            {
                if (!File.Exists(full)) throw new AudioDecodeException("файл не знайдено");
                sb.AppendLine("Розмір: " + new FileInfo(full).Length + " байт");

                var sw = Stopwatch.StartNew();
                DecodedAudio a = AudioDecoder.Decode(full);
                sw.Stop();
                sb.AppendLine("РОЗКОДУВАННЯ: OK (" + a.Format + ", " + a.SampleRate + " Гц, каналів: " + a.Channels + ", " + (a.DurationMs / 1000.0).ToString("0.00") +
                              " с, гучність RMS " + a.Rms().ToString("0.0000") + ", " + sw.ElapsedMilliseconds + " мс)");
                if (!string.IsNullOrEmpty(saveWav))
                {
                    File.WriteAllBytes(saveWav, Pcm.ToWav(a, 1f));
                    sb.AppendLine("WAV збережено: " + saveWav);
                }

                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    BackendResult br = ManagedBackend.PlayAudio(ManagedBackend.BackendName, a, 1f, () => false, null);
                    sb.AppendLine("ВІДТВОРЕННЯ " + ManagedBackend.BackendName + ": " + (br.Ok ? "OK" : "ПОМИЛКА — " + br.Error));
                    if (!br.Ok) code = 3;
                    int len;
                    string mci = Mci.Probe(full, out len);
                    sb.AppendLine("MCI (запасний спосіб): " + (mci == null ? "відкривається, " + (len / 1000.0).ToString("0.0") + " с" : "не працює — " + mci));
                }
                else sb.AppendLine("Відтворення пропущено (не Windows).");
            }
            catch (AudioDecodeException ex) { sb.AppendLine("РОЗКОДУВАННЯ: ПОМИЛКА — " + ex.Message); code = 2; }
            catch (Exception ex) { sb.AppendLine("ЗБІЙ: " + ex); code = 1; }

            try { File.WriteAllText(Path.Combine(Paths.DataDir, "audio-test.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            return code;
        }
    }
}
