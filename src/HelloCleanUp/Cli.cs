using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using HelloCleanUp.Core;

namespace HelloCleanUp
{
    /// <summary>
    /// Command-line mode for scripts and unattended installs: runs without a window when any argument is given.
    /// Output goes to the calling console (when there is one) and always to a log file.
    /// </summary>
    public static class Cli
    {
        public const int ExitOk = 0, ExitErrors = 1, ExitBadArgs = 2, ExitNoRestorePoint = 3;

        private sealed class Options
        {
            public string Tweaks, Clean, Revert, LogPath;
            public bool DryRun, List, Help, NoRestorePoint;
        }

        private static StreamWriter _file;

        public static int Run(string[] args)
        {
            AttachToConsole();
            try
            {
                return RunCore(args);
            }
            catch (Exception ex)
            {
                Out("Ошибка: " + ex.Message);
                return ExitErrors;
            }
            finally
            {
                if (_file != null)
                {
                    try { _file.Dispose(); } catch { }
                    _file = null;
                }
            }
        }

        private static int RunCore(string[] args)
        {
            Options o;
            string error;
            if (!TryParse(args, out o, out error))
            {
                Out("Ошибка в параметрах: " + error);
                Out("");
                PrintHelp();
                return ExitBadArgs;
            }
            if (o.Help) { PrintHelp(); return ExitOk; }

            var tweaks = TweakCatalog.Telemetry();
            var targets = CleanCatalog.All();
            if (o.List) { PrintList(tweaks, targets); return ExitOk; }

            if (o.Tweaks == null && o.Clean == null && o.Revert == null)
            {
                Out("Не указано, что делать.");
                Out("");
                PrintHelp();
                return ExitBadArgs;
            }

            List<Tweak> applyList = null, revertList = null;
            List<CleanTarget> cleanList = null;
            bool revertAll = false;

            if (o.Tweaks != null && !TrySelect(o.Tweaks, tweaks, t => t.Id, t => t.Recommended, out applyList, out error))
                return BadArgs("/tweaks: " + error);
            if (o.Clean != null && !TrySelect(o.Clean, targets, t => t.Id, t => t.Recommended, out cleanList, out error))
                return BadArgs("/clean: " + error);
            if (o.Revert != null)
            {
                if (string.Equals(o.Revert.Trim(), "all", StringComparison.OrdinalIgnoreCase)) revertAll = true;
                else if (!TrySelect(o.Revert, tweaks, t => t.Id, t => t.Recommended, out revertList, out error))
                    return BadArgs("/revert: " + error);
            }
            if (applyList != null && (revertAll || revertList != null))
                return BadArgs("/tweaks и /revert нельзя указывать вместе");

            if (o.LogPath != null || !o.DryRun) OpenLog(o.LogPath);

            var v = Assembly.GetExecutingAssembly().GetName().Version;
            Out("Hello, CleanUp. v" + v.Major + "." + v.Minor + "." + v.Build + " — консольный режим" +
                (o.DryRun ? " (только просмотр: ничего не меняется)" : ""));
            Out("Windows " + Environment.OSVersion.Version + ", пользователь " + UserContext.ProcessUser);
            var other = UserContext.OtherSessionUser();
            if (other != null)
                Out("Внимание: в Windows вошёл " + other + ", а программа запущена от имени " + UserContext.ProcessUser +
                    ". Настройки HKCU применятся к " + Environment.UserName + ".");

            int errors = 0;
            bool restorePointFailed = false;
            BackupStore backup = null;
            if (!o.DryRun)
            {
                backup = new BackupStore();
                if (backup.LoadProblem != null) Out("Бэкап: " + backup.LoadProblem);
            }

            // ---------------------------------------------------------- revert
            if (revertAll)
            {
                Out("");
                Out("— Откат всего, что меняла программа —");
                if (o.DryRun)
                {
                    Out("  будут возвращены все исходные значения из " + BackupStore.FilePath);
                }
                else
                {
                    var left = Rollback.RevertAll(backup, Out);
                    if (left > 0) { errors++; Out("Не откатилось записей: " + left + " (остались в бэкапе)."); }
                }
            }
            else if (revertList != null)
            {
                Out("");
                Out("— Откат выбранного —");
                foreach (var t in revertList)
                {
                    if (o.DryRun) Out("  " + t.Title + "  [" + t.Id + "]");
                    else errors += t.Revert(backup, Out);
                }
            }

            // ---------------------------------------------------------- tweaks
            if (applyList != null)
            {
                Out("");
                Out("— Телеметрия и реклама —");
                int count;
                var preview = Preview.ForTweaks(applyList, out count);
                if (count == 0)
                {
                    Out("  всё выбранное уже применено, менять нечего");
                }
                else
                {
                    Out("Изменится (" + count + "):");
                    foreach (var line in preview.Split('\n')) Out("  " + line.TrimEnd('\r'));
                    if (!o.DryRun)
                    {
                        Out("");
                        if (!o.NoRestorePoint && !RestorePoint.Create(Out))
                        {
                            restorePointFailed = true;
                            Out("Точка восстановления не создана — твики НЕ применены. Добавь /norestorepoint, чтобы применить без неё.");
                        }
                        else
                        {
                            foreach (var t in applyList) errors += t.Apply(backup, Out);
                            Out("Часть изменений заработает после перезапуска проводника или перезагрузки.");
                        }
                    }
                }
            }

            // ---------------------------------------------------------- cleanup
            if (cleanList != null)
            {
                Out("");
                Out(o.DryRun ? "— Чистка диска: сколько освободится —" : "— Чистка диска —");
                long total = 0;
                bool unknown = false;
                foreach (var t in cleanList)
                {
                    long size;
                    try
                    {
                        if (o.DryRun)
                        {
                            size = t.Scan();
                            Out("  " + t.Title + "  [" + t.Id + "] — " + (size < 0 ? "заранее не узнать" : FileWiper.Pretty(size)));
                        }
                        else
                        {
                            Out("▸ " + t.Title);
                            size = t.Clean(Out);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        Out("  ошибка: " + ex.Message);
                        size = 0;
                    }
                    if (size < 0) unknown = true; else total += size;
                }
                Out((o.DryRun ? "Освободится примерно: " : "Освобождено: ") + FileWiper.Pretty(total) + (unknown ? " (+ WinSxS)" : ""));
            }

            Out("");
            if (restorePointFailed) { Out("Готово с ошибкой: нет точки восстановления."); return ExitNoRestorePoint; }
            if (errors > 0) { Out("Готово, но были ошибки: " + errors + ". Подробности выше."); return ExitErrors; }
            Out(o.DryRun ? "Просмотр закончен, ничего не изменено." : "Готово.");
            return ExitOk;
        }

        // ================================================================== arguments

        private static bool TryParse(string[] args, out Options o, out string error)
        {
            o = new Options();
            error = null;
            foreach (var raw in args)
            {
                var a = raw.Trim();
                if (a.Length == 0) continue;
                if (a.StartsWith("--", StringComparison.Ordinal)) a = a.Substring(2);
                else if (a.StartsWith("/", StringComparison.Ordinal) || a.StartsWith("-", StringComparison.Ordinal)) a = a.Substring(1);
                else { error = "непонятный параметр «" + raw + "»"; return false; }

                string name = a, value = null;
                var sep = a.IndexOfAny(new[] { ':', '=' });
                if (sep >= 0) { name = a.Substring(0, sep); value = a.Substring(sep + 1); }

                switch (name.ToLowerInvariant())
                {
                    case "tweaks":
                    case "apply":
                        o.Tweaks = string.IsNullOrEmpty(value) ? "recommended" : value; break;
                    case "clean":
                        o.Clean = string.IsNullOrEmpty(value) ? "recommended" : value; break;
                    case "revert":
                        o.Revert = string.IsNullOrEmpty(value) ? "all" : value; break;
                    case "dry-run":
                    case "dryrun":
                    case "preview":
                    case "whatif":
                        o.DryRun = true; break;
                    case "norestorepoint":
                    case "no-restore-point":
                        o.NoRestorePoint = true; break;
                    case "log":
                        if (string.IsNullOrEmpty(value)) { error = "для /log нужен путь к файлу: /log:C:\\путь\\файл.log"; return false; }
                        o.LogPath = value.Trim('"'); break;
                    case "list":
                        o.List = true; break;
                    case "quiet":
                    case "silent":
                        break; // the command line never shows windows anyway
                    case "?":
                    case "h":
                    case "help":
                        o.Help = true; break;
                    default:
                        error = "неизвестный параметр «" + raw + "»"; return false;
                }
            }
            return true;
        }

        private static bool TrySelect<T>(string spec, IList<T> all, Func<T, string> id, Func<T, bool> recommended,
                                         out List<T> result, out string error) where T : class
        {
            error = null;
            var s = spec.Trim();
            if (string.Equals(s, "recommended", StringComparison.OrdinalIgnoreCase)) { result = all.Where(recommended).ToList(); return true; }
            if (string.Equals(s, "all", StringComparison.OrdinalIgnoreCase)) { result = all.ToList(); return true; }

            result = new List<T>();
            foreach (var part in s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = part.Trim();
                var item = all.FirstOrDefault(x => string.Equals(id(x), p, StringComparison.OrdinalIgnoreCase));
                if (item == null) { error = "неизвестный id «" + p + "» (список: HelloCleanUp.exe /list)"; return false; }
                if (!result.Contains(item)) result.Add(item);
            }
            if (result.Count == 0) { error = "пустой список"; return false; }
            return true;
        }

        private static int BadArgs(string error)
        {
            Out("Ошибка в параметрах: " + error);
            return ExitBadArgs;
        }

        // ================================================================== output

        private static void PrintHelp()
        {
            Out("Hello, CleanUp. — консольный режим (без окна). Нужны права администратора.");
            Out("");
            Out("  /tweaks[:recommended|all|id,id…]   применить твики (по умолчанию — рекомендуемые)");
            Out("  /clean[:recommended|all|id,id…]    почистить диск (по умолчанию — рекомендуемое)");
            Out("  /revert[:all|id,id…]               откатить (по умолчанию — всё из бэкапа)");
            Out("  /dry-run                           только показать, что изменится; ничего не менять");
            Out("  /norestorepoint                    не создавать точку восстановления перед твиками");
            Out("  /log:путь                          куда писать журнал (по умолчанию " + Path.Combine(BackupStore.Folder, "logs") + ")");
            Out("  /list                              список id твиков и пунктов чистки");
            Out("  /?                                 эта справка");
            Out("");
            Out("Примеры:");
            Out("  start /wait HelloCleanUp.exe /tweaks /clean");
            Out("  start /wait HelloCleanUp.exe /tweaks:telemetry-level,start-ads /dry-run");
            Out("  start /wait HelloCleanUp.exe /revert");
            Out("");
            Out("Коды выхода: 0 — готово, 1 — были ошибки, 2 — ошибка в параметрах, 3 — не создалась точка восстановления (твики не применены).");
        }

        private static void PrintList(IList<Tweak> tweaks, IList<CleanTarget> targets)
        {
            Out("Твики (/tweaks, /revert), * — входит в recommended:");
            foreach (var t in tweaks) Out("  " + (t.Recommended ? "* " : "  ") + t.Id.PadRight(24) + t.Title);
            Out("");
            Out("Чистка (/clean), * — входит в recommended:");
            foreach (var t in targets) Out("  " + (t.Recommended ? "* " : "  ") + t.Id.PadRight(24) + t.Title);
        }

        private static void OpenLog(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                    path = Path.Combine(BackupStore.Folder, "logs", "cli-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                path = Path.GetFullPath(path);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                _file = new StreamWriter(path, true, new UTF8Encoding(false)) { AutoFlush = true };
                Out("Журнал: " + path);
            }
            catch (Exception ex)
            {
                Out("Не удалось открыть журнал (" + ex.Message + "), пишу только в консоль.");
            }
        }

        private static void Out(string line)
        {
            try { Console.WriteLine(line); } catch { }
            if (_file != null)
            {
                try { _file.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "  " + line); } catch { }
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        private static void AttachToConsole()
        {
            // A WinExe has no console of its own; borrow the caller's (cmd / PowerShell) if there is one.
            try
            {
                if (AttachConsole(-1))
                {
                    try { Console.OutputEncoding = Encoding.UTF8; } catch { }
                    Console.WriteLine();
                }
            }
            catch
            {
            }
        }
    }
}
