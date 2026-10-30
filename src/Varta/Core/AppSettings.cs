using System;
using System.Collections.Generic;

namespace Varta.Core
{
    /// <summary>Усі налаштування програми. Зберігаються в settings.json поруч із Varta.exe.</summary>
    public sealed class AppSettings
    {
        public int SchemaVersion = 1;
        public bool FirstRunDone = false;

        // ---- Загальне ----
        public string OrganizationName = "";
        public string CityDisplayName = "Харків";

        // ---- Розташування (ключові слова через «|», регістр не важливий) ----
        public string CityKeywords = "м. Харків|Харків|Харкові|Харківська територіальна громада";
        public string RaionKeywords = "Харківський район";
        public string OblastKeywords = "Харківська область|Харківській області";
        public bool CountOblastAlert = true;
        public bool RaionAlertIsYellow = true;
        public bool OtherThreatTypesAreYellow = true;

        // ---- Джерела даних ----
        public bool UseAlertsInUa = true;
        public string AlertsInUaToken = "";
        public bool UseTelegram = true;
        public string TelegramChannels = "kharkivlife";
        public int TelegramBackfillHours = 6;
        public int TelegramEventMaxAgeHours = 12;
        public int ChannelStaleHours = 48;
        public int PollIntervalSeconds = 15;
        public int ClearConfirmPolls = 2;
        public int FailuresBeforeLost = 4;
        public int StaleAfterSeconds = 120;

        // ---- Звук ----
        public bool VoiceEnabled = true;
        public string SoundRed = "sounds\\red_alert.mp3";
        public string SoundYellow = "sounds\\yellow_alert.mp3";
        public string SoundYellowFromRed = "sounds\\downgrade_yellow.mp3";
        public string SoundClear = "sounds\\all_clear.mp3";
        public string SoundReminder = "";
        public int AnnounceVolumePercent = 100;
        public int RepeatRed = 2;
        public int RepeatOther = 1;
        public int RepeatGapSeconds = 3;
        public int ReminderMinutes = 0;
        public bool DuckMusic = true;
        public int MusicDuckPercent = 10;
        public bool BoostMasterVolume = true;
        public int MinMasterVolumePercent = 70;
        public bool UnmuteMaster = true;

        // ---- Розклад голосових оголошень ----
        public bool VoiceOnlyInWorkHours = false;
        public string WorkStart = "08:30";
        public string WorkEnd = "20:00";
        public string WorkDays = "Mon,Tue,Wed,Thu,Fri,Sat";

        // ---- Банер на екрані ----
        public bool BannerAllScreens = true;
        public bool BannerStayUntilClear = true;
        public int BannerSeconds = 90;
        public int BannerHeight = 160;

        // ---- Тексти (можна змінювати; {city} {time} {detail} {org}) ----
        public string TextRedTitle = "ПОВІТРЯНА ТРИВОГА";
        public string TextRedBody = "Червоний рівень. Негайно пройдіть в укриття!";
        public string TextRedFromYellowTitle = "РІВЕНЬ ПІДВИЩЕНО ДО ЧЕРВОНОГО";
        public string TextRedFromYellowBody = "Повітряна тривога. Негайно пройдіть в укриття!";
        public string TextYellowTitle = "ЖОВТИЙ РІВЕНЬ ЗАГРОЗИ";
        public string TextYellowBody = "Підвищена небезпека. Будьте уважні та готові пройти в укриття.";
        public string TextYellowFromRedTitle = "РІВЕНЬ ЗНИЖЕНО ДО ЖОВТОГО";
        public string TextYellowFromRedBody = "Загроза зберігається. Залишайтеся уважними, будьте готові пройти в укриття.";
        public string TextClearTitle = "ВІДБІЙ ТРИВОГИ";
        public string TextClearBody = "Небезпеку знято. Дякуємо за організованість.";

        // ---- Адміністратор (Telegram-бот) ----
        public bool AdminEnabled = false;
        public string AdminBotToken = "";
        public string AdminChatId = "";
        public string AdminPcName = "";
        public bool AdminDailyReport = false;
        public string AdminDailyReportTime = "09:00";
        public bool TestsGoToAdmin = false;

        // ---- Система ----
        public bool StartMinimized = true;
        public bool CloseToTray = true;
        public int LogRetentionDays = 30;

        // ---- Маршрути сповіщень: подія → "voice,banner,tray,admin" ----
        public Dictionary<string, string> Routes = RoutePolicy.Defaults();

        public static int Clamp(int v, int min, int max) { return v < min ? min : (v > max ? max : v); }

        public void Normalize()
        {
            PollIntervalSeconds = Clamp(PollIntervalSeconds, 10, 300);
            ClearConfirmPolls = Clamp(ClearConfirmPolls, 1, 6);
            FailuresBeforeLost = Clamp(FailuresBeforeLost, 2, 30);
            StaleAfterSeconds = Clamp(StaleAfterSeconds, 30, 1800);
            TelegramBackfillHours = Clamp(TelegramBackfillHours, 1, 24);
            TelegramEventMaxAgeHours = Clamp(TelegramEventMaxAgeHours, 1, 48);
            ChannelStaleHours = Clamp(ChannelStaleHours, 1, 720);
            AnnounceVolumePercent = Clamp(AnnounceVolumePercent, 10, 100);
            RepeatRed = Clamp(RepeatRed, 1, 5);
            RepeatOther = Clamp(RepeatOther, 1, 3);
            RepeatGapSeconds = Clamp(RepeatGapSeconds, 0, 30);
            ReminderMinutes = Clamp(ReminderMinutes, 0, 120);
            MusicDuckPercent = Clamp(MusicDuckPercent, 0, 80);
            MinMasterVolumePercent = Clamp(MinMasterVolumePercent, 10, 100);
            BannerSeconds = Clamp(BannerSeconds, 5, 600);
            BannerHeight = Clamp(BannerHeight, 100, 400);
            LogRetentionDays = Clamp(LogRetentionDays, 1, 365);

            AlertsInUaToken = (AlertsInUaToken ?? "").Trim();
            AdminBotToken = (AdminBotToken ?? "").Trim();
            AdminChatId = (AdminChatId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(CityDisplayName)) CityDisplayName = "Харків";
            if (!Schedule.IsTime(WorkStart)) WorkStart = "08:30";
            if (!Schedule.IsTime(WorkEnd)) WorkEnd = "20:00";
            if (!Schedule.IsTime(AdminDailyReportTime)) AdminDailyReportTime = "09:00";

            if (Routes == null) Routes = new Dictionary<string, string>();
            foreach (AlertEventKind k in RoutePolicy.Order)
            {
                string key = k.ToString();
                if (!Routes.ContainsKey(key)) { Routes[key] = RoutePolicy.ToText(RoutePolicy.DefaultFor(k)); continue; }
                Channel c = RoutePolicy.FromText(Routes[key]);
                if (!RoutePolicy.VoiceCapable(k)) c &= ~Channel.Voice;
                Routes[key] = RoutePolicy.ToText(c);
            }
        }
    }
}
