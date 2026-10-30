using System;
using System.IO;

namespace Varta.Platform
{
    public sealed class SoundImportResult
    {
        /// <summary>Що зберегти в налаштуваннях (відносний шлях, якщо файл у папці програми).</summary>
        public string Stored = "";
        /// <summary>Де файл лежить фактично.</summary>
        public string FullPath = "";
        public bool Copied;
        public string Warning = "";
    }

    /// <summary>Підготовка вибраного користувачем звукового файлу: копіювання в папку sounds без перезапису чужих файлів.</summary>
    public static class SoundFiles
    {
        public static SoundImportResult Import(string chosen)
        {
            var r = new SoundImportResult { FullPath = chosen };
            string root = Paths.DataDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!chosen.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.CreateDirectory(Paths.SoundsDir);
                    string name = Path.GetFileName(chosen);
                    string target = Path.Combine(Paths.SoundsDir, name);
                    int i = 2;
                    while (File.Exists(target) && !SameContent(chosen, target))
                        target = Path.Combine(Paths.SoundsDir, Path.GetFileNameWithoutExtension(name) + "_" + (i++) + Path.GetExtension(name));
                    if (!File.Exists(target)) { File.Copy(chosen, target); r.Copied = true; }
                    r.FullPath = target;
                }
                catch (Exception ex)
                {
                    r.Warning = "Не вдалося скопіювати файл у папку «sounds»: " + ex.Message + "\nБуде використано оригінальний шлях — якщо файл буде видалено чи переміщено, голос не прозвучить.";
                }
            }
            r.Stored = Paths.ToStored(r.FullPath);
            return r;
        }

        static bool SameContent(string a, string b)
        {
            try
            {
                if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
                byte[] x = File.ReadAllBytes(a), y = File.ReadAllBytes(b);
                for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
                return true;
            }
            catch { return false; }
        }
    }
}
