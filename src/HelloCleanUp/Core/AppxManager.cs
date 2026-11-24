using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HelloCleanUp.Core
{
    /// <summary>An installed or provisioned Appx/MSIX package, merged by Name.</summary>
    public sealed class AppxPackage
    {
        public string Name { get; set; }
        public string FamilyName { get; set; }

        /// <summary>Full names installed for the account the program runs as (can be several, e.g. x64 + x86).</summary>
        public List<string> UserFullNames { get; private set; }

        /// <summary>Full name of the provisioned copy that Windows installs for every new account; null if none.</summary>
        public string ProvisionedName { get; set; }

        public bool NonRemovable { get; set; }

        public bool ForUser { get { return UserFullNames.Count > 0; } }
        public bool Provisioned { get { return ProvisionedName != null; } }

        public AppxPackage() { UserFullNames = new List<string>(); }
    }

    public sealed class AppxInventory
    {
        public IList<AppxPackage> Packages { get; private set; }

        /// <summary>Set when the list could not be read (completely or partly).</summary>
        public string Error { get; private set; }

        public AppxInventory(IList<AppxPackage> packages, string error)
        {
            Packages = packages; Error = error;
        }

        public IList<AppxPackage> Find(AppEntry entry)
        {
            return Packages.Where(p => entry.Matches(p.Name)).ToList();
        }
    }

    /// <summary>A removed app that can only come back from the Microsoft Store.</summary>
    public sealed class StoreLink
    {
        public string Name { get; set; }
        public string Uri { get; set; }
    }

    /// <summary>
    /// Removes and restores preinstalled Store apps through Windows PowerShell's Appx module.
    ///
    /// Removal is for the account the program runs as. Optionally the provisioned copy is removed too, so new
    /// accounts don't get the app — that part can't be undone locally. Before removal the package family is saved
    /// in the backup as "appx|SID|Name". Restoring re-registers the app from the files Windows still keeps
    /// (they stay while the app is provisioned or installed for another account); if they are gone,
    /// the app's Microsoft Store page is the way back.
    /// </summary>
    public static class AppxManager
    {
        /// <summary>Never removed, whatever a catalog entry or a wildcard says: removing these breaks Windows or the Store.</summary>
        private static readonly string[] Protected =
        {
            "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller",
            "Microsoft.SecHealthUI", "Microsoft.XboxIdentityProvider", "Microsoft.Xbox.TCUI", "Microsoft.GamingServices",
            "Microsoft.Windows.*", "MicrosoftWindows.*", "Microsoft.VCLibs.*", "Microsoft.NET.*", "Microsoft.UI.Xaml.*",
            "Microsoft.WindowsAppRuntime.*", "Microsoft.Services.Store.Engagement", "Microsoft.AAD.BrokerPlugin",
            "Microsoft.AccountsControl", "windows.immersivecontrolpanel", "Microsoft.WindowsCalculator",
            "Microsoft.Windows.Photos", "Microsoft.WindowsNotepad", "Microsoft.Paint", "Microsoft.WindowsTerminal",
            "Microsoft.ScreenSketch", "Microsoft.WindowsCamera"
        };

        /// <summary>Exceptions to the "Microsoft.Windows.*" rule above that the catalog does list.</summary>
        private static readonly string[] Allowed = { "Microsoft.Windows.DevHome" };

        public static bool IsProtected(string name)
        {
            if (Allowed.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase))) return false;
            return Protected.Any(p => AppEntry.NameMatches(p, name));
        }

        // ================================================================== inventory

        public static AppxInventory Load()
        {
            const string script =
                "foreach ($p in @(Get-AppxPackage)) {\r\n" +
                "  if ($p.IsFramework -or $p.IsResourcePackage) { continue }\r\n" +
                "  'U|' + $p.Name + '|' + $p.PackageFullName + '|' + $p.PackageFamilyName + '|' + [string]$p.NonRemovable\r\n" +
                "}\r\n" +
                "try { foreach ($p in @(Get-AppxProvisionedPackage -Online)) { 'P|' + $p.DisplayName + '|' + $p.PackageName } }\r\n" +
                "catch { 'PERR|' + $_.Exception.Message }\r\n" +
                "'END'";

            string output;
            Shell.PowerShell(script, out output, 180000);

            var byName = new Dictionary<string, AppxPackage>(StringComparer.OrdinalIgnoreCase);
            string error = null;
            bool ended = false;
            foreach (var raw in (output ?? "").Split('\n'))
            {
                var line = raw.Trim();
                var f = line.Split('|');
                if (line == "END") { ended = true; continue; }
                if (f[0] == "U" && f.Length >= 5)
                {
                    var p = Get(byName, f[1]);
                    if (!p.UserFullNames.Contains(f[2], StringComparer.OrdinalIgnoreCase)) p.UserFullNames.Add(f[2]);
                    p.FamilyName = f[3];
                    p.NonRemovable = string.Equals(f[4], "True", StringComparison.OrdinalIgnoreCase);
                }
                else if (f[0] == "P" && f.Length >= 3)
                {
                    var p = Get(byName, f[1]);
                    p.ProvisionedName = f[2];
                    if (string.IsNullOrEmpty(p.FamilyName)) p.FamilyName = FamilyFromFullName(f[1], f[2]);
                }
                else if (f[0] == "PERR")
                {
                    error = "не удалось прочитать список приложений для новых пользователей: " + line.Substring(5);
                }
            }
            if (!ended) error = "PowerShell не вернул список приложений: " + Shell.StripClixml(output).Trim();
            return new AppxInventory(byName.Values.OrderBy(p => p.Name).ToList(), error);
        }

        private static AppxPackage Get(Dictionary<string, AppxPackage> byName, string name)
        {
            AppxPackage p;
            if (!byName.TryGetValue(name, out p))
            {
                p = new AppxPackage { Name = name };
                byName[name] = p;
            }
            return p;
        }

        /// <summary>"Name_1.2.3.0_neutral_~_8wekyb3d8bbwe" → "Name_8wekyb3d8bbwe".</summary>
        private static string FamilyFromFullName(string name, string fullName)
        {
            var i = fullName.LastIndexOf('_');
            return i > 0 ? name + "_" + fullName.Substring(i + 1) : null;
        }

        // ================================================================== backup keys

        private static string Key(string name) { return "appx|" + UserContext.Sid + "|" + name; }

        /// <summary>Package names this account removed through Hello CleanUp (and hasn't restored yet).</summary>
        public static IList<string> RemovedNames(BackupStore backup)
        {
            var prefix = "appx|" + UserContext.Sid + "|";
            return backup.KeysNewestFirst()
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(k => k.Substring(prefix.Length))
                .ToList();
        }

        /// <summary>Backup keys of this account's removed packages that belong to the given entries.</summary>
        public static IList<string> KeysFor(BackupStore backup, IEnumerable<AppEntry> entries)
        {
            var list = entries.ToList();
            return RemovedNames(backup)
                .Where(n => list.Any(e => e.Matches(n)))
                .Select(n => Key(n))
                .ToList();
        }

        // ================================================================== preview & state

        /// <summary>What Remove would do for these entries, one line per change.</summary>
        public static string Preview(IEnumerable<AppEntry> entries, AppxInventory inv, bool deprovision, out int changeCount)
        {
            changeCount = 0;
            var sb = new StringBuilder();
            foreach (var e in entries)
            {
                var lines = new List<string>();
                foreach (var p in inv.Find(e))
                {
                    if (IsProtected(p.Name) || p.NonRemovable) { lines.Add("пропуск: " + p.Name + " — системный компонент"); continue; }
                    foreach (var full in p.UserFullNames) lines.Add("удалить у " + Environment.UserName + ": " + full);
                    if (deprovision && p.Provisioned) lines.Add("не ставить новым пользователям: " + p.ProvisionedName);
                }
                int real = lines.Count(l => !l.StartsWith("пропуск", StringComparison.Ordinal));
                if (real == 0) continue;
                changeCount += real;
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine(e.Title + "  [" + e.Id + "]");
                foreach (var l in lines) sb.AppendLine("    " + l);
            }
            return sb.ToString().TrimEnd();
        }

        // ================================================================== remove

        private sealed class Job
        {
            public AppxPackage Package;
            public string Key;
            public bool Deprovision;
        }

        /// <summary>Removes the entries' packages. Returns the number of packages that failed.</summary>
        public static int Remove(IEnumerable<AppEntry> entries, AppxInventory inv, bool deprovision, BackupStore backup, Action<string> log)
        {
            var jobs = new List<Job>();
            int errors = 0;
            foreach (var e in entries)
            {
                foreach (var p in inv.Find(e))
                {
                    if (jobs.Any(j => j.Package == p)) continue;
                    if (IsProtected(p.Name) || p.NonRemovable)
                    {
                        log("  пропуск: " + p.Name + " — системный компонент, его удалять нельзя");
                        continue;
                    }
                    bool dep = deprovision && p.Provisioned;
                    if (!p.ForUser && !dep) continue;

                    var job = new Job { Package = p, Deprovision = dep };
                    if (p.ForUser)
                    {
                        // Remember it first: without a backup entry the app could not be brought back from here.
                        try
                        {
                            job.Key = Key(p.Name);
                            backup.RememberOnce(job.Key, (p.FamilyName ?? "") + "|" + (dep ? "1" : "0"));
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            log("  " + p.Name + ": не удалось записать бэкап (" + ex.Message + ") — не удаляю");
                            continue;
                        }
                    }
                    jobs.Add(job);
                }
            }
            if (jobs.Count == 0)
            {
                log("  удалять нечего — выбранных приложений уже нет");
                return errors;
            }

            var sb = new StringBuilder();
            sb.Append("$ErrorActionPreference = 'Stop'\r\n");
            sb.Append(MsgFunction);
            sb.Append("$items = New-Object System.Collections.ArrayList\r\n");
            foreach (var j in jobs)
            {
                sb.Append("[void]$items.Add(@(").Append(Q(j.Package.Name)).Append(", ")
                  .Append(Q(string.Join(";", j.Package.UserFullNames))).Append(", ")
                  .Append(Q(j.Deprovision ? (j.Package.ProvisionedName ?? "") : "")).Append("))\r\n");
            }
            sb.Append(
                "foreach ($i in $items) {\r\n" +
                "  try {\r\n" +
                "    foreach ($f in ($i[1] -split ';')) { if ($f) { Remove-AppxPackage -Package $f } }\r\n" +
                "    if ($i[2]) { Remove-AppxProvisionedPackage -Online -PackageName $i[2] | Out-Null }\r\n" +
                "    'RES|' + $i[0] + '|OK'\r\n" +
                "  } catch { 'RES|' + $i[0] + '|FAIL|' + (Msg $_) }\r\n" +
                "}\r\n" +
                "'END'");

            string output;
            Shell.PowerShell(sb.ToString(), out output, 1200000);
            var results = ParseResults(output);

            foreach (var j in jobs)
            {
                string res;
                if (!results.TryGetValue(j.Package.Name, out res)) res = "FAIL|PowerShell не ответил";
                if (res == "OK")
                {
                    log("  удалено: " + j.Package.Name + (j.Deprovision ? " (и для новых пользователей)" : ""));
                }
                else
                {
                    errors++;
                    log("  не удалось удалить " + j.Package.Name + ": " + res.Substring(res.IndexOf('|') + 1));
                    // Still installed, so there is nothing to restore later.
                    if (j.Key != null) { try { backup.Forget(j.Key); } catch { } }
                }
            }
            return errors;
        }

        // ================================================================== restore

        /// <summary>
        /// Restores "appx|SID|Name" backup entries. Apps whose files are gone are added to storeLinks (and
        /// forgotten: the Store page is all that can be done). Returns how many entries could not be handled.
        /// </summary>
        public static int Restore(BackupStore backup, IList<string> keys, Action<string> log, IList<StoreLink> storeLinks)
        {
            var mine = new List<string>();
            int left = 0;
            var myPrefix = "appx|" + UserContext.Sid + "|";
            foreach (var key in keys)
            {
                if (backup.Get(key) == null) continue;
                if (key.StartsWith(myPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    mine.Add(key);
                }
                else
                {
                    left++;
                    log("  приложение " + key.Substring(key.LastIndexOf('|') + 1) +
                        ": удалено у другой учётной записи — чтобы вернуть, запусти программу под ней");
                }
            }
            if (mine.Count == 0) return left;

            var names = mine.Select(k => k.Substring(myPrefix.Length)).ToList();
            var script =
                "$ErrorActionPreference = 'Stop'\r\n" +
                MsgFunction +
                "$script:prov = $null\r\n" +
                "function Manifest($n) {\r\n" +
                "  $p = @(Get-AppxPackage -AllUsers -Name $n | Where-Object { $_.InstallLocation } | Sort-Object Version -Descending) | Select-Object -First 1\r\n" +
                "  if ($p) { $m = Join-Path $p.InstallLocation 'AppxManifest.xml'; if (Test-Path -LiteralPath $m) { return $m } }\r\n" +
                "  if ($null -eq $script:prov) { try { $script:prov = @(Get-AppxProvisionedPackage -Online) } catch { $script:prov = @() } }\r\n" +
                "  foreach ($pp in $script:prov) {\r\n" +
                "    if ($pp.DisplayName -eq $n -and $pp.InstallLocation) {\r\n" +
                "      $m = [Environment]::ExpandEnvironmentVariables($pp.InstallLocation)\r\n" +
                "      if (-not $m.EndsWith('.xml')) { $m = Join-Path $m 'AppxManifest.xml' }\r\n" +
                "      if (Test-Path -LiteralPath $m) { return $m }\r\n" +
                "    }\r\n" +
                "  }\r\n" +
                "  return $null\r\n" +
                "}\r\n" +
                "$names = New-Object System.Collections.ArrayList\r\n" +
                string.Concat(names.Select(n => "[void]$names.Add(" + Q(n) + ")\r\n")) +
                "foreach ($n in $names) {\r\n" +
                "  try {\r\n" +
                "    if (@(Get-AppxPackage -Name $n).Count -gt 0) { 'RES|' + $n + '|ALREADY' }\r\n" +
                "    else {\r\n" +
                "      $m = Manifest $n\r\n" +
                "      if ($m) { Add-AppxPackage -Register $m -DisableDevelopmentMode; 'RES|' + $n + '|OK' }\r\n" +
                "      else { 'RES|' + $n + '|STORE' }\r\n" +
                "    }\r\n" +
                "  } catch { 'RES|' + $n + '|FAIL|' + (Msg $_) }\r\n" +
                "}\r\n" +
                "'END'";

            string output;
            Shell.PowerShell(script, out output, 1200000);
            var results = ParseResults(output);

            for (int i = 0; i < mine.Count; i++)
            {
                var key = mine[i];
                var name = names[i];
                string res;
                if (!results.TryGetValue(name, out res)) res = "FAIL|PowerShell не ответил";

                if (res == "OK" || res == "ALREADY")
                {
                    log("  приложение " + name + (res == "OK" ? ": возвращено" : ": уже установлено"));
                    backup.Forget(key);
                }
                else if (res == "STORE")
                {
                    var family = (backup.Get(key) ?? "").Split('|')[0];
                    var uri = string.IsNullOrEmpty(family)
                        ? "ms-windows-store://search/?query=" + System.Uri.EscapeDataString(name)
                        : "ms-windows-store://pdp/?PFN=" + family;
                    log("  приложение " + name + ": его файлов в Windows уже нет — поставь его из Microsoft Store: " + uri);
                    if (storeLinks != null) storeLinks.Add(new StoreLink { Name = name, Uri = uri });
                    backup.Forget(key);
                }
                else
                {
                    left++;
                    log("  приложение " + name + ": не удалось вернуть — " + res.Substring(res.IndexOf('|') + 1));
                }
            }
            return left;
        }

        // ================================================================== helpers

        /// <summary>First non-empty line of an error message — Appx errors are long and multi-line.</summary>
        private const string MsgFunction =
            "function Msg($e) { $t = @(($e.Exception.Message -split '\\r?\\n') | Where-Object { $_.Trim() }); if ($t.Count) { $t[0].Trim() } else { 'неизвестная ошибка' } }\r\n";

        /// <summary>PowerShell single-quoted literal.</summary>
        private static string Q(string s) { return "'" + (s ?? "").Replace("'", "''") + "'"; }

        private static Dictionary<string, string> ParseResults(string output)
        {
            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in (output ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith("RES|", StringComparison.Ordinal)) continue;
                var rest = line.Substring(4);
                var bar = rest.IndexOf('|');
                if (bar <= 0) continue;
                results[rest.Substring(0, bar)] = rest.Substring(bar + 1);
            }
            return results;
        }
    }
}
