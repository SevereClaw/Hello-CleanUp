using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Win32;

namespace HelloCleanUp.Core
{
    public interface ITweakAction
    {
        /// <summary>True when the system already matches the tweaked state (or the thing does not exist here).</summary>
        bool IsApplied();
        void Apply(BackupStore backup, Action<string> log);
        void Revert(BackupStore backup, Action<string> log);
    }

    // ---------------------------------------------------------------- registry

    public sealed class RegAction : ITweakAction
    {
        private readonly RegistryHive _hive;
        private readonly string _path;
        private readonly string _name;
        private readonly RegistryValueKind _kind;
        private readonly object _value;

        private RegAction(RegistryHive hive, string path, string name, RegistryValueKind kind, object value)
        {
            _hive = hive; _path = path; _name = name; _kind = kind; _value = value;
        }

        public static RegAction Dword(string fullPath, string name, int value)
        {
            RegistryHive hive; string path;
            Split(fullPath, out hive, out path);
            return new RegAction(hive, path, name, RegistryValueKind.DWord, value);
        }

        private static void Split(string full, out RegistryHive hive, out string path)
        {
            var i = full.IndexOf('\\');
            var root = full.Substring(0, i).ToUpperInvariant();
            path = full.Substring(i + 1);
            switch (root)
            {
                case "HKLM": hive = RegistryHive.LocalMachine; break;
                case "HKCU": hive = RegistryHive.CurrentUser; break;
                default: throw new ArgumentException("Unknown hive " + root);
            }
        }

        private string HiveName { get { return _hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU"; } }
        private string Key { get { return "reg|" + HiveName + "\\" + _path + "|" + _name; } }
        public override string ToString() { return HiveName + "\\" + _path + " → " + _name + " = " + _value; }

        private RegistryKey OpenBase()
        {
            return RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
        }

        private object Read(out RegistryValueKind kind)
        {
            kind = RegistryValueKind.Unknown;
            using (var b = OpenBase())
            using (var k = b.OpenSubKey(_path, false))
            {
                if (k == null) return null;
                var v = k.GetValue(_name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (v != null) kind = k.GetValueKind(_name);
                return v;
            }
        }

        public bool IsApplied()
        {
            RegistryValueKind kind;
            var v = Read(out kind);
            if (v == null) return false;
            if (_kind == RegistryValueKind.DWord)
            {
                try { return Convert.ToInt64(v, CultureInfo.InvariantCulture) == Convert.ToInt64(_value, CultureInfo.InvariantCulture); }
                catch { return false; }
            }
            return string.Equals(Convert.ToString(v, CultureInfo.InvariantCulture),
                                 Convert.ToString(_value, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        public void Apply(BackupStore backup, Action<string> log)
        {
            RegistryValueKind kind;
            var current = Read(out kind);
            backup.RememberOnce(Key, Serialize(current, kind));

            using (var b = OpenBase())
            using (var k = b.CreateSubKey(_path, true))
            {
                k.SetValue(_name, _value, _kind);
            }
            log("  реестр: " + this);
        }

        public void Revert(BackupStore backup, Action<string> log)
        {
            var saved = backup.Get(Key);
            if (saved == null)
            {
                // Nothing remembered: the value was set by someone else, leave it alone.
                return;
            }

            using (var b = OpenBase())
            {
                if (saved == "absent")
                {
                    using (var k = b.OpenSubKey(_path, true))
                    {
                        if (k != null) k.DeleteValue(_name, false);
                    }
                    log("  реестр: удалено " + HiveName + "\\" + _path + " → " + _name);
                }
                else
                {
                    var sep = saved.IndexOf('|');
                    var kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), saved.Substring(0, sep));
                    var raw = saved.Substring(sep + 1);
                    object value;
                    switch (kind)
                    {
                        case RegistryValueKind.DWord: value = unchecked((int)uint.Parse(raw, CultureInfo.InvariantCulture)); break;
                        case RegistryValueKind.QWord: value = unchecked((long)ulong.Parse(raw, CultureInfo.InvariantCulture)); break;
                        case RegistryValueKind.MultiString: value = raw.Split('\0'); break;
                        case RegistryValueKind.Binary: value = Convert.FromBase64String(raw); break;
                        default: value = raw; break;
                    }
                    using (var k = b.CreateSubKey(_path, true))
                    {
                        k.SetValue(_name, value, kind);
                    }
                    log("  реестр: восстановлено " + HiveName + "\\" + _path + " → " + _name + " = " + raw);
                }
            }
            backup.Forget(Key);
        }

        private static string Serialize(object v, RegistryValueKind kind)
        {
            if (v == null) return "absent";
            string raw;
            switch (kind)
            {
                case RegistryValueKind.DWord: raw = unchecked((uint)(int)v).ToString(CultureInfo.InvariantCulture); break;
                case RegistryValueKind.QWord: raw = unchecked((ulong)(long)v).ToString(CultureInfo.InvariantCulture); break;
                case RegistryValueKind.MultiString: raw = string.Join("\0", (string[])v); break;
                case RegistryValueKind.Binary: raw = Convert.ToBase64String((byte[])v); break;
                default: raw = Convert.ToString(v, CultureInfo.InvariantCulture); break;
            }
            return kind + "|" + raw;
        }
    }

    // ---------------------------------------------------------------- services

    /// <summary>Sets a service to Disabled (Start = 4) via the registry and stops it.</summary>
    public sealed class ServiceAction : ITweakAction
    {
        private const string ServicesPath = @"SYSTEM\CurrentControlSet\Services\";
        private readonly string _name;

        public ServiceAction(string name) { _name = name; }

        private string Key { get { return "svc|" + _name; } }

        private int? ReadStart()
        {
            using (var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = b.OpenSubKey(ServicesPath + _name, false))
            {
                if (k == null) return null;
                var v = k.GetValue("Start");
                return v == null ? (int?)null : Convert.ToInt32(v, CultureInfo.InvariantCulture);
            }
        }

        private void WriteStart(int value)
        {
            using (var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = b.OpenSubKey(ServicesPath + _name, true))
            {
                if (k != null) k.SetValue("Start", value, RegistryValueKind.DWord);
            }
        }

        public bool IsApplied()
        {
            var s = ReadStart();
            return s == null || s.Value == 4; // missing service counts as "already gone"
        }

        public void Apply(BackupStore backup, Action<string> log)
        {
            var s = ReadStart();
            if (s == null) { log("  служба " + _name + ": нет в этой системе, пропуск"); return; }
            backup.RememberOnce(Key, s.Value.ToString(CultureInfo.InvariantCulture));
            WriteStart(4);
            Shell.Run(Shell.System32("sc.exe"), "stop " + _name, 30000);
            log("  служба " + _name + ": отключена");
        }

        public void Revert(BackupStore backup, Action<string> log)
        {
            var saved = backup.Get(Key);
            if (saved == null) return;
            var start = int.Parse(saved, CultureInfo.InvariantCulture);
            WriteStart(start);
            if (start == 2) Shell.Run(Shell.System32("sc.exe"), "start " + _name, 30000);
            log("  служба " + _name + ": тип запуска восстановлен (" + start + ")");
            backup.Forget(Key);
        }
    }

    // ---------------------------------------------------------------- scheduled tasks

    /// <summary>Disables a Task Scheduler task. State is read from the task XML, so it works on any UI language.</summary>
    public sealed class TaskAction : ITweakAction
    {
        private readonly string _task;

        public TaskAction(string taskPath) { _task = taskPath; }

        private string Key { get { return "task|" + _task; } }

        /// <summary>null = task does not exist.</summary>
        private bool? IsEnabled()
        {
            string xml;
            var code = Shell.Run(Shell.System32("schtasks.exe"), "/Query /TN \"" + _task + "\" /XML", out xml, 30000);
            if (code != 0) return null;
            try
            {
                var start = xml.IndexOf('<');
                if (start < 0) return null;
                var doc = XDocument.Parse(xml.Substring(start));
                var settings = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "Settings");
                if (settings == null) return true;
                var enabled = settings.Elements().FirstOrDefault(e => e.Name.LocalName == "Enabled");
                return enabled == null || !string.Equals(enabled.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return null;
            }
        }

        public bool IsApplied()
        {
            var e = IsEnabled();
            return e == null || e.Value == false;
        }

        public void Apply(BackupStore backup, Action<string> log)
        {
            var e = IsEnabled();
            if (e == null) { log("  задача " + _task + ": нет в этой системе, пропуск"); return; }
            backup.RememberOnce(Key, e.Value ? "1" : "0");
            var code = Shell.Run(Shell.System32("schtasks.exe"), "/Change /TN \"" + _task + "\" /Disable", 30000);
            log("  задача " + _task + (code == 0 ? ": отключена" : ": не удалось отключить (код " + code + ")"));
        }

        public void Revert(BackupStore backup, Action<string> log)
        {
            var saved = backup.Get(Key);
            if (saved == null) return;
            if (saved == "1")
            {
                Shell.Run(Shell.System32("schtasks.exe"), "/Change /TN \"" + _task + "\" /Enable", 30000);
                log("  задача " + _task + ": включена обратно");
            }
            backup.Forget(Key);
        }
    }
}
