using System.Collections.Generic;

namespace HelloCleanUp.Core
{
    /// <summary>
    /// Telemetry &amp; ads tweaks. Everything here is a documented setting, policy, service or task,
    /// and everything is reversible through <see cref="BackupStore"/>.
    /// Security features (Defender, SmartScreen, UAC, firewall, Windows Update) are deliberately NOT touched.
    /// </summary>
    public static class TweakCatalog
    {
        private const string CDM = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        private const string ExplorerAdv = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string DataCollectionPolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection";

        public static IList<Tweak> Telemetry()
        {
            return new List<Tweak>
            {
                new Tweak("telemetry-level",
                    "Минимум диагностических данных",
                    "Политика «AllowTelemetry = 0»: Windows отправляет только обязательный минимум (на Home/Pro это уровень «Необходимые»).",
                    true,
                    RegAction.Dword(DataCollectionPolicy, "AllowTelemetry", 0),
                    RegAction.Dword(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0)),

                new Tweak("telemetry-service",
                    "Служба телеметрии",
                    "Отключает службы «Функциональные возможности для подключённых пользователей и телеметрия» (DiagTrack) и dmwappushservice.",
                    true,
                    new ServiceAction("DiagTrack"),
                    new ServiceAction("dmwappushservice")),

                new Tweak("telemetry-tasks",
                    "Задачи сбора данных в планировщике",
                    "Программа улучшения качества ПО, оценщик совместимости, сборщик данных диагностики диска и опросы обратной связи.",
                    true,
                    new TaskAction(@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator"),
                    new TaskAction(@"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip"),
                    new TaskAction(@"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser"),
                    new TaskAction(@"\Microsoft\Windows\Application Experience\ProgramDataUpdater"),
                    new TaskAction(@"\Microsoft\Windows\Autochk\Proxy"),
                    new TaskAction(@"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector"),
                    new TaskAction(@"\Microsoft\Windows\Feedback\Siuf\DmClient"),
                    new TaskAction(@"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload")),

                new Tweak("advertising-id",
                    "Рекламный идентификатор",
                    "Приложения больше не получают ваш рекламный ID, а сайты — список ваших языков.",
                    true,
                    RegAction.Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0),
                    RegAction.Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1),
                    RegAction.Dword(@"HKCU\Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1)),

                new Tweak("tailored",
                    "Персонализированные предложения",
                    "Microsoft не использует диагностические данные, чтобы подбирать вам советы и рекламу.",
                    true,
                    RegAction.Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0),
                    RegAction.Dword(@"HKCU\Software\Policies\Microsoft\Windows\CloudContent", "DisableTailoredExperiencesWithDiagnosticData", 1)),

                new Tweak("start-ads",
                    "Реклама и «рекомендации» в Пуске",
                    "Убирает предлагаемые приложения, рекламу в Пуске и тихую автоустановку «рекомендованных» игр и программ.",
                    true,
                    RegAction.Dword(CDM, "SystemPaneSuggestionsEnabled", 0),
                    RegAction.Dword(CDM, "SubscribedContent-338388Enabled", 0),
                    RegAction.Dword(CDM, "SilentInstalledAppsEnabled", 0),
                    RegAction.Dword(CDM, "PreInstalledAppsEnabled", 0),
                    RegAction.Dword(CDM, "OemPreInstalledAppsEnabled", 0),
                    RegAction.Dword(ExplorerAdv, "Start_IrisRecommendations", 0),
                    RegAction.Dword(ExplorerAdv, "Start_AccountNotifications", 0),
                    RegAction.Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1)),

                new Tweak("settings-suggestions",
                    "Подсказки в Параметрах и после обновлений",
                    "Без «Давайте завершим настройку устройства», экрана приветствия после обновлений и предложений в приложении «Параметры».",
                    true,
                    RegAction.Dword(CDM, "SubscribedContent-338393Enabled", 0),
                    RegAction.Dword(CDM, "SubscribedContent-353694Enabled", 0),
                    RegAction.Dword(CDM, "SubscribedContent-353696Enabled", 0),
                    RegAction.Dword(CDM, "SubscribedContent-310093Enabled", 0),
                    RegAction.Dword(CDM, "SoftLandingEnabled", 0),
                    RegAction.Dword(@"HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0)),

                new Tweak("lockscreen-tips",
                    "Советы и «факты» на экране блокировки",
                    "Сама картинка экрана блокировки остаётся, пропадают только рекламные подписи и советы поверх неё.",
                    true,
                    RegAction.Dword(CDM, "RotatingLockScreenOverlayEnabled", 0),
                    RegAction.Dword(CDM, "SubscribedContent-338387Enabled", 0)),

                new Tweak("explorer-ads",
                    "Реклама в проводнике",
                    "Убирает уведомления поставщиков синхронизации (реклама OneDrive и Microsoft 365) в проводнике.",
                    true,
                    RegAction.Dword(ExplorerAdv, "ShowSyncProviderNotifications", 0)),

                new Tweak("activity-history",
                    "История действий",
                    "Windows не собирает и не отправляет историю открытых приложений, файлов и сайтов.",
                    true,
                    RegAction.Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0),
                    RegAction.Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
                    RegAction.Dword(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0)),

                new Tweak("feedback",
                    "Запросы отзывов",
                    "Windows перестаёт спрашивать «Насколько вероятно, что вы порекомендуете…».",
                    true,
                    RegAction.Dword(@"HKCU\Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0),
                    RegAction.Dword(DataCollectionPolicy, "DoNotShowFeedbackNotifications", 1)),

                new Tweak("inking-typing",
                    "Сбор данных рукописного и клавиатурного ввода",
                    "Не отправлять в Microsoft то, что вы печатаете и пишете от руки, для «улучшения распознавания».",
                    true,
                    RegAction.Dword(@"HKCU\Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1),
                    RegAction.Dword(@"HKCU\Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1),
                    RegAction.Dword(@"HKCU\Software\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", 0),
                    RegAction.Dword(@"HKCU\Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", 0),
                    RegAction.Dword(@"HKCU\Software\Microsoft\Input\TIPC", "Enabled", 0)),

                new Tweak("error-reporting",
                    "Отправка отчётов об ошибках",
                    "Не отправлять отчёты о сбоях программ в Microsoft. Выключено по умолчанию: иногда эти отчёты помогают Microsoft чинить баги.",
                    false,
                    RegAction.Dword(@"HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1)),
            };
        }
    }
}
