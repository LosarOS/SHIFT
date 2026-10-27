using Shift.Application;
using Shift.Domain;

namespace Shift.Infrastructure;

public sealed partial class LocalDataStore : IStudioWidgetEditorStore
{
    public Task<WidgetDefinition> SaveWidgetEditAsync(WidgetDefinition definition, DesktopProfile? targetProfile,
        CancellationToken token = default)
        => ExecuteAsync(() => inner.SaveWidgetEditAsync(definition, targetProfile, token), token);
}
