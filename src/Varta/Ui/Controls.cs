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
    /// <summary>Біла картка з тонкою рамкою.</summary>
    public sealed class Card : Panel
    {
        public Card()
        {
            BackColor = Color.White;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ControlPaint.DrawBorder(e.Graphics, ClientRectangle, Theme.Line, ButtonBorderStyle.Solid);
        }
    }

    /// <summary>Пункт бічного меню.</summary>
    public sealed class NavButton : Control
    {
        bool _hover;
        bool _selected;
        Color? _badge;

        public NavButton(string text)
        {
            Text = text;
            Height = 44;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public bool Selected { get { return _selected; } set { _selected = value; Invalidate(); } }
        public Color? Badge { get { return _badge; } set { _badge = value; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color bg = _selected ? Theme.Navy3 : (_hover ? Theme.Navy2 : Theme.Navy);
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, ClientRectangle);
            if (_selected) using (var b = new SolidBrush(Theme.Gold)) g.FillRectangle(b, 0, 0, 4, Height);
            Rectangle r = new Rectangle(24, 0, Width - 48, Height);
            TextRenderer.DrawText(g, Text, Theme.F(10.5f, _selected ? FontStyle.Bold : FontStyle.Regular), r,
                _selected ? Color.White : Theme.NavText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (_badge.HasValue)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(_badge.Value)) g.FillEllipse(b, Width - 30, Height / 2 - 5, 10, 10);
            }
        }
    }

    public sealed class StatusModel
    {
        public AlertLevel Level = AlertLevel.Unknown;
        public bool Paused;
        public string Title = "";
        public string Subtitle = "";
        public string Meta1 = "";
        public string Meta2 = "";
    }

    /// <summary>Велика картка поточного рівня небезпеки.</summary>
    public sealed class StatusCard : Control
    {
        StatusModel _m = new StatusModel();

        public StatusCard()
        {
            Height = 190;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void SetModel(StatusModel m) { _m = m; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = Math.Max(1f, g.DpiX / 96f);

            Color bg, ink, sub;
            AlertLevel lvl = _m.Paused ? AlertLevel.Unknown : _m.Level;
            switch (lvl)
            {
                case AlertLevel.Red: bg = Theme.Red; ink = Color.White; sub = Color.FromArgb(235, 255, 255, 255); break;
                case AlertLevel.Yellow: bg = Theme.Yellow; ink = Theme.YellowInk; sub = Color.FromArgb(220, 61, 45, 0); break;
                case AlertLevel.Clear: bg = Theme.GreenSoft; ink = Theme.GreenInk; sub = Color.FromArgb(220, 15, 81, 50); break;
                default: bg = Theme.GreySoft; ink = Theme.Text; sub = Theme.Muted; break;
            }

            using (var b = new SolidBrush(bg)) g.FillRectangle(b, ClientRectangle);
            using (var b = new SolidBrush(_m.Paused ? Theme.Grey : Theme.LevelColor(lvl))) g.FillRectangle(b, 0, 0, (int)(8 * k), Height);

            int shield = (int)(Height * 0.62f);
            Color shieldFill = lvl == AlertLevel.Red ? Color.White : (lvl == AlertLevel.Yellow ? Theme.YellowInk : (_m.Paused ? Theme.Grey : Theme.LevelColor(lvl)));
            Color glyphInk = lvl == AlertLevel.Red ? Theme.Red : (lvl == AlertLevel.Yellow ? Theme.Yellow : Color.White);
            BrandArt.DrawShield(g, new RectangleF(28 * k, (Height - shield) / 2f, shield * 0.88f, shield), shieldFill, BrandArt.GlyphFor(_m.Level, _m.Paused), glyphInk, false);

            int x = (int)(28 * k + shield * 0.88f + 28 * k);
            int w = Width - x - (int)(24 * k);
            var flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            int y = (int)(22 * k);
            TextRenderer.DrawText(g, _m.Title, Theme.F(24f, FontStyle.Bold), new Rectangle(x, y, w, (int)(44 * k)), ink, flags);
            y += (int)(48 * k);
            TextRenderer.DrawText(g, _m.Subtitle, Theme.F(12f), new Rectangle(x, y, w, (int)(46 * k)), sub, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            y = Height - (int)(54 * k);
            TextRenderer.DrawText(g, _m.Meta1, Theme.F(10f, FontStyle.Bold), new Rectangle(x, y, w, (int)(22 * k)), sub, flags);
            TextRenderer.DrawText(g, _m.Meta2, Theme.F(9.5f), new Rectangle(x, y + (int)(22 * k), w, (int)(22 * k)), sub, flags);
        }
    }

    /// <summary>Список перевірок стану: кольорова позначка, назва, пояснення, кнопка дії.</summary>
    public sealed class HealthPanel : Panel
    {
        List<HealthItem> _items = new List<HealthItem>();
        readonly Dictionary<string, Button> _btns = new Dictionary<string, Button>();
        readonly List<Rectangle> _rows = new List<Rectangle>();
        string _sig = "";

        public event Action<string> ActionClicked;

        public HealthPanel()
        {
            BackColor = Color.White;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        float K { get { return Math.Max(1f, UiKit.Scale(this)); } }

        public void SetItems(List<HealthItem> items)
        {
            _items = items;
            string sig = string.Join("|", items.Select(i => i.Id + ":" + i.ActionId + ":" + i.ActionText).ToArray());
            if (sig != _sig)
            {
                foreach (Button b in _btns.Values) { Controls.Remove(b); b.Dispose(); }
                _btns.Clear();
                foreach (HealthItem it in items)
                {
                    if (string.IsNullOrEmpty(it.ActionId)) continue;
                    Button b = UiKit.Btn(it.ActionText, false);
                    b.MinimumSize = new Size(0, 28);
                    b.Font = Theme.F(9f);
                    string action = it.ActionId;
                    b.Click += delegate { var h = ActionClicked; if (h != null) h(action); };
                    _btns[it.Id] = b;
                    Controls.Add(b);
                }
                _sig = sig;
            }
            LayoutRows();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutRows();
        }

        void LayoutRows()
        {
            if (Width <= 0) return;
            float k = K;
            int y = (int)(4 * k);
            int titleW = (int)(190 * k);
            _rows.Clear();
            foreach (HealthItem it in _items)
            {
                Button b;
                _btns.TryGetValue(it.Id, out b);
                int btnW = b != null ? b.Width + (int)(16 * k) : 0;
                int textW = Width - (int)(44 * k) - titleW - btnW - (int)(16 * k);
                int h = Math.Max((int)(38 * k), UiKit.TextHeight(it.Message, Theme.F(9.5f), textW) + (int)(16 * k));
                _rows.Add(new Rectangle(0, y, Width, h));
                if (b != null) b.Location = new Point(Width - b.Width - (int)(12 * k), y + (h - b.Height) / 2);
                y += h;
            }
            y += (int)(4 * k);
            if (Height != y) Height = y;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = K;
            int titleW = (int)(190 * k);
            for (int i = 0; i < _items.Count && i < _rows.Count; i++)
            {
                HealthItem it = _items[i];
                Rectangle r = _rows[i];
                if (i > 0) using (var p = new Pen(Theme.Line)) g.DrawLine(p, (int)(16 * k), r.Y, Width - (int)(16 * k), r.Y);
                using (var b = new SolidBrush(Theme.HealthColor(it.State)))
                    g.FillEllipse(b, (int)(20 * k), r.Y + r.Height / 2 - (int)(6 * k), (int)(12 * k), (int)(12 * k));
                TextRenderer.DrawText(g, it.Title, Theme.F(9.75f, FontStyle.Bold), new Rectangle((int)(44 * k), r.Y, titleW, r.Height), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                Button b2;
                _btns.TryGetValue(it.Id, out b2);
                int btnW = b2 != null ? b2.Width + (int)(16 * k) : 0;
                Color c = it.State == HealthState.Error ? Theme.Red : (it.State == HealthState.Warn ? Color.FromArgb(138, 94, 0) : Theme.Muted);
                TextRenderer.DrawText(g, it.Message, Theme.F(9.5f), new Rectangle((int)(44 * k) + titleW, r.Y, Width - (int)(44 * k) - titleW - btnW - (int)(16 * k), r.Height), c,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
        }
    }
}
