using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using Varta.Core;
using Varta.Platform;
using Varta.Ui;

namespace Varta
{
    static class Program
    {
        const string MutexName = "Local\\Varta.Kharkiv.SingleInstance";
        const string ShowEventName = "Local\\Varta.Kharkiv.Show";

        static bool Has(string[] args, string name)
        {
            foreach (string a in args) if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string Value(string[] args, string prefix)
        {
            foreach (string a in args)
                if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return a.Substring(prefix.Length);
            return null;
        }

        [STAThread]
        static int Main(string[] args)
        {
            string audioTest = Value(args, "--audio-test=");
            if (audioTest != null) return AudioSelfTest.Run(audioTest, Value(args, "--save-wav="));

            if (Has(args, "--autostart-on")) return Autostart.Set(true, Paths.ExePath) == null ? 0 : 1;
            if (Has(args, "--autostart-off")) return Autostart.Set(false, Paths.ExePath) == null ? 0 : 1;

            bool launchedByAutostart = Has(args, "--minimized");
            int restarts = 0;
            int.TryParse(Value(args, "--restarts=") ?? "0", out restarts);

            Mutex mutex = null;
            bool createdNew = false;
            for (int attempt = 0; attempt < (restarts > 0 ? 20 : 1); attempt++)
            {
                try { mutex = new Mutex(true, MutexName, out createdNew); } catch { createdNew = true; break; }
                if (createdNew) break;
                mutex.Dispose();
                Thread.Sleep(500);
            }
            if (!createdNew)
            {
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Error("Необроблена помилка інтерфейсу", e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log.Error("Критична помилка: " + e.ExceptionObject);
                if (e.IsTerminating && restarts < 3)
                {
                    try { Process.Start(new ProcessStartInfo(Paths.ExePath, "--minimized --restarts=" + (restarts + 1)) { UseShellExecute = false }); } catch { }
                }
            };

            EventWaitHandle showEvt = null;
            try { showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName); } catch { }

            List<string> warnings;
            bool existed;
            AppSettings settings = SettingsStore.Load(out warnings, out existed);

            using (var app = new AppController(settings, warnings, existed))
            {
                var main = new MainForm(app);
                var tray = new TrayController(app, main);
                app.Tray = tray;
                main.Tray = tray;
                main.CreateControl();
                IntPtr force = main.Handle;

                if (showEvt != null)
                {
                    var t = new Thread(delegate()
                    {
                        while (true)
                        {
                            try { showEvt.WaitOne(); main.BeginInvoke(new Action(main.ShowAndActivate)); }
                            catch { break; }
                        }
                    }) { IsBackground = true, Name = "Varta.ShowSignal" };
                    t.Start();
                }

                Autostart.RepairIfNeeded(Paths.ExePath);
                app.Start();

                string page = Value(args, "--page=");
                if (page != null) main.ShowByKey(page);
                string shot = Value(args, "--test-banner=");
                if (shot != null)
                {
                    AlertEventKind k = shot == "yellow" ? AlertEventKind.YellowStarted : (shot == "green" ? AlertEventKind.AllClear : AlertEventKind.RedStarted);
                    app.RaiseTest(k);
                }

                bool hidden = launchedByAutostart && settings.StartMinimized && !app.FirstRun;
                if (!hidden)
                {
                    main.Show();
                    if (app.FirstRun) main.Shown += delegate { FirstRun(app, main); };
                }

                Application.Run(new ApplicationContext());

                tray.Dispose();
            }

            try { mutex.ReleaseMutex(); } catch { }
            return 0;
        }

        static void FirstRun(AppController app, MainForm main)
        {
            try
            {
                DialogResult r = MessageBox.Show(main,
                    "Ласкаво просимо до ВАРТИ!\n\nЗапускати програму автоматично разом із Windows? Це рекомендовано, щоб тривоги відстежувалися після кожного перезавантаження комп'ютера.",
                    "ВАРТА — перший запуск", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.Yes) app.SetAutostart(true);

                AppSettings s = SettingsJson.Clone(app.Settings);
                s.FirstRunDone = true;
                if (string.IsNullOrWhiteSpace(s.AdminPcName)) s.AdminPcName = Environment.MachineName;
                app.ApplySettings(s);

                MessageBox.Show(main,
                    "Готово! Програма вже працює і стежить за тривогами.\n\n" +
                    "• Перевірте звук: вкладка «Стан» → «Тест сповіщень».\n" +
                    "• Свої голосові файли: вкладка «Звук».\n" +
                    "• Для рівнів «жовтий/червоний» отримайте безкоштовний ключ: вкладка «Джерела даних».",
                    "ВАРТА", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { Log.Error("Помилка першого запуску", ex); }
        }
    }
}
