using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Varta.Platform
{
    public sealed class AutostartInfo
    {
        public bool Enabled;
        public bool PathMatches;
        public bool DisabledInTaskManager;
        /// <summary>Запис автозапуску вказує на іншу наявну копію Varta.exe.</summary>
        public bool OtherCopy;
        public string Command = "";
        public string RegisteredExe = "";
        public string Error = "";

        /// <summary>Програма справді запуститься разом із Windows саме з цієї папки.</summary>
        public bool Effective { get { return Enabled && PathMatches && !DisabledInTaskManager && string.IsNullOrEmpty(Error); } }
    }

    /// <summary>Автозапуск через HKCU\...\Run — без прав адміністратора. Шлях самовідновлюється після перенесення папки.</summary>
    public static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string ValueName = "Varta";

        public const string TempLocationError =
            "програму запущено з архіву або тимчасової папки — розпакуйте папку Varta (наприклад, у C:\\Varta) і запустіть Varta.exe звідти";

        public static string BuildCommand(string exe) { return "\"" + exe + "\" --minimized"; }

        /// <summary>Шлях до програми з команди автозапуску (у лапках або без).</summary>
        public static string ExeFromCommand(string command)
        {
            string c = (command ?? "").Trim();
            if (c.Length == 0) return "";
            if (c[0] == '"')
            {
                int end = c.IndexOf('"', 1);
                return (end > 0 ? c.Substring(1, end - 1) : c.Substring(1)).Trim();
            }
            int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe > 0) return c.Substring(0, exe + 4);
            int space = c.IndexOf(' ');
            return space > 0 ? c.Substring(0, space) : c;
        }

        public static AutostartInfo Query(string exe)
        {
            var info = new AutostartInfo();
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    string v = k == null ? null : k.GetValue(ValueName) as string;
                    info.Enabled = !string.IsNullOrWhiteSpace(v);
                    info.Command = v ?? "";
                    info.PathMatches = info.Enabled && string.Equals(v.Trim(), BuildCommand(exe), StringComparison.OrdinalIgnoreCase);
                    info.RegisteredExe = ExeFromCommand(info.Command);
                    info.OtherCopy = info.Enabled && info.RegisteredExe.Length > 0 && !Paths.SamePath(info.RegisteredExe, exe) && File.Exists(info.RegisteredExe);
                }
                using (RegistryKey a = Registry.CurrentUser.OpenSubKey(ApprovedKey, false))
                {
                    // Перший байт: парний — дозволено, непарний — вимкнено в «Диспетчері завдань → Автозавантаження».
                    byte[] data = a == null ? null : a.GetValue(ValueName) as byte[];
                    info.DisabledInTaskManager = data != null && data.Length > 0 && (data[0] & 1) == 1;
                }
            }
            catch (Exception ex) { info.Error = ex.Message; }
            return info;
        }

        public static string Set(bool on, string exe) { return Set(on, exe, true); }

        static string Set(bool on, string exe, bool clearTaskManagerDisable)
        {
            if (on && Paths.IsTemporaryPath(Path.GetDirectoryName(exe))) return TempLocationError;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k == null) return "не вдалося відкрити розділ реєстру";
                    if (on) k.SetValue(ValueName, BuildCommand(exe), RegistryValueKind.String);
                    else k.DeleteValue(ValueName, false);
                }
                if (on && clearTaskManagerDisable) ClearTaskManagerDisable();

                AutostartInfo check = Query(exe);
                if (on && !check.PathMatches) return "запис не зберігся в реєстрі (можливо, його блокує антивірус)" + (check.Error.Length > 0 ? ": " + check.Error : "");
                if (!on && check.Enabled) return "запис не вдалося видалити з реєстру" + (check.Error.Length > 0 ? ": " + check.Error : "");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static void ClearTaskManagerDisable()
        {
            try
            {
                using (RegistryKey a = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
                {
                    if (a != null) a.DeleteValue(ValueName, false);
                }
            }
            catch { }
        }

        /// <summary>
        /// Папку перенесли — оновлює шлях у записі автозапуску. Не чіпає запис, що вказує на іншу наявну копію програми,
        /// і не скасовує вимкнення в «Диспетчері завдань». Повертає опис дії для журналу або null.
        /// </summary>
        public static string RepairIfNeeded(string exe)
        {
            if (Paths.IsTemporaryPath(Path.GetDirectoryName(exe))) return null;
            AutostartInfo i = Query(exe);
            if (!i.Enabled || i.PathMatches || i.OtherCopy || i.Error.Length > 0) return null;
            string err = Set(true, exe, false);
            return err == null
                ? "Автозапуск: шлях оновлено на " + exe + " (було: " + i.Command + ")."
                : "Автозапуск: не вдалося оновити шлях — " + err;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DeleteFileW(string path);

        /// <summary>
        /// Знімає позначку «завантажено з Інтернету» (потік Zone.Identifier) — те саме, що «Розблокувати» у властивостях файлу.
        /// Без цього Windows може показувати попередження безпеки під час кожного автозапуску, і програма не стартує, доки його не закрити.
        /// </summary>
        public static bool RemoveDownloadMark(string file)
        {
            try
            {
                if (Environment.OSVersion.Platform != PlatformID.Win32NT || string.IsNullOrEmpty(file)) return false;
                return DeleteFileW(file + ":Zone.Identifier");
            }
            catch { return false; }
        }
    }
}
