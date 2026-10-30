using System;
using System.Collections.Generic;
using System.Threading;
using Varta.Core;

namespace Varta.Platform
{
    /// <summary>Текстові сповіщення адміністратору через Telegram-бота (необов'язково).</summary>
    public sealed class AdminNotifier : IDisposable
    {
        readonly IHttp _http;
        readonly Func<AppSettings> _cfg;
        readonly Queue<string> _q = new Queue<string>();
        readonly object _l = new object();
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        readonly Thread _thread;
        volatile bool _stop;

        public string LastResult = "";
        public bool LastOk;
        public DateTime? LastAttemptUtc;

        public AdminNotifier(IHttp http, Func<AppSettings> cfg)
        {
            _http = http;
            _cfg = cfg;
            _thread = new Thread(Loop) { IsBackground = true, Name = "Varta.Admin" };
            _thread.Start();
        }

        public bool IsConfigured(AppSettings s)
        {
            return s.AdminEnabled && !string.IsNullOrWhiteSpace(s.AdminBotToken) && !string.IsNullOrWhiteSpace(s.AdminChatId);
        }

        public void Send(string text)
        {
            if (!IsConfigured(_cfg())) return;
            lock (_l)
            {
                _q.Enqueue(text);
                while (_q.Count > 20) _q.Dequeue();
            }
            _wake.Set();
        }

        /// <summary>Синхронна відправка (для кнопки «Надіслати тест»). null = успіх.</summary>
        public string SendNow(string token, string chatId, string text)
        {
            if (string.IsNullOrWhiteSpace(token)) return "не вказано токен бота";
            if (string.IsNullOrWhiteSpace(chatId)) return "не вказано ID чату";
            var form = new Dictionary<string, string>
            {
                { "chat_id", chatId.Trim() },
                { "text", text },
                { "disable_web_page_preview", "true" }
            };
            HttpResult r = _http.PostForm("https://api.telegram.org/bot" + token.Trim() + "/sendMessage", form, 12000);
            if (r.Ok) return null;
            string desc = "";
            try { desc = MiniJson.Str(MiniJson.Obj(MiniJson.Parse(r.Body)), "description"); } catch { }
            if (r.Status == 401 || r.Status == 404) return "Telegram відхилив токен бота (" + r.Status + ")";
            if (r.Status == 400 || r.Status == 403) return "Telegram: " + (desc.Length > 0 ? desc : "помилка " + r.Status) + " (перевірте ID чату і чи писали ви боту першим)";
            if (r.Status == 0) return r.TimedOut ? "час очікування вичерпано" : "немає з'єднання: " + r.Error;
            return "помилка HTTP " + r.Status + (desc.Length > 0 ? ": " + desc : "");
        }

        void Loop()
        {
            while (!_stop)
            {
                _wake.WaitOne(1000);
                while (!_stop)
                {
                    string text;
                    lock (_l) { if (_q.Count == 0) break; text = _q.Peek(); }
                    AppSettings s = _cfg();
                    if (!IsConfigured(s)) { lock (_l) _q.Clear(); break; }

                    string err = null;
                    int[] delays = { 0, 3000, 15000 };
                    for (int i = 0; i < delays.Length && !_stop; i++)
                    {
                        if (delays[i] > 0) Thread.Sleep(delays[i]);
                        err = SendNow(s.AdminBotToken, s.AdminChatId, text);
                        if (err == null) break;
                    }
                    LastAttemptUtc = DateTime.UtcNow;
                    LastOk = err == null;
                    LastResult = err ?? "надіслано";
                    if (err != null) Log.Warn("Telegram-адміністратор: не вдалося надіслати — " + err);
                    lock (_l) { if (_q.Count > 0) _q.Dequeue(); }
                }
            }
        }

        public void Dispose()
        {
            _stop = true;
            _wake.Set();
        }
    }
}
