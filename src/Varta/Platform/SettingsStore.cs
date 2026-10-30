using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Varta.Core;

namespace Varta.Platform
{
    /// <summary>settings.json у папці програми; попередня версія файлу — settings.json.bak.</summary>
    public static class SettingsStore
    {
        public static AppSettings Load(out List<string> warnings, out bool existed)
        {
            warnings = new List<string>();
            string path = Paths.SettingsFile;
            string bak = path + ".bak";
            existed = File.Exists(path);
            if (!existed)
            {
                // Збій між записом і заміною файлу: лишилась тільки попередня копія.
                AppSettings fromBak = TryRead(bak, warnings);
                if (fromBak == null) return NewDefaults();
                existed = true;
                warnings.Add("Файл settings.json не знайдено — налаштування відновлено з резервної копії settings.json.bak.");
                return fromBak;
            }
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                return SettingsJson.FromJson(json, warnings);
            }
            catch (Exception ex)
            {
                string backup = Path.Combine(Paths.DataDir, "settings.damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                try { File.Copy(path, backup, true); } catch { }
                AppSettings fromBak = TryRead(bak, warnings);
                warnings.Add("Файл налаштувань пошкоджено (" + ex.Message + "). " +
                             (fromBak != null ? "Відновлено попередню збережену версію (settings.json.bak)" : "Використано стандартні налаштування") +
                             "; пошкоджений файл збережено як " + Path.GetFileName(backup) + ".");
                return fromBak ?? NewDefaults();
            }
        }

        static AppSettings TryRead(string file, List<string> warnings)
        {
            try { return File.Exists(file) ? SettingsJson.FromJson(File.ReadAllText(file, Encoding.UTF8), warnings) : null; }
            catch { return null; }
        }

        static AppSettings NewDefaults()
        {
            var s = new AppSettings();
            s.AdminPcName = Environment.MachineName;
            s.Normalize();
            return s;
        }

        public static void Save(AppSettings s)
        {
            s.Normalize();
            Directory.CreateDirectory(Paths.DataDir);
            string path = Paths.SettingsFile;
            string tmp = path + ".tmp";
            string bak = path + ".bak";
            File.WriteAllText(tmp, SettingsJson.ToJson(s), new UTF8Encoding(true));
            if (!File.Exists(path)) { File.Move(tmp, path); return; }
            try { File.Replace(tmp, path, bak, true); }
            catch (Exception ex) when (ex is IOException || ex is PlatformNotSupportedException || ex is UnauthorizedAccessException)
            {
                // File.Replace не працює на деяких флешках (FAT32/exFAT) і мережевих дисках — звичайне копіювання.
                File.Copy(path, bak, true);
                File.Copy(tmp, path, true);
                try { File.Delete(tmp); } catch { }
            }
        }
    }
}
