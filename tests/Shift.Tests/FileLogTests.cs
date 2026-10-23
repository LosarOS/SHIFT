using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Shift.Infrastructure;
using Xunit;
namespace Shift.Tests;

public sealed class FileLogTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "shift-log-" + Guid.NewGuid());
    public FileLogTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, recursive: true);
    private string CurrentLog => Path.Combine(directory, $"shift-{DateTime.UtcNow:yyyyMMdd}.log");

    [Fact]
    public void Empty_exception_message_still_records_hresult_and_failing_methods()
    {
        const string argument = "this-sensitive-argument-must-not-appear";
        var log = new FileLog(directory);
        try { ThrowComFailure(argument); }
        catch (COMException exception) { log.Error("Native startup failed", exception); }
        var entry = File.ReadAllText(CurrentLog);
        Assert.Contains("ERROR Native startup failed", entry);
        Assert.Contains("COMException 0x80070490", entry);
        Assert.Contains("Shift.Tests.FileLogTests.ThrowComFailure", entry);
        Assert.DoesNotContain(argument, entry);
        Assert.DoesNotContain("FileLogTests.cs", entry);
        Assert.DoesNotContain(directory, entry);
        Assert.DoesNotContain(" in ", entry);
        Assert.Single(File.ReadAllLines(CurrentLog));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowComFailure(string privateArgument)
    {
        GC.KeepAlive(privateArgument);
        throw new COMException("", unchecked((int)0x80070490));
    }

    [Fact]
    public void Error_with_no_thrown_stack_is_logged_without_failing()
    {
        new FileLog(directory).Error("Not yet thrown", new COMException("", unchecked((int)0x80004005)));
        Assert.Contains("0x80004005", File.ReadAllText(CurrentLog));
    }

    [Fact]
    public void Blocked_log_directory_does_not_escape_to_application()
    {
        var obstruction = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(obstruction, "existing file must remain");
        var log = new FileLog(obstruction);
        log.Info("An informational event");
        log.Error("An error event", new InvalidOperationException("detail"));
        Assert.Equal("existing file must remain", File.ReadAllText(obstruction));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void Rotation_keeps_fourteen_recent_matching_logs_and_unrelated_files()
    {
        var baseline = DateTime.UtcNow.AddDays(-30);
        for (var index = 0; index < 20; index++)
        {
            var path = Path.Combine(directory, $"shift-fixture-{index:00}.log");
            File.WriteAllText(path, index.ToString());
            File.SetLastWriteTimeUtc(path, baseline.AddDays(index));
        }
        var unrelated = Path.Combine(directory, "user-notes.txt");
        File.WriteAllText(unrelated, "retain");
        new FileLog(directory).Info("Fresh event");
        var logs = Directory.GetFiles(directory, "shift-*.log");
        Assert.Equal(14, logs.Length);
        Assert.Contains(CurrentLog, logs);
        Assert.All(Enumerable.Range(0, 7), index => Assert.False(File.Exists(Path.Combine(directory, $"shift-fixture-{index:00}.log"))));
        Assert.All(Enumerable.Range(7, 13), index => Assert.True(File.Exists(Path.Combine(directory, $"shift-fixture-{index:00}.log"))));
        Assert.Equal("retain", File.ReadAllText(unrelated));
    }

    [Fact]
    public void Full_daily_log_does_not_grow_after_size_limit()
    {
        const int limit = 5 * 1024 * 1024;
        using (var file = new FileStream(CurrentLog, FileMode.CreateNew, FileAccess.Write))
            file.SetLength(limit);
        new FileLog(directory).Info("This record should not grow the full daily file");
        Assert.Equal(limit, new FileInfo(CurrentLog).Length);
    }

    [Fact]
    public void Multiline_error_is_one_physical_log_record()
    {
        new FileLog(directory).Error("Failed\ncomponent", new IOException("line one\r\nline two"));
        var entry = Assert.Single(File.ReadAllLines(CurrentLog));
        Assert.Contains("Failed component", entry);
        Assert.Contains("line one  line two", entry);
    }
}
