using System.Buffers.Binary;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Shift.Application;
using Shift.Domain;
using Shift.Infrastructure;
using Xunit;
namespace Shift.Tests;

public sealed class WorkspaceDoctorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "shift-doctor-" + Guid.NewGuid());
    public WorkspaceDoctorTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, recursive: true);
    private static Workspace Space(params WorkspaceItem[] items) => new() { Name = "Doctor", Items = items };
    private static WorkspaceItem App(string name = "Editor", string target = @"C:\Doctor\editor.exe") => new() { Name = name, Kind = ItemKind.App, Target = target };
    private string FilePath(string name = "document.txt") => Path.Combine(directory, name);
    private static void Has(IEnumerable<DoctorFinding> findings, DoctorState state, string code) =>
        Assert.Contains(findings, finding => finding.State == state && finding.Code == code);

    [Theory]
    [InlineData("document.txt")]
    [InlineData("Документ 日本語.txt")]
    public async Task Existing_readable_file_is_observed_without_modification(string name)
    {
        var path = FilePath(name);
        var original = "Read-only inspection — не изменять"u8.ToArray();
        await File.WriteAllBytesAsync(path, original);
        var before = File.GetLastWriteTimeUtc(path);
        var findings = await new LocalItemInspection().InspectAsync(new() { Name = name, Kind = ItemKind.File, Target = path }, default);
        Assert.DoesNotContain(findings, f => f.State == DoctorState.Error);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Missing_file_is_reported_without_creating_it()
    {
        var path = FilePath("missing.txt");
        var findings = await new LocalItemInspection().InspectAsync(new() { Name = "Missing", Kind = ItemKind.File, Target = path }, default);
        Has(findings, DoctorState.Error, "Doctor.Missing");
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFileSystemEntries(directory));
    }

    [Theory]
    [InlineData(ItemKind.File)]
    [InlineData(ItemKind.App)]
    public async Task Directory_cannot_be_reported_as_a_file(ItemKind kind)
    {
        var findings = await new LocalItemInspection().InspectAsync(new() { Name = "Wrong object", Kind = kind, Target = directory }, default);
        Has(findings, DoctorState.Error, "Doctor.WrongObject");
    }

    [Fact]
    public async Task File_cannot_be_reported_as_a_folder()
    {
        var path = FilePath();
        await File.WriteAllTextAsync(path, "existing");
        var findings = await new LocalItemInspection().InspectAsync(new() { Name = "Wrong object", Kind = ItemKind.Folder, Target = path }, default);
        Has(findings, DoctorState.Error, "Doctor.WrongObject");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(76)]
    public async Task Corrupt_shortcut_is_an_error_without_shell_resolution(int length)
    {
        var path = FilePath("broken.lnk");
        var bytes = new byte[length];
        await File.WriteAllBytesAsync(path, bytes);
        var findings = await new LocalItemInspection().InspectAsync(App(target: path), default);
        Has(findings, DoctorState.Error, "Doctor.BrokenShortcut");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Recognized_shortcut_header_does_not_claim_its_target_is_valid()
    {
        var path = FilePath("unresolved.lnk");
        var bytes = new byte[76];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 76);
        new Guid("00021401-0000-0000-C000-000000000046").TryWriteBytes(bytes.AsSpan(4, 16));
        await File.WriteAllBytesAsync(path, bytes);
        var findings = await new LocalItemInspection().InspectAsync(App(target: path), default);
        Has(findings, DoctorState.Unknown, "Doctor.ShortcutTarget");
        Assert.DoesNotContain(findings, f => f.State == DoctorState.Ready);
    }

    public static IEnumerable<object[]> InvalidItems()
    {
        yield return [App() with { Kind = (ItemKind)99 }, "Validation.ItemType"];
        yield return [new WorkspaceItem { Name = "Unsupported shell", Kind = ItemKind.Command, Shell = (CommandShell)99, Target = "echo test" }, "Validation.ItemType"];
        yield return [App(target: @"C:\Doctor\script.cmd"), "Validation.AppType"];
        yield return [App(target: @"C:\Windows\System32\cmd.exe"), "Validation.AppType"];
        yield return [new WorkspaceItem { Name = "Bad URL", Kind = ItemKind.Url, Target = "https://" }, "Validation.Url"];
        yield return [new WorkspaceItem { Name = "Bad protocol", Kind = ItemKind.Url, Target = "javascript:alert(1)" }, "Validation.Url"];
        yield return [App(target: @"\\server\shared\tool.exe"), "Validation.LocalPath"];
        yield return [App() with { WorkingDirectory = @"C:\Doctor\..\outside" }, "Validation.WorkingDirectory"];
        yield return [App() with { Arguments = "bad\0argument" }, "Validation.Arguments"];
    }

    [Theory]
    [MemberData(nameof(InvalidItems))]
    public async Task Invalid_model_is_not_sent_to_filesystem_probe(WorkspaceItem item, string code)
    {
        var inspection = new DelegateInspection((_, _) => throw new InvalidOperationException("Invalid input must not reach inspection"));
        var report = await new WorkspaceDoctor(inspection).CheckAsync(Space(item));
        var result = Assert.Single(report.Items);
        Assert.Equal(DoctorState.Error, result.State);
        Has(result.Findings, DoctorState.Error, code);
        Assert.Equal(0, inspection.Calls);
    }

    [Fact]
    public async Task Inspection_is_ordered_and_does_not_mutate_workspace_or_launch_commands()
    {
        var marker = FilePath("must-not-exist.txt");
        var command = new WorkspaceItem { Name = "Explicit command", Kind = ItemKind.Command, Target = $"[IO.File]::WriteAllText('{marker.Replace("'", "''")}', 'unexpected')", Order = 2 };
        var workspace = Space(command, App() with { Enabled = false, Order = 0 }, new() { Name = "Docs", Kind = ItemKind.Url, Target = "https://example.invalid/данные", Order = 1 });
        var before = JsonSerializer.Serialize(workspace);
        var report = await new WorkspaceDoctor(new LocalItemInspection()).CheckAsync(workspace);
        Assert.Equal(new[] { "Editor", "Docs", "Explicit command" }, report.Items.Select(i => i.Name));
        Assert.Equal(before, JsonSerializer.Serialize(workspace));
        Has(report.Items[0].Findings, DoctorState.Unknown, "Doctor.Disabled");
        Has(report.Items[1].Findings, DoctorState.Ready, "Doctor.UrlFormat");
        Has(report.Items[2].Findings, DoctorState.Warning, "Doctor.CommandConsent");
        Assert.False(File.Exists(marker));
        Assert.Empty(Directory.GetFileSystemEntries(directory));
    }

    [Fact]
    public async Task Disabled_item_never_probes_its_target()
    {
        var inspection = new DelegateInspection((_, _) => throw new InvalidOperationException("Disabled target was touched"));
        var report = await new WorkspaceDoctor(inspection).CheckAsync(Space(App() with { Enabled = false }));
        Assert.Equal(1, report.Unknown);
        Assert.Equal(0, report.Ready);
        Assert.Equal(0, inspection.Calls);
    }

    [Fact]
    public async Task Arguments_are_not_mistaken_for_a_verified_application_contract()
    {
        var report = await new WorkspaceDoctor(new DelegateInspection((_, _) => Task.FromResult<IReadOnlyList<DoctorFinding>>([])))
            .CheckAsync(Space(App() with { Arguments = "--project \"C:\\Файлы\\проект\"" }));
        Assert.Equal(1, report.Warnings);
        Assert.Equal(0, report.Ready);
        Has(Assert.Single(report.Items).Findings, DoctorState.Warning, "Doctor.Arguments");
    }

    [Fact]
    public async Task Precancelled_inspection_performs_no_probe()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var inspection = new DelegateInspection((_, _) => throw new InvalidOperationException("Cancelled inspection was entered"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WorkspaceDoctor(inspection).CheckAsync(Space(App()), cancel.Token));
        Assert.Equal(0, inspection.Calls);
    }

    [Fact]
    public async Task Cancellation_during_probe_does_not_continue_to_following_items()
    {
        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspection = new DelegateInspection(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return [];
        });
        var task = new WorkspaceDoctor(inspection).CheckAsync(Space(App() with { Order = 0 }, App("Following") with { Order = 1 }), cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(task.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, inspection.Calls);
    }

    [Fact]
    public async Task File_deleted_after_successful_check_is_revalidated_before_dispatch()
    {
        var physical = FilePath("editor.exe");
        await File.WriteAllBytesAsync(physical, PeHeaderFixture());
        var item = App();
        var probe = new DelegateInspection((candidate, token) => new LocalItemInspection().InspectAsync(candidate with { Target = physical }, token));
        var report = await new WorkspaceDoctor(probe).CheckAsync(Space(item));
        Assert.Equal(1, report.Ready);
        File.Delete(physical);
        Assert.Throws<FileNotFoundException>(() => LaunchRequestFactory.Create(item, @"C:\Windows", _ => File.Exists(physical)));
        Assert.Equal(1, report.Ready); // An observation is historical, not a cached permission to launch.
    }

    [Fact]
    public async Task Long_unicode_local_path_is_observed_without_truncation()
    {
        var nested = Path.Combine(directory, new string('a', 95), new string('b', 95), new string('c', 95));
        Directory.CreateDirectory(nested);
        var path = Path.Combine(nested, "Проект 日本語.exe");
        var fixture = PeHeaderFixture();
        await File.WriteAllBytesAsync(path, fixture);
        Assert.True(path.Length > 260);
        var findings = await new LocalItemInspection().InspectAsync(App(target: path), default);
        Assert.Empty(findings);
        Assert.Equal(fixture, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Access_denial_becomes_an_item_error_and_following_items_are_checked()
    {
        var inspection = new DelegateInspection((item, _) => item.Order == 0
            ? throw new UnauthorizedAccessException("denied")
            : Task.FromResult<IReadOnlyList<DoctorFinding>>([]));
        var report = await new WorkspaceDoctor(inspection).CheckAsync(Space(App("Denied") with { Order = 0 }, App("Readable") with { Order = 1 }));
        Assert.Equal(1, report.Errors);
        Assert.Equal(1, report.Ready);
        Has(report.Items[0].Findings, DoctorState.Error, "Doctor.AccessDenied");
    }

    [Fact]
    public async Task Inaccessible_actual_file_is_not_reported_as_missing_or_ready()
    {
        var path = FilePath("denied.txt");
        await File.WriteAllTextAsync(path, "private test fixture");
        var info = new FileInfo(path);
        if (OperatingSystem.IsWindows())
        {
            var security = info.GetAccessControl();
            var original = security.GetSecurityDescriptorBinaryForm();
            using var account = WindowsIdentity.GetCurrent();
            security.AddAccessRule(new FileSystemAccessRule(account.User!, FileSystemRights.ReadData, AccessControlType.Deny));
            info.SetAccessControl(security);
            try
            {
                var findings = await new LocalItemInspection().InspectAsync(new() { Name = "Denied", Kind = ItemKind.File, Target = path }, default);
                Has(findings, DoctorState.Error, "Doctor.AccessDenied");
            }
            finally
            {
                var restored = new FileSecurity();
                restored.SetSecurityDescriptorBinaryForm(original);
                info.SetAccessControl(restored);
            }
        }
        else
        {
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, UnixFileMode.None);
            try
            {
                // Linux tests run as the ordinary agent account, not root.
                var findings = await new LocalItemInspection().InspectAsync(new() { Name = "Denied", Kind = ItemKind.File, Target = path }, default);
                Has(findings, DoctorState.Error, "Doctor.AccessDenied");
            }
            finally { File.SetUnixFileMode(path, mode); }
        }
    }

    [Fact]
    public async Task Missing_working_directory_is_a_separate_blocker()
    {
        var path = FilePath("editor.exe");
        await File.WriteAllTextAsync(path, "fixture");
        var findings = await new LocalItemInspection().InspectAsync(App(target: path) with { WorkingDirectory = Path.Combine(directory, "missing") }, default);
        Has(findings, DoctorState.Error, "Doctor.Missing");
        Assert.False(Directory.Exists(Path.Combine(directory, "missing")));
    }

    [Fact]
    public async Task Null_and_oversized_collections_are_rejected_before_probing()
    {
        var inspection = new DelegateInspection((_, _) => throw new InvalidOperationException("Invalid collection was probed"));
        var doctor = new WorkspaceDoctor(inspection);
        foreach (var workspace in new[]
        {
            Space() with { Items = null! },
            Space(new WorkspaceItem[] { null! }),
            Space(Enumerable.Range(0, 101).Select(i => App("App " + i)).ToArray())
        })
            await Assert.ThrowsAsync<ValidationException>(() => doctor.CheckAsync(workspace));
        Assert.Equal(0, inspection.Calls);
    }

    [Fact]
    public async Task Io_failure_is_unknown_not_a_false_ready_result()
    {
        var inspection = new DelegateInspection((_, _) => throw new IOException("Resource temporarily unavailable"));
        var report = await new WorkspaceDoctor(inspection).CheckAsync(Space(App()));
        Assert.Equal(1, report.Unknown);
        Assert.Equal(0, report.Ready);
        Has(Assert.Single(report.Items).Findings, DoctorState.Unknown, "Doctor.Unavailable");
    }

    [Fact]
    public async Task Cancellation_racing_probe_completion_cannot_return_a_ready_report()
    {
        using var cancel = new CancellationTokenSource();
        var inspection = new DelegateInspection((_, _) =>
        {
            cancel.Cancel();
            return Task.FromResult<IReadOnlyList<DoctorFinding>>([]);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WorkspaceDoctor(inspection).CheckAsync(Space(App()), cancel.Token));
        Assert.Equal(1, inspection.Calls);
    }

    [Fact]
    public async Task Errors_dominate_unknown_and_warning_and_counts_describe_items()
    {
        var inspection = new DelegateInspection((_, _) => Task.FromResult<IReadOnlyList<DoctorFinding>>([
            new(DoctorState.Warning, "Doctor.Arguments"),
            new(DoctorState.Unknown, "Doctor.ShortcutTarget"),
            new(DoctorState.Error, "Doctor.Missing")
        ]));
        var report = await new WorkspaceDoctor(inspection).CheckAsync(Space(App()));
        Assert.Equal(1, report.Errors);
        Assert.Equal(0, report.Unknown + report.Warnings + report.Ready);
        Assert.Equal(3, Assert.Single(report.Items).Findings.Count);
    }

    [Fact]
    public async Task Existing_empty_folder_is_readable_without_creating_contents()
    {
        var findings = await new LocalItemInspection().InspectAsync(new() { Name = "Folder", Kind = ItemKind.Folder, Target = directory }, default);
        Assert.Empty(findings);
        Assert.Empty(Directory.GetFileSystemEntries(directory));
    }

    // Header-only test metadata, never an executable payload or a launch fixture.
    private static byte[] PeHeaderFixture()
    {
        var bytes = new byte[256];
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3C, 4), 0x80);
        "PE\0\0"u8.CopyTo(bytes.AsSpan(0x80));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x84, 2), 0x8664); // AMD64
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x98, 2), 0x20B); // PE32+
        return bytes;
    }

    [Fact]
    public async Task Recognized_executable_metadata_is_read_without_execution_or_mutation()
    {
        var path = FilePath("metadata.exe");
        var fixture = PeHeaderFixture();
        await File.WriteAllBytesAsync(path, fixture);
        var findings = await new LocalItemInspection().InspectAsync(App(target: path), default);
        Assert.Empty(findings);
        Assert.Equal(fixture, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("plain-text")]
    [InlineData("truncated-dos-header")]
    [InlineData("invalid-pe-signature")]
    [InlineData("negative-offset")]
    [InlineData("offset-outside-file")]
    public async Task Corrupt_executable_metadata_is_reported_without_dispatch(string damage)
    {
        var fixture = PeHeaderFixture();
        switch (damage)
        {
            case "plain-text": fixture = "not an executable"u8.ToArray(); break;
            case "truncated-dos-header": fixture = fixture[..32]; break;
            case "invalid-pe-signature": fixture[0x80] = (byte)'X'; break;
            case "negative-offset": BinaryPrimitives.WriteInt32LittleEndian(fixture.AsSpan(0x3C, 4), -1); break;
            case "offset-outside-file": BinaryPrimitives.WriteInt32LittleEndian(fixture.AsSpan(0x3C, 4), fixture.Length); break;
        }
        var path = FilePath("broken.exe");
        await File.WriteAllBytesAsync(path, fixture);
        var findings = await new LocalItemInspection().InspectAsync(App(target: path), default);
        Has(findings, DoctorState.Error, "Doctor.BrokenExecutable");
        Assert.Equal(fixture, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Executable_header_beyond_probe_budget_is_unknown_without_large_read()
    {
        var fixture = PeHeaderFixture();
        const int offset = 1024 * 1024 + 1;
        BinaryPrimitives.WriteInt32LittleEndian(fixture.AsSpan(0x3C, 4), offset);
        var path = FilePath("large-header.exe");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            file.Write(fixture);
            file.SetLength(offset + 4);
            file.Position = offset;
            file.Write("PE\0\0"u8);
        }
        var findings = await new LocalItemInspection().InspectAsync(App(target: path), default);
        Has(findings, DoctorState.Unknown, "Doctor.ExecutableUnknown");
        Assert.DoesNotContain(findings, f => f.State == DoctorState.Ready);
    }

    private sealed class DelegateInspection(Func<WorkspaceItem, CancellationToken, Task<IReadOnlyList<DoctorFinding>>> inspect) : IItemInspection
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<DoctorFinding>> InspectAsync(WorkspaceItem item, CancellationToken token)
        {
            Calls++;
            return inspect(item, token);
        }
    }
}
