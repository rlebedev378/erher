using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Varta.Core
{
    public sealed class AudioDecodeException : Exception
    {
        public AudioDecodeException(string message) : base(message) { }
    }

    /// <summary>Розкодований звук: 16-бітний PCM з чергуванням каналів.</summary>
    public sealed class DecodedAudio
    {
        public int SampleRate;
        public int Channels;
        public short[] Samples = new short[0];
        public string Format = "";

        public int DurationMs
        {
            get
            {
                if (SampleRate <= 0 || Channels <= 0) return 0;
                return (int)(Samples.LongLength * 1000L / ((long)SampleRate * Channels));
            }
        }

        public double Rms()
        {
            if (Samples.Length == 0) return 0;
            double sum = 0;
            foreach (short s in Samples) sum += (double)s * s;
            return Math.Sqrt(sum / Samples.Length) / 32768.0;
        }
    }

    /// <summary>
    /// Власний декодер MP3 (NLayer) і WAV. Не залежить від Windows MCI/DirectShow/кодеків і від кодування шляху:
    /// файл читається звичайним .NET, а звук потім відтворюється як PCM.
    /// </summary>
    public static class AudioDecoder
    {
        public const int MaxSeconds = 240;

        public static DecodedAudio Decode(string path)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex) { throw new AudioDecodeException("не вдалося прочитати файл: " + ex.Message); }
            return Decode(bytes);
        }

        public static DecodedAudio Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) throw new AudioDecodeException("файл порожній");
            if (bytes.Length < 64) throw new AudioDecodeException("файл занадто малий, щоб бути звуковим");
            return IsWav(bytes) ? DecodeWav(bytes) : DecodeMp3(bytes);
        }

        public static bool IsWav(byte[] b)
        {
            return b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F'
                   && b[8] == 'W' && b[9] == 'A' && b[10] == 'V' && b[11] == 'E';
        }

        // ---------------- MP3 ----------------

        static DecodedAudio DecodeMp3(byte[] bytes)
        {
            RequireMpegFrames(bytes);
            try { return DecodeMp3Core(bytes); }
            catch (AudioDecodeException) { throw; }
            catch (Exception ex)
            {
                throw new AudioDecodeException("файл не схожий на MP3 або WAV, або пошкоджений (" + ex.GetType().Name + ": " + ex.Message + ")");
            }
        }

        // ---- Перевірка, що це справді MP3 ----
        // Декодер NLayer «знаходить» хибні заголовки навіть у випадкових даних і відтворив би гучний шум
        // (так звучав би M4A/OGG/FLAC, перейменований у .mp3). Тому спершу вимагаємо ланцюжок справжніх кадрів MPEG.

        static readonly int[] BrV1L1 = { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 };
        static readonly int[] BrV1L2 = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 };
        static readonly int[] BrV1L3 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
        static readonly int[] BrV2L1 = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256 };
        static readonly int[] BrV2L23 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 };
        static readonly int[] RateV1 = { 44100, 48000, 32000 };
        static readonly int[] RateV2 = { 22050, 24000, 16000 };
        static readonly int[] RateV25 = { 11025, 12000, 8000 };

        /// <summary>Довжина кадру MPEG в байтах або -1, якщо за цим зсувом немає коректного заголовка.</summary>
        public static int FrameLength(byte[] b, int i)
        {
            if (i < 0 || i + 4 > b.Length) return -1;
            if (b[i] != 0xFF || (b[i + 1] & 0xE0) != 0xE0) return -1;
            int ver = (b[i + 1] >> 3) & 3;     // 0 = MPEG 2.5, 1 = зарезервовано, 2 = MPEG 2, 3 = MPEG 1
            int layer = (b[i + 1] >> 1) & 3;   // 1 = III, 2 = II, 3 = I
            int bi = (b[i + 2] >> 4) & 0xF;
            int si = (b[i + 2] >> 2) & 3;
            int pad = (b[i + 2] >> 1) & 1;
            if (ver == 1 || layer == 0 || bi == 0 || bi == 15 || si == 3) return -1;

            int[] table = ver == 3 ? (layer == 3 ? BrV1L1 : (layer == 2 ? BrV1L2 : BrV1L3))
                                   : (layer == 3 ? BrV2L1 : BrV2L23);
            int bitrate = table[bi] * 1000;
            int rate = ver == 3 ? RateV1[si] : (ver == 2 ? RateV2[si] : RateV25[si]);
            if (layer == 3) return (12 * bitrate / rate + pad) * 4;
            int coef = (layer == 1 && ver != 3) ? 72 : 144;
            return coef * bitrate / rate + pad;
        }

        static int SkipId3(byte[] b)
        {
            if (b.Length > 10 && b[0] == 'I' && b[1] == 'D' && b[2] == '3')
            {
                int size = ((b[6] & 0x7F) << 21) | ((b[7] & 0x7F) << 14) | ((b[8] & 0x7F) << 7) | (b[9] & 0x7F);
                int footer = (b[5] & 0x10) != 0 ? 10 : 0;
                return Math.Min(b.Length, 10 + size + footer);
            }
            return 0;
        }

        static void RequireMpegFrames(byte[] b)
        {
            int start = SkipId3(b);
            int limit = Math.Min(b.Length - 4, start + 65536);
            for (int i = start; i < limit; i++)
            {
                if (b[i] != 0xFF) continue;
                int pos = i, chain = 0;
                while (chain < 3)
                {
                    int len = FrameLength(b, pos);
                    if (len <= 0) break;
                    chain++;
                    pos += len;
                }
                if (chain >= 3) return;
            }
            throw new AudioDecodeException("файл не схожий на MP3 (не знайдено кадрів MPEG) — це може бути інший формат (M4A, OGG, FLAC…) із розширенням .mp3. Збережіть звук як MP3 або WAV");
        }

        static DecodedAudio DecodeMp3Core(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes, false))
            using (var mpeg = new NLayer.MpegFile(ms))
            {
                int rate = mpeg.SampleRate;
                int ch = mpeg.Channels;
                if (rate < 8000 || rate > 96000 || ch < 1 || ch > 2)
                    throw new AudioDecodeException("непідтримуваний формат MP3 (" + rate + " Гц, каналів: " + ch + ")");

                long max = (long)rate * ch * MaxSeconds;
                var samples = new short[(int)Math.Min(max, (long)rate * ch * 8)];
                var buf = new float[4096 * ch];
                int total = 0, n;
                while ((n = mpeg.ReadSamples(buf, 0, buf.Length)) > 0)
                {
                    if (total + n > max)
                        throw new AudioDecodeException("запис задовгий (понад " + (MaxSeconds / 60) + " хв) для вбудованого відтворення");
                    if (total + n > samples.Length)
                        Array.Resize(ref samples, (int)Math.Min(max, Math.Max((long)samples.Length * 2, (long)total + n)));
                    for (int i = 0; i < n; i++) samples[total + i] = Pcm.Clip(buf[i] * 32767f);
                    total += n;
                }
                if (total < rate * ch / 20) throw new AudioDecodeException("у файлі не знайдено звукових даних MP3");
                if (total != samples.Length) Array.Resize(ref samples, total);
                return new DecodedAudio { SampleRate = rate, Channels = ch, Samples = samples, Format = "MP3" };
            }
        }

        // ---------------- WAV ----------------

        static DecodedAudio DecodeWav(byte[] b)
        {
            int pos = 12;
            int tag = 0, channels = 0, rate = 0, bits = 0;
            int dataPos = -1, dataLen = 0;
            bool haveFmt = false;
            while (pos + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, pos, 4);
                long size = BitConverter.ToUInt32(b, pos + 4);
                int body = pos + 8;
                if (id == "fmt ")
                {
                    if (size < 16 || body + 16 > b.Length) throw new AudioDecodeException("пошкоджений заголовок WAV");
                    tag = BitConverter.ToUInt16(b, body);
                    channels = BitConverter.ToUInt16(b, body + 2);
                    rate = (int)BitConverter.ToUInt32(b, body + 4);
                    bits = BitConverter.ToUInt16(b, body + 14);
                    if (tag == 0xFFFE && size >= 26 && body + 26 <= b.Length) tag = BitConverter.ToUInt16(b, body + 24);
                    haveFmt = true;
                }
                else if (id == "data")
                {
                    dataPos = body;
                    long avail = b.Length - body;
                    dataLen = (int)((size == 0 || size > avail) ? avail : size);
                    break;
                }
                long next = body + size + (size & 1);
                if (next > b.Length) break;
                pos = (int)next;
            }
            if (!haveFmt || dataPos < 0) throw new AudioDecodeException("у файлі WAV немає даних звуку");
            if (channels < 1 || channels > 2) throw new AudioDecodeException("WAV: підтримується лише моно та стерео (у файлі каналів: " + channels + ")");
            if (rate < 8000 || rate > 192000) throw new AudioDecodeException("WAV: непідтримувана частота " + rate + " Гц");

            bool isFloat = tag == 3;
            if (!(tag == 1 || isFloat)) throw new AudioDecodeException("WAV: стиснений формат (код " + tag + ") не підтримується — збережіть як PCM або MP3");
            if (isFloat ? bits != 32 : !(bits == 8 || bits == 16 || bits == 24 || bits == 32))
                throw new AudioDecodeException("WAV: непідтримувана розрядність " + bits + " біт");

            int bytesPer = bits / 8;
            int count = dataLen / bytesPer;
            count -= count % channels;
            if (count / channels > (long)rate * MaxSeconds) throw new AudioDecodeException("запис задовгий (понад " + (MaxSeconds / 60) + " хв) для вбудованого відтворення");
            if (count < rate * channels / 20) throw new AudioDecodeException("у файлі WAV немає даних звуку");

            var s = new short[count];
            int p = dataPos;
            for (int i = 0; i < count; i++, p += bytesPer)
            {
                if (isFloat) s[i] = Pcm.Clip(BitConverter.ToSingle(b, p) * 32767f);
                else if (bits == 16) s[i] = (short)(b[p] | (b[p + 1] << 8));
                else if (bits == 8) s[i] = (short)((b[p] - 128) << 8);
                else if (bits == 24) s[i] = (short)((b[p + 1]) | (b[p + 2] << 8));
                else s[i] = (short)(BitConverter.ToInt32(b, p) >> 16);
            }
            return new DecodedAudio { SampleRate = rate, Channels = channels, Samples = s, Format = "WAV" };
        }
    }

    public static class Pcm
    {
        public static short Clip(float v)
        {
            int i = (int)Math.Round(v);
            return (short)(i > 32767 ? 32767 : (i < -32768 ? -32768 : i));
        }

        /// <summary>Стандартний WAV (RIFF, PCM 16 біт) із застосуванням гучності gain (0…1).</summary>
        public static byte[] ToWav(DecodedAudio a, float gain)
        {
            int dataBytes = a.Samples.Length * 2;
            var b = new byte[44 + dataBytes];
            Put(b, 0, "RIFF");
            PutInt(b, 4, 36 + dataBytes);
            Put(b, 8, "WAVE");
            Put(b, 12, "fmt ");
            PutInt(b, 16, 16);
            PutShort(b, 20, 1);
            PutShort(b, 22, a.Channels);
            PutInt(b, 24, a.SampleRate);
            PutInt(b, 28, a.SampleRate * a.Channels * 2);
            PutShort(b, 32, a.Channels * 2);
            PutShort(b, 34, 16);
            Put(b, 36, "data");
            PutInt(b, 40, dataBytes);
            int o = 44;
            bool unity = Math.Abs(gain - 1f) < 0.0001f;
            for (int i = 0; i < a.Samples.Length; i++, o += 2)
            {
                short v = unity ? a.Samples[i] : Clip(a.Samples[i] * gain);
                b[o] = (byte)(v & 0xFF);
                b[o + 1] = (byte)((v >> 8) & 0xFF);
            }
            return b;
        }

        static void Put(byte[] b, int o, string s) { for (int i = 0; i < s.Length; i++) b[o + i] = (byte)s[i]; }
        static void PutInt(byte[] b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
        static void PutShort(byte[] b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
    }

    public static class Tones
    {
        /// <summary>Різкий тривожний сигнал на випадок, коли голосовий файл відтворити не вдалося (не схожий на звичайний дзвінок Windows).</summary>
        public static DecodedAudio AlertBeeps()
        {
            const int rate = 22050;
            var list = new List<short>();
            for (int rep = 0; rep < 3; rep++)
            {
                AddTone(list, rate, 880, 220);
                AddTone(list, rate, 660, 220);
                AddTone(list, rate, 0, 140);
            }
            return new DecodedAudio { SampleRate = rate, Channels = 1, Samples = list.ToArray(), Format = "TONE" };
        }

        static void AddTone(List<short> list, int rate, double hz, int ms)
        {
            int n = rate * ms / 1000;
            int fade = Math.Min(n / 2, rate / 100);
            for (int i = 0; i < n; i++)
            {
                double env = i < fade ? (double)i / fade : (i > n - fade ? (double)(n - i) / fade : 1.0);
                double v = hz <= 0 ? 0 : Math.Sin(2 * Math.PI * hz * i / rate) * 0.6 * env;
                list.Add(Pcm.Clip((float)(v * 32767)));
            }
        }
    }
}
