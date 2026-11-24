using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HelloCleanUp.Core;

namespace HelloCleanUp
{
    public partial class MainWindow : Window
    {
        private readonly BackupStore _backup = new BackupStore();
        private readonly ObservableCollection<RowViewModel> _cleanRows = new ObservableCollection<RowViewModel>();
        private readonly ObservableCollection<RowViewModel> _tweakRows = new ObservableCollection<RowViewModel>();
        private readonly StringBuilder _log = new StringBuilder();
        private bool _busy;

        public MainWindow()
        {
            InitializeComponent();

            GreetingText.Text = "Hello, " + Environment.UserName + ".";
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = "v" + v.Major + "." + v.Minor + "." + v.Build;

            foreach (var t in CleanCatalog.All()) _cleanRows.Add(new RowViewModel(t));
            foreach (var t in TweakCatalog.Telemetry()) _tweakRows.Add(new RowViewModel(t));
            CleanList.ItemsSource = _cleanRows;
            TweakList.ItemsSource = _tweakRows;

            Grain.Fill = MakeGrain();
            StateChanged += OnStateChanged;
            Loaded += async (s, e) =>
            {
                Log("Hello, " + Environment.UserName + ". Windows " + Environment.OSVersion.Version);
                UpdateBackupInfo();
                if (_backup.LoadProblem != null)
                {
                    Log("Бэкап: " + _backup.LoadProblem);
                    MessageBox.Show(this, _backup.LoadProblem, "Hello, CleanUp.", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                WarnIfOtherAccount();
                CheckForUpdateAsync();
                await RunBusy("Смотрю, что можно убрать…", () =>
                {
                    ScanAll();
                    RefreshTweakStates();
                });
            };
        }

        // ================================================================== account & updates

        private void WarnIfOtherAccount()
        {
            var signedIn = UserContext.OtherSessionUser();
            if (signedIn == null) return;
            var text = "Программа запущена от имени " + UserContext.ProcessUser + ", а в Windows вошёл " + signedIn + ".\n\n" +
                       "Настройки Пуска, рекламы и приватности (разделы HKCU) применятся к " + Environment.UserName +
                       ", а не к " + signedIn + ". Чтобы они касались твоей учётной записи, запусти программу из неё.";
            Log("Внимание: " + text.Replace("\n\n", " "));
            MessageBox.Show(this, text, "Hello, CleanUp.", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private async void CheckForUpdateAsync()
        {
            var current = Assembly.GetExecutingAssembly().GetName().Version;
            var newer = await Task.Run(() => UpdateCheck.NewerVersion(current));
            if (newer == null) return;
            UpdateRun.Text = "Есть версия " + newer + " →";
            UpdateText.Visibility = Visibility.Visible;
            Log("Вышла новая версия " + newer + ": " + UpdateCheck.ReleasesPage);
        }

        private void OnOpenReleases(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(UpdateCheck.ReleasesPage) { UseShellExecute = true }); }
            catch { StatusText.Text = UpdateCheck.ReleasesPage; }
        }

        // ================================================================== window chrome

        private void OnMinimize(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }

        private void OnMaximize(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void OnClose(object sender, RoutedEventArgs e) { Close(); }

        private void OnStateChanged(object sender, EventArgs e)
        {
            // A borderless maximized window overhangs the screen by the resize border; compensate.
            RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
        }

        private static Brush MakeGrain()
        {
            const int size = 160;
            var rnd = new Random(1987);
            var pixels = new byte[size * size * 4];
            for (int i = 0; i < size * size; i++)
            {
                byte a = (byte)rnd.Next(0, 256);
                byte c = (byte)(a > 128 ? 255 : 0);
                // premultiplied BGRA
                byte pc = (byte)(c * a / 255);
                pixels[i * 4 + 0] = pc;
                pixels[i * 4 + 1] = pc;
                pixels[i * 4 + 2] = pc;
                pixels[i * 4 + 3] = a;
            }
            var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
            bmp.Freeze();
            var brush = new ImageBrush(bmp)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, size, size),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            brush.Freeze();
            return brush;
        }

        // ================================================================== navigation

        private void OnNav(object sender, RoutedEventArgs e)
        {
            if (PageClean == null) return; // fires during InitializeComponent
            PageClean.Visibility = NavClean.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageTweaks.Visibility = NavTweaks.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageLog.Visibility = NavLog.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (NavLog.IsChecked == true) UpdateBackupInfo();
        }

        // ================================================================== logging & busy state

        private void Log(string line)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => Log(line)));
                return;
            }
            var stamped = DateTime.Now.ToString("HH:mm:ss") + "  " + line;
            _log.AppendLine(stamped);
            LogBox.AppendText(stamped + Environment.NewLine);
            LogBox.ScrollToEnd();
        }

        private void Status(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => Status(text)));
                return;
            }
            StatusText.Text = text;
        }

        private async Task RunBusy(string status, Action work)
        {
            if (_busy) return;
            _busy = true;
            SetButtonsEnabled(false);
            Progress.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = true;
            StatusText.Text = status;
            try
            {
                await Task.Run(work);
            }
            catch (Exception ex)
            {
                Log("ошибка: " + ex.Message);
                StatusText.Text = "Ошибка: " + ex.Message;
            }
            finally
            {
                Progress.IsIndeterminate = false;
                Progress.Visibility = Visibility.Hidden;
                SetButtonsEnabled(true);
                _busy = false;
                UpdateCleanTotal();
                UpdateBackupInfo();
            }
        }

        private void SetButtonsEnabled(bool on)
        {
            CleanButton.IsEnabled = on;
            RescanButton.IsEnabled = on;
            ApplyButton.IsEnabled = on;
            RevertButton.IsEnabled = on;
            RevertAllButton.IsEnabled = on;
        }

        private void OnUi(Action a) { Dispatcher.Invoke(a); }

        private void UpdateBackupInfo()
        {
            var n = _backup.Count;
            BackupInfoText.Text = n == 0
                ? "Пока ничего не менялось. Бэкап исходных настроек: " + BackupStore.FilePath
                : "Сохранено исходных значений: " + n + ". «Откатить всё» вернёт их как было до Hello CleanUp. Бэкап: " + BackupStore.FilePath;
        }

        // ================================================================== disk cleanup

        private void ScanAll()
        {
            foreach (var row in _cleanRows)
            {
                var r = row;
                OnUi(() => r.SetStatus("считаю…", "none"));
                long size;
                try { size = r.Target.Scan(); } catch { size = 0; }
                r.Size = size;
                OnUi(() =>
                {
                    if (size < 0) r.SetStatus("—", "size");
                    else if (size == 0) r.SetStatus("пусто", "off");
                    else r.SetStatus(FileWiper.Pretty(size), "size");
                    UpdateCleanTotal();
                });
            }
            Status("Готово. Отметь, что убрать, и нажми «Очистить».");
        }

        private void UpdateCleanTotal()
        {
            long total = _cleanRows.Where(r => r.IsChecked && r.Size > 0).Sum(r => r.Size);
            bool unknown = _cleanRows.Any(r => r.IsChecked && r.Size < 0);
            CleanTotalText.Text = FileWiper.Pretty(total) + (unknown ? " +" : "");
        }

        private void OnRowToggled(object sender, RoutedEventArgs e) { UpdateCleanTotal(); }

        private void OnCleanRecommended(object sender, RoutedEventArgs e)
        {
            foreach (var r in _cleanRows) r.IsChecked = r.Recommended;
            UpdateCleanTotal();
        }

        private void OnCleanNone(object sender, RoutedEventArgs e)
        {
            foreach (var r in _cleanRows) r.IsChecked = false;
            UpdateCleanTotal();
        }

        private async void OnRescan(object sender, RoutedEventArgs e)
        {
            await RunBusy("Пересканирую…", ScanAll);
        }

        private async void OnClean(object sender, RoutedEventArgs e)
        {
            var selected = _cleanRows.Where(r => r.IsChecked).ToList();
            if (selected.Count == 0)
            {
                StatusText.Text = "Ничего не выбрано.";
                return;
            }
            if (selected.Any(r => r.Target.Id == "recycle-bin"))
            {
                var answer = MessageBox.Show(this, "Корзина будет очищена окончательно. Продолжить?", "Hello, CleanUp.",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
            }

            await RunBusy("Убираю…", () =>
            {
                Log("— Чистка диска —");
                long total = 0;
                foreach (var row in selected)
                {
                    var r = row;
                    Status("Убираю: " + r.Title + "…");
                    OnUi(() => r.SetStatus("убираю…", "none"));
                    Log("▸ " + r.Title);
                    long freed;
                    try { freed = r.Target.Clean(Log); }
                    catch (Exception ex) { Log("  ошибка: " + ex.Message); freed = 0; }
                    if (freed > 0) total += freed;
                    OnUi(() =>
                    {
                        r.Size = 0;
                        r.SetStatus(freed < 0 ? "✓ готово" : "✓ " + FileWiper.Pretty(freed), "done");
                    });
                }
                Log("Освобождено: " + FileWiper.Pretty(total));
                Status("Готово. Освобождено " + FileWiper.Pretty(total) + ".");
            });
        }

        // ================================================================== tweaks

        private void RefreshTweakStates()
        {
            foreach (var row in _tweakRows)
            {
                var r = row;
                TweakState s;
                try { s = r.Tweak.GetState(); } catch { s = TweakState.NotApplied; }
                OnUi(() => r.SetTweakState(s));
            }
        }

        private void OnTweaksRecommended(object sender, RoutedEventArgs e)
        {
            foreach (var r in _tweakRows) r.IsChecked = r.Recommended;
        }

        private void OnTweaksNone(object sender, RoutedEventArgs e)
        {
            foreach (var r in _tweakRows) r.IsChecked = false;
        }

        private async void OnApply(object sender, RoutedEventArgs e)
        {
            var selected = _tweakRows.Where(r => r.IsChecked).ToList();
            if (selected.Count == 0)
            {
                StatusText.Text = "Ничего не выбрано.";
                return;
            }
            bool restorePoint = RestorePointCheck.IsChecked == true;

            // First show exactly what will change, and apply only after a yes.
            string preview = null;
            int changes = 0;
            await RunBusy("Смотрю, что изменится…", () =>
            {
                preview = Preview.ForTweaks(selected.Select(r => r.Tweak), out changes);
            });
            if (preview == null) return; // busy or failed — RunBusy already said why
            if (changes == 0)
            {
                StatusText.Text = "Всё выбранное уже применено — менять нечего.";
                return;
            }
            var dialog = new PreviewDialog(this, "Что изменится",
                "Изменений: " + changes + ". Перед каждым из них исходное значение сохранится в бэкап, так что всё можно будет откатить" +
                (restorePoint ? ". Сначала будет создана точка восстановления Windows." : "."),
                preview, "Применить");
            if (dialog.ShowDialog() != true)
            {
                StatusText.Text = "Отменено, ничего не изменено.";
                return;
            }

            await RunBusy("Применяю…", () =>
            {
                Log("— Применение настроек —");
                if (restorePoint)
                {
                    Status("Создаю точку восстановления (может занять минуту)…");
                    if (!RestorePoint.Create(Log))
                    {
                        bool go = false;
                        OnUi(() =>
                        {
                            go = MessageBox.Show(this,
                                "Не удалось создать точку восстановления.\nНастройки всё равно можно будет откатить из самой программы.\n\nПродолжить?",
                                "Hello, CleanUp.", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
                        });
                        if (!go) { Log("Отменено."); Status("Отменено."); return; }
                    }
                }

                foreach (var row in selected)
                {
                    Status("Применяю: " + row.Title + "…");
                    row.Tweak.Apply(_backup, Log);
                }
                RefreshTweakStates();
                Log("Готово. Часть изменений вступит в силу после перезапуска проводника или перезагрузки.");
                Status("Готово. Некоторые изменения заработают после перезагрузки.");
            });
        }

        private async void OnRevertSelected(object sender, RoutedEventArgs e)
        {
            var selected = _tweakRows.Where(r => r.IsChecked).ToList();
            if (selected.Count == 0)
            {
                StatusText.Text = "Ничего не выбрано.";
                return;
            }
            await RunBusy("Откатываю…", () =>
            {
                Log("— Откат выбранного —");
                foreach (var row in selected) row.Tweak.Revert(_backup, Log);
                RefreshTweakStates();
                Status("Откатил выбранное.");
            });
        }

        // ================================================================== log page

        private async void OnRevertAll(object sender, RoutedEventArgs e)
        {
            if (_backup.Count == 0)
            {
                StatusText.Text = "Откатывать нечего — Hello CleanUp ещё ничего не менял.";
                return;
            }
            var answer = MessageBox.Show(this, "Вернуть все настройки, которые менял Hello CleanUp, к исходным?", "Hello, CleanUp.",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            await RunBusy("Откатываю всё…", () =>
            {
                Log("— Откат всего —");
                var left = Rollback.RevertAll(_backup, Log);
                RefreshTweakStates();
                if (left == 0)
                {
                    Status("Всё возвращено как было.");
                }
                else
                {
                    Log("Не откатилось записей: " + left + ". Они остались в бэкапе — подробности выше.");
                    Status("Откатил не всё: осталось записей — " + left + ". Подробности в журнале.");
                }
            });
        }

        private void OnOpenBackupFolder(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(BackupStore.Folder);
            Process.Start("explorer.exe", "\"" + BackupStore.Folder + "\"");
        }

        private void OnCopyLog(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_log.ToString());
                StatusText.Text = "Журнал скопирован.";
            }
            catch
            {
                StatusText.Text = "Не удалось скопировать журнал.";
            }
        }
    }
}
