using System;
using System.IO;
using Varta.Core;
using Varta.Platform;
using Xunit;

namespace Varta.Tests
{
    public sealed class SoundFilesTests : IDisposable
    {
        readonly string _root = Path.Combine(Path.GetTempPath(), "varta-test-" + Guid.NewGuid().ToString("N"));
        readonly string _app;
        readonly string _incoming;

        public SoundFilesTests()
        {
            _app = Path.Combine(_root, "app");
            _incoming = Path.Combine(_root, "incoming");
            Directory.CreateDirectory(Path.Combine(_app, "sounds"));
            Directory.CreateDirectory(_incoming);
            Paths.SetRoots(_app, _app);
            File.WriteAllText(Path.Combine(_app, "sounds", "red_alert.mp3"), "OLD");
        }

        public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

        string Incoming(string name, string content)
        {
            string p = Path.Combine(_incoming, name);
            File.WriteAllText(p, content);
            return p;
        }

        [Fact]
        public void New_file_is_copied_into_sounds_and_stored_relative()
        {
            SoundImportResult r = SoundFiles.Import(Incoming("my_siren.mp3", "NEW"));
            Assert.True(r.Copied);
            Assert.Equal("sounds\\my_siren.mp3", r.Stored);
            Assert.Equal("NEW", File.ReadAllText(Paths.Resolve(r.Stored)));
            Assert.Equal("", r.Warning);
        }

        [Fact]
        public void Same_name_with_different_content_never_overwrites_and_resolves_to_the_new_file()
        {
            SoundImportResult r = SoundFiles.Import(Incoming("red_alert.mp3", "MY RECORDING"));
            Assert.Equal("sounds\\red_alert_2.mp3", r.Stored);
            Assert.Equal("OLD", File.ReadAllText(Path.Combine(_app, "sounds", "red_alert.mp3")));
            Assert.Equal("MY RECORDING", File.ReadAllText(Paths.Resolve(r.Stored)));

            SoundImportResult r3 = SoundFiles.Import(Incoming("red_alert.mp3", "THIRD"));
            Assert.Equal("sounds\\red_alert_3.mp3", r3.Stored);
        }

        [Fact]
        public void Importing_identical_file_twice_creates_no_duplicate()
        {
            string src = Incoming("voice.mp3", "SAME");
            SoundImportResult a = SoundFiles.Import(src);
            SoundImportResult b = SoundFiles.Import(src);
            Assert.True(a.Copied);
            Assert.False(b.Copied);
            Assert.Equal(a.Stored, b.Stored);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(_app, "sounds")).Length);
        }

        [Fact]
        public void File_already_in_sounds_is_used_in_place()
        {
            string inside = Path.Combine(_app, "sounds", "own.mp3");
            File.WriteAllText(inside, "OWN");
            SoundImportResult r = SoundFiles.Import(inside);
            Assert.False(r.Copied);
            Assert.Equal("sounds\\own.mp3", r.Stored);
        }

        [Fact]
        public void Copy_failure_falls_back_to_original_path_and_warns()
        {
            string app2 = Path.Combine(_root, "app2");
            Directory.CreateDirectory(app2);
            File.WriteAllText(Path.Combine(app2, "sounds"), "i am a file, not a folder");
            Paths.SetRoots(app2, app2);
            string src = Incoming("x.mp3", "X");
            SoundImportResult r = SoundFiles.Import(src);
            Assert.False(r.Copied);
            Assert.Equal(src, r.Stored);
            Assert.NotEqual("", r.Warning);
        }

        [Fact]
        public void Chosen_file_survives_settings_roundtrip()
        {
            SoundImportResult r = SoundFiles.Import(Incoming("кастомна тривога.mp3", "UA"));
            var s = new AppSettings { SoundRed = r.Stored };
            AppSettings back = SettingsJson.FromJson(SettingsJson.ToJson(s), null);
            Assert.Equal("sounds\\кастомна тривога.mp3", back.SoundRed);
            Assert.Equal("UA", File.ReadAllText(Paths.Resolve(back.SoundRed)));
        }
    }
}
