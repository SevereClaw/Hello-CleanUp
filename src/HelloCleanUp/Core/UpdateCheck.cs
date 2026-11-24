using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace HelloCleanUp.Core
{
    /// <summary>
    /// Asks GitHub for the latest release tag. One anonymous GET to api.github.com, nothing about the PC is sent.
    /// Never downloads or installs anything: the user just gets a link.
    /// </summary>
    public static class UpdateCheck
    {
        public const string ReleasesPage = "https://github.com/SevereClaw/Hello-CleanUp/releases/latest";
        private const string Api = "https://api.github.com/repos/SevereClaw/Hello-CleanUp/releases/latest";

        /// <summary>Returns the newer version (e.g. "1.2.0"), or null if this one is current or the check failed.</summary>
        public static string NewerVersion(Version current)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create(Api);
                req.UserAgent = "Hello-CleanUp/" + current;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;

                string json;
                using (var resp = req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream()))
                    json = reader.ReadToEnd();

                var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?(\\d+(?:\\.\\d+){1,3})\"");
                if (!m.Success) return null;
                var latest = new Version(m.Groups[1].Value);
                var mine = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
                var theirs = new Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0));
                return theirs > mine ? theirs.ToString() : null;
            }
            catch
            {
                return null; // offline, rate-limited, blocked — not worth bothering the user
            }
        }
    }
}
