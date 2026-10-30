using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using Varta.Core;

namespace Varta.Platform
{
    public sealed class HttpFetcher : IHttp
    {
        static HttpFetcher()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.DefaultConnectionLimit = 8;
            }
            catch { }
        }

        public HttpResult Get(string url, IDictionary<string, string> headers, int timeoutMs)
        {
            return Send("GET", url, headers, null, timeoutMs);
        }

        public HttpResult PostForm(string url, IDictionary<string, string> form, int timeoutMs)
        {
            var sb = new StringBuilder();
            foreach (var kv in form)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value ?? ""));
            }
            return Send("POST", url, null, Encoding.UTF8.GetBytes(sb.ToString()), timeoutMs);
        }

        static HttpResult Send(string method, string url, IDictionary<string, string> headers, byte[] body, int timeoutMs)
        {
            var res = new HttpResult();
            var sw = Stopwatch.StartNew();
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = method;
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "Varta/" + AppInfo.Version + " (Windows; office alert monitor)";
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                req.KeepAlive = false;
                if (headers != null)
                {
                    foreach (var kv in headers)
                    {
                        if (string.Equals(kv.Key, "Accept", StringComparison.OrdinalIgnoreCase)) req.Accept = kv.Value;
                        else req.Headers[kv.Key] = kv.Value;
                    }
                }
                if (body != null)
                {
                    req.ContentType = "application/x-www-form-urlencoded";
                    req.ContentLength = body.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(body, 0, body.Length);
                }
                using (var resp = (HttpWebResponse)req.GetResponse()) Fill(res, resp);
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp != null)
                {
                    try { using (resp) Fill(res, resp); } catch (Exception e2) { res.Error = e2.Message; }
                }
                else
                {
                    res.Error = ex.Message;
                    res.TimedOut = ex.Status == WebExceptionStatus.Timeout;
                }
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
            }
            sw.Stop();
            res.ElapsedMs = (int)sw.ElapsedMilliseconds;
            return res;
        }

        static void Fill(HttpResult res, HttpWebResponse resp)
        {
            res.Status = (int)resp.StatusCode;
            string date = resp.Headers["Date"];
            DateTime d;
            if (!string.IsNullOrEmpty(date) &&
                DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d))
                res.ServerDateUtc = d;
            using (Stream st = resp.GetResponseStream())
            using (var rd = new StreamReader(st, Encoding.UTF8))
            {
                char[] buf = new char[8192];
                var sb = new StringBuilder();
                int n;
                while ((n = rd.Read(buf, 0, buf.Length)) > 0)
                {
                    sb.Append(buf, 0, n);
                    if (sb.Length > 4000000) break;
                }
                res.Body = sb.ToString();
            }
        }
    }
}
