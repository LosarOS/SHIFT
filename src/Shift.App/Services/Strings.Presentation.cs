using System.Globalization;
using Shift.Domain;
namespace Shift.App.Services;

// Display-only localization: persisted identifiers, user titles, paths and diagnostics stay unchanged.
public sealed partial class Strings
{
    public CultureInfo Culture => CultureInfo.GetCultureInfo(Language == "ru" ? "ru-RU" : "en-US");
    public static IReadOnlyCollection<string> Keys => Values.Keys;
    public string FormatDate(DateTimeOffset? value) => value?.ToLocalTime().ToString("g", Culture) ?? "—";
    public string TemplateName(string id) => id switch
    {
        "Minimal" => Language == "ru" ? "Минимализм" : "Minimal",
        "Glass" => Language == "ru" ? "Стекло · полупрозрачность" : "Glass · translucent",
        "Neon" => Language == "ru" ? "Неон" : "Neon",
        "Midnight" => this["Midnight"], "Light" => this["Light"], "Forest" => this["Forest"], _ => id
    };
    public string WidgetKindName(WidgetKind kind) => kind switch
    {
        WidgetKind.Clock => Language == "ru" ? "Часы" : "Clock",
        WidgetKind.SystemMonitor => Language == "ru" ? "Системный монитор" : "System monitor",
        WidgetKind.FocusTimer => Language == "ru" ? "Фокус-таймер" : "Focus timer",
        _ => Language == "ru" ? "Заметки" : "Notes"
    };
    private string DescribeError(Exception error)
    {
        var known = LocalizeMessage(error.Message);
        if (Language != "ru" || known != error.Message) return known;
        var summary = error switch
        {
            OperationCanceledException => "Действие отменено.",
            UnauthorizedAccessException => "Нет доступа. Проверьте разрешения файла или папки.",
            FileNotFoundException or DirectoryNotFoundException => "Файл или папка не найдены. Проверьте путь и подключение диска.",
            System.Text.Json.JsonException or InvalidDataException => "Данные повреждены или имеют неподдерживаемый формат.",
            IOException => "Не удалось прочитать или сохранить данные. Проверьте диск и доступ к файлу.",
            _ => "Действие не выполнено. Проверьте настройки и повторите попытку."
        };
        return summary + (string.IsNullOrWhiteSpace(error.Message) ? "" : "\nТехнические подробности: " + error.Message);
    }
    public string LocalizeMessage(string message)
    {
        if (Language != "ru" || string.IsNullOrEmpty(message)) return message;
        foreach (var (english, russian) in OperationalMessages)
        {
            if (message == english) return russian;
            if (english.EndsWith(": ", StringComparison.Ordinal) && message.StartsWith(english, StringComparison.Ordinal))
                return russian + message[english.Length..];
        }
        return message;
    }
    private static readonly (string English, string Russian)[] OperationalMessages =
    [
        ("A workspace launch is already in progress.", "Пространство уже запускается. Дождитесь завершения очереди."),
        ("Command execution was declined.", "Выполнение команд отклонено пользователем."),
        ("Launch cancelled before dispatch.", "Запуск отменён до отправки запроса Windows."),
        ("Launch interrupted. Check the local log.", "Запуск прерван. Подробности сохранены в локальном журнале."),
        ("This database belongs to a newer SHIFT version. Update SHIFT before opening it.", "Эта база создана более новой версией SHIFT. Обновите приложение перед открытием."),
        ("Studio data changed. Reload it before saving; your newer data was not overwritten.", "Данные Studio изменились. Обновите профили и откройте редактор заново. Более новые данные не перезаписаны."),
        ("Edited widget is missing from the target profile.", "Редактируемый виджет отсутствует в выбранном профиле."),
        ("Studio preset was not saved. Existing data was not overwritten.", "Пресет не сохранён. Существующие данные не перезаписаны."),
        ("Studio preset bundle is invalid.", "Данные пресета повреждены или имеют неподдерживаемый формат."),
        ("Another dialog is already open.", "Сначала закройте открытое диалоговое окно."),
        ("Studio is disabled.", "Studio выключен. Включите его в разделе «Студия»."),
        ("Resolve the unfinished Studio operation before applying another profile.", "Сначала завершите или восстановите незавершённое изменение оформления в разделе «Восстановление»."),
        ("Desktop profile was not found.", "Визуальный профиль не найден. Обновите список профилей."),
        ("This desktop profile is disabled.", "Этот визуальный профиль отключён."),
        ("A widget definition is missing.", "Определение виджета не найдено. Проверьте состав профиля."),
        ("Changing Windows wallpaper requires current explicit static-image confirmation.", "Перед изменением обоев подтвердите, что Windows использует статичное изображение."),
        ("The current wallpaper configuration cannot be restored safely.", "Текущее оформление нельзя безопасно восстановить. Используйте статичные локальные обои."),
        ("A selected display is no longer available.", "Выбранный монитор больше недоступен. Настройте расположение заново."),
        ("Studio was disabled while the profile was applying.", "Studio был выключен во время применения профиля. Проверьте результат в журнале восстановления."),
        ("Recovery record was not found.", "Запись восстановления не найдена."),
        ("This operation was explicitly acknowledged as kept.", "Для этой операции уже выбрано «Оставить текущее оформление»."),
        ("Windows wallpaper state cannot be compared safely.", "Нельзя безопасно сравнить текущее оформление Windows. Проверьте настройки обоев."),
        ("Display unavailable or recovery snapshot incomplete: ", "Монитор недоступен или снимок восстановления неполон: "),
        ("Wallpaper changed after SHIFT: ", "Обои изменены после применения SHIFT: "),
        ("Wallpaper placement changed after SHIFT.", "Режим размещения обоев изменён после SHIFT."),
        ("A newer Studio widget layout is active.", "Сейчас применяется более новое расположение виджетов Studio."),
        ("Restore in progress; verify current desktop before retrying.", "Восстановление не завершено. Проверьте рабочий стол перед повторной попыткой."),
        ("Windows wallpaper state changed during restore: ", "Оформление Windows изменилось во время восстановления: "),
        ("Display unavailable: ", "Монитор недоступен: "),
        ("Wallpaper changed during restore: ", "Обои изменились во время восстановления: "),
        ("Original wallpaper and managed recovery copy are unavailable: ", "Исходные обои и сохранённая копия недоступны: "),
        ("Windows wallpaper state changed during placement restore.", "Оформление Windows изменилось во время восстановления режима размещения."),
        ("Wallpaper placement changed during restore.", "Режим размещения обоев изменился во время восстановления."),
        ("Studio cannot safely restore slideshow, disabled, or remote-session wallpaper state.", "Нельзя безопасно восстановить слайд-шоу, отключённые обои или оформление удалённого сеанса. Выберите статичное локальное изображение."),
        ("Restoring an empty/solid-color wallpaper is not guaranteed by IDesktopWallpaper.SetWallpaper.", "Возврат к сплошному цвету без изображения не поддерживается. Выберите статичное изображение до применения профиля."),
        ("An original wallpaper file is empty.", "Файл исходных обоев пуст."),
        ("An original wallpaper is missing or not an accessible local regular file.", "Исходные обои отсутствуют или недоступны как обычный локальный файл."),
        ("Only static image values and position are captured. Windows Spotlight/provider configuration is not detectable or restorable through this API.", "Сохраняются только статичное изображение и режим размещения. Конфигурация Windows Spotlight не определяется и не восстанавливается."),
        ("The selected display is no longer connected.", "Выбранный монитор отключён."),
        ("Network, device and URL image paths are not supported.", "Для обоев не поддерживаются сетевые пути, устройства и URL. Выберите локальное изображение."),
        ("Choose an absolute local image path.", "Выберите полный путь к локальному изображению."),
        ("Only local PNG, JPEG and BMP images are supported.", "Поддерживаются локальные изображения PNG, JPEG и BMP."),
        ("Only decoded PNG, JPEG and BMP images are supported.", "Файл должен декодироваться как PNG, JPEG или BMP."),
        ("Linked and offline image paths are not supported.", "Ссылки и файлы на отключённых дисках для обоев не поддерживаются."),
        ("Image must be between 1 byte and 32 MiB.", "Размер изображения должен быть от 1 байта до 32 МиБ."),
        ("Image exceeds 32 MiB.", "Изображение превышает лимит 32 МиБ."),
        ("Managed image content differs from its content-addressed name.", "Содержимое сохранённого изображения изменилось. Выберите файл заново."),
        ("Decoded image exceeds the 32-million-pixel limit or has invalid dimensions.", "Изображение повреждено или превышает лимит 32 миллиона пикселей."),
        ("Image path changed or resolves through a link.", "Путь к изображению изменился или проходит через ссылку. Выберите обычный локальный файл."),
        ("Managed image storage must be on a local drive.", "Папка изображений Studio должна находиться на локальном диске."),
        ("Managed images cannot be stored in linked or offline directories.", "Изображения Studio нельзя сохранять в связанных или недоступных папках."),
        ("Network drives are not supported.", "Сетевые диски для обоев не поддерживаются."),
        ("Alternate data streams are not supported.", "Альтернативные потоки данных не поддерживаются."),
        ("An image must be a regular file.", "Изображение должно быть обычным файлом."),
        ("Studio data exceeds its size limit.", "Данные Studio превышают допустимый размер."),
        ("Studio data is empty.", "Данные Studio пусты."),
        ("Studio data format is invalid.", "Формат данных Studio повреждён или не поддерживается."),
        ("Widget date format is invalid.", "Некорректный формат даты виджета."),
        ("Studio model violates the supported declaration schema.", "Настройки Studio не соответствуют поддерживаемому формату."),
    ];
}
