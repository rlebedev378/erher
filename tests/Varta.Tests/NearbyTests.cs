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
            var s = new AppSettings();
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
            var s = new AppSettings { NearbyRepeatMinutes = 0 };
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
            var s = new AppSettings();
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
        public void Nearby_event_is_a_voice_alert_with_own_texts_and_a_transient_banner()
        {
            Assert.True(RoutePolicy.VoiceCapable(AlertEventKind.ThreatNearby));
            Assert.Contains(AlertEventKind.ThreatNearby, RoutePolicy.Order);
            Assert.NotEqual(Channel.None, RoutePolicy.DefaultFor(AlertEventKind.ThreatNearby) & Channel.Voice);

            var s = new AppSettings();
            Notice n = Messages.Compose(new EngineEvent { Kind = AlertEventKind.ThreatNearby, Detail = "«Далее Центр ⚠️» (@tlknewsua, 09:00)", Source = "@tlknewsua" },
                s, DateTime.Now, "PC");
            Assert.Equal("ЦІЛЬ ЛЕТИТЬ У НАШ БІК", n.Title);
            Assert.Equal("«Далее Центр ⚠️» (@tlknewsua, 09:00). Негайно пройдіть в укриття!", n.Body);
            Assert.Equal(AlertLevel.Red, n.Level);
            Assert.True(n.Transient);
            Assert.Equal("sounds\\threat_nearby.mp3", s.SoundNearby);
            Assert.True(s.NearbyEnabled);
            Assert.Equal("tlknewsua", s.NearbyChannels);
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
