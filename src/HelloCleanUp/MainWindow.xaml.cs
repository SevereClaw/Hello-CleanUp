using System;
using System.Collections.Generic;
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
        private readonly ObservableCollection<RowViewModel> _appRows = new ObservableCollection<RowViewModel>();
        private readonly IList<AppEntry> _appCatalog = AppCatalog.All();
        private AppxInventory _apps;
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
            AppList.ItemsSource = _appRows;

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
                    LoadApps(AppChecks.Recommended);
                    Status("Готово. Отметь, что убрать, и нажми «Очистить».");
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

        private UpdateCheck.Release _update;

        private async void CheckForUpdateAsync()
        {
            var current = UpdateCheck.Normalize(Assembly.GetExecutingAssembly().GetName().Version);
            string error = null;
            var release = await Task.Run(() => UpdateCheck.Newer(current, out error));
            if (release == null) return; // up to date, or offline — not worth a word
            _update = release;
            UpdateRun.Text = "Обновить до " + release.Version + " →";
            UpdateText.Visibility = Visibility.Visible;
            Log("Вышла новая версия " + release.Version + ": " + UpdateCheck.ReleasesPage);

            // Don't interrupt the first scan or anything else that is running.
            while (_busy) await Task.Delay(300);
            if (_update == null) return;
            var answer = MessageBox.Show(this,
                "Вышла новая версия Hello, CleanUp. — " + release.Version + " (у тебя " + current + ").\n\n" +
                "Обновить сейчас? Программа скачает её с GitHub, проверит контрольную сумму SHA-256 и перезапустится. " +
                "Бэкап и возможность отката сохранятся.\n\nЧто нового: " + UpdateCheck.ReleasesPage,
                "Hello, CleanUp.", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes) await InstallUpdate();
        }

        private async void OnOpenReleases(object sender, RoutedEventArgs e)
        {
            if (_update != null) await InstallUpdate();
        }

        private async Task InstallUpdate()
        {
            var release = _update;
            if (release == null || _busy) return;
            bool installed = false;
            await RunBusy("Скачиваю версию " + release.Version + "…", () =>
            {
                Updater.Install(release, Log);
                installed = true;
            });
            if (!installed)
            {
                MessageBox.Show(this,
                    "Не удалось обновиться — подробности в журнале. Текущая версия не тронута.\n\nМожно скачать вручную: " + UpdateCheck.ReleasesPage,
                    "Hello, CleanUp.", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _update = null;
            try
            {
                Updater.Restart();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Новая версия установлена, но не запустилась (" + ex.Message + "). Запусти программу заново.",
                    "Hello, CleanUp.", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Application.Current.Shutdown();
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
            PageApps.Visibility = NavApps.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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
            ReloadAppsButton.IsEnabled = on;
            RemoveAppsButton.IsEnabled = on;
            RevertAppsButton.IsEnabled = on;
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

        // ================================================================== preinstalled apps

        private enum AppChecks { Recommended, Keep, Clear }

        /// <summary>Reads the installed apps (slow: PowerShell) and rebuilds the list. Call from background work.</summary>
        private void LoadApps(AppChecks checks)
        {
            Status("Смотрю установленные приложения…");
            AppxInventory inv;
            try { inv = AppxManager.Load(); }
            catch (Exception ex) { inv = new AppxInventory(new List<AppxPackage>(), ex.Message); }
            if (inv.Error != null) Log("Приложения: " + inv.Error);
            var removed = AppxManager.RemovedNames(_backup);
            OnUi(() => BuildAppRows(inv, removed, checks));
        }

        private void BuildAppRows(AppxInventory inv, IList<string> removed, AppChecks checks)
        {
            var wasChecked = new HashSet<string>(_appRows.Where(r => r.IsChecked).Select(r => r.App.Id));
            _apps = inv;
            _appRows.Clear();
            foreach (var e in _appCatalog)
            {
                var found = inv.Find(e).Where(p => !AppxManager.IsProtected(p.Name) && !p.NonRemovable).ToList();
                var removedHere = removed.Where(n => e.Matches(n)).ToList();
                bool installed = found.Any(p => p.ForUser);
                bool provisioned = found.Any(p => p.Provisioned);
                if (!installed && !provisioned && removedHere.Count == 0) continue;

                var description = e.Description;
                if (e.Names.Count > 1 || e.Names.Any(n => n.EndsWith("*", StringComparison.Ordinal)))
                {
                    var names = found.Select(p => p.Name).Concat(removedHere).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (names.Count > 0) description += " Найдено: " + string.Join(", ", names) + ".";
                }

                bool check;
                switch (checks)
                {
                    case AppChecks.Recommended: check = e.Recommended && installed; break;
                    case AppChecks.Keep: check = wasChecked.Contains(e.Id); break;
                    default: check = false; break;
                }
                var row = new RowViewModel(e, description, check) { AppInstalled = installed };
                if (installed) row.SetStatus("установлено", "off");
                else if (removedHere.Count > 0) row.SetStatus("удалено", "applied");
                else row.SetStatus("у новых учёток", "partial");
                _appRows.Add(row);
            }

            AppsEmptyText.Text = inv.Error != null && inv.Packages.Count == 0
                ? "Не удалось получить список приложений: " + inv.Error
                : "Ничего лишнего не нашлось — встроенных приложений из списка на этом компьютере нет.";
            AppsEmptyText.Visibility = _appRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnAppsRecommended(object sender, RoutedEventArgs e)
        {
            foreach (var r in _appRows) r.IsChecked = r.Recommended && r.AppInstalled;
        }

        private void OnAppsNone(object sender, RoutedEventArgs e)
        {
            foreach (var r in _appRows) r.IsChecked = false;
        }

        private async void OnReloadApps(object sender, RoutedEventArgs e)
        {
            await RunBusy("Смотрю установленные приложения…", () =>
            {
                LoadApps(AppChecks.Keep);
                Status("Список приложений обновлён.");
            });
        }

        private async void OnRemoveApps(object sender, RoutedEventArgs e)
        {
            if (_apps == null)
            {
                StatusText.Text = "Список приложений ещё загружается.";
                return;
            }
            var selected = _appRows.Where(r => r.IsChecked).Select(r => r.App).ToList();
            if (selected.Count == 0)
            {
                StatusText.Text = "Ничего не выбрано.";
                return;
            }
            bool deprovision = DeprovisionCheck.IsChecked == true;

            int changes;
            var preview = AppxManager.Preview(selected, _apps, deprovision, out changes);
            if (changes == 0)
            {
                StatusText.Text = "Выбранных приложений уже нет — удалять нечего.";
                return;
            }
            var dialog = new PreviewDialog(this, "Что будет удалено",
                "Изменений: " + changes + ". Каждое удалённое приложение записывается в бэкап — вернуть его можно кнопкой «Вернуть выбранное» или «Откатить всё»." +
                (deprovision ? " Отметку «не ставить новым пользователям» откатить нельзя: новым учётным записям эти приложения можно будет поставить только из Microsoft Store." : ""),
                preview, "Удалить");
            if (dialog.ShowDialog() != true)
            {
                StatusText.Text = "Отменено, ничего не удалено.";
                return;
            }

            var inv = _apps;
            await RunBusy("Удаляю приложения…", () =>
            {
                Log("— Удаление встроенных приложений —");
                Log("▸ " + string.Join(", ", selected.Select(a => a.Title)));
                var errors = AppxManager.Remove(selected, inv, deprovision, _backup, Log);
                LoadApps(AppChecks.Clear);
                Log(errors == 0 ? "Готово." : "Готово, но не всё: ошибок — " + errors + ".");
                Status(errors == 0
                    ? "Готово. Приложения удалены — вернуть их можно здесь же."
                    : "Удалил не всё: ошибок — " + errors + ". Подробности в журнале.");
            });
        }

        private async void OnRevertApps(object sender, RoutedEventArgs e)
        {
            var selected = _appRows.Where(r => r.IsChecked).Select(r => r.App).ToList();
            if (selected.Count == 0)
            {
                StatusText.Text = "Ничего не выбрано. Отметь приложения со статусом «удалено».";
                return;
            }
            var keys = AppxManager.KeysFor(_backup, selected);
            if (keys.Count == 0)
            {
                StatusText.Text = "Среди выбранного нет приложений, удалённых через Hello CleanUp.";
                return;
            }

            var storeLinks = new List<StoreLink>();
            await RunBusy("Возвращаю приложения…", () =>
            {
                Log("— Возврат приложений —");
                var left = AppxManager.Restore(_backup, keys, Log, storeLinks);
                LoadApps(AppChecks.Clear);
                Status(left == 0 ? "Готово." : "Вернул не всё — подробности в журнале.");
            });
            OfferStore(storeLinks);
        }

        /// <summary>Apps whose files Windows already deleted can only come back from the Store: offer to open their pages.</summary>
        private void OfferStore(IList<StoreLink> links)
        {
            if (links.Count == 0) return;
            var text = "Этих приложений в Windows уже нет — вернуть их можно только из Microsoft Store:\n\n" +
                       string.Join("\n", links.Select(l => "• " + l.Name)) +
                       "\n\nОткрыть их страницы в Microsoft Store? Ссылки есть и в журнале.";
            if (MessageBox.Show(this, text, "Hello, CleanUp.", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            foreach (var l in links.Take(10))
            {
                try { Process.Start(new ProcessStartInfo(l.Uri) { UseShellExecute = true }); }
                catch (Exception ex) { Log("Не удалось открыть Microsoft Store: " + ex.Message); }
            }
        }

        // ================================================================== log page

        private async void OnRevertAll(object sender, RoutedEventArgs e)
        {
            if (_backup.Count == 0)
            {
                StatusText.Text = "Откатывать нечего — Hello CleanUp ещё ничего не менял.";
                return;
            }
            var answer = MessageBox.Show(this, "Вернуть все настройки и приложения, которые менял Hello CleanUp, как было?", "Hello, CleanUp.",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            var storeLinks = new List<StoreLink>();
            await RunBusy("Откатываю всё…", () =>
            {
                Log("— Откат всего —");
                var left = Rollback.RevertAll(_backup, Log, storeLinks);
                RefreshTweakStates();
                LoadApps(AppChecks.Clear);
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
            OfferStore(storeLinks);
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
