using System;
using System.Collections.Generic;
using System.IO;
using Varta.Core;
using Varta.Platform;
using Xunit;

namespace Varta.Tests
{
    /// <summary>Усе зберігається в папці програми: налаштування, звуки, перенесення зі старого місця, автозапуск.</summary>
    [Collection("Paths")]
    public sealed class PortableStorageTests : IDisposable
    {
        readonly string _root = Path.Combine(Path.GetTempPath(), "varta-portable-" + Guid.NewGuid().ToString("N"));
        readonly string _app;
        readonly string _legacy;

        public PortableStorageTests()
        {
            _app = Path.Combine(_root, "Varta");
            _legacy = Path.Combine(_root, "AppData", "Varta");
            Directory.CreateDirectory(_app);
            Paths.SetRoots(_app, _app);
            Paths.SetLegacyDataDir(_legacy);
        }

        public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

        [Fact]
        public void Settings_are_saved_next_to_the_program_with_previous_version_as_bak()
        {
            SettingsStore.Save(new AppSettings { AnnounceVolumePercent = 40, FirstRunDone = true });
            SettingsStore.Save(new AppSettings { AnnounceVolumePercent = 55, FirstRunDone = true });

            Assert.Equal(Path.Combine(_app, "settings.json"), Paths.SettingsFile);
            Assert.True(File.Exists(Path.Combine(_app, "settings.json")));
            Assert.False(File.Exists(Path.Combine(_app, "settings.json.tmp")));

            List<string> warn;
            bool existed;
            AppSettings s = SettingsStore.Load(out warn, out existed);
            Assert.True(existed);
            Assert.Empty(warn);
            Assert.Equal(55, s.AnnounceVolumePercent);
            Assert.Equal(40, SettingsJson.FromJson(File.ReadAllText(Path.Combine(_app, "settings.json.bak")), null).AnnounceVolumePercent);
        }

        [Fact]
        public void Missing_settings_file_is_restored_from_bak()
        {
            File.WriteAllText(Path.Combine(_app, "settings.json.bak"), SettingsJson.ToJson(new AppSettings { AnnounceVolumePercent = 35, FirstRunDone = true }));
            List<string> warn;
            bool existed;
            AppSettings s = SettingsStore.Load(out warn, out existed);
            Assert.True(existed);
            Assert.Equal(35, s.AnnounceVolumePercent);
            Assert.Single(warn);
        }

        [Fact]
        public void Damaged_settings_fall_back_to_previous_saved_version()
        {
            File.WriteAllText(Path.Combine(_app, "settings.json.bak"), SettingsJson.ToJson(new AppSettings { AnnounceVolumePercent = 65 }));
            File.WriteAllText(Path.Combine(_app, "settings.json"), "{ зламаний json");
            List<string> warn;
            bool existed;
            AppSettings s = SettingsStore.Load(out warn, out existed);
            Assert.Equal(65, s.AnnounceVolumePercent);
            Assert.Contains(warn, w => w.Contains("settings.json.bak"));
            Assert.Single(Directory.GetFiles(_app, "settings.damaged-*.json"));
        }

        [Fact]
        public void Old_volume_boost_keys_are_ignored_and_dropped_on_save()
        {
            string old = "{ \"AnnounceVolumePercent\": 60, \"BoostMasterVolume\": true, \"MinMasterVolumePercent\": 70, \"UnmuteMaster\": true }";
            var warn = new List<string>();
            AppSettings s = SettingsJson.FromJson(old, warn);
            Assert.Empty(warn);
            Assert.Equal(60, s.AnnounceVolumePercent);
            string json = SettingsJson.ToJson(s);
            Assert.DoesNotContain("BoostMasterVolume", json);
            Assert.DoesNotContain("MinMasterVolumePercent", json);
            Assert.DoesNotContain("UnmuteMaster", json);
        }

        [Fact]
        public void Legacy_appdata_settings_and_sounds_move_into_the_program_folder()
        {
            Directory.CreateDirectory(Path.Combine(_legacy, "sounds"));
            File.WriteAllText(Path.Combine(_legacy, "settings.json"), SettingsJson.ToJson(new AppSettings { SoundRed = "sounds\\мій запис.mp3", FirstRunDone = true }));
            File.WriteAllText(Path.Combine(_legacy, "sounds", "мій запис.mp3"), "MINE");
            Directory.CreateDirectory(Path.Combine(_app, "sounds"));
            File.WriteAllText(Path.Combine(_app, "sounds", "red_alert.mp3"), "KEEP");
            File.WriteAllText(Path.Combine(_legacy, "sounds", "red_alert.mp3"), "LEGACY");

            string msg = Paths.MigrateLegacyData();

            Assert.NotNull(msg);
            Assert.True(File.Exists(Path.Combine(_app, "settings.json")));
            Assert.Equal("MINE", File.ReadAllText(Path.Combine(_app, "sounds", "мій запис.mp3")));
            Assert.Equal("KEEP", File.ReadAllText(Path.Combine(_app, "sounds", "red_alert.mp3")));
            List<string> warn;
            bool existed;
            AppSettings s = SettingsStore.Load(out warn, out existed);
            Assert.Equal("MINE", File.ReadAllText(Paths.Resolve(s.SoundRed)));
            Assert.Null(Paths.MigrateLegacyData());
        }

        [Fact]
        public void Migration_never_replaces_settings_already_in_the_program_folder()
        {
            Directory.CreateDirectory(_legacy);
            File.WriteAllText(Path.Combine(_legacy, "settings.json"), SettingsJson.ToJson(new AppSettings { AnnounceVolumePercent = 20 }));
            SettingsStore.Save(new AppSettings { AnnounceVolumePercent = 90 });
            Assert.Null(Paths.MigrateLegacyData());
            List<string> warn;
            bool existed;
            Assert.Equal(90, SettingsStore.Load(out warn, out existed).AnnounceVolumePercent);
        }

        [Fact]
        public void Running_from_temp_folder_is_detected()
        {
            Assert.True(Paths.IsTemporaryPath(Path.Combine(Path.GetTempPath(), "Temp1_Varta-portable.zip", "Varta")));
            Assert.False(Paths.IsTemporaryPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Varta")));
            Assert.False(Paths.IsTemporaryPath(""));
        }

        [Fact]
        public void Missing_default_sounds_are_restored_without_touching_user_files()
        {
            Directory.CreateDirectory(Path.Combine(_app, "sounds"));
            File.WriteAllText(Path.Combine(_app, "sounds", "red_alert.mp3"), "MY RECORDING");

            Assert.Equal(4, SoundFiles.EnsureDefaults());
            Assert.Equal("MY RECORDING", File.ReadAllText(Path.Combine(_app, "sounds", "red_alert.mp3")));
            string shipped = Path.Combine(AppContext.BaseDirectory, "Fixtures", "default_sounds", "yellow_alert.mp3");
            Assert.Equal(File.ReadAllBytes(shipped), File.ReadAllBytes(Path.Combine(_app, "sounds", "yellow_alert.mp3")));
            Assert.Empty(Directory.GetFiles(Path.Combine(_app, "sounds"), "*.tmp"));
            Assert.Equal(0, SoundFiles.EnsureDefaults());
        }

        [Fact]
        public void Autostart_command_points_to_this_exe_and_is_parsed_back()
        {
            string exe = "C:\\Program Files\\Varta\\Varta.exe";
            Assert.Equal("\"C:\\Program Files\\Varta\\Varta.exe\" --minimized", Autostart.BuildCommand(exe));
            Assert.Equal(exe, Autostart.ExeFromCommand(Autostart.BuildCommand(exe)));
            Assert.Equal(exe, Autostart.ExeFromCommand(exe + " --minimized"));
            Assert.Equal("C:\\Varta\\Varta.exe", Autostart.ExeFromCommand("\"C:\\Varta\\Varta.exe\""));
            Assert.Equal("", Autostart.ExeFromCommand("  "));
        }

        [Fact]
        public void Token_pasted_with_bearer_prefix_or_quotes_is_cleaned()
        {
            Assert.Equal("abc123", AppSettings.CleanToken("Bearer abc123"));
            Assert.Equal("abc123", AppSettings.CleanToken("\"abc123\"\r\n"));
            Assert.Equal("abc123", AppSettings.CleanToken("  abc123 "));
            Assert.Equal("", AppSettings.CleanToken(null));
        }

        [Fact]
        public void Http_403_is_reported_as_blocked_access_not_as_bad_key()
        {
            var http = new FakeHttp { OnGet = u => new HttpResult { Status = 403, Body = "" } };
            var src = new AlertsInUaSource(() => new AppSettings { AlertsInUaToken = "t" }, http, () => DateTime.UtcNow);
            SourceReading r = src.Fetch();
            Assert.Equal(ErrorKind.Forbidden, r.ErrorKind);
            Assert.Contains("403", r.Error);
        }
    }
}
