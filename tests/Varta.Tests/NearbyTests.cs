using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Varta.Core;
using Xunit;

namespace Varta.Tests
{
    /// <summary>«Ціль летить у наш бік»: розбір повідомлень каналу @tlknewsua про рух цілей.</summary>
    public sealed class NearbyTests
    {
        static readonly string Kw = AppSettings.DefaultNearbyKeywords;

        /// <summary>Справжня сторінка t.me/s/tlknewsua (повідомлення 133298–133317, 30.10.2026).</summary>
        static string Page() { return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tlknewsua_sample.html")); }

        /// <summary>Сповіщення йде адміністратору, тож без підключеного бота канали не читаються.</summary>
        static AppSettings WithAdmin() { return new AppSettings { AdminEnabled = true, AdminBotToken = "1:test", AdminChatId = "42" }; }

        static DateTime Utc(string s) { return DateTime.Parse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal); }

        /// <summary>Та сама сторінка, але ніби новіших за maxId повідомлень ще немає.</summary>
        static string PageUpTo(long maxId)
        {
            string[] parts = Page().Split(new[] { "tgme_widget_message_wrap" }, StringSplitOptions.None);
            var keep = new List<string> { parts[0] };
            for (int i = 1; i < parts.Length; i++)
            {
                Match m = Regex.Match(parts[i], "data-post=\"[^\"/]+/(\\d+)\"");
                if (m.Success && long.Parse(m.Groups[1].Value) <= maxId) keep.Add(parts[i]);
            }
            return string.Join("tgme_widget_message_wrap", keep.ToArray());
        }

        [Theory]
        [InlineData("Дале Центральный рынок/Центр ⚠️ 📷 TlkInst 🎞 Канал со стримами")]
        [InlineData("Далее на Центр ⚠️")]
        [InlineData("Далее ЮЖД ⚠️")]
        [InlineData("Далее Москалевка/Левада ⚠️")]
        [InlineData("Ещё один на ХГ ⚠️")]
        [InlineData("Шахед курсом на Холодную гору")]
        [InlineData("Летить у бік центру")]
        [InlineData("Над Центральним ринком")]
        [InlineData("Мопед над Благбазом")]
        [InlineData("Ціль на Москалівку")]
        [InlineData("Далее Москалёвка")]
        public void Messages_about_our_area_match(string text)
        {
            Assert.NotNull(NearbyMatcher.Match(text, Kw));
        }

        [Theory]
        [InlineData("Далее Сортировка/ЛГ ⚠️")]
        [InlineData("Далее Основа/Одесская/Жихарь ⚠️")]
        [InlineData("Еще один на СС ⚠️")]
        [InlineData("Шахед над Центральной Салтовкой")]
        [InlineData("Ещё цель на город/пригород⚠️⚠️⚠️")]
        [InlineData("Отбой для города ❤️")]
        public void Other_places_do_not_match(string text)
        {
            Assert.Null(NearbyMatcher.Match(text, Kw));
        }

        [Fact]
        public void Long_news_digest_is_ignored_even_if_it_mentions_the_centre()
        {
            string digest = "Вечір, новини! " + string.Join(" ", Enumerable.Repeat("Сьогодні в центрі міста було гучно.", 10));
            Assert.True(digest.Length > NearbyMatcher.MaxLength);
            Assert.Null(NearbyMatcher.Match(digest, Kw));
        }

        [Fact]
        public void Wildcard_keywords_match_endings_only_from_the_start_of_a_word()
        {
            Assert.True(TextUtil.ContainsPhrase(TextUtil.Norm("Летить на Холодну гору"), "холодн* гор*"));
            Assert.False(TextUtil.ContainsPhrase(TextUtil.Norm("Нехолодна гора"), "холодн* гор*"));
            Assert.False(TextUtil.ContainsPhrase(TextUtil.Norm("будь-що"), "*"));
            Assert.True(TextUtil.ContainsPhrase(TextUtil.Norm("ЮЖД"), "южд"));
        }

        [Fact]
        public void Headline_drops_the_channel_footer()
        {
            Assert.Equal("Дале Центральный рынок/Центр ⚠️", NearbyMatcher.Headline("Дале Центральный рынок/Центр ⚠️ 📷 TlkInst 🎞 Канал со стримами"));
            Assert.Equal("Ракета на місто", NearbyMatcher.Headline("Ракета на місто"));
        }

        [Fact]
        public void Real_channel_page_matches_only_messages_about_our_area()
        {
            List<TgMessage> msgs = TelegramPageParser.Parse(Page(), "tlknewsua");
            Assert.Equal(20, msgs.Count);
            // 133316 — відповідь на 133315: береться власний текст, а не цитата.
            Assert.StartsWith("Дале Центральный рынок/Центр", msgs.Single(m => m.Id == 133315).Text);
            Assert.StartsWith("Далее Москалевка/Левада", msgs.Single(m => m.Id == 133316).Text);
            long[] hits = msgs.Where(m => NearbyMatcher.Match(m.Text, Kw) != null).Select(m => m.Id).ToArray();
            Assert.Equal(new long[] { 133315, 133316 }, hits);
        }

        [Fact]
        public void Watcher_is_silent_at_startup_then_announces_a_new_message_about_our_area_once()
        {
            DateTime server = Utc("2026-10-30T07:00:30Z");
            string body = PageUpTo(133314);
            var http = new FakeHttp { OnGet = u => new HttpResult { Status = 200, Body = body, ServerDateUtc = server } };
            AppSettings s = WithAdmin();
            var w = new NearbyWatcher(() => s, http, () => server);

            Assert.Empty(w.Poll());
            Assert.True(w.GetStatus().Single().Ok);
            Assert.Contains("https://t.me/s/tlknewsua", http.Requests);

            body = Page();   // з'явилися 133315 (Центральний ринок/Центр), 133316 (Москалівка/Левада), 133317 (Основа)
            List<NearbyMatch> hits = w.Poll();
            Assert.Equal(new long[] { 133315, 133316 }, hits.Select(h => h.Id).ToArray());
            Assert.True(hits[0].Announce);
            Assert.False(hits[1].Announce);   // та сама ціль через 34 с — без повторного оповіщення
            Assert.Equal("Дале Центральный рынок/Центр ⚠️", hits[0].Headline);
            Assert.Same(hits[1], w.LastMatch);

            Assert.Empty(w.Poll());
        }

        [Fact]
        public void Watcher_announces_every_match_without_pause_and_skips_stale_messages()
        {
            DateTime server = Utc("2026-10-30T07:00:30Z");
            string body = PageUpTo(133314);
            var http = new FakeHttp { OnGet = u => new HttpResult { Status = 200, Body = body, ServerDateUtc = server } };
            AppSettings s = WithAdmin();
            s.NearbyRepeatMinutes = 0;
            var w = new NearbyWatcher(() => s, http, () => server);
            w.Poll();
            body = Page();
            Assert.True(w.Poll().All(h => h.Announce));

            var late = new NearbyWatcher(() => s, http, () => server);
            body = PageUpTo(133314);
            late.Poll();
            body = Page();
            server = server.AddMinutes(30);   // ПК «спав»: повідомлення старші за 10 хв не оголошуються
            Assert.Empty(late.Poll());
        }

        [Fact]
        public void Watcher_reports_unreachable_channel_and_stays_idle_when_disabled()
        {
            var http = new FakeHttp { OnGet = u => new HttpResult { Status = 0, Error = "dns", Body = "" } };
            AppSettings s = WithAdmin();
            var w = new NearbyWatcher(() => s, http, () => DateTime.UtcNow);
            Assert.Empty(w.Poll());
            NearbyChannelStatus st = w.GetStatus().Single();
            Assert.False(st.Ok);
            Assert.Contains("dns", st.Error);

            s.NearbyEnabled = false;
            http.Requests.Clear();
            Assert.Empty(w.Poll());
            Assert.Empty(http.Requests);
            Assert.Empty(w.GetStatus());
        }

        [Fact]
        public void Preview_lists_recent_matches()
        {
            var http = new FakeHttp { OnGet = u => new HttpResult { Status = 200, Body = Page() } };
            string r = NearbyWatcher.Preview(http, "https://t.me/tlknewsua", Kw, DateTime.UtcNow);
            Assert.StartsWith("✓ @tlknewsua: прочитано 20 останніх повідомлень, збігів із вашим районом: 2", r);
            Assert.Contains("Дале Центральный рынок/Центр ⚠️", r);
            Assert.StartsWith("✗", NearbyWatcher.Preview(http, "", Kw, DateTime.UtcNow));
            Assert.StartsWith("✗", NearbyWatcher.Preview(http, "tlknewsua", " | ", DateTime.UtcNow));
        }

        [Fact]
        public void Nearby_alert_is_a_personal_telegram_message_without_voice()
        {
            Assert.False(RoutePolicy.VoiceCapable(AlertEventKind.ThreatNearby));
            Assert.Contains(AlertEventKind.ThreatNearby, RoutePolicy.Order);
            Assert.Equal(Channel.Admin, RoutePolicy.DefaultFor(AlertEventKind.ThreatNearby));
            var s = new AppSettings();
            RoutePolicy.Set(s, AlertEventKind.ThreatNearby, Channel.Voice | Channel.Admin);
            Assert.Equal(Channel.Admin, RoutePolicy.Get(s, AlertEventKind.ThreatNearby));   // голос не вмикається навіть вручну
            Assert.True(RoutePolicy.AdminGetsTest(s, AlertEventKind.ThreatNearby));
            Assert.False(RoutePolicy.AdminGetsTest(s, AlertEventKind.RedStarted));

            var e = new EngineEvent
            {
                Kind = AlertEventKind.ThreatNearby, Detail = "Дале Центральный рынок/Центр ⚠️", Source = "@tlknewsua",
                Link = "https://t.me/tlknewsua/133315"
            };
            Notice n = Messages.Compose(e, s, new DateTime(2026, 10, 30, 8, 59, 14), "PC");
            Assert.Equal("Ціль летить у наш бік", n.Title);
            Assert.Equal("Дале Центральный рынок/Центр ⚠️", n.Body);
            Assert.Equal(AlertLevel.Unknown, n.Level);   // не рівень тривоги: банер (якщо ввімкнути) — фіолетовий і не висить до відбою
            Assert.False(n.IsTechnical);
            Assert.Equal("🎯 Ціль летить у наш бік\nДале Центральный рынок/Центр ⚠️\nhttps://t.me/tlknewsua/133315\n🕒 30.10 08:59:14 • PC • @tlknewsua",
                Messages.AdminText(n, "PC"));
            Assert.True(s.NearbyEnabled);
            Assert.Equal("tlknewsua", s.NearbyChannels);
        }

        [Fact]
        public void Channels_are_read_only_when_the_alert_can_reach_someone()
        {
            var s = new AppSettings();
            Assert.False(NearbyWatcher.CanDeliver(s));   // за замовчуванням бота не підключено
            var http = new FakeHttp { OnGet = u => new HttpResult { Status = 200, Body = Page() } };
            var w = new NearbyWatcher(() => s, http, () => DateTime.UtcNow);
            Assert.Empty(w.Poll());
            Assert.Empty(http.Requests);
            Assert.Empty(w.GetStatus());

            s.AdminEnabled = true; s.AdminBotToken = "1:test"; s.AdminChatId = "42";
            Assert.True(NearbyWatcher.CanDeliver(s));
            Assert.Empty(w.Poll());                      // перше читання — лише запам'ятати, що вже є в каналі
            Assert.Single(http.Requests);

            RoutePolicy.Set(s, AlertEventKind.ThreatNearby, Channel.None);
            Assert.False(NearbyWatcher.CanDeliver(s));
            s.AdminEnabled = false;
            RoutePolicy.Set(s, AlertEventKind.ThreatNearby, Channel.Tray);
            Assert.True(NearbyWatcher.CanDeliver(s));    // лише вікно Windows на цьому ПК
        }

        [Fact]
        public void Settings_from_1_0_4_make_the_nearby_alert_admin_only_once()
        {
            string json = "{\"SchemaVersion\":1,\"TextNearbyTitle\":\"ЦІЛЬ ЛЕТИТЬ У НАШ БІК\",\"TextNearbyBody\":\"{detail}. Негайно пройдіть в укриття!\"," +
                          "\"SoundNearby\":\"sounds\\\\threat_nearby.mp3\",\"Routes\":{\"ThreatNearby\":\"voice,banner,tray,admin\",\"AllClear\":\"tray\"}}";
            var w = new List<string>();
            AppSettings s = SettingsJson.FromJson(json, w);
            Assert.Empty(w);
            Assert.Equal(2, s.SchemaVersion);
            Assert.Equal(Channel.Admin, RoutePolicy.Get(s, AlertEventKind.ThreatNearby));
            Assert.Equal(Channel.Tray, RoutePolicy.Get(s, AlertEventKind.AllClear));
            Assert.Equal(AppSettings.DefaultNearbyTitle, s.TextNearbyTitle);
            Assert.Equal(AppSettings.DefaultNearbyBody, s.TextNearbyBody);
            Assert.DoesNotContain("SoundNearby", SettingsJson.ToJson(s));

            // Те, що обрано вже в 1.0.5, не скидається.
            AppSettings mine = SettingsJson.FromJson("{\"SchemaVersion\":2,\"Routes\":{\"ThreatNearby\":\"tray,admin\"}}", null);
            Assert.Equal(Channel.Tray | Channel.Admin, RoutePolicy.Get(mine, AlertEventKind.ThreatNearby));
            Assert.Equal(Channel.Tray | Channel.Admin, RoutePolicy.Get(SettingsJson.Clone(mine), AlertEventKind.ThreatNearby));
        }

        [Fact]
        public void Old_settings_get_the_nearby_route_and_defaults()
        {
            AppSettings s = SettingsJson.FromJson("{\"NearbyRepeatMinutes\": 99, \"Routes\": {\"AllClear\": \"tray\"}}", null);
            Assert.Equal(30, s.NearbyRepeatMinutes);
            Assert.Equal(RoutePolicy.DefaultFor(AlertEventKind.ThreatNearby), RoutePolicy.Get(s, AlertEventKind.ThreatNearby));
            Assert.Equal(AppSettings.DefaultNearbyKeywords, s.NearbyKeywords);
        }
    }
}
