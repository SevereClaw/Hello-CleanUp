using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace HelloCleanUp.Core
{
    /// <summary>
    /// Asks GitHub for the latest release. One anonymous GET to api.github.com, nothing about the PC is sent.
    /// Downloading and installing is done by <see cref="Updater"/>, and only when the user agrees.
    /// </summary>
    public static class UpdateCheck
    {
        public const string ReleasesPage = "https://github.com/SevereClaw/Hello-CleanUp/releases/latest";
        private const string Api = "https://api.github.com/repos/SevereClaw/Hello-CleanUp/releases/latest";

        /// <summary>Release assets are only ever taken from this repository's own release downloads.</summary>
        public const string DownloadPrefix = "https://github.com/SevereClaw/Hello-CleanUp/releases/download/";

        public sealed class Release
        {
            public Version Version { get; set; }
            public string ExeUrl { get; set; }
            public string ShaUrl { get; set; }
        }

        /// <summary>The newer release, or null if this one is current or the check failed (see error).</summary>
        public static Release Newer(Version current, out string error)
        {
            error = null;
            try
            {
                var json = DownloadString(Api);

                var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?(\\d+(?:\\.\\d+){1,3})\"");
                if (!m.Success) { error = "не нашёл номер версии в ответе GitHub"; return null; }
                var latest = Normalize(new Version(m.Groups[1].Value));
                if (latest <= Normalize(current)) return null;

                var exe = Asset(json, "HelloCleanUp.exe");
                var sha = Asset(json, "HelloCleanUp.exe.sha256");
                if (exe == null || sha == null)
                {
                    error = "в релизе " + latest + " нет HelloCleanUp.exe или HelloCleanUp.exe.sha256";
                    return null;
                }
                return new Release { Version = latest, ExeUrl = exe, ShaUrl = sha };
            }
            catch (Exception ex)
            {
                error = ex.Message; // offline, rate-limited, blocked
                return null;
            }
        }

        public static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }

        private static string Asset(string json, string fileName)
        {
            foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
            {
                var url = m.Groups[1].Value;
                if (url.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase)
                    && url.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase))
                    return url;
            }
            return null;
        }

        private static HttpWebRequest Request(string url, int timeoutMs)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Hello-CleanUp";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.AllowAutoRedirect = true; // release downloads redirect to GitHub's file storage
            return req;
        }

        public static string DownloadString(string url)
        {
            var req = Request(url, 10000);
            req.Accept = "application/vnd.github+json";
            using (var resp = req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream()))
                return reader.ReadToEnd();
        }

        public static void DownloadFile(string url, string path, long maxBytes)
        {
            var req = Request(url, 60000);
            using (var resp = req.GetResponse())
            using (var src = resp.GetResponseStream())
            using (var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long total = 0;
                int n;
                while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += n;
                    if (total > maxBytes) throw new InvalidDataException("файл обновления подозрительно большой, загрузка остановлена");
                    dst.Write(buffer, 0, n);
                }
            }
        }
    }
}
