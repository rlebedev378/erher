using System;
using Microsoft.Win32;

namespace Varta.Platform
{
    public sealed class AutostartInfo
    {
        public bool Enabled;
        public bool PathMatches;
        public bool DisabledInTaskManager;
        public string Command = "";
        public string Error = "";
    }

    /// <summary>Автозапуск через HKCU\...\Run — без прав адміністратора. Шлях самовідновлюється після перенесення папки.</summary>
    public static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string ValueName = "Varta";

        public static string BuildCommand(string exe) { return "\"" + exe + "\" --minimized"; }

        public static AutostartInfo Query(string exe)
        {
            var info = new AutostartInfo();
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    string v = k == null ? null : k.GetValue(ValueName) as string;
                    info.Enabled = !string.IsNullOrEmpty(v);
                    info.Command = v ?? "";
                    info.PathMatches = info.Enabled && string.Equals(v, BuildCommand(exe), StringComparison.OrdinalIgnoreCase);
                }
                using (RegistryKey a = Registry.CurrentUser.OpenSubKey(ApprovedKey, false))
                {
                    byte[] data = a == null ? null : a.GetValue(ValueName) as byte[];
                    info.DisabledInTaskManager = data != null && data.Length > 0 && (data[0] & 1) == 1;
                }
            }
            catch (Exception ex) { info.Error = ex.Message; }
            return info;
        }

        public static string Set(bool on, string exe)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k == null) return "не вдалося відкрити розділ реєстру";
                    if (on) k.SetValue(ValueName, BuildCommand(exe), RegistryValueKind.String);
                    else k.DeleteValue(ValueName, false);
                }
                if (on) ClearTaskManagerDisable();
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

        /// <summary>Якщо автозапуск увімкнено, але шлях застарів (папку перенесли) — оновлює запис.</summary>
        public static bool RepairIfNeeded(string exe)
        {
            AutostartInfo i = Query(exe);
            if (i.Enabled && !i.PathMatches) { return Set(true, exe) == null; }
            return false;
        }
    }
}
