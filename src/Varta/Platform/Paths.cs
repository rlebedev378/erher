using System;
using System.IO;
using System.Reflection;

namespace Varta.Platform
{
    /// <summary>Усі шляхи відносно папки програми — щоб папку можна було просто скопіювати на інший ПК.</summary>
    public static class Paths
    {
        public static readonly string ExePath;
        public static string AppDir { get; private set; }
        public static string DataDir { get; private set; }
        /// <summary>Куди програма зберігала дані, коли папка програми була недоступна для запису.</summary>
        public static string LegacyDataDir { get; private set; }

        internal static void SetRoots(string appDir, string dataDir) { AppDir = appDir; DataDir = dataDir; }
        internal static void SetLegacyDataDir(string dir) { LegacyDataDir = dir; }

        static Paths()
        {
            Assembly a = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            ExePath = a.Location;
            AppDir = Path.GetDirectoryName(ExePath);
            if (string.IsNullOrEmpty(AppDir)) AppDir = AppDomain.CurrentDomain.BaseDirectory;
            LegacyDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Varta");
            // Дані завжди поруч із Varta.exe; %AppData%\Varta — лише якщо в папку програми писати не можна (наприклад, Program Files).
            DataDir = IsWritable(AppDir) ? AppDir : LegacyDataDir;
            try { Directory.CreateDirectory(DataDir); } catch { }
        }

        public static string SettingsFile { get { return Path.Combine(DataDir, "settings.json"); } }
        public static string LogDir { get { return Path.Combine(DataDir, "logs"); } }
        public static string SoundsDir { get { return Path.Combine(DataDir, "sounds"); } }
        public static string RecoveryFile { get { return Path.Combine(DataDir, "audio-recovery.json"); } }
        public static bool IsPortableFolder { get { return SamePath(DataDir, AppDir); } }

        /// <summary>Програма лежить у тимчасовій папці — найчастіше її запустили просто з ZIP-архіву.</summary>
        public static bool IsTemporaryLocation { get { return IsTemporaryPath(AppDir); } }

        public static bool IsTemporaryPath(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            string d = Normalize(dir);
            if (d == null) return false;
            // GetTempPath() буває у короткій формі 8.3 (C:\Users\KORIST~1\...), тому перевіряється й повна.
            string[] temps =
            {
                Path.GetTempPath(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
            };
            foreach (string t in temps)
            {
                string n = Normalize(t);
                if (n != null && n.Length > 3 && d.StartsWith(n, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static string Normalize(string dir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir)) return null;
                return Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            }
            catch { return null; }
        }

        public static bool SamePath(string a, string b)
        {
            string x = Normalize(a), y = Normalize(b);
            return x != null && y != null && string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsWritable(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".varta-write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Переносить settings.json і звуки з %AppData%\Varta у папку програми, якщо там їх ще немає
        /// (програма раніше працювала з папки без права запису). Повертає опис дії для журналу або null.
        /// </summary>
        public static string MigrateLegacyData()
        {
            try
            {
                if (!IsPortableFolder || File.Exists(SettingsFile) || SamePath(LegacyDataDir, DataDir)) return null;
                string legacySettings = Path.Combine(LegacyDataDir, "settings.json");
                if (!File.Exists(legacySettings)) return null;

                int sounds = 0;
                string legacySounds = Path.Combine(LegacyDataDir, "sounds");
                if (Directory.Exists(legacySounds))
                {
                    Directory.CreateDirectory(SoundsDir);
                    foreach (string f in Directory.GetFiles(legacySounds))
                    {
                        string target = Path.Combine(SoundsDir, Path.GetFileName(f));
                        if (File.Exists(target)) continue;
                        File.Copy(f, target);
                        sounds++;
                    }
                }
                // settings.json — останнім: якщо копіювання звуків зірветься, наступний запуск повторить перенесення.
                File.Copy(legacySettings, SettingsFile, false);
                return "Налаштування" + (sounds > 0 ? " і звуки (" + sounds + ")" : "") + " перенесено з " + LegacyDataDir + " у папку програми " + DataDir + ".";
            }
            catch (Exception ex)
            {
                return "Не вдалося перенести налаштування з " + LegacyDataDir + ": " + ex.Message;
            }
        }

        static string Fix(string p)
        {
            return p.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>Повний шлях до файлу з налаштувань (відносний шлях шукається в папці даних, потім у папці програми).</summary>
        public static string Resolve(string stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return "";
            string p = Fix(stored.Trim());
            if (Path.IsPathRooted(p)) return p;
            string c1 = Path.Combine(DataDir, p);
            if (File.Exists(c1)) return c1;
            string c2 = Path.Combine(AppDir, p);
            if (File.Exists(c2)) return c2;
            return c1;
        }

        /// <summary>Як зберегти шлях: відносний, якщо файл лежить у папці програми.</summary>
        public static string ToStored(string absolute)
        {
            string root = DataDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return absolute.Substring(root.Length).Replace('/', '\\');
            return absolute;
        }
    }
}
