using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Varta.Core;

namespace Varta.Ui
{
    /// <summary>Смуга-банер поверх усіх вікон. Не забирає фокус у того, хто зараз працює.</summary>
    public sealed class BannerForm : Form
    {
        const int WS_EX_TOPMOST = 0x00000008;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_NOACTIVATE = 0x08000000;

        readonly Notice _n;
        readonly Color _bg;
        readonly Color _ink;
        readonly Button _hide;
        readonly float _k;

        public event Action HideRequested;

        public BannerForm(Notice n, AppSettings s, Screen screen)
        {
            _n = n;
            using (Graphics g = CreateGraphics()) _k = Math.Max(1f, g.DpiX / 96f);

            if (n.IsTechnical) { _bg = Theme.Orange; _ink = Color.White; }
            else if (n.IsInfo) { _bg = Theme.Navy3; _ink = Color.White; }
            else
            {
                switch (n.Level)
                {
                    case AlertLevel.Red: _bg = Theme.Red; _ink = Color.White; break;
                    case AlertLevel.Yellow: _bg = Theme.Yellow; _ink = Theme.YellowInk; break;
                    case AlertLevel.Clear: _bg = Theme.Green; _ink = Color.White; break;
                    default: _bg = Theme.Navy3; _ink = Color.White; break;
                }
            }

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = _bg;
            DoubleBuffered = true;
            Bounds = new Rectangle(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, (int)(s.BannerHeight * _k));

            _hide = new Button
            {
                Text = "Сховати",
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = Theme.F(10f, FontStyle.Bold),
                ForeColor = _ink,
                BackColor = Color.FromArgb(40, 0, 0, 0),
                UseVisualStyleBackColor = false,
                Size = new Size((int)(120 * _k), (int)(36 * _k)),
                TabStop = false
            };
            _hide.FlatAppearance.BorderColor = _ink;
            _hide.FlatAppearance.BorderSize = 1;
            _hide.Click += delegate { var h = HideRequested; if (h != null) h(); };
            Controls.Add(_hide);
            PlaceButton();
        }

        void PlaceButton()
        {
            if (_hide == null) return;
            _hide.Location = new Point(Width - _hide.Width - (int)(24 * _k), Height - _hide.Height - (int)(16 * _k));
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); PlaceButton(); }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillRectangle(p, 0, Height - (int)(6 * _k), Width, (int)(6 * _k));

            int shield = (int)(Height * 0.62f);
            Color shieldFill = _ink == Color.White ? Color.White : Theme.YellowInk;
            Glyph glyph = _n.IsTechnical || _n.IsInfo ? Glyph.Exclaim : BrandArt.GlyphFor(_n.Level, false);
            BrandArt.DrawShield(g, new RectangleF(32 * _k, (Height - shield) / 2f - 4 * _k, shield * 0.88f, shield), shieldFill, glyph, _bg, false);

            int x = (int)(32 * _k + shield * 0.88f + 32 * _k);
            int rightW = (int)(190 * _k);
            int w = Width - x - rightW - (int)(24 * _k);
            var fl = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            int y = (int)(Height * 0.17f);
            TextRenderer.DrawText(g, _n.Title, Theme.F(30f, FontStyle.Bold), new Rectangle(x, y, w, (int)(56 * _k)), _ink, fl);
            y += (int)(60 * _k);
            TextRenderer.DrawText(g, _n.Body, Theme.F(16f), new Rectangle(x, y, w, Height - y - (int)(10 * _k)), _ink,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

            var rf = TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, _n.LocalTime.ToString("HH:mm"), Theme.F(28f, FontStyle.Bold), new Rectangle(Width - rightW - (int)(24 * _k), (int)(12 * _k), rightW, (int)(50 * _k)), _ink, rf);
            if (!string.IsNullOrEmpty(_n.Source))
                TextRenderer.DrawText(g, _n.Source, Theme.F(9.5f), new Rectangle(Width - rightW - (int)(24 * _k), (int)(62 * _k), rightW, (int)(22 * _k)), _ink, rf | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Керує смугами-банерами на всіх екранах.</summary>
    public sealed class BannerManager : IDisposable
    {
        readonly List<BannerForm> _forms = new List<BannerForm>();
        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();

        public bool IsVisible { get { return _forms.Count > 0; } }

        public BannerManager()
        {
            _timer.Tick += delegate { _timer.Stop(); Hide(); };
        }

        public void Show(Notice n, AppSettings s)
        {
            Hide();
            Screen[] screens = s.BannerAllScreens ? Screen.AllScreens : new[] { Screen.PrimaryScreen };
            foreach (Screen sc in screens)
            {
                try
                {
                    var f = new BannerForm(n, s, sc);
                    f.HideRequested += Hide;
                    _forms.Add(f);
                    f.Show();
                }
                catch (Exception ex) { Varta.Platform.Log.Error("Не вдалося показати банер", ex); }
            }

            bool sticky = s.BannerStayUntilClear && !n.IsTechnical && !n.IsInfo && (n.Level == AlertLevel.Red || n.Level == AlertLevel.Yellow);
            _timer.Stop();
            if (!sticky)
            {
                _timer.Interval = Math.Max(5, s.BannerSeconds) * 1000;
                _timer.Start();
            }
        }

        public void Hide()
        {
            _timer.Stop();
            foreach (BannerForm f in _forms)
            {
                try { f.Close(); f.Dispose(); } catch { }
            }
            _forms.Clear();
        }

        public void Dispose() { Hide(); _timer.Dispose(); }
    }
}
