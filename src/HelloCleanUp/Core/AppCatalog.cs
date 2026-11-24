using System;
using System.Collections.Generic;
using System.Linq;

namespace HelloCleanUp.Core
{
    /// <summary>
    /// One line on the "Apps" page: a preinstalled Store app (or a small family of them) that can be removed.
    /// Only entries that are actually present on the PC are shown.
    /// </summary>
    public sealed class AppEntry
    {
        public string Id { get; private set; }
        public string Title { get; private set; }
        public string Description { get; private set; }
        public bool Recommended { get; private set; }

        /// <summary>Appx package names (the Name, not the full name). A trailing * matches a prefix.</summary>
        public IList<string> Names { get; private set; }

        public AppEntry(string id, string title, string description, bool recommended, params string[] names)
        {
            Id = id; Title = title; Description = description; Recommended = recommended; Names = names;
        }

        public bool Matches(string packageName)
        {
            return Names.Any(n => NameMatches(n, packageName));
        }

        public static bool NameMatches(string pattern, string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return false;
            return pattern.EndsWith("*", StringComparison.Ordinal)
                ? packageName.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.OrdinalIgnoreCase)
                : string.Equals(pattern, packageName, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Preinstalled apps that are safe to remove. Deliberately NOT here (and refused by AppxManager even if asked):
    /// Store, App Installer (winget), Windows Security, Calculator, Photos, Camera, Notepad, Paint, Terminal,
    /// Snipping Tool, Xbox sign-in components, image/video codecs and all runtime frameworks.
    /// </summary>
    public static class AppCatalog
    {
        public static IList<AppEntry> All()
        {
            return new List<AppEntry>
            {
                // ------------------------------------------------ recommended: ads, promos, abandoned apps
                new AppEntry("candy-crush", "Candy Crush и другие игры King",
                    "Рекламные игры, которые Windows ставит сама: Candy Crush Saga, Candy Crush Soda, Bubble Witch и похожие.",
                    true, "king.com.*"),
                new AppEntry("msn-news", "Новости MSN",
                    "Лента новостей с рекламой. В Windows 10 сюда же входят «Финансы», «Спорт» и «Путешествия» от MSN.",
                    true, "Microsoft.BingNews", "Microsoft.BingFinance", "Microsoft.BingSports", "Microsoft.BingTravel",
                    "Microsoft.BingFoodAndDrink", "Microsoft.BingHealthAndFitness"),
                new AppEntry("bing-search", "Поиск Bing",
                    "Отдельное приложение поиска Bing. Поиск в Пуске и браузер не затрагиваются.",
                    true, "Microsoft.BingSearch"),
                new AppEntry("tips", "Советы",
                    "Подсказки и напоминания о функциях Windows («Советы», «Начало работы»).",
                    true, "Microsoft.Getstarted"),
                new AppEntry("office-hub", "Microsoft 365 (витрина Office)",
                    "Приложение-витрина с предложением подписки. Установленные Word, Excel и другие программы Office не затрагиваются.",
                    true, "Microsoft.MicrosoftOfficeHub"),
                new AppEntry("solitaire", "Microsoft Solitaire Collection",
                    "Пасьянсы с рекламой и платной подпиской.",
                    true, "Microsoft.MicrosoftSolitaireCollection"),
                new AppEntry("feedback-hub", "Центр отзывов",
                    "Отправка отзывов и предложений в Microsoft.",
                    true, "Microsoft.WindowsFeedbackHub"),
                new AppEntry("maps", "Карты",
                    "Приложение «Карты» Windows. Карты в браузере и на телефоне не затрагиваются.",
                    true, "Microsoft.WindowsMaps"),
                new AppEntry("people", "Люди",
                    "Старое приложение контактов.",
                    true, "Microsoft.People"),
                new AppEntry("cortana", "Кортана",
                    "Голосовой помощник, который Microsoft больше не развивает.",
                    true, "Microsoft.549981C3F5F10"),
                new AppEntry("skype", "Skype (встроенный)",
                    "Встроенное приложение Skype — сам сервис Microsoft закрыла в 2025 году.",
                    true, "Microsoft.SkypeApp"),
                new AppEntry("teams-personal", "Чат Teams из Windows 11",
                    "Домашний чат Teams, встроенный в Windows 11. Новый Teams для работы и учёбы (пакет MSTeams) не затрагивается.",
                    true, "MicrosoftTeams"),
                new AppEntry("clipchamp", "Clipchamp",
                    "Видеоредактор с платными функциями.",
                    true, "Clipchamp.Clipchamp"),
                new AppEntry("power-automate", "Power Automate",
                    "Автоматизация действий в Windows для офисных сценариев.",
                    true, "Microsoft.PowerAutomateDesktop"),
                new AppEntry("dev-home", "Dev Home",
                    "Панель для разработчиков.",
                    true, "Microsoft.Windows.DevHome"),
                new AppEntry("mixed-reality", "Портал смешанной реальности",
                    "Нужен только для VR-шлемов Windows Mixed Reality.",
                    true, "Microsoft.MixedReality.Portal"),
                new AppEntry("3d", "Средство 3D-просмотра и Print 3D",
                    "Просмотр 3D-моделей и подготовка к 3D-печати.",
                    true, "Microsoft.Microsoft3DViewer", "Microsoft.Print3D"),
                new AppEntry("win10-legacy", "Старые приложения Windows 10",
                    "«Сообщения», «Тарифные планы» и «Кошелёк» — давно не работают.",
                    true, "Microsoft.Messaging", "Microsoft.OneConnect", "Microsoft.Wallet"),

                // ------------------------------------------------ optional: useful to some people
                new AppEntry("weather", "Погода (MSN)",
                    "Погода от MSN с рекламой. Виджет погоды на панели задач — отдельная настройка на странице «Телеметрия и реклама».",
                    false, "Microsoft.BingWeather"),
                new AppEntry("get-help", "Техническая поддержка",
                    "Через неё работают встроенные средства устранения неполадок Windows 11 — лучше оставить.",
                    false, "Microsoft.GetHelp"),
                new AppEntry("paint3d", "Paint 3D",
                    "Обычный Paint не затрагивается.",
                    false, "Microsoft.MSPaint"),
                new AppEntry("onenote-uwp", "OneNote для Windows 10",
                    "Старая версия OneNote. OneNote из Office не затрагивается.",
                    false, "Microsoft.Office.OneNote"),
                new AppEntry("mail-calendar", "Почта и Календарь",
                    "Старые «Почта» и «Календарь» Windows.",
                    false, "microsoft.windowscommunicationsapps"),
                new AppEntry("outlook-new", "Новый Outlook",
                    "Новое приложение Outlook. Outlook из Office не затрагивается.",
                    false, "Microsoft.OutlookForWindows"),
                new AppEntry("todo", "Microsoft To Do",
                    "Списки дел, синхронизируются с учётной записью Microsoft.",
                    false, "Microsoft.Todos"),
                new AppEntry("sticky-notes", "Записки",
                    "Заметки-стикеры на рабочем столе.",
                    false, "Microsoft.MicrosoftStickyNotes"),
                new AppEntry("phone-link", "Связь с телефоном",
                    "Уведомления, звонки и фото с телефона на компьютере.",
                    false, "Microsoft.YourPhone"),
                new AppEntry("films-tv", "Кино и ТВ",
                    "Старый видеоплеер. Видео будет открываться другим плеером.",
                    false, "Microsoft.ZuneVideo"),
                new AppEntry("media-player", "Медиаплеер / Музыка Groove",
                    "В Windows 11 это новый «Медиаплеер»: без него музыка и видео будут открываться другими программами.",
                    false, "Microsoft.ZuneMusic"),
                new AppEntry("sound-recorder", "Звукозапись",
                    "Запись звука с микрофона.",
                    false, "Microsoft.WindowsSoundRecorder"),
                new AppEntry("alarms", "Часы и будильник",
                    "Будильники, таймер, секундомер, фокусировка.",
                    false, "Microsoft.WindowsAlarms"),
                new AppEntry("xbox", "Приложения Xbox и Game Bar",
                    "Приложение Xbox и Xbox Game Bar (Win+G: запись экрана, счётчик FPS). Не удаляй, если играешь через Game Pass. " +
                    "Служебные компоненты входа Xbox не трогаются.",
                    false, "Microsoft.XboxApp", "Microsoft.GamingApp", "Microsoft.XboxGamingOverlay", "Microsoft.XboxSpeechToTextOverlay"),
                new AppEntry("copilot-app", "Приложение Copilot",
                    "ИИ-помощник Microsoft. Отключить кнопку и политику можно на странице «Телеметрия и реклама».",
                    false, "Microsoft.Copilot"),
                new AppEntry("family", "Microsoft Family",
                    "Родительский контроль. Не удаляй, если на компьютере есть детская учётная запись.",
                    false, "MicrosoftCorporationII.MicrosoftFamily"),
                new AppEntry("quick-assist", "Быстрая помощь",
                    "Удалённое подключение для помощи от знакомого или поддержки.",
                    false, "MicrosoftCorporationII.QuickAssist"),

                // ------------------------------------------------ third-party promos (only listed when present)
                new AppEntry("disney", "Disney+",
                    "Windows иногда ставит его сама как рекламу. Если пользуешься — не отмечай.",
                    false, "Disney.37853FC22B2CE"),
                new AppEntry("spotify", "Spotify",
                    "Windows иногда ставит его сама как рекламу. Если пользуешься — не отмечай.",
                    false, "SpotifyAB.SpotifyMusic"),
                new AppEntry("tiktok", "TikTok",
                    "Windows иногда ставит его сама как рекламу. Если пользуешься — не отмечай.",
                    false, "BytedancePte.Ltd.TikTok"),
                new AppEntry("facebook", "Facebook и Instagram",
                    "Windows иногда ставит их сама как рекламу. Если пользуешься — не отмечай.",
                    false, "Facebook.Facebook", "Facebook.InstagramBeta"),
                new AppEntry("prime-video", "Prime Video",
                    "Windows иногда ставит его сама как рекламу. Если пользуешься — не отмечай.",
                    false, "AmazonVideo.PrimeVideo"),
            };
        }
    }
}
