using System;
using System.Collections.Generic;
using System.IO;

namespace HelloCleanUp.Core
{
    /// <summary>
    /// Safe recursive size/delete. Never follows junctions or symlinks (so it cannot escape the target folder),
    /// silently skips files that are locked or access-denied, and keeps the root folder itself.
    /// </summary>
    public static class FileWiper
    {
        private static bool IsReparse(FileSystemInfo info)
        {
            return (info.Attributes & FileAttributes.ReparsePoint) != 0;
        }

        public static long SizeOfDir(string path, string filePattern = "*")
        {
            long total = 0;
            try
            {
                var dir = new DirectoryInfo(path);
                if (!dir.Exists) return 0;
                foreach (var f in SafeFiles(dir, filePattern)) total += SafeLength(f);
                foreach (var d in SafeDirs(dir))
                {
                    if (IsReparse(d)) continue;
                    total += SizeOfDir(d.FullName, filePattern);
                }
            }
            catch { }
            return total;
        }

        public static long SizeOfFile(string path)
        {
            try { var f = new FileInfo(path); return f.Exists ? f.Length : 0; } catch { return 0; }
        }

        /// <summary>Deletes the contents of <paramref name="path"/> (not the folder itself). Returns bytes freed.</summary>
        public static long WipeDir(string path, string filePattern = "*")
        {
            long freed = 0;
            DirectoryInfo dir;
            try
            {
                dir = new DirectoryInfo(path);
                if (!dir.Exists) return 0;
            }
            catch { return 0; }

            foreach (var f in SafeFiles(dir, filePattern)) freed += DeleteFile(f);

            foreach (var d in SafeDirs(dir))
            {
                if (IsReparse(d))
                {
                    // Remove the link itself, never what it points to.
                    try { d.Delete(false); } catch { }
                    continue;
                }
                freed += WipeDir(d.FullName, filePattern);
                try
                {
                    if (filePattern == "*") d.Delete(false); // only succeeds when empty
                }
                catch { }
            }
            return freed;
        }

        public static long DeleteFile(string path)
        {
            try { return DeleteFile(new FileInfo(path)); } catch { return 0; }
        }

        private static long DeleteFile(FileInfo f)
        {
            try
            {
                if (!f.Exists) return 0;
                var len = f.Length;
                if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes = FileAttributes.Normal;
                f.Delete();
                return len;
            }
            catch
            {
                return 0; // locked / in use / no access → skip
            }
        }

        private static long SafeLength(FileInfo f)
        {
            try { return f.Length; } catch { return 0; }
        }

        private static IEnumerable<FileInfo> SafeFiles(DirectoryInfo dir, string pattern)
        {
            FileInfo[] files;
            try { files = dir.GetFiles(pattern, SearchOption.TopDirectoryOnly); }
            catch { yield break; }
            foreach (var f in files) yield return f;
        }

        private static IEnumerable<DirectoryInfo> SafeDirs(DirectoryInfo dir)
        {
            DirectoryInfo[] dirs;
            try { dirs = dir.GetDirectories(); }
            catch { yield break; }
            foreach (var d in dirs) yield return d;
        }

        public static string Pretty(long bytes)
        {
            if (bytes < 0) return "—";
            string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return (u == 0 ? v.ToString("0") : v.ToString("0.#")) + " " + units[u];
        }
    }
}
