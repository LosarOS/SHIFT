using System.Text.Json;
using System.Text.Json.Serialization;
using Shift.Domain;
namespace Shift.Application;

public sealed record WorkspaceExport(int SchemaVersion, Workspace Workspace);
public static class WorkspaceTransfer
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 32,
        AllowDuplicateProperties = false
    };

    public static string Export(Workspace workspace)
    {
        WorkspaceValidator.Validate(workspace);
        var json = JsonSerializer.Serialize(new WorkspaceExport(1, workspace), Json);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes)
            throw new ValidationException([new ValidationIssue("ExportSize", "Workspace export exceeds 2 MB. Shorten item targets, arguments or notes.")]);
        return json;
    }

    public static async Task<Workspace> ImportAsync(Stream stream, CancellationToken token = default)
    {
        // Bound actual bytes read, even when the selected file grows after the picker closes.
        var buffer = new byte[MaxBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(length), token);
            if (count == 0)
                break;
            length += count;
        }
        if (length > MaxBytes)
            throw new ValidationException(["Import exceeds 2 MB."]);
        var offset = length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;
        try
        {
            return Import(new System.Text.UTF8Encoding(false, true).GetString(buffer, offset, length - offset));
        }
        catch (System.Text.DecoderFallbackException) { throw new ValidationException(["Workspace JSON must be UTF-8."]); }
    }

    public static Workspace Import(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes)
            throw new ValidationException(["Import exceeds 2 MB."]);
        try
        {
            var envelope = JsonSerializer.Deserialize<WorkspaceExport>(json, Json) ?? throw new JsonException("Empty export.");
            if (envelope.SchemaVersion != 1)
                throw new ValidationException(["Unsupported export version."]);
            var workspace = envelope.Workspace ?? throw new JsonException("Missing workspace.");
            // Defend against explicit JSON nulls despite non-nullable CLR annotations.
            if (workspace.Name is null || workspace.Description is null || workspace.Color is null || workspace.Tags is null || workspace.Items is null || workspace.Tags.Any(t => t is null) || workspace.Items.Any(i => i is null || i.Name is null || i.Target is null || i.Arguments is null || i.WorkingDirectory is null || i.Note is null))
                throw new JsonException("Null fields are not allowed.");
            WorkspaceValidator.Validate(workspace);
            return workspace.Duplicate(workspace.Name);
        }
        catch (JsonException ex) { throw new ValidationException([$"Invalid workspace JSON: {ex.Message}"]); }
    }
}
