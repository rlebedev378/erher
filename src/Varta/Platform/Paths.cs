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

        internal static void SetRoots(string appDir, string dataDir) { AppDir = appDir; DataDir = dataDir; }

        static Paths()
        {
            Assembly a = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            ExePath = a.Location;
            AppDir = Path.GetDirectoryName(ExePath);
            if (string.IsNullOrEmpty(AppDir)) AppDir = AppDomain.CurrentDomain.BaseDirectory;
            DataDir = IsWritable(AppDir)
                ? AppDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Varta");
            try { Directory.CreateDirectory(DataDir); } catch { }
        }

        public static string SettingsFile { get { return Path.Combine(DataDir, "settings.json"); } }
        public static string LogDir { get { return Path.Combine(DataDir, "logs"); } }
        public static string SoundsDir { get { return Path.Combine(DataDir, "sounds"); } }
        public static string RecoveryFile { get { return Path.Combine(DataDir, "audio-recovery.json"); } }
        public static bool IsPortableFolder { get { return string.Equals(DataDir, AppDir, StringComparison.OrdinalIgnoreCase); } }

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
