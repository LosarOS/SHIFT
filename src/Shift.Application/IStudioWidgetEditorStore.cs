using Shift.Domain;

namespace Shift.Application;

/// <summary>Persists a Widget Lab definition and its optional profile placement as one durable operation.</summary>
public interface IStudioWidgetEditorStore
{
    Task<WidgetDefinition> SaveWidgetEditAsync(WidgetDefinition definition, DesktopProfile? targetProfile,
        CancellationToken token = default);
}
