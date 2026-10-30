using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Varta.Core;
using Varta.Platform;

namespace Varta.Ui
{
    /// <summary>Значок у системному треї: колір = рівень небезпеки, меню швидких дій, спливаючі сповіщення.</summary>
    public sealed class TrayController : IDisposable
    {
        readonly AppController _c;
        readonly MainForm _main;
        readonly NotifyIcon _ni;
        readonly ContextMenuStrip _menu = new ContextMenuStrip();
        readonly ToolStripMenuItem _status = new ToolStripMenuItem { Enabled = false };
        readonly ToolStripMenuItem _resume = new ToolStripMenuItem("Відновити моніторинг");
        readonly ToolStripMenuItem _pause = new ToolStripMenuItem("Призупинити моніторинг");
        string _lastKey = "";

        public TrayController(AppController c, MainForm main)
        {
            _c = c;
            _main = main;
            _menu.Font = Theme.F(9.5f);

            var open = new ToolStripMenuItem("Відкрити ВАРТУ") { Font = Theme.F(9.5f, FontStyle.Bold) };
            open.Click += delegate { _main.ShowAndActivate(); };
            _menu.Items.Add(open);
            _menu.Items.Add(_status);
            _menu.Items.Add(new ToolStripSeparator());

            var check = new ToolStripMenuItem("Перевірити зараз");
            check.Click += delegate { _c.Monitor.RefreshNow(); };
            _menu.Items.Add(check);

            var tests = new ToolStripMenuItem("Тест сповіщень");
            tests.DropDownItems.Add(MenuItem("Червоний рівень (тривога)", delegate { _c.RaiseTest(AlertEventKind.RedStarted); }));
            tests.DropDownItems.Add(MenuItem("Жовтий рівень", delegate { _c.RaiseTest(AlertEventKind.YellowStarted); }));
            tests.DropDownItems.Add(MenuItem("Відбій", delegate { _c.RaiseTest(AlertEventKind.AllClear); }));
            tests.DropDownItems.Add(new ToolStripSeparator());
            tests.DropDownItems.Add(MenuItem("Зупинити звук", delegate { _c.StopSound(); _c.Banners.Hide(); }));
            _menu.Items.Add(tests);

            _pause.DropDownItems.Add(MenuItem("на 15 хвилин", delegate { _c.Monitor.PauseFor(TimeSpan.FromMinutes(15)); }));
            _pause.DropDownItems.Add(MenuItem("на 30 хвилин", delegate { _c.Monitor.PauseFor(TimeSpan.FromMinutes(30)); }));
            _pause.DropDownItems.Add(MenuItem("на 1 годину", delegate { _c.Monitor.PauseFor(TimeSpan.FromHours(1)); }));
            _pause.DropDownItems.Add(MenuItem("на 2 години", delegate { _c.Monitor.PauseFor(TimeSpan.FromHours(2)); }));
            _menu.Items.Add(_pause);
            _resume.Click += delegate { _c.Monitor.Resume("вручну"); };
            _menu.Items.Add(_resume);

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(MenuItem("Відкрити папку програми", delegate { UiKit.Open(Paths.AppDir); }));
            _menu.Items.Add(MenuItem("Вийти", delegate { _main.ConfirmExit(); }));

            _ni = new NotifyIcon { ContextMenuStrip = _menu, Visible = true };
            _ni.DoubleClick += delegate { _main.ShowAndActivate(); };
            _menu.Opening += delegate { UpdateMenu(); };
            Update();
        }

        static ToolStripMenuItem MenuItem(string text, Action a)
        {
            var i = new ToolStripMenuItem(text);
            i.Click += delegate { a(); };
            return i;
        }

        void UpdateMenu()
        {
            bool paused = _c.Monitor.IsPaused;
            _resume.Visible = paused;
            _pause.Visible = !paused;
            _status.Text = StatusText();
        }

        string StatusText()
        {
            if (_c.Monitor.IsPaused) return "Моніторинг призупинено";
            AlertLevel l = _c.Monitor.Engine.EffectiveLevel();
            return l == AlertLevel.Unknown ? "Стан невідомий — немає даних" : Lang.LevelTitle(l);
        }

        /// <summary>Оновлює значок і підказку.</summary>
        public void Update()
        {
            AlertLevel l = _c.Monitor.Engine.EffectiveLevel();
            bool paused = _c.Monitor.IsPaused;
            bool problem = _c.Monitor.Engine.State.ConnectionLost;
            string key = l + "|" + paused + "|" + problem;
            string tip = "ВАРТА — " + StatusText();
            if (tip.Length > 62) tip = tip.Substring(0, 62);
            _ni.Text = tip;
            if (key == _lastKey) return;
            _lastKey = key;
            _ni.Icon = BrandArt.TrayIcon(l, paused, problem);
        }

        public void Balloon(Notice n)
        {
            try
            {
                ToolTipIcon icon = n.IsTechnical ? ToolTipIcon.Warning
                                 : n.IsInfo ? ToolTipIcon.Info
                                 : (n.Level == AlertLevel.Red ? ToolTipIcon.Error : (n.Level == AlertLevel.Yellow ? ToolTipIcon.Warning : ToolTipIcon.Info));
                _ni.ShowBalloonTip(10000, n.Title, string.IsNullOrEmpty(n.Body) ? n.Title : n.Body, icon);
            }
            catch (Exception ex) { Log.Warn("Не вдалося показати спливаюче сповіщення: " + ex.Message); }
        }

        public void Dispose()
        {
            _ni.Visible = false;
            _ni.Dispose();
            _menu.Dispose();
        }
    }
}
