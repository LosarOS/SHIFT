using Shift.Application;
namespace Shift.Infrastructure;

/// <summary>Bounded local diagnostic log. Does not record command text, arguments or targets.</summary>
public sealed class FileLog(string directory) : IAppLog
{
    private readonly object gate = new();
    public string DirectoryPath { get; } = directory;
    public void Info(string message) => Write("INFO", message);
    public void Error(string message, Exception exception)
    {
        // COM/XAML failures may have an empty Message. Keep HRESULT and method frames,
        // without local variable values, command arguments or source file paths.
        var frames = new System.Diagnostics.StackTrace(exception, false).GetFrames()
            .Take(12).Select(frame => frame.GetMethod())
            .Select(method => $"{method?.DeclaringType?.FullName}.{method?.Name}");
        Write("ERROR", $"{message} [{exception.GetType().Name} 0x{exception.HResult:X8}] {exception.Message} {string.Join(" <- ", frames)}");
    }
    private void Write(string level, string message)
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var path = Path.Combine(DirectoryPath, $"shift-{DateTime.UtcNow:yyyyMMdd}.log");
                if (!File.Exists(path) || new FileInfo(path).Length < 5 * 1024 * 1024)
                    File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {level} {message.Replace('\r', ' ').Replace('\n', ' ')}{Environment.NewLine}");
                foreach (var old in Directory.EnumerateFiles(DirectoryPath, "shift-*.log").OrderByDescending(File.GetLastWriteTimeUtc).Skip(14))
                    File.Delete(old);
            }
            catch (IOException) { /* Logging failure must not crash the app. */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
