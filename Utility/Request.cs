﻿using CloudFlareUtilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Utility
{
    public class Request
    {
        public HttpClient Client;
        public CookieContainer Cookie;
        public ClearanceHandler Handler;

        public static Request Context { get; set; }

        static Request()
        {
            Context = new Request();
        }

        public Request()
        {
            Cookie = new CookieContainer();
            Cookie.Add(new System.Net.Cookie("osu_site_v", "old", "/", "osu.ppy.sh"));

            Handler = new ClearanceHandler
            {
                InnerHandler = new HttpClientHandler
                {
                    CookieContainer = Cookie,
                },
            };

            Client = new HttpClient(Handler);
        }

        public HttpWebRequest Create(string url, bool post = false)
        {
            var wr = WebRequest.CreateHttp(url);
            wr.ServicePoint.Expect100Continue = false;
            wr.CookieContainer = Cookie;
            wr.Timeout = Settings.ResponseTimeout;
            wr.CachePolicy = new HttpRequestCachePolicy(HttpRequestCacheLevel.BypassCache);
            if (post)
            {
                wr.Method = "POST";
                wr.ContentType = "application/x-www-form-urlencoded";
            }
            return wr;
        }

        public void AddCookie(string name, string content)
        {
            Cookie.Add(new Cookie(name, content, "/", "osu.ppy.sh"));
        }

        public string GetCookie(string name)
        {
            return Cookie.GetCookies(new Uri("http://osu.ppy.sh"))[name]?.Value;
        }

        private bool LoginValidate(HttpWebRequest wr)
        {
            using (var rp = (HttpWebResponse)wr.GetResponse())
            {
                return rp.Cookies["last_login"] != null;
            }
        }

        public string Login(string id, string pw)
        {
            const string url = "http://osu.ppy.sh/forum/ucp.php?mode=login";

            var wr = Create(url, true);
            using (var sw = new StreamWriter(wr.GetRequestStream()))
            {
                sw.Write($"login=Login&username={Uri.EscapeDataString(id)}" +
                    $"&password={Uri.EscapeDataString(pw)}&autologin=on");
            }

            return LoginValidate(wr) ? GetCookie(Settings.SessionKey) : null;
        }

        public string Login(string sid)
        {
            const string url = "http://osu.ppy.sh/forum/ucp.php?mode=login";

            AddCookie(Settings.SessionKey, sid);

            var wr = Create(url, true);
            return LoginValidate(wr) ? GetCookie(Settings.SessionKey) : null;
        }

        private bool ValidateOsz(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var zs = new osu.Game.IO.Archives.ZipArchiveReader(fs))
            {
                return true;
            }
        }

        /// <summary>
        /// osu!에서 비트맵셋을 내려받고 올바른 파일인지 확인합니다.
        /// </summary>
        /// <param name="id">비트맵셋 ID</param>
        /// <param name="onprogress"><code>(received, total) => { ... }</code></param>
        /// <param name="skipDownload">파일을 내려받고 검증할 때 <code>true</code></param>
        /// <exception cref="WebException"></exception>
        /// <exception cref="IOException"></exception>
        /// <exception cref="SharpZipBaseException">올바른 비트맵 파일이 아님.</exception>
        public string Download(int id, Action<int, long> onprogress, bool skipDownload = false)
        {
            var url = "http://osu.ppy.sh/d/" + id;
            var path = Path.Combine(Settings.Storage, id + ".osz.download");

            if (skipDownload)
            {
                if (File.Exists(path) && ValidateOsz(path))
                {
                    return path;
                }

                path = path.Remove(path.LastIndexOf(".download"));
                if (ValidateOsz(path))
                {
                    return path;
                }

                throw new Exception("올바른 비트맵 파일이 아님");
            }

            var wr = Create(url);
            using (var rp = (HttpWebResponse) wr.GetResponse())
            using (var rs = rp.GetResponseStream())
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                int got;
                var received = 0;
                var buffer = new byte[4096];
                onprogress?.Invoke(received, rp.ContentLength);
                while ((got = rs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    fs.Write(buffer, 0, got);
                    received += got;
                    onprogress?.Invoke(received, rp.ContentLength);
                }
            }
            return Download(id, onprogress, true);
        }

        public JArray GetBeatmapsAPI(string query)
        {
            const string url = "http://osu.ppy.sh/api/get_beatmaps?k={0}&{1}";

            try
            {
                var wr = Create(string.Format(url, Settings.APIKey, query));
                using (var rp = new StreamReader(wr.GetResponse().GetResponseStream()))
                {
                    return JArray.Parse(rp.ReadToEnd());
                }
            }
            catch (Exception e) when (e is WebException || e is JsonReaderException || e is IOException)
            {
                return GetBeatmapsAPI(query);
            }
        }
    }
}
