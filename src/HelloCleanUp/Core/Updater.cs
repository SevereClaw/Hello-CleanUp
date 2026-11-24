using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;

namespace HelloCleanUp.Core
{
    /// <summary>
    /// Replaces the running HelloCleanUp.exe with a newer release.
    /// The download is checked against the release's SHA-256 file and must be a HelloCleanUp assembly
    /// of the expected version; otherwise nothing is replaced. A running exe can't be overwritten but
    /// can be renamed, so the old one becomes HelloCleanUp.exe.old and is deleted on the next start.
    /// </summary>
    public static class Updater
    {
        private const long MaxExeBytes = 50L * 1024 * 1024;

        public static string CurrentExe
        {
            get
            {
                using (var p = Process.GetCurrentProcess()) return p.MainModule.FileName;
            }
        }

        public static void Install(UpdateCheck.Release release, Action<string> log)
        {
            var exe = CurrentExe;
            var newPath = exe + ".new";
            var oldPath = exe + ".old";

            log("Обновление до " + release.Version + ":");
            log("  скачиваю контрольную сумму " + release.ShaUrl);
            var expected = ParseSha(UpdateCheck.DownloadString(release.ShaUrl));
            if (expected == null) throw new InvalidDataException("в файле .sha256 нет контрольной суммы");

            log("  скачиваю " + release.ExeUrl);
            TryDelete(newPath);
            try
            {
                UpdateCheck.DownloadFile(release.ExeUrl, newPath, MaxExeBytes);

                var actual = Sha256(newPath);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("контрольная сумма не совпала — файл повреждён или подменён, обновление отменено");
                log("  SHA-256 совпала: " + actual);

                var name = AssemblyName.GetAssemblyName(newPath);
                if (!string.Equals(name.Name, "HelloCleanUp", StringComparison.OrdinalIgnoreCase)
                    || UpdateCheck.Normalize(name.Version) != release.Version)
                    throw new InvalidDataException("скачанный файл — не Hello, CleanUp. " + release.Version + " (" + name.Name + " " + name.Version + "), обновление отменено");

                TryDelete(oldPath);
                File.Move(exe, oldPath);
                try
                {
                    File.Move(newPath, exe);
                }
                catch
                {
                    File.Move(oldPath, exe); // put the working version back
                    throw;
                }
            }
            catch
            {
                TryDelete(newPath);
                throw;
            }
            log("  установлено: " + exe);
        }

        /// <summary>Starts the (new) exe in a fresh process. Already elevated, so no second UAC prompt.</summary>
        public static void Restart()
        {
            Process.Start(new ProcessStartInfo(CurrentExe) { UseShellExecute = true });
        }

        /// <summary>Removes the exe left over from the previous update; it may still be exiting, so retry a little.</summary>
        public static void CleanupLeftovers()
        {
            string exe;
            try { exe = CurrentExe; } catch { return; }
            var old = exe + ".old";
            var stale = exe + ".new";
            if (!File.Exists(old) && !File.Exists(stale)) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                for (int i = 0; i < 20 && (File.Exists(old) || File.Exists(stale)); i++)
                {
                    TryDelete(old);
                    TryDelete(stale);
                    Thread.Sleep(500);
                }
            });
        }

        private static string ParseSha(string text)
        {
            if (text == null) return null;
            var token = text.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (token.Length == 0 || token[0].Length != 64) return null;
            foreach (var c in token[0])
                if (!Uri.IsHexDigit(c)) return null;
            return token[0].ToLowerInvariant();
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
