using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Varta.Core;

namespace Varta.Platform
{
    public static class SettingsStore
    {
        public static AppSettings Load(out List<string> warnings, out bool existed)
        {
            warnings = new List<string>();
            string path = Paths.SettingsFile;
            existed = File.Exists(path);
            if (!existed) return NewDefaults();
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                return SettingsJson.FromJson(json, warnings);
            }
            catch (Exception ex)
            {
                string backup = Path.Combine(Paths.DataDir, "settings.damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                try { File.Copy(path, backup, true); } catch { }
                warnings.Add("Файл налаштувань пошкоджено (" + ex.Message + "). Використано стандартні налаштування; копію збережено як " + Path.GetFileName(backup) + ".");
                return NewDefaults();
            }
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
            string path = Paths.SettingsFile;
            string tmp = path + ".tmp";
            string bak = path + ".bak";
            File.WriteAllText(tmp, SettingsJson.ToJson(s), new UTF8Encoding(true));
            if (File.Exists(path)) File.Replace(tmp, path, bak);
            else File.Move(tmp, path);
        }
    }
}
