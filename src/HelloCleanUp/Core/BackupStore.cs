using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HelloCleanUp.Core
{
    /// <summary>
    /// Remembers the original value of everything Hello CleanUp changes, so it can be reverted.
    /// Stored in %ProgramData%\HelloCleanUp\backup.txt as "key TAB base64(value)" lines.
    /// Only the FIRST original value is kept: re-applying a tweak never overwrites the backup.
    /// </summary>
    public sealed class BackupStore
    {
        private readonly Dictionary<string, string> _items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        public static string Folder
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HelloCleanUp");
            }
        }

        public static string FilePath { get { return Path.Combine(Folder, "backup.txt"); } }

        public BackupStore()
        {
            Load();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    var key = line.Substring(0, tab);
                    var val = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(tab + 1)));
                    _items[key] = val;
                }
            }
            catch
            {
                // A broken backup file must never stop the app; worst case nothing to revert.
            }
        }

        private void Save()
        {
            Directory.CreateDirectory(Folder);
            var sb = new StringBuilder();
            foreach (var kv in _items)
            {
                sb.Append(kv.Key).Append('\t')
                  .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(kv.Value)))
                  .Append("\r\n");
            }
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);
        }

        public bool Has(string key)
        {
            lock (_lock) return _items.ContainsKey(key);
        }

        public string Get(string key)
        {
            lock (_lock)
            {
                string v;
                return _items.TryGetValue(key, out v) ? v : null;
            }
        }

        /// <summary>Stores the original value unless one is already stored.</summary>
        public void RememberOnce(string key, string originalValue)
        {
            lock (_lock)
            {
                if (_items.ContainsKey(key)) return;
                _items[key] = originalValue;
                Save();
            }
        }

        public void Forget(string key)
        {
            lock (_lock)
            {
                if (_items.Remove(key)) Save();
            }
        }

        public int Count
        {
            get { lock (_lock) return _items.Count; }
        }
    }
}
