using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Varta.Core;
using Varta.Platform;

namespace Varta.Ui
{
    public sealed class MainForm : Form
    {
        readonly AppController _c;
        readonly Panel _sidebar = new Panel { Dock = DockStyle.Left, Width = 252, BackColor = Theme.Navy };
        readonly FlowLayoutPanel _navFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Navy, Padding = new Padding(0, 8, 0, 0) };
        readonly Panel _brand = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Theme.Navy };
        readonly Panel _foot = new Panel { Dock = DockStyle.Bottom, Height = 44, BackColor = Theme.Navy };
        readonly Panel _content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        readonly Panel _header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Color.White };
        readonly Panel _stripe = new Panel { Dock = DockStyle.Bottom, Height = 4 };
        readonly Panel _saveBar = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.FromArgb(255, 244, 229), Visible = false };
        readonly Panel _pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        readonly Label _title = new Label { AutoSize = true, Font = Theme.F(17f, FontStyle.Bold), ForeColor = Theme.Navy, Location = new Point(28, 10), BackColor = Color.Transparent };
        readonly Label _subtitle = new Label { AutoSize = true, Font = Theme.F(10f), ForeColor = Theme.Muted, Location = new Point(30, 46), BackColor = Color.Transparent };
        readonly Label _pill = new Label
        {
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = Theme.F(10f, FontStyle.Bold),
            Size = new Size(210, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        readonly Label _dirtyLabel = new Label { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.F(9.75f, FontStyle.Bold), ForeColor = Theme.Orange, BackColor = Color.Transparent };
        readonly Button _save = UiKit.Btn("Зберегти налаштування", true);
        readonly Button _cancel = UiKit.Btn("Скасувати зміни", false);
        readonly List<NavButton> _nav = new List<NavButton>();
        readonly List<PageBase> _pages = new List<PageBase>();
        readonly List<string> _keys = new List<string>();
        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly DashboardPage _dash;
        PageBase _current;
        bool _dirty;
        bool _exiting;
        bool _hintShown;

        public TrayController Tray;

        public MainForm(AppController c)
        {
            _c = c;
            Text = "ВАРТА " + AppInfo.Version + " — сповіщення про повітряну тривогу";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Theme.F(9.5f);
            BackColor = Theme.Bg;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1120, 780);
            MinimumSize = new Size(1000, 660);
            try { Icon = BrandArt.BrandIcon(); } catch { }

            _dash = new DashboardPage(c);
            _dash.Navigate += ShowByKey;
            Add("dashboard", "Стан", _dash);
            Add("notifications", "Сповіщення", new NotificationsPage(c));
            Add("sound", "Звук", new SoundPage(c));
            Add("sources", "Джерела даних", new SourcesPage(c));
            Add("admin", "Адміністратор", new AdminPage(c));
            Add("system", "Система", new SystemPage(c));
            Add("log", "Журнал", new LogPage(c));
            Add("about", "Про ВАРТУ", new AboutPage());

            BuildChrome();

            foreach (PageBase p in _pages)
            {
                var sp = p as SettingsPage;
                if (sp != null)
                {
                    sp.Changed += delegate { SetDirty(true); };
                    sp.CommitRequested += delegate { SaveAll(false); };
                }
            }
            ReloadPages();
            c.SettingsApplied += delegate { ReloadPages(); };
            c.StateChanged += UpdateAll;
            _timer.Tick += delegate { UpdateAll(); };
            _timer.Start();
            ShowByKey("dashboard");
        }

        void Add(string key, string title, PageBase page)
        {
            var nb = new NavButton(title);
            nb.Click += delegate { ShowByKey(key); };
            _nav.Add(nb);
            _pages.Add(page);
            _keys.Add(key);
            page.Visible = false;
        }

        IEnumerable<SettingsPage> SettingsPages { get { return _pages.OfType<SettingsPage>(); } }

        void BuildChrome()
        {
            // бічна панель
            _brand.Paint += delegate(object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float k = Math.Max(1f, g.DpiX / 96f);
                var shield = new RectangleF(22 * k, 20 * k, 52 * k, 60 * k);
                BrandArt.DrawShield(g, shield, Theme.Blue, Glyph.None, Color.White, true);
                BrandArt.DrawSiren(g, shield, Color.White, Theme.Gold);
                TextRenderer.DrawText(g, AppInfo.Name, Theme.F(21f, FontStyle.Bold), new Point((int)(84 * k), (int)(22 * k)), Color.White);
                TextRenderer.DrawText(g, "повітряна тривога • Харків", Theme.F(8f), new Point((int)(86 * k), (int)(58 * k)), Theme.NavText);
                using (var p = new Pen(Theme.Gold, 2f * k)) g.DrawLine(p, 86 * k, 80 * k, 130 * k, 80 * k);
            };
            _foot.Paint += delegate(object s, PaintEventArgs e)
            {
                float k = Math.Max(1f, e.Graphics.DpiX / 96f);
                TextRenderer.DrawText(e.Graphics, "версія " + AppInfo.Version, Theme.F(8.5f), new Point((int)(24 * k), (int)(14 * k)), Theme.NavText);
            };
            foreach (NavButton nb in _nav) { nb.Margin = new Padding(0); nb.Width = _sidebar.Width; _navFlow.Controls.Add(nb); }
            _sidebar.Controls.Add(_navFlow);
            _sidebar.Controls.Add(_brand);
            _sidebar.Controls.Add(_foot);

            // шапка сторінки
            _stripe.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var br = new LinearGradientBrush(_stripe.ClientRectangle, Theme.Blue, Theme.Gold, 0f))
                    e.Graphics.FillRectangle(br, _stripe.ClientRectangle);
            };
            _header.Controls.Add(_stripe);
            _header.Controls.Add(_title);
            _header.Controls.Add(_subtitle);
            _header.Controls.Add(_pill);
            _header.Resize += delegate { _pill.Location = new Point(_header.Width - _pill.Width - 28, 20); };
            _pill.Location = new Point(800, 20);

            // панель збереження: видима на будь-якій сторінці, доки є незбережені зміни
            var barLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(28, 8, 28, 8), BackColor = Color.Transparent };
            barLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            barLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            barLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            barLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _save.Anchor = AnchorStyles.None;
            _cancel.Anchor = AnchorStyles.None;
            _save.Margin = new Padding(0);
            _cancel.Margin = new Padding(12, 0, 12, 0);
            barLayout.Controls.Add(_dirtyLabel, 0, 0);
            barLayout.Controls.Add(_cancel, 1, 0);
            barLayout.Controls.Add(_save, 2, 0);
            _saveBar.Controls.Add(barLayout);
            _saveBar.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(Theme.Orange, 2f)) e.Graphics.DrawLine(p, 0, 1, _saveBar.Width, 1); };
            _save.Click += delegate { SaveAll(true); };
            _cancel.Click += delegate { ReloadPages(); };

            foreach (PageBase p in _pages) { p.Dock = DockStyle.Fill; _pageHost.Controls.Add(p); }

            _content.Controls.Add(_pageHost);
            _content.Controls.Add(_saveBar);
            _content.Controls.Add(_header);

            Controls.Add(_content);
            Controls.Add(_sidebar);
        }

        // ---------- навігація ----------

        public void ShowByKey(string key)
        {
            int i = _keys.IndexOf(key);
            if (i >= 0) ShowPage(i);
        }

        void ShowPage(int index)
        {
            PageBase p = _pages[index];
            foreach (PageBase o in _pages) o.Visible = ReferenceEquals(o, p);
            for (int i = 0; i < _nav.Count; i++) _nav[i].Selected = i == index;
            _current = p;
            _title.Text = p.PageTitle;
            _subtitle.Text = p.PageSubtitle;
            UpdateDirtyLabel();
            p.OnShown();
            UpdateAll();
        }

        // ---------- налаштування ----------

        void ReloadPages()
        {
            foreach (SettingsPage sp in SettingsPages) sp.LoadFrom(_c.Settings);
            SetDirty(false);
        }

        void SetDirty(bool d)
        {
            _dirty = d;
            UpdateDirtyLabel();
        }

        void UpdateDirtyLabel()
        {
            _dirtyLabel.Text = "●  Є незбережені зміни — тривоги й тести поки що працюють за старими налаштуваннями.";
            _saveBar.Visible = _dirty;
        }

        List<string> Validate(AppSettings s)
        {
            var w = new List<string>();
            if (!s.UseAlertsInUa && !s.UseTelegram) w.Add("Не ввімкнено жодного джерела даних — тривоги не відстежуватимуться.");
            if (s.UseAlertsInUa && string.IsNullOrWhiteSpace(s.AlertsInUaToken) && !s.UseTelegram) w.Add("Для alerts.in.ua не вказано ключ, а Telegram-канали вимкнено.");
            if (s.UseTelegram && TextUtil.SplitChannels(s.TelegramChannels).Count == 0) w.Add("Не вказано жодного коректного Telegram-каналу.");
            if (s.AdminEnabled && (string.IsNullOrWhiteSpace(s.AdminBotToken) || string.IsNullOrWhiteSpace(s.AdminChatId))) w.Add("Telegram-адміністратор увімкнено, але не вказано токен бота або ID чату.");
            if (s.VoiceEnabled)
            {
                var checks = new Dictionary<AlertEventKind, string>
                {
                    { AlertEventKind.RedStarted, s.SoundRed }, { AlertEventKind.YellowStarted, s.SoundYellow },
                    { AlertEventKind.YellowFromRed, s.SoundYellowFromRed }, { AlertEventKind.AllClear, s.SoundClear }
                };
                foreach (var kv in checks)
                    if ((RoutePolicy.Get(s, kv.Key) & Channel.Voice) != 0 && !System.IO.File.Exists(Paths.Resolve(kv.Value)))
                        w.Add("Не знайдено файл голосу для події «" + RoutePolicy.Title(kv.Key) + "»: " + (string.IsNullOrWhiteSpace(kv.Value) ? "(не вказано)" : kv.Value));
            }
            return w;
        }

        /// <summary>Зберігає й застосовує зміни з усіх сторінок. interactive=false — без вікна з попередженнями (їх буде записано в журнал).</summary>
        bool SaveAll(bool interactive)
        {
            AppSettings s = SettingsJson.Clone(_c.Settings);
            foreach (SettingsPage p in SettingsPages) p.SaveTo(s);
            s.FirstRunDone = true;
            string extra = null;
            foreach (SettingsPage p in SettingsPages) { string e = p.ApplyExtras(); if (e != null) extra = e; }
            List<string> warn = Validate(s);
            string err = _c.ApplySettings(s);
            if (err != null)
            {
                MessageBox.Show(this, "Не вдалося зберегти налаштування: " + err, "ВАРТА", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            if (extra != null) warn.Add(extra);
            if (warn.Count > 0)
            {
                if (interactive)
                    MessageBox.Show(this, "Налаштування збережено, але зверніть увагу:\n\n• " + string.Join("\n• ", warn.ToArray()), "ВАРТА", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    foreach (string w in warn) Log.Warn("Після збереження: " + w);
            }
            return true;
        }

        // ---------- оновлення ----------

        void UpdateAll()
        {
            if (IsDisposed || !IsHandleCreated) return;
            UpdatePill();
            if (Tray != null) Tray.Update();
            List<HealthItem> items = _c.Health();
            HealthState worst = HealthService.Worst(items);
            _nav[0].Badge = worst == HealthState.Error ? Theme.Red : (worst == HealthState.Warn ? Theme.Yellow : (Color?)null);
            if (Visible && ReferenceEquals(_current, _dash)) _dash.UpdateView(items);
        }

        void UpdatePill()
        {
            AlertLevel l = _c.Monitor.Engine.EffectiveLevel();
            string text;
            Color bg, fg = Color.White;
            if (_c.Monitor.IsPaused) { text = "ПАУЗА"; bg = Theme.Grey; }
            else
            {
                switch (l)
                {
                    case AlertLevel.Red: text = "●  ЧЕРВОНИЙ РІВЕНЬ"; bg = Theme.Red; break;
                    case AlertLevel.Yellow: text = "●  ЖОВТИЙ РІВЕНЬ"; bg = Theme.Yellow; fg = Theme.YellowInk; break;
                    case AlertLevel.Clear: text = "●  ТРИВОГИ НЕМАЄ"; bg = Theme.Green; break;
                    default: text = "●  СТАН НЕВІДОМИЙ"; bg = Theme.Grey; break;
                }
            }
            _pill.Text = text;
            _pill.BackColor = bg;
            _pill.ForeColor = fg;
        }

        // ---------- вікно ----------

        public void ShowAndActivate()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            UpdateAll();
        }

        public void ConfirmExit()
        {
            ShowAndActivate();
            DialogResult r = MessageBox.Show(this, "Вийти з ВАРТИ?\n\nПісля виходу повітряні тривоги більше не відстежуватимуться.", "ВАРТА",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
            _exiting = true;
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                if (_dirty)
                {
                    DialogResult r = MessageBox.Show(this, "Є незбережені зміни. Зберегти їх?", "ВАРТА", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                    if (r == DialogResult.Yes && !SaveAll(true)) { e.Cancel = true; return; }
                    if (r == DialogResult.No) ReloadPages();
                }
                if (_c.Settings.CloseToTray)
                {
                    e.Cancel = true;
                    Hide();
                    if (!_hintShown && Tray != null)
                    {
                        _hintShown = true;
                        Tray.Balloon(new Notice { Title = "ВАРТА працює у фоні", Body = "Вікно згорнуто в трей. Вийти повністю: значок у трей → «Вийти».", IsInfo = true });
                    }
                    return;
                }
                _exiting = true;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
