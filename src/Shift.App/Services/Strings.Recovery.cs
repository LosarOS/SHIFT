namespace Shift.App.Services;

public sealed partial class Strings
{
    public string Recovery(string key) => RecoveryValues.TryGetValue(key, out var value) ? (Language == "ru" ? value.Ru : value.En) : key;
    private static readonly Dictionary<string, (string Ru, string En)> RecoveryValues = new()
    {
        ["CancelPending"] = ("Отмена запрошена. Дождитесь завершения текущего снимка, затем закройте окно.", "Cancellation requested. Wait for the current snapshot to finish, then close this window."),
        ["Center"] = ("Центр восстановления", "Recovery Center"),
        ["Scope"] = ("Копия SQLite содержит пространства, историю, настройки и конфигурацию Studio. Файлы обоев и локальные recovery-черновики заметок в неё НЕ входят. Восстановление заменяет базу, но не применяет оформление и не запускает приложения.", "A SQLite backup contains workspaces, history, settings and Studio configuration. Wallpaper files and local Notes recovery drafts are NOT included. Restore replaces the database without applying profiles or launching apps."),
        ["FullCopy"] = ("Для полной копии: выйдите из SHIFT через «Выход», затем скопируйте весь каталог данных, включая Studio/Images, Studio/RecoveryNotes и migration-backups. Не копируйте только shift.db из работающего приложения: часть данных может находиться в WAL.", "For a complete copy: Quit SHIFT, then copy the entire data directory, including Studio/Images, Studio/RecoveryNotes and migration-backups. Do not copy only shift.db while SHIFT is running: committed data may still be in WAL."),
        ["Choose"] = ("Выберите копию SQLite", "Choose a SQLite backup"),
        ["Inspect"] = ("Проверить и просмотреть", "Validate and preview"),
        ["Checking"] = ("Проверяется отдельный снимок. Рабочая база не изменяется…", "Validating a private snapshot. The live database is unchanged…"),
        ["Summary"] = ("Копия прошла проверку: схема {0} · Пространства: {1} · Запуски: {2} · Профили Studio: {3} · Определения виджетов: {4}. Файлы Studio не включены. Перед заменой будет выполнена повторная проверка и создана safety-копия.", "Backup validation passed: schema {0} · Workspaces: {1} · Runs: {2} · Studio profiles: {3} · Widget definitions: {4}. Studio files are not included. Restore revalidates the file and creates a safety backup before replacement."),
        ["Empty"] = ("В этой папке копий не найдено. Создайте копию или выберите .db через «Обзор».", "No backups found here. Create one or use Browse to choose a .db file."),
        ["Limited"] = ("Показаны первые 100 найденных копий, отсортированные по времени. Для других файлов используйте «Обзор».", "Showing the first 100 discovered backups sorted by time. Use Browse for other files."),
        ["Unavailable"] = ("Некоторые каталоги недоступны или являются ссылками. Их содержимое не просматривалось.", "Some directories are inaccessible or symbolic links. Their contents were not inspected."),
        ["NotChecked"] = ("Выберите файл и нажмите «Проверить и просмотреть». Проверка не меняет базу.", "Choose a file and select Validate and preview. Validation does not replace your database."),
        ["Failed"] = ("Копия не подтверждена. Восстановление недоступно. ", "Backup could not be validated. Restore is unavailable. "),
        ["Browse"] = ("Выбрать другой файл…", "Choose another file…")
    };
}
