using Shift.Application;
using Shift.Domain;

namespace Shift.Infrastructure;

public sealed partial class LocalDataStore : IStudioPresetStore
{
    public Task<DesktopProfile> SavePresetAsync(StudioPresetDraft draft, CancellationToken token = default)
        => ExecuteAsync(() => inner.SavePresetAsync(draft, token), token);
}
