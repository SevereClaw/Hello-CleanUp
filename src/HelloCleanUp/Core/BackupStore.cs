using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HelloCleanUp.Core
{
    /// <summary>
    /// Remembers the original value of everything Hello CleanUp changes, so it can be reverted.
    /// Stored in %ProgramData%\HelloCleanUp\backup.txt as "key TAB base64(value)" lines, oldest first.
    /// Only the FIRST original value is kept: re-applying a tweak never overwrites the backup.
    ///
    /// Keys: "reg|HKLM\path|name", "reg|HKU\{SID}\path|name" (per-user values), "svc|name", "task|\path".
    /// v1.0 wrote per-user values as "reg|HKCU\..." without a SID; those are migrated on load to the
    /// account the program runs as.
    /// </summary>
    public sealed class BackupStore
    {
        private readonly Dictionary<string, string> _items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _order = new List<string>();
        private readonly object _lock = new object();

        /// <summary>True when the file exists but could not be read: nothing may be written, or the backup would be lost.</summary>
        private bool _unreadable;

        /// <summary>Set when something was wrong with the backup file on load; shown to the user.</summary>
        public string LoadProblem { get; private set; }

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
            if (!File.Exists(FilePath)) return;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(FilePath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _unreadable = true;
                LoadProblem = "Не удалось прочитать файл бэкапа (" + ex.Message + "). Пока он не читается, новые изменения не применяются, чтобы не потерять старые исходные значения.";
                return;
            }

            int bad = 0;
            foreach (var line in lines)
            {
                if (line.Trim().Length == 0) continue;
                try
                {
                    var tab = line.IndexOf('\t');
                    if (tab <= 0) throw new FormatException();
                    var key = line.Substring(0, tab);
                    var val = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(tab + 1).Trim()));
                    if (!_items.ContainsKey(key)) _order.Add(key);
                    _items[key] = val;
                }
                catch
                {
                    bad++;
                }
            }

            if (bad > 0)
            {
                // Keep the damaged original next to it before anything rewrites backup.txt.
                var copy = Path.Combine(Folder, "backup.broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                try { File.Copy(FilePath, copy, true); } catch { }
                LoadProblem = "В файле бэкапа повреждено строк: " + bad + ". Остальные значения загружены, копия исходного файла сохранена: " + copy;
            }

            MigrateLegacyUserKeys();
        }

        private void MigrateLegacyUserKeys()
        {
            const string legacy = "reg|HKCU\\";
            bool changed = false;
            for (int i = 0; i < _order.Count; i++)
            {
                var key = _order[i];
                if (!key.StartsWith(legacy, StringComparison.OrdinalIgnoreCase)) continue;
                var migrated = "reg|HKU\\" + UserContext.Sid + "\\" + key.Substring(legacy.Length);
                var value = _items[key];
                _items.Remove(key);
                if (_items.ContainsKey(migrated))
                {
                    _order.RemoveAt(i);
                    i--;
                }
                else
                {
                    _items[migrated] = value;
                    _order[i] = migrated;
                }
                changed = true;
            }
            if (changed)
            {
                try { Save(); } catch { /* retried on the next write */ }
            }
        }

        private void Save()
        {
            if (_unreadable)
                throw new IOException("файл бэкапа " + FilePath + " не читается — изменение не применено, чтобы его не перезаписать");

            Directory.CreateDirectory(Folder);
            var sb = new StringBuilder();
            foreach (var key in _order)
            {
                sb.Append(key).Append('\t')
                  .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(_items[key])))
                  .Append("\r\n");
            }
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(FilePath))
                File.Replace(tmp, FilePath, null, true); // atomic: backup.txt is never missing
            else
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

        /// <summary>Stores the original value unless one is already stored. Throws if it can't be stored, so the change is not made.</summary>
        public void RememberOnce(string key, string originalValue)
        {
            lock (_lock)
            {
                if (_items.ContainsKey(key)) return;
                _items[key] = originalValue;
                _order.Add(key);
                try
                {
                    Save();
                }
                catch
                {
                    _items.Remove(key);
                    _order.Remove(key);
                    throw;
                }
            }
        }

        public void Forget(string key)
        {
            lock (_lock)
            {
                if (_items.Remove(key))
                {
                    _order.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                    Save();
                }
            }
        }

        /// <summary>All stored keys, newest first — the order to undo them in.</summary>
        public IList<string> KeysNewestFirst()
        {
            lock (_lock) return Enumerable.Reverse(_order).ToList();
        }

        public int Count
        {
            get { lock (_lock) return _items.Count; }
        }
    }
}
