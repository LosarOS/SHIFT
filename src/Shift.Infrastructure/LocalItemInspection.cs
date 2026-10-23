using System.Runtime.InteropServices;
using System.Text;
using Shift.Application;
using Shift.Domain;
namespace Shift.Infrastructure;

/// <summary>Metadata/access probes only. No ShellExecute, COM shortcut resolution or network requests.</summary>
public sealed class LocalItemInspection : IItemInspection
{
    public Task<IReadOnlyList<DoctorFinding>> InspectAsync(WorkspaceItem item, CancellationToken token) =>
        Task.Run<IReadOnlyList<DoctorFinding>>(() => Inspect(item, token), token);

    private static IReadOnlyList<DoctorFinding> Inspect(WorkspaceItem item, CancellationToken token)
    {
        var findings = new List<DoctorFinding>();
        void Probe(string path, bool folder, bool shortcut = false)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                path = Environment.ExpandEnvironmentVariables(path);
                if (OperatingSystem.IsWindows() && new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network)
                { findings.Add(new(DoctorState.Unknown, "Doctor.NetworkDrive")); return; }
                // Stop at an ancestor junction/symlink before touching its descendants.
                // This also avoids hydrating cloud placeholders during a local-only scan.
                var parents = new Stack<string>();
                for (var parent = Path.GetDirectoryName(path); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                    parents.Push(parent);
                foreach (var parent in parents)
                {
                    token.ThrowIfCancellationRequested();
                    if ((File.GetAttributes(parent) & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
                    { findings.Add(new(DoctorState.Unknown, "Doctor.LinkedOrOffline")); return; }
                }
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
                { findings.Add(new(DoctorState.Unknown, "Doctor.LinkedOrOffline")); return; }
                if (folder != attributes.HasFlag(FileAttributes.Directory))
                { findings.Add(new(DoctorState.Error, "Doctor.WrongObject")); return; }
                if (folder)
                {
                    using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
                    _ = entries.MoveNext(); // Read access, no directory creation or modification.
                }
                else
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (shortcut)
                    {
                        Span<byte> header = stackalloc byte[76];
                        var count = stream.ReadAtLeast(header, 76, throwOnEndOfStream: false);
                        if (count != 76 || !IsShellLinkHeader(header))
                            findings.Add(new(DoctorState.Error, "Doctor.BrokenShortcut"));
                        else
                            findings.Add(new(DoctorState.Unknown, "Doctor.ShortcutTarget"));
                    }
                }
            }
            catch (FileNotFoundException) { findings.Add(new(DoctorState.Error, "Doctor.Missing")); }
            catch (DirectoryNotFoundException) { findings.Add(new(DoctorState.Error, "Doctor.Missing")); }
            catch (UnauthorizedAccessException) { findings.Add(new(DoctorState.Error, "Doctor.AccessDenied")); }
            catch (System.Security.SecurityException) { findings.Add(new(DoctorState.Error, "Doctor.AccessDenied")); }
            catch (PathTooLongException) { findings.Add(new(DoctorState.Error, "Doctor.PathTooLong")); }
            catch (IOException) { findings.Add(new(DoctorState.Unknown, "Doctor.Unavailable")); }
            catch (ArgumentException) { findings.Add(new(DoctorState.Error, "Doctor.InvalidPath")); }
            token.ThrowIfCancellationRequested();
        }
        if (!string.IsNullOrWhiteSpace(item.WorkingDirectory))
            Probe(item.WorkingDirectory, true);
        if (item.Kind == ItemKind.Command)
        {
            if (OperatingSystem.IsWindows())
            {
                var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var shell = item.Shell == CommandShell.PowerShell ? Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe") : Path.Combine(windows, "System32", "cmd.exe");
                Probe(shell, false);
            }
            else findings.Add(new(DoctorState.Unknown, "Doctor.WindowsRequired"));
        }
        if (item.Kind is ItemKind.App or ItemKind.File or ItemKind.Folder)
        {
            Probe(item.Target, item.Kind == ItemKind.Folder, item.Kind == ItemKind.App && Path.GetExtension(item.Target).Equals(".lnk", StringComparison.OrdinalIgnoreCase));
            if (item.Kind == ItemKind.File && findings.Count == 0)
            {
                if (!OperatingSystem.IsWindows()) findings.Add(new(DoctorState.Unknown, "Doctor.AssociationUnknown"));
                else
                {
                    uint length = 0;
                    // ASSOCSTR_EXECUTABLE lookup only: does not launch a handler or resolve a shortcut.
                    var result = AssocQueryString(0, 2, Path.GetExtension(item.Target), "open", null, ref length);
                    if (result != 1 || length == 0) findings.Add(new(DoctorState.Warning, "Doctor.AssociationUnknown"));
                }
            }
        }
        return findings;
    }
    public static bool IsShellLinkHeader(ReadOnlySpan<byte> header) => header.Length >= 76
        && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header) == 76
        && new Guid(header.Slice(4, 16)) == new Guid("00021401-0000-0000-C000-000000000046");

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, EntryPoint = "AssocQueryStringW")]
    private static extern int AssocQueryString(uint flags, uint kind, string association, string extra, StringBuilder? output, ref uint length);
}
