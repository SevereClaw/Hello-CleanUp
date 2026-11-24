using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace HelloCleanUp.Core
{
    public sealed class CleanTarget
    {
        public string Id { get; private set; }
        public string Title { get; private set; }
        public string Description { get; private set; }
        public bool Recommended { get; private set; }

        /// <summary>Returns bytes that would be freed, or -1 when it can't be known in advance.</summary>
        public Func<long> Scan { get; private set; }

        /// <summary>Does the cleanup, returns bytes freed (or -1 if unknown).</summary>
        public Func<Action<string>, long> Clean { get; private set; }

        public CleanTarget(string id, string title, string description, bool recommended,
                           Func<long> scan, Func<Action<string>, long> clean)
        {
            Id = id; Title = title; Description = description; Recommended = recommended;
            Scan = scan; Clean = clean;
        }
    }

    public static class CleanCatalog
    {
        private static string LocalAppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        private static string ProgramData { get { return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData); } }
        private static string WinDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }

        private static CleanTarget Folders(string id, string title, string description, bool recommended, Func<IEnumerable<string>> dirs, string pattern = "*")
        {
            return new CleanTarget(id, title, description, recommended,
                () => dirs().Sum(d => FileWiper.SizeOfDir(d, pattern)),
                log =>
                {
                    long freed = 0;
                    foreach (var d in dirs())
                    {
                        if (!Directory.Exists(d)) continue;
                        var f = FileWiper.WipeDir(d, pattern);
                        freed += f;
                        log("  " + d + " — " + FileWiper.Pretty(f));
                    }
                    return freed;
                });
        }

        public static IList<CleanTarget> All()
        {
            return new List<CleanTarget>
            {
                Folders("user-temp", "Временные файлы пользователя",
                    "Папка %TEMP%. Файлы, которые сейчас заняты программами, пропускаются.",
                    true, () => new[] { Path.GetTempPath() }),

                Folders("windows-temp", "Временные файлы Windows",
                    @"C:\Windows\Temp — остатки установщиков и системных служб.",
                    true, () => new[] { Path.Combine(WinDir, "Temp") }),

                new CleanTarget("update-cache", "Загруженные обновления Windows",
                    "Установочные файлы уже установленных обновлений. Служба обновлений будет на секунду остановлена и снова запущена, если работала.",
                    true,
                    () => FileWiper.SizeOfDir(Path.Combine(WinDir, @"SoftwareDistribution\Download")),
                    log =>
                    {
                        var dir = Path.Combine(WinDir, @"SoftwareDistribution\Download");
                        // Only restart what was running before: a service the user stopped stays stopped.
                        bool wuWasRunning = Shell.IsServiceRunning("wuauserv");
                        bool bitsWasRunning = Shell.IsServiceRunning("bits");
                        if (wuWasRunning) Shell.Run(Shell.System32("net.exe"), "stop wuauserv /y", 60000);
                        if (bitsWasRunning) Shell.Run(Shell.System32("net.exe"), "stop bits /y", 60000);
                        long freed;
                        try { freed = FileWiper.WipeDir(dir); }
                        finally
                        {
                            if (bitsWasRunning) Shell.Run(Shell.System32("net.exe"), "start bits", 60000);
                            if (wuWasRunning) Shell.Run(Shell.System32("net.exe"), "start wuauserv", 60000);
                        }
                        log("  " + dir + " — " + FileWiper.Pretty(freed));
                        return freed;
                    }),

                new CleanTarget("delivery-optimization", "Кэш оптимизации доставки",
                    "Файлы обновлений, которые Windows раздаёт другим компьютерам. Очищается штатной командой Windows.",
                    true,
                    () => FileWiper.SizeOfDir(Path.Combine(WinDir, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")),
                    log =>
                    {
                        var before = FileWiper.SizeOfDir(Path.Combine(WinDir, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"));
                        string output;
                        var code = Shell.PowerShell("Delete-DeliveryOptimizationCache -Force", out output, 300000);
                        log(code == 0 ? "  кэш очищен" : "  не удалось очистить: " + output.Trim());
                        return code == 0 ? before : 0;
                    }),

                new CleanTarget("crash-dumps", "Дампы памяти после сбоев",
                    "Файлы .dmp, которые пишутся при вылетах программ и синих экранах. Нужны только для отладки.",
                    true,
                    () => FileWiper.SizeOfDir(Path.Combine(LocalAppData, "CrashDumps"))
                        + FileWiper.SizeOfDir(Path.Combine(WinDir, "Minidump"))
                        + FileWiper.SizeOfDir(Path.Combine(WinDir, "LiveKernelReports"), "*.dmp")
                        + FileWiper.SizeOfFile(Path.Combine(WinDir, "MEMORY.DMP")),
                    log =>
                    {
                        long freed = FileWiper.WipeDir(Path.Combine(LocalAppData, "CrashDumps"))
                                   + FileWiper.WipeDir(Path.Combine(WinDir, "Minidump"))
                                   + FileWiper.WipeDir(Path.Combine(WinDir, "LiveKernelReports"), "*.dmp")
                                   + FileWiper.DeleteFile(Path.Combine(WinDir, "MEMORY.DMP"));
                        log("  дампы — " + FileWiper.Pretty(freed));
                        return freed;
                    }),

                Folders("error-reports", "Отчёты об ошибках",
                    "Накопленные отчёты Windows Error Reporting, в том числе неотправленные.",
                    true, () => new[]
                    {
                        Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportArchive"),
                        Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportQueue"),
                        Path.Combine(ProgramData, @"Microsoft\Windows\WER\Temp"),
                        Path.Combine(LocalAppData, @"Microsoft\Windows\WER"),
                    }),

                Folders("browser-cache", "Кэш браузеров",
                    "Только кэш страниц и шейдеров в Chrome, Edge, Яндекс Браузере, Brave, Opera и Firefox. Пароли, куки и история не трогаются. Лучше закрыть браузеры перед очисткой.",
                    false, BrowserCacheDirs),

                Folders("shader-cache", "Кэш шейдеров видеокарты",
                    "DirectX, NVIDIA и AMD. Пересоберётся сам, но первый запуск игр после очистки может подлагивать.",
                    false, () => new[]
                    {
                        Path.Combine(LocalAppData, "D3DSCache"),
                        Path.Combine(LocalAppData, @"NVIDIA\DXCache"),
                        Path.Combine(LocalAppData, @"NVIDIA\GLCache"),
                        Path.Combine(LocalAppData, @"AMD\DxCache"),
                        Path.Combine(LocalAppData, @"AMD\DxcCache"),
                        Path.Combine(LocalAppData, @"AMD\GLCache"),
                    }),

                Folders("thumbnails", "Кэш эскизов",
                    "Миниатюры картинок и видео в проводнике. Пересоздаются при следующем открытии папок.",
                    false, () => new[] { Path.Combine(LocalAppData, @"Microsoft\Windows\Explorer") }, "thumbcache_*.db"),

                new CleanTarget("recycle-bin", "Корзина",
                    "Окончательно удаляет всё, что лежит в корзине на всех дисках.",
                    false,
                    RecycleBinSize,
                    log =>
                    {
                        var size = RecycleBinSize();
                        var hr = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                        log(hr == 0 || size == 0 ? "  корзина пуста" : "  не удалось очистить корзину (0x" + hr.ToString("X8") + ")");
                        return hr == 0 ? size : 0;
                    }),

                new CleanTarget("component-store", "Хранилище компонентов (WinSxS)",
                    "Штатная очистка DISM: удаляет старые версии системных файлов, заменённые обновлениями. Может идти 5–15 минут, размер заранее не узнать.",
                    false,
                    () => -1,
                    log =>
                    {
                        log("  запускаю DISM, это надолго…");
                        string output;
                        var code = Shell.Run(Shell.System32("Dism.exe"), "/Online /Cleanup-Image /StartComponentCleanup", out output, 60 * 60 * 1000);
                        log(code == 0 ? "  DISM завершил очистку" : "  DISM вернул код " + code);
                        return -1;
                    }),
            };
        }

        private static IEnumerable<string> BrowserCacheDirs()
        {
            var chromium = new[]
            {
                @"Google\Chrome\User Data",
                @"Microsoft\Edge\User Data",
                @"Yandex\YandexBrowser\User Data",
                @"BraveSoftware\Brave-Browser\User Data",
                @"Vivaldi\User Data",
            };
            foreach (var root in chromium)
            {
                var full = Path.Combine(LocalAppData, root);
                foreach (var profile in SafeSubdirs(full))
                {
                    var name = Path.GetFileName(profile);
                    if (name != "Default" && !name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase)) continue;
                    yield return Path.Combine(profile, "Cache");
                    yield return Path.Combine(profile, "Code Cache");
                    yield return Path.Combine(profile, "GPUCache");
                }
                yield return Path.Combine(full, "ShaderCache");
                yield return Path.Combine(full, "GrShaderCache");
            }

            // Opera keeps its cache directly under Local\Opera Software\<edition>\Cache
            foreach (var opera in SafeSubdirs(Path.Combine(LocalAppData, "Opera Software")))
            {
                yield return Path.Combine(opera, "Cache");
            }

            foreach (var ff in SafeSubdirs(Path.Combine(LocalAppData, @"Mozilla\Firefox\Profiles")))
            {
                yield return Path.Combine(ff, "cache2");
            }
        }

        private static IEnumerable<string> SafeSubdirs(string path)
        {
            try { return Directory.Exists(path) ? Directory.GetDirectories(path) : new string[0]; }
            catch { return new string[0]; }
        }

        // ---------------------------------------------------------------- recycle bin

        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        private const uint SHERB_NOCONFIRMATION = 0x1, SHERB_NOPROGRESSUI = 0x2, SHERB_NOSOUND = 0x4;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        private static long RecycleBinSize()
        {
            try
            {
                var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
                return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
            }
            catch { return 0; }
        }
    }
}
