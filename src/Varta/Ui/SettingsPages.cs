using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Varta.Core;
using Varta.Platform;

namespace Varta.Ui
{
    public sealed class NotificationsPage : SettingsPage
    {
        public override string PageTitle { get { return "Сповіщення"; } }
        public override string PageSubtitle { get { return "Хто, коли і як сповіщається: голос, банер, спливаюче вікно, Telegram"; } }

        public NotificationsPage(AppController c) : base(c)
        {
            CardBuilder g = NewCard("Які сповіщення вмикати",
                "Відмічайте, чим сповіщати про кожну подію. Голос доступний лише для подій про тривогу. Технічні події (зв'язок, помилки) краще залишати текстовими: " +
                "банер, спливаюче вікно Windows або повідомлення адміністратору в Telegram.");
            DataGridView grid = BuildGrid();
            g.Custom(grid, 40 + RoutePolicy.Order.Length * 32 + 4);

            Button tr = UiKit.Btn("Перевірити: червоний рівень", false);
            Button ty = UiKit.Btn("Перевірити: жовтий рівень", false);
            Button tc = UiKit.Btn("Перевірити: відбій", false);
            Button ts = UiKit.Btn("Зупинити звук і сховати банер", false);
            tr.Click += delegate { C.RaiseTest(AlertEventKind.RedStarted); };
            ty.Click += delegate { C.RaiseTest(AlertEventKind.YellowStarted); };
            tc.Click += delegate { C.RaiseTest(AlertEventKind.AllClear); };
            ts.Click += delegate { C.StopSound(); C.Banners.Hide(); };
            g.Buttons(tr, ty, tc, ts);
            g.Finish();

            CardBuilder r = NewCard("Повторення голосу та нагадування", null);
            r.Num("Повторів при червоному рівні", 1, 5, s => s.RepeatRed, (s, v) => s.RepeatRed = v, "разів", "Скільки разів поспіль прозвучить оголошення про тривогу.");
            r.Num("Повторів для інших подій", 1, 3, s => s.RepeatOther, (s, v) => s.RepeatOther = v, "разів", null);
            r.Num("Пауза між повторами", 0, 30, s => s.RepeatGapSeconds, (s, v) => s.RepeatGapSeconds = v, "секунд", null);
            r.Num("Нагадувати, поки триває тривога", 0, 120, s => s.ReminderMinutes, (s, v) => s.ReminderMinutes = v, "хвилин", "0 — не нагадувати. Нагадування озвучується за правилом «Нагадування» у таблиці вище.");
            r.Finish();

            CardBuilder b = NewCard("Банер на екрані", "Велика кольорова смуга зверху екрана поверх усіх вікон. Вона не забирає фокус, тож не заважає друкувати.");
            b.Check("Показувати на всіх екранах", s => s.BannerAllScreens, (s, v) => s.BannerAllScreens = v, null);
            b.Check("Тримати банер жовтого/червоного рівня до відбою", s => s.BannerStayUntilClear, (s, v) => s.BannerStayUntilClear = v,
                "Якщо вимкнено — банер сам зникне через вказаний час.");
            b.Num("Показувати іншим подіям", 5, 600, s => s.BannerSeconds, (s, v) => s.BannerSeconds = v, "секунд", "Для відбою та технічних повідомлень.");
            b.Num("Висота банера", 100, 400, s => s.BannerHeight, (s, v) => s.BannerHeight = v, "пікселів", null);
            b.Finish();

            CardBuilder t = NewCard("Тексти повідомлень",
                "Заголовок і текст для банера, спливаючого вікна та Telegram. Можна використовувати: {city} — місто, {time} — час, {detail} — деталі від джерела, {org} — назва організації.");
            t.Txt("Червоний рівень — заголовок", s => s.TextRedTitle, (s, v) => s.TextRedTitle = v, null, false, 1);
            t.Txt("Червоний рівень — текст", s => s.TextRedBody, (s, v) => s.TextRedBody = v, null, false, 1);
            t.Txt("Жовтий → червоний — заголовок", s => s.TextRedFromYellowTitle, (s, v) => s.TextRedFromYellowTitle = v, null, false, 1);
            t.Txt("Жовтий → червоний — текст", s => s.TextRedFromYellowBody, (s, v) => s.TextRedFromYellowBody = v, null, false, 1);
            t.Txt("Жовтий рівень — заголовок", s => s.TextYellowTitle, (s, v) => s.TextYellowTitle = v, null, false, 1);
            t.Txt("Жовтий рівень — текст", s => s.TextYellowBody, (s, v) => s.TextYellowBody = v, null, false, 1);
            t.Txt("Червоний → жовтий — заголовок", s => s.TextYellowFromRedTitle, (s, v) => s.TextYellowFromRedTitle = v, null, false, 1);
            t.Txt("Червоний → жовтий — текст", s => s.TextYellowFromRedBody, (s, v) => s.TextYellowFromRedBody = v, null, false, 1);
            t.Txt("Відбій — заголовок", s => s.TextClearTitle, (s, v) => s.TextClearTitle = v, null, false, 1);
            t.Txt("Відбій — текст", s => s.TextClearBody, (s, v) => s.TextClearBody = v, null, false, 1);
            t.Finish();

            Bind(s => LoadGrid(grid, s), s => SaveGrid(grid, s));
            grid.CurrentCellDirtyStateChanged += delegate { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += delegate(object o, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) MarkChanged(); };
        }

        DataGridView BuildGrid()
        {
            var grid = new DataGridView
            {
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToResizeColumns = false,
                AllowUserToOrderColumns = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Theme.Line,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 40,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                ScrollBars = ScrollBars.None,
                Font = Theme.F(9.5f)
            };
            grid.RowTemplate.Height = 32;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.GreySoft,
                ForeColor = Theme.Text,
                Font = Theme.F(9.25f, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                SelectionBackColor = Theme.GreySoft,
                SelectionForeColor = Theme.Text,
                WrapMode = DataGridViewTriState.True
            };
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 240, 253);
            grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            grid.DefaultCellStyle.ForeColor = Theme.Text;

            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Подія", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
            string[] heads = { "Голос", "Банер", "Вікно Windows", "Telegram адміну" };
            foreach (string h in heads)
                grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = h, Width = 96, SortMode = DataGridViewColumnSortMode.NotSortable });

            foreach (AlertEventKind k in RoutePolicy.Order)
            {
                int i = grid.Rows.Add(RoutePolicy.Title(k), false, false, false, false);
                grid.Rows[i].Tag = k;
                if (!RoutePolicy.VoiceCapable(k))
                {
                    DataGridViewCell cell = grid.Rows[i].Cells[1];
                    cell.ReadOnly = true;
                    cell.Style.BackColor = Theme.GreySoft;
                    cell.Style.SelectionBackColor = Theme.GreySoft;
                }
            }
            return grid;
        }

        static void LoadGrid(DataGridView grid, AppSettings s)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                Channel c = RoutePolicy.Get(s, (AlertEventKind)row.Tag);
                row.Cells[1].Value = (c & Channel.Voice) != 0;
                row.Cells[2].Value = (c & Channel.Banner) != 0;
                row.Cells[3].Value = (c & Channel.Tray) != 0;
                row.Cells[4].Value = (c & Channel.Admin) != 0;
            }
        }

        static void SaveGrid(DataGridView grid, AppSettings s)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                Channel c = Channel.None;
                if (true.Equals(row.Cells[1].Value)) c |= Channel.Voice;
                if (true.Equals(row.Cells[2].Value)) c |= Channel.Banner;
                if (true.Equals(row.Cells[3].Value)) c |= Channel.Tray;
                if (true.Equals(row.Cells[4].Value)) c |= Channel.Admin;
                RoutePolicy.Set(s, (AlertEventKind)row.Tag, c);
            }
        }
    }

    public sealed class SoundPage : SettingsPage
    {
        public override string PageTitle { get { return "Звук"; } }
        public override string PageSubtitle { get { return "Ваші голосові файли, гучність, приглушення музики та розклад"; } }

        public SoundPage(AppController c) : base(c)
        {
            CardBuilder f = NewCard("Голосові оголошення",
                "Оберіть свої MP3-файли для кожної ситуації. Обраний файл застосовується одразу — кнопку «Зберегти» натискати не потрібно. " +
                "Файл копіюється в папку «sounds» поруч із програмою, тож усе переноситься разом. Можна й просто замінити файли в папці sounds, залишивши ті самі назви.");
            f.Check("Озвучувати оголошення (головний перемикач голосу)", s => s.VoiceEnabled, (s, v) => s.VoiceEnabled = v,
                "Вимкніть, якщо потрібні лише банер і текстові сповіщення. Окремі події можна вимкнути у вкладці «Сповіщення».");
            f.SoundRow("Червоний рівень (тривога)", s => s.SoundRed, (s, v) => s.SoundRed = v, "sounds\\red_alert.mp3", null);
            f.SoundRow("Жовтий рівень", s => s.SoundYellow, (s, v) => s.SoundYellow = v, "sounds\\yellow_alert.mp3", null);
            f.SoundRow("Зниження: червоний → жовтий", s => s.SoundYellowFromRed, (s, v) => s.SoundYellowFromRed = v, "sounds\\downgrade_yellow.mp3", null);
            f.SoundRow("Відбій тривоги", s => s.SoundClear, (s, v) => s.SoundClear = v, "sounds\\all_clear.mp3", null);
            f.SoundRow("Нагадування під час тривоги", s => s.SoundReminder, (s, v) => s.SoundReminder = v, "", "Порожньо — використовується файл червоного рівня.", s => s.SoundRed, "файл червоного рівня");
            f.Finish();

            CardBuilder v2 = NewCard("Гучність і музика",
                "Під час оголошення музика в салоні тихішає, а потім повертається до колишньої гучності. Працює з будь-яким плеєром або браузером.");
            v2.Num("Гучність оголошення", 10, 100, s => s.AnnounceVolumePercent, (s, v) => s.AnnounceVolumePercent = v, "%", null);
            v2.Check("Приглушувати музику під час оголошення", s => s.DuckMusic, (s, v) => s.DuckMusic = v, null);
            v2.Num("Залишати музиці", 0, 80, s => s.MusicDuckPercent, (s, v) => s.MusicDuckPercent = v, "% її гучності", "0 — повна тиша, 10 — ледь чутно.");
            v2.Check("Піднімати гучність Windows під час оголошення, якщо вона низька", s => s.BoostMasterVolume, (s, v) => s.BoostMasterVolume = v,
                "Після оголошення гучність повертається як була.");
            v2.Num("Мінімальна гучність Windows", 10, 100, s => s.MinMasterVolumePercent, (s, v) => s.MinMasterVolumePercent = v, "%", null);
            v2.Check("Вмикати звук, якщо в Windows увімкнено «Без звуку» (mute)", s => s.UnmuteMaster, (s, v) => s.UnmuteMaster = v, null);
            v2.Finish();

            CardBuilder sch = NewCard("Розклад голосових оголошень",
                "Поза робочим часом голос мовчить (щоб не лунати в порожньому салоні), а банер, спливаюче вікно та Telegram працюють як зазвичай.");
            sch.Check("Озвучувати лише в робочий час", s => s.VoiceOnlyInWorkHours, (s, v) => s.VoiceOnlyInWorkHours = v, null);
            sch.Time("Початок", s => s.WorkStart, (s, v) => s.WorkStart = v, null);
            sch.Time("Кінець", s => s.WorkEnd, (s, v) => s.WorkEnd = v, "Якщо кінець раніше за початок — діапазон переходить через північ.");
            sch.Days("Робочі дні", s => s.WorkDays, (s, v) => s.WorkDays = v);
            sch.Finish();
        }
    }

    public sealed class SourcesPage : SettingsPage
    {
        public override string PageTitle { get { return "Джерела даних"; } }
        public override string PageSubtitle { get { return "Звідки береться інформація про тривогу і як визначається ваше місто"; } }

        public SourcesPage(AppController c) : base(c)
        {
            CardBuilder city = NewCard("Ваше місто",
                "Ключові слова допомагають відрізнити «ваше» місто від інших у даних джерел. Для Харкова вже налаштовано — змінюйте лише якщо потрібне інше місце.");
            city.Txt("Назва міста у повідомленнях", s => s.CityDisplayName, (s, v) => s.CityDisplayName = v, null, false, 1);
            city.Txt("Місто (слова через «|»)", s => s.CityKeywords, (s, v) => s.CityKeywords = v, "Тривога на ці назви = червоний рівень.", false, 1);
            city.Txt("Район (слова через «|»)", s => s.RaionKeywords, (s, v) => s.RaionKeywords = v, null, false, 1);
            city.Txt("Область (слова через «|»)", s => s.OblastKeywords, (s, v) => s.OblastKeywords = v, null, false, 1);
            city.Check("Тривога в районі = жовтий рівень", s => s.RaionAlertIsYellow, (s, v) => s.RaionAlertIsYellow = v,
                "Рання ознака: тривога поруч, але для самого міста її ще не оголошено.");
            city.Check("Тривога по всій області = червоний рівень", s => s.CountOblastAlert, (s, v) => s.CountOblastAlert = v,
                "Береться лише тривога, оголошена на всю область (а не на окремі райони).");
            city.Check("Інші загрози (обстріли, вуличні бої, хімічна) = жовтий рівень", s => s.OtherThreatTypesAreYellow, (s, v) => s.OtherThreatTypesAreYellow = v, null);
            city.Finish();

            CardBuilder aiu = NewCard("alerts.in.ua — рекомендовано",
                "Структуровані дані з рівнями «жовтий» і «червоний» та переліком загроз (БпЛА, КАБ, ракети). Потрібен безкоштовний ключ: заявка на alerts.in.ua/api-request. " +
                "Ключ особистий — нікому його не передавайте.");
            aiu.Check("Використовувати alerts.in.ua", s => s.UseAlertsInUa, (s, v) => s.UseAlertsInUa = v, null);
            TextBox token = aiu.Txt("Ключ (токен) API", s => s.AlertsInUaToken, (s, v) => s.AlertsInUaToken = v, null, true, 1);
            Button get = UiKit.Btn("Отримати ключ…", false);
            Button show = UiKit.Btn("Показати/сховати ключ", false);
            Button test = UiKit.Btn("Перевірити ключ", true);
            Label res = null;
            get.Click += delegate { UiKit.Open("https://alerts.in.ua/api-request"); };
            show.Click += delegate { token.UseSystemPasswordChar = !token.UseSystemPasswordChar; };
            test.Click += delegate
            {
                CardBuilder.ShowResult(res, "Перевірка…");
                C.TestAlertsInUa(token.Text.Trim(), r => CardBuilder.ShowResult(res, r));
            };
            aiu.Buttons(get, show, test);
            res = aiu.Result();
            aiu.Finish();

            CardBuilder tg = NewCard("Telegram-канали — без ключа",
                "Резервне джерело, яке працює без реєстрації: читає публічні канали з оголошеннями про тривогу в місті. Рівні в ньому не розрізняються — тривога вважається червоним рівнем. " +
                "Якщо alerts.in.ua недоступний, програма автоматично переходить сюди.");
            tg.Check("Використовувати Telegram-канали", s => s.UseTelegram, (s, v) => s.UseTelegram = v, null);
            TextBox chans = tg.Txt("Канали (по одному в рядку)", s => string.Join("\r\n", TextUtil.SplitChannels(s.TelegramChannels).ToArray()),
                (s, v) => s.TelegramChannels = v, "Можна @назва або посилання t.me/назва. Канал має бути публічним і з увімкненим веб-переглядом.", false, 3);
            Button tgTest = UiKit.Btn("Перевірити канали", true);
            Label tgRes = null;
            tgTest.Click += delegate
            {
                CardBuilder.ShowResult(tgRes, "Перевірка… (може тривати до хвилини)");
                C.TestTelegram(chans.Text, r => CardBuilder.ShowResult(tgRes, r));
            };
            tg.Buttons(tgTest);
            tgRes = tg.Result();
            tg.Finish();

            CardBuilder rel = NewCard("Частота перевірки та надійність", null);
            rel.Num("Перевіряти кожні", 10, 300, s => s.PollIntervalSeconds, (s, v) => s.PollIntervalSeconds = v, "секунд",
                "Безкоштовний ліміт alerts.in.ua — до 12 запитів на хвилину; не ставте частіше 10 с.");
            rel.Num("Підтверджувати відбій", 1, 6, s => s.ClearConfirmPolls, (s, v) => s.ClearConfirmPolls = v, "перевірок поспіль",
                "Захист від хибного відбою: рівень знижується лише після кількох однакових відповідей. Підвищення рівня — миттєве.");
            rel.Num("Вважати зв'язок втраченим після", 2, 30, s => s.FailuresBeforeLost, (s, v) => s.FailuresBeforeLost = v, "невдалих спроб", null);
            rel.Num("Дані застарілі, якщо немає відповіді", 30, 1800, s => s.StaleAfterSeconds, (s, v) => s.StaleAfterSeconds = v, "секунд",
                "Після цього на екрані з'явиться «Стан невідомий» замість хибного зеленого.");
            rel.Num("Telegram: глибина історії при запуску", 1, 24, s => s.TelegramBackfillHours, (s, v) => s.TelegramBackfillHours = v, "годин", null);
            rel.Num("Telegram: тривога без відбою діє не довше", 1, 48, s => s.TelegramEventMaxAgeHours, (s, v) => s.TelegramEventMaxAgeHours = v, "годин", "Захист від «застряглої» тривоги, якщо повідомлення про відбій пропущено.");
            rel.Num("Telegram: канал вважати мертвим після", 1, 720, s => s.ChannelStaleHours, (s, v) => s.ChannelStaleHours = v, "годин мовчання", null);
            rel.Finish();
        }
    }

    public sealed class AdminPage : SettingsPage
    {
        public override string PageTitle { get { return "Адміністратор"; } }
        public override string PageSubtitle { get { return "Текстові сповіщення в Telegram: тривоги, проблеми зі зв'язком, щоденний звіт"; } }

        public AdminPage(AppController c) : base(c)
        {
            CardBuilder b = NewCard("Telegram-бот адміністратора",
                "Повідомлення приходять у Telegram навіть коли ви не біля комп'ютера. Як підключити: 1) у Telegram відкрийте @BotFather, команда /newbot, скопіюйте токен; " +
                "2) напишіть своєму боту будь-яке повідомлення; 3) дізнайтесь свій ID чату через @userinfobot і вставте нижче.");
            b.Check("Надсилати повідомлення адміністратору", s => s.AdminEnabled, (s, v) => s.AdminEnabled = v,
                "Які саме події надсилати — у вкладці «Сповіщення», колонка «Telegram адміну».");
            TextBox tok = b.Txt("Токен бота", s => s.AdminBotToken, (s, v) => s.AdminBotToken = v, null, true, 1);
            TextBox chat = b.Txt("ID чату", s => s.AdminChatId, (s, v) => s.AdminChatId = v, "Число (для груп — зі знаком мінус).", false, 1);
            b.Txt("Назва цього комп'ютера", s => s.AdminPcName, (s, v) => s.AdminPcName = v, "Показується в повідомленнях, щоб розрізняти кілька ПК.", false, 1);
            Button show = UiKit.Btn("Показати/сховати токен", false);
            Button test = UiKit.Btn("Надіслати тестове повідомлення", true);
            Label res = null;
            show.Click += delegate { tok.UseSystemPasswordChar = !tok.UseSystemPasswordChar; };
            test.Click += delegate
            {
                CardBuilder.ShowResult(res, "Надсилання…");
                C.TestAdmin(tok.Text, chat.Text, r => CardBuilder.ShowResult(res, r));
            };
            b.Buttons(show, test);
            res = b.Result();
            b.Finish();

            CardBuilder r2 = NewCard("Звіти", null);
            r2.Check("Щодня надсилати звіт «усе працює»", s => s.AdminDailyReport, (s, v) => s.AdminDailyReport = v,
                "Якщо звіт раптом не прийшов — комп'ютер вимкнено, програма не запущена або немає інтернету.");
            r2.Time("Час звіту", s => s.AdminDailyReportTime, (s, v) => s.AdminDailyReportTime = v, null);
            r2.Check("Надсилати адміністратору й тестові сповіщення", s => s.TestsGoToAdmin, (s, v) => s.TestsGoToAdmin = v, "Зазвичай тести адміністратору не потрібні.");
            r2.Finish();
        }
    }

    public sealed class SystemPage : SettingsPage
    {
        CheckBox _auto;
        TextBox _diag;

        public override string PageTitle { get { return "Система"; } }
        public override string PageSubtitle { get { return "Автозапуск, перенесення на інший комп'ютер, резервні копії, діагностика"; } }

        public SystemPage(AppController c) : base(c)
        {
            CardBuilder st = NewCard("Запуск", null);
            _auto = st.Check("Запускати ВАРТУ разом із Windows (рекомендовано)", s => Autostart.Query(Paths.ExePath).Enabled, (s, v) => { },
                "Працює без прав адміністратора. Щоб ПК вмикався одразу в потрібний обліковий запис, налаштуйте автоматичний вхід у Windows.");
            st.Check("Запускатись згорнутою в трей", s => s.StartMinimized, (s, v) => s.StartMinimized = v, null);
            st.Check("Хрестик закриває вікно в трей (програма продовжує працювати)", s => s.CloseToTray, (s, v) => s.CloseToTray = v, "Повністю вийти можна через значок у трей → «Вийти».");
            st.Num("Зберігати журнал", 1, 365, s => s.LogRetentionDays, (s, v) => s.LogRetentionDays = v, "днів", null);
            st.Finish();

            CardBuilder fold = NewCard("Папка програми й перенесення",
                "ВАРТА переносна: усі налаштування, звуки та журнал лежать у її папці. Щоб перенести на інший комп'ютер — скопіюйте всю папку й запустіть Varta.exe. Автозапуск оновиться сам.");
            Label path = UiKit.Lbl("Папка: " + Paths.DataDir + (Paths.IsPortableFolder ? "" : "   (папка програми захищена від запису)"), 9.5f, false, Theme.Text);
            path.AutoSize = false;
            fold.Custom(path, 22);
            Button o1 = UiKit.Btn("Відкрити папку програми", false);
            Button o2 = UiKit.Btn("Папка зі звуками", false);
            Button o3 = UiKit.Btn("Папка журналів", false);
            o1.Click += delegate { UiKit.Open(Paths.DataDir); };
            o2.Click += delegate { try { Directory.CreateDirectory(Paths.SoundsDir); } catch { } UiKit.Open(Paths.SoundsDir); };
            o3.Click += delegate { try { Directory.CreateDirectory(Paths.LogDir); } catch { } UiKit.Open(Paths.LogDir); };
            fold.Buttons(o1, o2, o3);
            fold.Finish();

            CardBuilder bk = NewCard("Резервна копія налаштувань",
                "Файл налаштувань містить токени й ключі — зберігайте копію в надійному місці.");
            Button ex = UiKit.Btn("Зберегти копію…", false);
            Button im = UiKit.Btn("Завантажити з файлу…", false);
            Button rs = UiKit.Btn("Скинути до стандартних…", false);
            ex.Click += delegate { Export(); };
            im.Click += delegate { Import(); };
            rs.Click += delegate { Reset(); };
            bk.Buttons(ex, im, rs);
            bk.Finish();

            CardBuilder dg = NewCard("Діагностика",
                "Перевіряє звук, файли оголошень, автозапуск і доступність джерел. Якщо щось не працює — натисніть і надішліть результат тому, хто вас підтримує.");
            Button run = UiKit.Btn("Запустити діагностику", true);
            Button copy = UiKit.Btn("Скопіювати результат", false);
            _diag = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9f)
            };
            run.Click += delegate
            {
                _diag.Text = "Виконується… (може тривати до хвилини)";
                C.RunDiagnostics(r => { _diag.Text = r.Replace("\n", "\r\n"); });
            };
            copy.Click += delegate { try { if (_diag.Text.Length > 0) Clipboard.SetText(_diag.Text); } catch { } };
            dg.Buttons(run, copy);
            dg.Custom(_diag, 260);
            dg.Finish();
        }

        public override string ApplyExtras()
        {
            bool now = Autostart.Query(Paths.ExePath).Enabled;
            if (_auto.Checked == now) return null;
            string err = C.SetAutostart(_auto.Checked);
            return err == null ? null : "Автозапуск: " + err;
        }

        void Export()
        {
            using (var d = new SaveFileDialog { Title = "Зберегти копію налаштувань", Filter = "Налаштування ВАРТИ (*.json)|*.json", FileName = "varta-settings-" + DateTime.Now.ToString("yyyyMMdd") + ".json" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(d.FileName, SettingsJson.ToJson(C.Settings), new System.Text.UTF8Encoding(true));
                    MessageBox.Show("Копію збережено.", "ВАРТА", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show("Не вдалося зберегти: " + ex.Message, "ВАРТА", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        void Import()
        {
            using (var d = new OpenFileDialog { Title = "Завантажити налаштування", Filter = "Налаштування ВАРТИ (*.json)|*.json|Усі файли|*.*" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var warn = new List<string>();
                    AppSettings s = SettingsJson.FromJson(File.ReadAllText(d.FileName), warn);
                    s.FirstRunDone = true;
                    string err = C.ApplySettings(s);
                    MessageBox.Show(err == null ? "Налаштування завантажено." + (warn.Count > 0 ? "\nДеякі значення пропущено: " + string.Join("; ", warn.ToArray()) : "") : "Не вдалося застосувати: " + err,
                        "ВАРТА", MessageBoxButtons.OK, err == null ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                }
                catch (Exception ex) { MessageBox.Show("Файл не вдалося прочитати: " + ex.Message, "ВАРТА", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        void Reset()
        {
            if (MessageBox.Show("Скинути ВСІ налаштування до стандартних? Ключі, токени й тексти буде втрачено.", "ВАРТА",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var s = new AppSettings { AdminPcName = Environment.MachineName, FirstRunDone = true };
            C.ApplySettings(s);
        }
    }
}
