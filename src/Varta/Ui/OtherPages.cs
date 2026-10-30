using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Varta.Core;
using Varta.Platform;

namespace Varta.Ui
{
    public sealed class DashboardPage : PageBase
    {
        readonly AppController _c;
        readonly FlowLayoutPanel _flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(24, 20, 24, 20),
            BackColor = Theme.Bg
        };
        readonly StatusCard _card = new StatusCard { Margin = new Padding(0, 0, 0, 16) };
        readonly FlowLayoutPanel _actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 16), BackColor = Theme.Bg };
        readonly Card _healthCard = new Card { Height = 120, Margin = new Padding(0, 0, 0, 16) };
        readonly HealthPanel _health = new HealthPanel();
        readonly Card _eventsCard = new Card { Height = 260, Margin = new Padding(0, 0, 0, 16) };
        readonly RichTextBox _events = new RichTextBox();
        readonly Button _pauseBtn;
        int _lastLogCount = -1;

        public event Action<string> Navigate;

        public override string PageTitle { get { return "Стан"; } }
        public override string PageSubtitle { get { return "Поточний рівень небезпеки та перевірка системи"; } }

        public DashboardPage(AppController c)
        {
            _c = c;

            Button check = UiKit.Btn("Перевірити зараз", true);
            Button test = UiKit.Btn("Тест сповіщень ▾", false);
            _pauseBtn = UiKit.Btn("Призупинити ▾", false);
            Button stop = UiKit.Btn("Зупинити звук", false);
            check.Click += delegate { _c.Monitor.RefreshNow(); };
            stop.Click += delegate { _c.StopSound(); _c.Banners.Hide(); };

            var testMenu = new ContextMenuStrip { Font = Theme.F(9.5f) };
            testMenu.Items.Add("Червоний рівень (тривога)", null, delegate { _c.RaiseTest(AlertEventKind.RedStarted); });
            testMenu.Items.Add("Жовтий рівень", null, delegate { _c.RaiseTest(AlertEventKind.YellowStarted); });
            testMenu.Items.Add("Відбій тривоги", null, delegate { _c.RaiseTest(AlertEventKind.AllClear); });
            test.Click += delegate { testMenu.Show(test, new Point(0, test.Height)); };

            var pauseMenu = new ContextMenuStrip { Font = Theme.F(9.5f) };
            pauseMenu.Items.Add("на 15 хвилин", null, delegate { _c.Monitor.PauseFor(TimeSpan.FromMinutes(15)); });
            pauseMenu.Items.Add("на 30 хвилин", null, delegate { _c.Monitor.PauseFor(TimeSpan.FromMinutes(30)); });
            pauseMenu.Items.Add("на 1 годину", null, delegate { _c.Monitor.PauseFor(TimeSpan.FromHours(1)); });
            pauseMenu.Items.Add("на 2 години", null, delegate { _c.Monitor.PauseFor(TimeSpan.FromHours(2)); });
            _pauseBtn.Click += delegate
            {
                if (_c.Monitor.IsPaused) _c.Monitor.Resume("вручну");
                else pauseMenu.Show(_pauseBtn, new Point(0, _pauseBtn.Height));
            };

            _actions.Controls.AddRange(new Control[] { check, test, _pauseBtn, stop });

            Label ht = UiKit.Lbl("Перевірка системи", 12f, true, Theme.Navy);
            ht.Location = new Point(20, 14);
            _healthCard.Controls.Add(ht);
            _health.SetBounds(1, 48, 600, 60);
            _health.ActionClicked += OnHealthAction;
            _health.SizeChanged += delegate { _healthCard.Height = _health.Bottom + 8; };
            _healthCard.Controls.Add(_health);
            _healthCard.SizeChanged += delegate { FitInner(); };
            _eventsCard.SizeChanged += delegate { FitInner(); };

            Label et = UiKit.Lbl("Останні події", 12f, true, Theme.Navy);
            et.Location = new Point(20, 14);
            _eventsCard.Controls.Add(et);
            _events.SetBounds(16, 48, 620, 196);
            _events.ReadOnly = true;
            _events.BorderStyle = BorderStyle.None;
            _events.BackColor = Color.White;
            _events.Font = Theme.F(9.5f);
            _events.ScrollBars = RichTextBoxScrollBars.Vertical;
            _eventsCard.Controls.Add(_events);

            _flow.Controls.Add(_card);
            _flow.Controls.Add(_actions);
            _flow.Controls.Add(_healthCard);
            _flow.Controls.Add(_eventsCard);
            Controls.Add(_flow);
            _flow.Resize += delegate { Relayout(); };
            _flow.ClientSizeChanged += delegate { Relayout(); };
            _flow.Layout += delegate { Relayout(); };
            Relayout();
        }

        bool _relayout;

        void Relayout()
        {
            if (_relayout) return;
            _relayout = true;
            try
            {
                int w = UiKit.StackWidth(_flow, _flow.Padding.Horizontal);
                if (_card.Width != w) _card.Width = w;
                if (_healthCard.Width != w) _healthCard.Width = w;
                if (_eventsCard.Width != w) _eventsCard.Width = w;
                FitInner();
            }
            finally { _relayout = false; }
        }

        /// <summary>Вкладені елементи — явна ширина картки (без Anchor: його поведінка залежить від моменту першої розкладки).</summary>
        void FitInner()
        {
            int hw = _healthCard.ClientSize.Width - 2;
            if (hw > 100 && _health.Width != hw) _health.Width = hw;
            int ew = _eventsCard.ClientSize.Width - 32;
            if (ew > 100 && _events.Width != ew) _events.Width = ew;
        }

        public override void OnShown() { Relayout(); UpdateView(_c.Health()); }

        void OnHealthAction(string id)
        {
            switch (id)
            {
                case "resume": _c.Monitor.Resume("вручну"); break;
                case "refresh": _c.Monitor.RefreshNow(); break;
                case "autostart-on":
                    string err = _c.SetAutostart(true);
                    if (err != null) MessageBox.Show("Не вдалося увімкнути автозапуск: " + err, "ВАРТА", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
                default:
                    var h = Navigate;
                    if (h != null && id.StartsWith("goto-")) h(id.Substring(5));
                    break;
            }
            UpdateView(_c.Health());
        }

        public StatusModel BuildModel()
        {
            var m = new StatusModel();
            AlertEngine eng = _c.Monitor.Engine;
            EngineState st = eng.State;
            DateTime now = DateTime.UtcNow;

            if (_c.Monitor.IsPaused)
            {
                m.Paused = true;
                m.Title = "МОНІТОРИНГ ПРИЗУПИНЕНО";
                m.Subtitle = "Тривоги зараз не відстежуються. Автоматичне відновлення о " + _c.Monitor.PausedUntilUtc.Value.ToLocalTime().ToString("HH:mm") + ".";
                m.Meta1 = "Натисніть «Відновити моніторинг», щоб увімкнути негайно.";
                return m;
            }

            AlertLevel lvl = eng.EffectiveLevel();
            m.Level = lvl;
            if (lvl == AlertLevel.Unknown)
            {
                m.Title = "СТАН НЕВІДОМИЙ";
                m.Subtitle = st.LastOkUtc.HasValue
                    ? "Немає свіжих даних від джерел (остання відповідь " + HealthService.Ago(st.LastOkUtc.Value) + "). Останній відомий рівень: " + Lang.LevelShort(st.Level) + "."
                    : (st.ConsecutiveFailures > 0 ? "Не вдається отримати дані: " + st.LastError : "Очікування першої відповіді від джерела…");
                m.Meta1 = "Не покладайтесь на цей екран — дійте за офіційними сигналами.";
            }
            else
            {
                m.Title = Lang.LevelTitle(lvl);
                m.Subtitle = Lang.LevelSubtitle(lvl) + (string.IsNullOrEmpty(st.Detail) || lvl == AlertLevel.Clear ? "" : "\n" + st.Detail);
                if (lvl != AlertLevel.Clear && st.SinceUtc.HasValue)
                    m.Meta1 = "Триває " + TextUtil.FormatDuration(now - st.SinceUtc.Value) + " • з " + st.SinceUtc.Value.ToLocalTime().ToString("HH:mm");
            }
            m.Meta2 = "Джерело: " + (string.IsNullOrEmpty(st.Source) ? "—" : st.Source) +
                      (st.LastOkUtc.HasValue ? " • перевірено " + HealthService.Ago(st.LastOkUtc.Value) : "") +
                      " • кожні " + _c.Settings.PollIntervalSeconds + " с";
            return m;
        }

        public void UpdateView(List<HealthItem> items)
        {
            if (IsDisposed) return;
            _card.SetModel(BuildModel());
            _pauseBtn.Text = _c.Monitor.IsPaused ? "Відновити моніторинг" : "Призупинити ▾";
            _health.SetItems(items);
            RefreshEvents();
        }

        void RefreshEvents()
        {
            List<LogEntry> all = Log.Snapshot();
            if (all.Count == _lastLogCount) return;
            _lastLogCount = all.Count;
            _events.SuspendLayout();
            _events.Clear();
            foreach (LogEntry e in all.Skip(Math.Max(0, all.Count - 12)).Reverse())
                LogPage.AppendLine(_events, e, true);
            _events.SelectionStart = 0;
            _events.ResumeLayout();
        }
    }

    public sealed class LogPage : PageBase
    {
        readonly RichTextBox _box = new RichTextBox();
        readonly ComboBox _filter = new ComboBox();

        public override string PageTitle { get { return "Журнал"; } }
        public override string PageSubtitle { get { return "Усі події, тривоги та помилки (також зберігаються у файлах у папці logs)"; } }

        public LogPage(AppController c)
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Theme.Bg, Padding = new Padding(24, 12, 24, 8) };
            _filter.DropDownStyle = ComboBoxStyle.DropDownList;
            _filter.Items.AddRange(new object[] { "Усі записи", "Лише тривоги", "Лише проблеми" });
            _filter.SelectedIndex = 0;
            _filter.Font = Theme.F(9.5f);
            _filter.Width = 180;
            _filter.Location = new Point(24, 14);
            _filter.SelectedIndexChanged += delegate { Render(); };
            Button open = UiKit.Btn("Відкрити файл журналу", false);
            Button copy = UiKit.Btn("Копіювати все", false);
            open.Location = new Point(220, 11);
            copy.Location = new Point(220 + 200, 11);
            open.Click += delegate
            {
                string f = Path.Combine(Paths.LogDir, "varta-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                UiKit.Open(File.Exists(f) ? f : Paths.LogDir);
            };
            copy.Click += delegate { try { if (_box.TextLength > 0) Clipboard.SetText(_box.Text); } catch { } };
            top.Controls.Add(_filter);
            top.Controls.Add(open);
            top.Controls.Add(copy);

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 0, 24, 20), BackColor = Theme.Bg };
            var frame = new Card { Dock = DockStyle.Fill, Padding = new Padding(8) };
            _box.Dock = DockStyle.Fill;
            _box.ReadOnly = true;
            _box.BorderStyle = BorderStyle.None;
            _box.BackColor = Color.White;
            _box.Font = new Font("Consolas", 9.5f);
            _box.DetectUrls = false;
            frame.Controls.Add(_box);
            host.Controls.Add(frame);

            Controls.Add(host);
            Controls.Add(top);
            Log.Added += OnAdded;
            Disposed += delegate { Log.Added -= OnAdded; };
            Render();
        }

        bool Passes(LogEntry e)
        {
            switch (_filter.SelectedIndex)
            {
                case 1: return e.Kind == LogKind.Alert;
                case 2: return e.Kind == LogKind.Warn || e.Kind == LogKind.Error;
                default: return true;
            }
        }

        void OnAdded(LogEntry e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (Passes(e)) { AppendLine(_box, e, false); _box.ScrollToCaret(); }
                }));
            }
            catch { }
        }

        void Render()
        {
            _box.Clear();
            foreach (LogEntry e in Log.Snapshot()) if (Passes(e)) AppendLine(_box, e, false);
            _box.SelectionStart = _box.TextLength;
            _box.ScrollToCaret();
        }

        public override void OnShown() { Render(); }

        public static void AppendLine(RichTextBox box, LogEntry e, bool compact)
        {
            Color col = Theme.Text;
            FontStyle fs = FontStyle.Regular;
            switch (e.Kind)
            {
                case LogKind.Alert: col = Theme.Red; fs = FontStyle.Bold; break;
                case LogKind.Error: col = Theme.Red; break;
                case LogKind.Warn: col = Color.FromArgb(150, 90, 0); break;
            }
            string line = e.Time.ToString(compact ? "HH:mm:ss" : "dd.MM HH:mm:ss") + "  " + e.Text + "\n";
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionColor = col;
            box.SelectionFont = new Font(box.Font, fs);
            box.AppendText(line);
        }
    }

    public sealed class AboutPage : PageBase
    {
        public override string PageTitle { get { return "Про ВАРТУ"; } }
        public override string PageSubtitle { get { return "Як це працює, важливе застереження та поради"; } }

        public AboutPage()
        {
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                Padding = new Padding(24, 20, 24, 20), BackColor = Theme.Bg
            };
            var card = new Card { Width = 700, Margin = new Padding(0, 0, 0, 16) };
            var logo = new PictureBox { Image = BrandArt.Logo(96), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(96, 96), Location = new Point(24, 22), BackColor = Color.Transparent };
            card.Controls.Add(logo);
            Label name = UiKit.Lbl(AppInfo.Name, 26f, true, Theme.Navy);
            name.Location = new Point(136, 22);
            card.Controls.Add(name);
            Label tag = UiKit.Lbl(AppInfo.Tagline + " • версія " + AppInfo.Version, 11f, false, Theme.Muted);
            tag.Location = new Point(138, 70);
            card.Controls.Add(tag);
            Label motto = UiKit.Lbl("Спокійно. Уважно. Вчасно.", 10.5f, true, Theme.Blue);
            motto.Location = new Point(138, 96);
            card.Controls.Add(motto);

            int y = 140;
            y = AddBlock(card, y, "Як це працює",
                "ВАРТА стежить за станом повітряної тривоги у вашому місті й оголошує його в офісі: голосом через колонки (музика на цей час тихішає), великим банером поверх вікон, " +
                "спливаючим вікном Windows і, за бажанням, повідомленням адміністратору в Telegram.");
            y = AddBlock(card, y, "Рівні небезпеки",
                "ЧЕРВОНИЙ — повітряна тривога: негайно пройдіть в укриття.\n" +
                "ЖОВТИЙ — підвищена загроза: будьте уважні та готові до укриття.\n" +
                "ЗЕЛЕНИЙ — відбій тривоги, працюємо у звичайному режимі.\n" +
                "СІРИЙ — стан невідомий: немає свіжих даних. Ніколи не вважайте його «все спокійно».");
            y = AddBlock(card, y, "Важливо",
                "ВАРТА — допоміжний засіб і не замінює офіційну сирену, державні застосунки та вказівки влади. Дані надходять із зовнішніх джерел і можуть запізнюватися або тимчасово бути недоступними. " +
                "Завжди дійте за офіційними сигналами оповіщення.");
            y = AddBlock(card, y, "Свої голосові файли",
                "Вкладка «Звук» → «Обрати файл…»: обраний файл застосовується одразу, зберігати нічого не треба. Інші налаштування діють після кнопки «Зберегти налаштування» — вона з'являється внизу вікна на будь-якій сторінці. " +
                "Підтримуються MP3 і WAV. Можна й просто замінити MP3 у папці sounds (з тими самими назвами). Кнопка «▶ Прослухати» показує, чи відтворився файл і яким способом. " +
                "Якщо файл відтворити не вдалося, замість голосу лунає різкий сигнал із шести гудків, а причина видна в «Стані» та в журналі.");
            y = AddBlock(card, y, "Перенесення на інший комп'ютер",
                "Скопіюйте всю папку ВАРТИ (разом із settings.json і sounds) і запустіть Varta.exe. Windows може показати попередження SmartScreen для невідомої програми: «Докладніше» → «Все одно виконати».");
            y = AddBlock(card, y, "Відкриті компоненти",
                "Для розкодування MP3 використано NLayer (ліцензія MIT, © Mark Heath, Andrew Ward & Contributors). Текст ліцензії — у файлі THIRD_PARTY_NOTICES.txt поруч із програмою.");
            y = AddBlock(card, y, "Якщо щось не працює",
                "Вкладка «Стан» показує перевірку системи з підказками. Вкладка «Система» → «Запустити діагностику» збирає звіт, який можна надіслати тому, хто вас підтримує.");
            card.Height = y + 10;
            flow.Controls.Add(card);
            Controls.Add(flow);
            Action fit = delegate { int w = UiKit.StackWidth(flow, flow.Padding.Horizontal); if (card.Width != w) card.Width = w; };
            flow.Resize += delegate { fit(); };
            flow.ClientSizeChanged += delegate { fit(); };
            flow.Layout += delegate { fit(); };
        }

        static int AddBlock(Card card, int y, string title, string text)
        {
            Label t = UiKit.Lbl(title, 11f, true, Theme.Navy);
            t.Location = new Point(24, y);
            card.Controls.Add(t);
            y += 26;
            var l = new Label { Text = text, AutoSize = false, Font = Theme.F(9.75f), ForeColor = Theme.Text, BackColor = Color.Transparent };
            int h = UiKit.FitHeight(text, l.Font, 652);
            l.SetBounds(24, y, 652, h);
            l.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(l);
            return y + h + 18;
        }
    }
}
