using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Varta.Core;
using Varta.Platform;

namespace Varta.Ui
{
    public enum Glyph { Waves, Exclaim, Check, Question, Pause, Siren, None }

    /// <summary>
    /// Фірмовий стиль ВАРТА: глибокий темно-синій + «український» синій акцент, золотий штрих;
    /// рівні небезпеки — зелений / жовтий / червоний.
    /// </summary>
    public static class Theme
    {
        static Color C(string hex) { return ColorTranslator.FromHtml(hex); }

        public static readonly Color Navy = C("#0B1B33");
        public static readonly Color Navy2 = C("#132B4F");
        public static readonly Color Navy3 = C("#1F3F70");
        public static readonly Color Blue = C("#2F6FDE");
        public static readonly Color BlueDark = C("#2459B8");
        public static readonly Color Gold = C("#F2B705");
        public static readonly Color NavText = C("#B8C7E0");

        public static readonly Color Bg = C("#F3F5F9");
        public static readonly Color Text = C("#1B2433");
        public static readonly Color Muted = C("#667085");
        public static readonly Color Line = C("#DDE3EC");

        public static readonly Color Red = C("#D62839");
        public static readonly Color RedSoft = C("#FDECEE");
        public static readonly Color Yellow = C("#F5B700");
        public static readonly Color YellowSoft = C("#FFF5D1");
        public static readonly Color YellowInk = C("#3D2D00");
        public static readonly Color Green = C("#1F9D61");
        public static readonly Color GreenSoft = C("#E4F5EC");
        public static readonly Color GreenInk = C("#0F5132");
        public static readonly Color Grey = C("#667085");
        public static readonly Color GreySoft = C("#E8ECF2");
        public static readonly Color Orange = C("#D9480F");

        static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();

        public static Font F(float pt, FontStyle st)
        {
            string key = pt.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (int)st;
            lock (Fonts)
            {
                Font f;
                if (!Fonts.TryGetValue(key, out f))
                {
                    f = new Font("Segoe UI", pt, st, GraphicsUnit.Point);
                    Fonts[key] = f;
                }
                return f;
            }
        }

        public static Font F(float pt) { return F(pt, FontStyle.Regular); }

        public static Color LevelColor(AlertLevel l)
        {
            switch (l)
            {
                case AlertLevel.Red: return Red;
                case AlertLevel.Yellow: return Yellow;
                case AlertLevel.Clear: return Green;
                default: return Grey;
            }
        }

        public static Color LevelInk(AlertLevel l)
        {
            return l == AlertLevel.Yellow ? YellowInk : Color.White;
        }

        public static Color HealthColor(HealthState s)
        {
            switch (s)
            {
                case HealthState.Ok: return Green;
                case HealthState.Warn: return Yellow;
                case HealthState.Error: return Red;
                default: return C("#8A9BB5");
            }
        }
    }

    public static class BrandArt
    {
        public static GraphicsPath ShieldPath(RectangleF r)
        {
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            var p = new GraphicsPath();
            p.StartFigure();
            p.AddBezier(x + w * 0.5f, y, x + w * 0.62f, y + h * 0.10f, x + w * 0.82f, y + h * 0.14f, x + w, y + h * 0.14f);
            p.AddLine(x + w, y + h * 0.14f, x + w, y + h * 0.50f);
            p.AddBezier(x + w, y + h * 0.50f, x + w, y + h * 0.78f, x + w * 0.72f, y + h * 0.92f, x + w * 0.5f, y + h);
            p.AddBezier(x + w * 0.5f, y + h, x + w * 0.28f, y + h * 0.92f, x, y + h * 0.78f, x, y + h * 0.50f);
            p.AddLine(x, y + h * 0.50f, x, y + h * 0.14f);
            p.AddBezier(x, y + h * 0.14f, x + w * 0.18f, y + h * 0.14f, x + w * 0.38f, y + h * 0.10f, x + w * 0.5f, y);
            p.CloseFigure();
            return p;
        }

        static void Arc(Graphics g, Pen pen, float cx, float cy, float rad)
        {
            g.DrawArc(pen, cx - rad, cy - rad, rad * 2, rad * 2, 225f, 90f);
        }

        public static void DrawShield(Graphics g, RectangleF r, Color fill, Glyph glyph, Color ink, bool gradient)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = ShieldPath(r))
            {
                if (gradient)
                {
                    using (var br = new LinearGradientBrush(r, Theme.Blue, Theme.Navy3, 90f)) g.FillPath(br, path);
                }
                else using (var br = new SolidBrush(fill)) g.FillPath(br, path);
            }

            float w = r.Width, h = r.Height, cx = r.X + w * 0.5f;
            using (var pen = new Pen(ink, Math.Max(1.6f, w * 0.085f)))
            using (var inkBrush = new SolidBrush(ink))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (glyph)
                {
                    case Glyph.Waves:
                        {
                            float cy = r.Y + h * 0.62f;
                            g.FillEllipse(inkBrush, cx - w * 0.075f, cy - w * 0.075f, w * 0.15f, w * 0.15f);
                            Arc(g, pen, cx, cy, w * 0.24f);
                            Arc(g, pen, cx, cy, w * 0.40f);
                            break;
                        }
                    case Glyph.Exclaim:
                        g.DrawLine(pen, cx, r.Y + h * 0.27f, cx, r.Y + h * 0.57f);
                        g.FillEllipse(inkBrush, cx - w * 0.065f, r.Y + h * 0.68f, w * 0.13f, w * 0.13f);
                        break;
                    case Glyph.Check:
                        g.DrawLines(pen, new[]
                        {
                            new PointF(cx - w * 0.22f, r.Y + h * 0.50f),
                            new PointF(cx - w * 0.05f, r.Y + h * 0.65f),
                            new PointF(cx + w * 0.24f, r.Y + h * 0.33f)
                        });
                        break;
                    case Glyph.Question:
                        g.DrawArc(pen, cx - w * 0.15f, r.Y + h * 0.24f, w * 0.30f, w * 0.30f, 200f, 250f);
                        g.DrawLine(pen, cx, r.Y + h * 0.50f, cx, r.Y + h * 0.56f);
                        g.FillEllipse(inkBrush, cx - w * 0.06f, r.Y + h * 0.69f, w * 0.12f, w * 0.12f);
                        break;
                    case Glyph.Pause:
                        g.DrawLine(pen, cx - w * 0.13f, r.Y + h * 0.30f, cx - w * 0.13f, r.Y + h * 0.66f);
                        g.DrawLine(pen, cx + w * 0.13f, r.Y + h * 0.30f, cx + w * 0.13f, r.Y + h * 0.66f);
                        break;
                    case Glyph.Siren:
                        DrawSiren(g, r, ink, ink);
                        break;
                }
            }
            g.SmoothingMode = old;
        }

        /// <summary>Сирена з променями — знак ВАРТИ: купол, основа та три промені.</summary>
        public static void DrawSiren(Graphics g, RectangleF r, Color dome, Color rays)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = r.Width, h = r.Height, cx = r.X + w * 0.5f;
            using (var db = new SolidBrush(dome))
            {
                g.FillPie(db, cx - w * 0.17f, r.Y + h * 0.66f - h * 0.24f, w * 0.34f, h * 0.48f, 180, 180);
                g.FillRectangle(db, cx - w * 0.24f, r.Y + h * 0.66f, w * 0.48f, h * 0.085f);
            }
            using (var pen = new Pen(rays, Math.Max(1.5f, w * 0.07f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                float cy = r.Y + h * 0.58f;
                foreach (double deg in new[] { -90.0, -145.0, -35.0 })
                {
                    double a = deg * Math.PI / 180.0;
                    float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
                    g.DrawLine(pen, cx + c * w * 0.31f, cy + s * w * 0.31f, cx + c * w * 0.43f, cy + s * w * 0.43f);
                }
            }
            g.SmoothingMode = old;
        }

        public static Glyph GlyphFor(AlertLevel l, bool paused)
        {
            if (paused) return Glyph.Pause;
            switch (l)
            {
                case AlertLevel.Red:
                case AlertLevel.Yellow: return Glyph.Exclaim;
                case AlertLevel.Clear: return Glyph.Check;
                default: return Glyph.Question;
            }
        }

        public static Bitmap Logo(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                var r = new RectangleF(size * 0.08f, size * 0.03f, size * 0.84f, size * 0.94f);
                DrawShield(g, r, Theme.Blue, Glyph.None, Color.White, true);
                DrawSiren(g, r, Color.White, Theme.Gold);
                using (var pen = new Pen(Theme.Gold, Math.Max(1f, size * 0.025f)))
                using (GraphicsPath p = ShieldPath(new RectangleF(r.X + size * 0.035f, r.Y + size * 0.035f, r.Width - size * 0.07f, r.Height - size * 0.07f)))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawPath(pen, p);
                }
            }
            return bmp;
        }

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        static Icon _brandIcon;

        /// <summary>Фірмовий значок вікна (синій щит із хвилями).</summary>
        public static Icon BrandIcon()
        {
            if (_brandIcon != null) return _brandIcon;
            try
            {
                using (Bitmap bmp = Logo(32))
                {
                    IntPtr h = bmp.GetHicon();
                    using (Icon tmp = Icon.FromHandle(h)) _brandIcon = (Icon)tmp.Clone();
                    DestroyIcon(h);
                }
            }
            catch { _brandIcon = SystemIcons.Application; }
            return _brandIcon;
        }

        static readonly Dictionary<string, Icon> IconCache = new Dictionary<string, Icon>();

        /// <summary>Значок для системного трею: колір = рівень, плюс позначка «є проблема».</summary>
        public static Icon TrayIcon(AlertLevel level, bool paused, bool problem)
        {
            string key = level + "|" + paused + "|" + problem;
            lock (IconCache)
            {
                Icon cached;
                if (IconCache.TryGetValue(key, out cached)) return cached;
                Icon icon = null;
                try
                {
                    using (var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.Transparent);
                            Color fill = paused ? Theme.Grey : Theme.LevelColor(level);
                            Color ink = paused ? Color.White : Theme.LevelInk(level);
                            DrawShield(g, new RectangleF(3f, 1f, 26f, 30f), fill, GlyphFor(level, paused), ink, false);
                            if (problem)
                            {
                                g.SmoothingMode = SmoothingMode.AntiAlias;
                                using (var b = new SolidBrush(Theme.Orange)) g.FillEllipse(b, 17, 17, 14, 14);
                                using (var p = new Pen(Color.White, 2f))
                                {
                                    g.DrawLine(p, 24, 20, 24, 25);
                                    g.FillEllipse(Brushes.White, 23, 27, 2, 2);
                                }
                            }
                        }
                        IntPtr h = bmp.GetHicon();
                        using (Icon tmp = Icon.FromHandle(h)) icon = (Icon)tmp.Clone();
                        DestroyIcon(h);
                    }
                }
                catch { icon = SystemIcons.Application; }
                IconCache[key] = icon;
                return icon;
            }
        }
    }

    public static class UiKit
    {
        public static float Scale(Control c)
        {
            try { using (Graphics g = c.CreateGraphics()) return g.DpiX / 96f; }
            catch { return 1f; }
        }

        /// <summary>Ширина карток у прокручуваній панелі з урахуванням смуги прокрутки (однаково на Windows і Mono).</summary>
        public static int StackWidth(ScrollableControl p, int paddingH)
        {
            return Math.Max(420, Math.Min(p.ClientSize.Width, p.Width - SystemInformation.VerticalScrollBarWidth) - paddingH);
        }

        static float _screenScale;

        /// <summary>Масштаб екрана (DPI/96) без прив'язки до конкретного елемента.</summary>
        public static float ScreenScale()
        {
            if (_screenScale > 0) return _screenScale;
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) _screenScale = Math.Max(1f, g.DpiX / 96f); }
            catch { _screenScale = 1f; }
            return _screenScale;
        }

        /// <summary>Висота тексту в «логічних» пікселях (96 DPI) для заданої логічної ширини.</summary>
        public static int FitHeight(string text, Font f, int logicalWidth)
        {
            float k = ScreenScale();
            int h = TextHeight(text, f, (int)(logicalWidth * k));
            return (int)Math.Ceiling(h / k) + 2;
        }

        public static Button Btn(string text, bool primary)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = Theme.F(9.5f),
                UseVisualStyleBackColor = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 3, 8, 3),
                MinimumSize = new Size(0, 32),
                Margin = new Padding(0, 0, 8, 0),
                BackColor = primary ? Theme.Blue : Color.White,
                ForeColor = primary ? Color.White : Theme.Text
            };
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Theme.Line;
            b.FlatAppearance.MouseOverBackColor = primary ? Theme.BlueDark : Theme.GreySoft;
            b.FlatAppearance.MouseDownBackColor = primary ? Theme.Navy3 : Theme.Line;
            return b;
        }

        public static Label Lbl(string text, float pt, bool bold, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = Theme.F(pt, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color,
                BackColor = Color.Transparent
            };
        }

        public static int TextHeight(string text, Font f, int width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return TextRenderer.MeasureText(text, f, new Size(Math.Max(20, width), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        }

        public static void Open(string pathOrUrl)
        {
            try { Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Warn("Не вдалося відкрити «" + pathOrUrl + "»: " + ex.Message); }
        }
    }
}
