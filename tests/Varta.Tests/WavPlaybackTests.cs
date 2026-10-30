using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Varta.Core;
using Xunit;

namespace Varta.Tests
{
    public class WavPlaybackTests
    {
        sealed class FakeSink : IWavSink
        {
            readonly ManualResetEvent _stopped = new ManualResetEvent(false);
            public int PlayMs = 1200;
            public bool BlockUntilStop;
            public bool IgnoreStop;
            public Exception LoadError, PlayError;
            public bool WasStopped, Disposed, WasLoaded;
            public string Label { get { return "Fake"; } }

            public void Load(byte[] wav) { if (LoadError != null) throw LoadError; WasLoaded = true; }

            public void PlaySync()
            {
                if (PlayError != null) throw PlayError;
                if (BlockUntilStop) { _stopped.WaitOne(IgnoreStop ? 4500 : 20000); return; }
                _stopped.WaitOne(PlayMs);
            }

            public void Stop() { WasStopped = true; if (!IgnoreStop) _stopped.Set(); }
            public void Dispose() { Disposed = true; }
        }

        static BackendResult Run(FakeSink s, int durationMs, Func<bool> cancelled = null, Action tick = null, int timeoutExtra = 15000)
        {
            return WavPlayback.Play("T", s, new byte[44], durationMs, cancelled ?? (() => false), tick, timeoutExtra);
        }

        [Fact]
        public void Normal_playback_succeeds_and_cleans_up()
        {
            var s = new FakeSink { PlayMs = 1200 };
            BackendResult r = Run(s, 1200);
            Assert.True(r.Ok);
            Assert.Equal(1200, r.DurationMs);
            Assert.True(s.WasLoaded);
            Assert.True(s.Disposed);
            Assert.False(s.WasStopped);
        }

        [Fact]
        public void Sound_that_ends_immediately_is_reported_as_failure()
        {
            var s = new FakeSink { PlayMs = 50 };
            BackendResult r = Run(s, 3000);
            Assert.False(r.Ok);
            Assert.Contains("завершився одразу", r.Error);
        }

        [Fact]
        public void Very_short_clips_are_not_flagged_as_too_fast()
        {
            var s = new FakeSink { PlayMs = 50 };
            Assert.True(Run(s, 600).Ok);
        }

        [Fact]
        public void Cancellation_stops_the_sound_quickly_and_reports_cancel_not_error()
        {
            var s = new FakeSink { BlockUntilStop = true };
            var sw = Stopwatch.StartNew();
            DateTime start = DateTime.UtcNow;
            BackendResult r = Run(s, 10000, () => (DateTime.UtcNow - start).TotalMilliseconds > 250);
            sw.Stop();
            Assert.True(r.Cancelled);
            Assert.False(r.Ok);
            Assert.Equal("", r.Error);
            Assert.True(s.WasStopped);
            Assert.True(sw.ElapsedMilliseconds < 1500, "скасування має спрацьовувати швидко, зайняло " + sw.ElapsedMilliseconds + " мс");
        }

        [Fact]
        public void Exception_during_playback_becomes_a_clear_error()
        {
            var s = new FakeSink { PlayError = new InvalidOperationException("пристрій зайнятий") };
            BackendResult r = Run(s, 3000);
            Assert.False(r.Ok);
            Assert.Contains("InvalidOperationException", r.Error);
            Assert.Contains("пристрій зайнятий", r.Error);
            Assert.True(s.Disposed);
        }

        [Fact]
        public void Load_failure_is_reported_with_the_sink_label_and_nothing_plays()
        {
            var s = new FakeSink { LoadError = new InvalidOperationException("The wave header is corrupt.") };
            BackendResult r = Run(s, 3000);
            Assert.False(r.Ok);
            Assert.StartsWith("Fake: ", r.Error);
            Assert.Contains("wave header", r.Error);
            Assert.True(s.Disposed);
        }

        [Fact]
        public void Playback_that_never_finishes_is_stopped_by_the_timeout()
        {
            var s = new FakeSink { BlockUntilStop = true };
            var sw = Stopwatch.StartNew();
            BackendResult r = Run(s, 200, timeoutExtra: 400);
            sw.Stop();
            Assert.False(r.Ok);
            Assert.Contains("не завершилось вчасно", r.Error);
            Assert.True(s.WasStopped);
            Assert.True(sw.ElapsedMilliseconds < 2500);
        }

        [Fact]
        public void Tick_is_called_repeatedly_during_playback()
        {
            var s = new FakeSink { PlayMs = 1200 };
            int ticks = 0;
            Assert.True(Run(s, 1200, tick: () => ticks++).Ok);
            Assert.InRange(ticks, 8, 20);
        }

        [Fact]
        public void Sink_that_cannot_be_stopped_is_reported_instead_of_hanging_forever()
        {
            var s = new FakeSink { BlockUntilStop = true, IgnoreStop = true };
            var sw = Stopwatch.StartNew();
            BackendResult r = Run(s, 100, () => true);
            sw.Stop();
            Assert.True(r.Cancelled);
            Assert.True(sw.ElapsedMilliseconds < 5000);
            Assert.False(s.Disposed); // потік ще всередині PlaySync — звільняти небезпечно
        }
    }
}
