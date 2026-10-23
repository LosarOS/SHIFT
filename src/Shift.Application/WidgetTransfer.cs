using System.Text.Json;
using System.Text.Json.Serialization;
using Shift.Domain;
namespace Shift.Application;

public static class WidgetTransfer
{
    public const int MaxBytes = 256 * 1024;
    public static JsonSerializerOptions Json { get; } = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
    public static WidgetDefinition Import(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaxBytes) throw new InvalidDataException("Widget file exceeds the supported size.");
        return Import(System.Text.Encoding.UTF8.GetBytes(json));
    }
    public static WidgetDefinition Import(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length == 0 || utf8.Length > MaxBytes) throw new InvalidDataException("Widget file exceeds the supported size.");
        try
        {
            var widget = JsonSerializer.Deserialize<WidgetDefinition>(utf8, Json) ?? throw new InvalidDataException("Widget definition is empty.");
            StudioValidator.Validate(widget);
            // Imported objects receive a fresh identity and never create instances or launch anything.
            return widget with { Id = Guid.NewGuid(), Revision = 0 };
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid declarative widget format.", ex); }
    }
    public static string Export(WidgetDefinition definition)
    {
        StudioValidator.Validate(definition);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(definition, Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Widget file exceeds the supported size.");
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
