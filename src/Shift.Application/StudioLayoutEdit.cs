using Shift.Domain;
namespace Shift.Application;

/// <summary>Compare the acknowledged instance before writing only its live layout/mode.</summary>
public sealed record StudioLayoutEdit(WidgetInstance Expected, WidgetLayout Layout, WidgetInteraction Interaction);
public sealed record StudioLayoutWriteResult(IReadOnlyList<WidgetInstance> Saved, IReadOnlyList<Guid> Conflicts);
