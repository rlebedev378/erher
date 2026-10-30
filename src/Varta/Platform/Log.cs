using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Varta.Platform
{
    public enum LogKind { Info, Alert, Warn, Error }

    public sealed class LogEntry
    {
        public DateTime Time;
        public LogKind Kind;
        public string Text = "";
    }

    /// <summary>Журнал: файл logs\varta-РРРР-ММ-ДД.log + буфер в пам'яті для вкладки «Журнал».</summary>
    public static class Log
    {
        static readonly object L = new object();
        static readonly List<LogEntry> Ring = new List<LogEntry>();
        const int MaxRing = 800;

        public static event Action<LogEntry> Added;

        public static void Info(string m) { Write(LogKind.Info, m); }
        public static void Alert(string m) { Write(LogKind.Alert, m); }
        public static void Warn(string m) { Write(LogKind.Warn, m); }

        public static void Error(string m, Exception ex)
        {
            Write(LogKind.Error, ex == null ? m : m + " — " + ex.GetType().Name + ": " + ex.Message);
            if (ex != null) WriteFileOnly("    " + ex.StackTrace);
        }

        public static void Error(string m) { Write(LogKind.Error, m); }

        public static List<LogEntry> Snapshot()
        {
            lock (L) { return new List<LogEntry>(Ring); }
        }

        static void Write(LogKind kind, string text)
        {
            var e = new LogEntry { Time = DateTime.Now, Kind = kind, Text = text ?? "" };
            lock (L)
            {
                Ring.Add(e);
                if (Ring.Count > MaxRing) Ring.RemoveRange(0, Ring.Count - MaxRing);
            }
            WriteFileOnly(e.Time.ToString("HH:mm:ss") + " [" + Tag(kind) + "] " + e.Text);
            try { var h = Added; if (h != null) h(e); } catch { }
        }

        static string Tag(LogKind k)
        {
            switch (k)
            {
                case LogKind.Alert: return "ТРИВОГА";
                case LogKind.Warn: return "УВАГА";
                case LogKind.Error: return "ПОМИЛКА";
                default: return "INFO";
            }
        }

        static void WriteFileOnly(string line)
        {
            try
            {
                lock (L)
                {
                    Directory.CreateDirectory(Paths.LogDir);
                    string f = Path.Combine(Paths.LogDir, "varta-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                    File.AppendAllText(f, line + Environment.NewLine, new UTF8Encoding(true));
                }
            }
            catch { }
        }

        public static void Cleanup(int keepDays)
        {
            try
            {
                if (!Directory.Exists(Paths.LogDir)) return;
                DateTime limit = DateTime.Now.AddDays(-keepDays);
                foreach (string f in Directory.GetFiles(Paths.LogDir, "varta-*.log"))
                    if (File.GetLastWriteTime(f) < limit) File.Delete(f);
            }
            catch { }
        }
    }
}
