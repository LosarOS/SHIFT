namespace Shift.App.Services;
public sealed class AppPaths
{
    public bool Portable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));
    public string DataDirectory
    {
        get;
    }
    public string LogDirectory => Path.Combine(DataDirectory, "logs");
    public string BackupDirectory => Path.Combine(DataDirectory, "backups");
    public string DatabasePath => Path.Combine(DataDirectory, "shift.db");
    public AppPaths() => DataDirectory = Portable ? Path.Combine(AppContext.BaseDirectory, "data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SHIFT");
}
