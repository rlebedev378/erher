using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Varta.Core;
using Xunit;

namespace Varta.Tests
{
    public class AudioDecoderTests
    {
        static string Audio(string name) { return Path.Combine(AppContext.BaseDirectory, "Fixtures", "audio", name); }
        static string Default(string name) { return Path.Combine(AppContext.BaseDirectory, "Fixtures", "default_sounds", name); }

        // Еталон: ffprobe/ffmpeg для тих самих файлів
        [Theory]
        [InlineData("red_alert.mp3", 44100, 1, 18868, 0.0814)]
        [InlineData("yellow_alert.mp3", 44100, 1, 10659, 0.0738)]
        [InlineData("downgrade_yellow.mp3", 44100, 1, 11810, 0.0767)]
        [InlineData("all_clear.mp3", 44100, 1, 15982, 0.0792)]
        public void Default_voice_files_decode_to_the_expected_length_and_loudness(string name, int rate, int ch, int ms, double rms)
        {
            DecodedAudio a = AudioDecoder.Decode(Default(name));
            Assert.Equal(rate, a.SampleRate);
            Assert.Equal(ch, a.Channels);
            Assert.InRange(a.DurationMs, ms - 80, ms + 80);
            Assert.InRange(a.Rms(), rms * 0.9, rms * 1.1);
            Assert.Equal("MP3", a.Format);
        }

        [Theory]
        [InlineData("mono22k_id3_cover.mp3", 22050, 1, 2000, 0.0998)]
        [InlineData("mpeg2_24k_mono.mp3", 24000, 1, 2000, 0.1001)]
        [InlineData("stereo48k_vbr.mp3", 48000, 2, 2000, 0.0746)]
        [InlineData("cbr128_44k_joint.mp3", 44100, 2, 2000, 0.0708)]
        public void Common_mp3_flavours_decode_correctly(string name, int rate, int ch, int ms, double rms)
        {
            DecodedAudio a = AudioDecoder.Decode(Audio(name));
            Assert.Equal(rate, a.SampleRate);
            Assert.Equal(ch, a.Channels);
            Assert.InRange(a.DurationMs, ms - 90, ms + 90);
            Assert.InRange(a.Rms(), rms * 0.9, rms * 1.1);
        }

        [Theory]
        [InlineData("s16_mono16k.wav", 16000, 0.0713)]
        [InlineData("u8_mono11k.wav", 11025, 0.0709)]
        [InlineData("s24_mono22k.wav", 22050, 0.0715)]
        [InlineData("f32_mono22k.wav", 22050, 0.0715)]
        public void Wav_variants_decode_to_pcm16(string name, int rate, double rms)
        {
            DecodedAudio a = AudioDecoder.Decode(Audio(name));
            Assert.Equal("WAV", a.Format);
            Assert.Equal(rate, a.SampleRate);
            Assert.Equal(1, a.Channels);
            Assert.InRange(a.DurationMs, 995, 1005);
            Assert.InRange(a.Rms(), rms * 0.93, rms * 1.07);
        }

        [Fact]
        public void File_with_cyrillic_name_and_path_decodes()
        {
            string dir = Path.Combine(Path.GetTempPath(), "varta-ua-" + Guid.NewGuid().ToString("N"), "Папка з озвучкою");
            Directory.CreateDirectory(dir);
            try
            {
                string p = Path.Combine(dir, "Тривога — червоний рівень.mp3");
                File.Copy(Default("red_alert.mp3"), p);
                Assert.InRange(AudioDecoder.Decode(p).DurationMs, 18700, 19000);
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(dir), true); } catch { } }
        }

        [Fact]
        public void Wav_content_is_detected_even_with_mp3_extension()
        {
            string p = Path.Combine(Path.GetTempPath(), "varta-mislabeled-" + Guid.NewGuid().ToString("N") + ".mp3");
            File.Copy(Audio("s16_mono16k.wav"), p);
            try { Assert.Equal("WAV", AudioDecoder.Decode(p).Format); }
            finally { File.Delete(p); }
        }

        [Fact]
        public void Garbage_empty_and_missing_files_give_clear_errors()
        {
            Assert.Throws<AudioDecodeException>(() => AudioDecoder.Decode(new byte[0]));
            Assert.Throws<AudioDecodeException>(() => AudioDecoder.Decode(new byte[10]));
            var noise = new byte[50000];
            new Random(7).NextBytes(noise);
            AudioDecodeException ex = Assert.Throws<AudioDecodeException>(() => AudioDecoder.Decode(noise));
            Assert.NotEqual("", ex.Message);
            Assert.Throws<AudioDecodeException>(() => AudioDecoder.Decode(Path.Combine(Path.GetTempPath(), "varta-no-such-file.mp3")));
            Assert.Throws<AudioDecodeException>(() => AudioDecoder.Decode(File.ReadAllBytes(Audio("s16_mono16k.wav")).Take(40).ToArray()));
        }

        [Fact]
        public void Truncated_mp3_still_plays_what_is_there()
        {
            byte[] full = File.ReadAllBytes(Default("red_alert.mp3"));
            DecodedAudio a = AudioDecoder.Decode(full.Take(full.Length / 2).ToArray());
            Assert.InRange(a.DurationMs, 5000, 15000);
        }

        [Theory]
        [InlineData("not_mp3_aac.m4a")]
        [InlineData("not_mp3_flac.flac")]
        public void Other_formats_renamed_to_mp3_are_rejected_instead_of_played_as_noise(string name)
        {
            string p = Path.Combine(Path.GetTempPath(), "varta-fake-" + Guid.NewGuid().ToString("N") + ".mp3");
            File.Copy(Audio(name), p);
            try
            {
                AudioDecodeException ex = Assert.Throws<AudioDecodeException>(() => AudioDecoder.Decode(p));
                Assert.Contains("не схожий на MP3", ex.Message);
            }
            finally { File.Delete(p); }
        }

        [Fact]
        public void Id3_tag_with_large_cover_is_skipped_to_reach_the_first_frame()
        {
            byte[] b = File.ReadAllBytes(Audio("mono22k_id3_cover.mp3"));
            Assert.Equal((byte)'I', b[0]);
            int start = 10 + (((b[6] & 0x7F) << 21) | ((b[7] & 0x7F) << 14) | ((b[8] & 0x7F) << 7) | (b[9] & 0x7F));
            Assert.True(start > 1000, "тег має містити обкладинку");
            Assert.True(AudioDecoder.FrameLength(b, start) > 0);
        }

        [Fact]
        public void Frame_length_matches_known_headers()
        {
            // MPEG-1 Layer III, 128 кбіт/с, 44,1 кГц, без padding → 417 байт
            Assert.Equal(417, AudioDecoder.FrameLength(new byte[] { 0xFF, 0xFB, 0x90, 0x00 }, 0));
            // той самий, з padding → 418
            Assert.Equal(418, AudioDecoder.FrameLength(new byte[] { 0xFF, 0xFB, 0x92, 0x00 }, 0));
            // MPEG-2 Layer III, 32 кбіт/с, 24 кГц → 72*32000/24000 = 96
            Assert.Equal(96, AudioDecoder.FrameLength(new byte[] { 0xFF, 0xF3, 0x44, 0x00 }, 0));
            Assert.Equal(-1, AudioDecoder.FrameLength(new byte[] { 0xFF, 0xFB, 0xF0, 0x00 }, 0)); // bitrate index 15
            Assert.Equal(-1, AudioDecoder.FrameLength(new byte[] { 0x12, 0x34, 0x56, 0x78 }, 0));
        }

        [Fact]
        public void Compressed_wav_is_rejected_with_a_hint()
        {
            byte[] w = File.ReadAllBytes(Audio("s16_mono16k.wav"));
            w[20] = 0x55; w[21] = 0x00; // тег формату 0x55 (MP3-у-WAV)
            AudioDecodeException ex = Assert.Throws<AudioDecodeException>(() => AudioDecoder.Decode(w));
            Assert.Contains("PCM", ex.Message);
        }
    }

    public class PcmTests
    {
        static DecodedAudio Sample()
        {
            return new DecodedAudio { SampleRate = 8000, Channels = 2, Samples = new short[] { 0, 1000, -1000, 32767, -32768, 5 } };
        }

        [Fact]
        public void Wav_header_is_a_valid_canonical_pcm16_riff()
        {
            byte[] w = Pcm.ToWav(Sample(), 1f);
            Assert.Equal(44 + 12, w.Length);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(w, 0, 4));
            Assert.Equal(w.Length - 8, BitConverter.ToInt32(w, 4));
            Assert.Equal("WAVEfmt ", System.Text.Encoding.ASCII.GetString(w, 8, 8));
            Assert.Equal(16, BitConverter.ToInt32(w, 16));
            Assert.Equal(1, BitConverter.ToInt16(w, 20));
            Assert.Equal(2, BitConverter.ToInt16(w, 22));
            Assert.Equal(8000, BitConverter.ToInt32(w, 24));
            Assert.Equal(32000, BitConverter.ToInt32(w, 28));
            Assert.Equal(4, BitConverter.ToInt16(w, 32));
            Assert.Equal(16, BitConverter.ToInt16(w, 34));
            Assert.Equal("data", System.Text.Encoding.ASCII.GetString(w, 36, 4));
            Assert.Equal(12, BitConverter.ToInt32(w, 40));
        }

        [Fact]
        public void Wav_roundtrip_is_lossless_at_full_volume_including_extremes()
        {
            var s = new short[8000];
            for (int i = 0; i < s.Length; i++) s[i] = (short)(Math.Sin(i / 20.0) * 30000);
            s[10] = short.MaxValue; s[11] = short.MinValue; s[12] = 0;
            var src = new DecodedAudio { SampleRate = 16000, Channels = 1, Samples = s };
            DecodedAudio back = AudioDecoder.Decode(Pcm.ToWav(src, 1f));
            Assert.Equal(16000, back.SampleRate);
            Assert.Equal(1, back.Channels);
            Assert.True(s.SequenceEqual(back.Samples));
        }

        [Fact]
        public void Volume_scales_samples_and_clips_instead_of_wrapping()
        {
            byte[] half = Pcm.ToWav(Sample(), 0.5f);
            Assert.Equal(500, BitConverter.ToInt16(half, 44 + 2));
            Assert.Equal(-500, BitConverter.ToInt16(half, 44 + 4));
            Assert.Equal(16384, BitConverter.ToInt16(half, 44 + 6));
            byte[] loud = Pcm.ToWav(Sample(), 4f);
            Assert.Equal(4000, BitConverter.ToInt16(loud, 44 + 2));
            Assert.Equal(32767, BitConverter.ToInt16(loud, 44 + 6));
            Assert.Equal(-32768, BitConverter.ToInt16(loud, 44 + 8));
            byte[] mute = Pcm.ToWav(Sample(), 0f);
            for (int i = 44; i < mute.Length; i++) Assert.Equal(0, mute[i]);
        }

        [Fact]
        public void Real_file_goes_through_wav_and_back_unchanged()
        {
            DecodedAudio a = AudioDecoder.Decode(Path.Combine(AppContext.BaseDirectory, "Fixtures", "default_sounds", "yellow_alert.mp3"));
            DecodedAudio b = AudioDecoder.Decode(Pcm.ToWav(a, 1f));
            Assert.Equal(a.SampleRate, b.SampleRate);
            Assert.Equal(a.Samples.Length, b.Samples.Length);
            Assert.True(a.Samples.SequenceEqual(b.Samples));
        }

        [Fact]
        public void Fallback_alert_tone_is_audible_distinct_and_short()
        {
            DecodedAudio t = Tones.AlertBeeps();
            Assert.InRange(t.DurationMs, 1500, 2500);
            Assert.InRange(t.Rms(), 0.1, 0.6);
            byte[] w = Pcm.ToWav(t, 1f);
            Assert.Equal(1, BitConverter.ToInt16(w, 22));
        }
    }

    public class PlaybackChainTests
    {
        sealed class Fake : IPlayBackend
        {
            readonly Func<BackendResult> _f;
            public int Calls;
            public Fake(string name, Func<BackendResult> f) { Name = name; _f = f; }
            public string Name { get; private set; }
            public BackendResult Play(string file, int volumePercent, Func<bool> cancelled, Action tick) { Calls++; return _f(); }
        }

        [Fact]
        public void First_working_backend_wins_and_later_ones_are_not_touched()
        {
            var a = new Fake("A", () => BackendResult.Success("A", 1500));
            var b = new Fake("B", () => BackendResult.Success("B", 1));
            BackendResult r = PlaybackChain.Play(new IPlayBackend[] { a, b }, "x", 100, () => false, null);
            Assert.True(r.Ok);
            Assert.Equal("A", r.Backend);
            Assert.Equal(0, b.Calls);
        }

        [Fact]
        public void Failure_falls_through_to_the_next_backend()
        {
            var a = new Fake("A", () => BackendResult.Fail("A", "не відкрився"));
            var b = new Fake("B", () => BackendResult.Success("B", 900));
            BackendResult r = PlaybackChain.Play(new IPlayBackend[] { a, b }, "x", 100, () => false, null);
            Assert.True(r.Ok);
            Assert.Equal("B", r.Backend);
        }

        [Fact]
        public void A_throwing_backend_is_treated_as_failure()
        {
            var a = new Fake("A", () => { throw new InvalidOperationException("boom"); });
            var b = new Fake("B", () => BackendResult.Success("B", 10));
            Assert.True(PlaybackChain.Play(new IPlayBackend[] { a, b }, "x", 100, () => false, null).Ok);
        }

        [Fact]
        public void When_everything_fails_all_reasons_are_reported()
        {
            var a = new Fake("NLayer", () => BackendResult.Fail("NLayer", "не MP3"));
            var b = new Fake("MCI", () => BackendResult.Fail("MCI", "драйвер не завантажується"));
            BackendResult r = PlaybackChain.Play(new IPlayBackend[] { a, b }, "x", 100, () => false, null);
            Assert.False(r.Ok);
            Assert.Contains("NLayer: не MP3", r.Error);
            Assert.Contains("MCI: драйвер не завантажується", r.Error);
        }

        [Fact]
        public void Cancellation_stops_the_chain_without_error()
        {
            var a = new Fake("A", () => BackendResult.Cancel("A"));
            var b = new Fake("B", () => BackendResult.Success("B", 1));
            BackendResult r = PlaybackChain.Play(new IPlayBackend[] { a, b }, "x", 100, () => false, null);
            Assert.True(r.Cancelled);
            Assert.Equal(0, b.Calls);

            var c = new Fake("C", () => BackendResult.Fail("C", "e"));
            var d = new Fake("D", () => BackendResult.Success("D", 1));
            BackendResult r2 = PlaybackChain.Play(new IPlayBackend[] { c, d }, "x", 100, () => true, null);
            Assert.True(r2.Cancelled);
            Assert.Equal(0, d.Calls);
        }
    }

    public class DecodeCacheTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "varta-cache-" + Guid.NewGuid().ToString("N"));
        int _decodes;

        public DecodeCacheTests() { Directory.CreateDirectory(_dir); }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        string Make(string name, string content)
        {
            string p = Path.Combine(_dir, name);
            File.WriteAllText(p, content);
            return p;
        }

        DecodeCache Cache()
        {
            return new DecodeCache(p => { _decodes++; return new DecodedAudio { SampleRate = 8000, Channels = 1, Samples = new short[800] }; });
        }

        [Fact]
        public void Second_request_for_unchanged_file_is_served_from_memory()
        {
            DecodeCache c = Cache();
            string f = Make("a.mp3", "1");
            DecodedAudio x = c.Get(f);
            Assert.Same(x, c.Get(f));
            Assert.Equal(1, _decodes);
        }

        [Fact]
        public void Changed_file_is_decoded_again_and_the_old_version_is_dropped()
        {
            DecodeCache c = Cache();
            string f = Make("a.mp3", "1");
            c.Get(f);
            File.WriteAllText(f, "22222");
            File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(5));
            c.Get(f);
            Assert.Equal(2, _decodes);
            Assert.Equal(1, c.Count);
        }

        [Fact]
        public void Cache_is_bounded()
        {
            DecodeCache c = Cache();
            for (int i = 0; i < 12; i++) c.Get(Make("f" + i + ".mp3", "x" + i));
            Assert.Equal(8, c.Count);
        }

        [Fact]
        public void Missing_file_is_reported_not_cached()
        {
            DecodeCache c = Cache();
            Assert.Throws<AudioDecodeException>(() => c.Get(Path.Combine(_dir, "nope.mp3")));
            Assert.Equal(0, c.Count);
        }
    }
}
