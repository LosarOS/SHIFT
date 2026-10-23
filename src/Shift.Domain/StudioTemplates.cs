namespace Shift.Domain;

public static class StudioTemplates
{
    public static IReadOnlyList<WidgetStyle> All { get; } = Array.AsReadOnly(new[]
    {
        new WidgetStyle { TemplateId = "Minimal", Background = "#25262B", Foreground = "#F7F7FA", Accent = "#B8B9C7", CornerRadius = 8 },
        new WidgetStyle { TemplateId = "Glass", Background = "#242838", Foreground = "#FFFFFF", Accent = "#A8D8FF", Surface = WidgetSurface.Translucent, Opacity = .9 },
        new WidgetStyle { TemplateId = "Neon", Background = "#191727", Foreground = "#F3EEFF", Accent = "#C39AFF", BorderWidth = 2 },
        new WidgetStyle { TemplateId = "Midnight" },
        new WidgetStyle { TemplateId = "Light", Background = "#F5F6FA", Foreground = "#202331", Accent = "#6041BD" },
        new WidgetStyle { TemplateId = "Forest", Background = "#172923", Foreground = "#EDF8F0", Accent = "#8ED8AC" }
    });
    public static WidgetStyle For(string templateId) => All.FirstOrDefault(t => t.TemplateId == templateId) ?? throw new ArgumentException("Unknown built-in style.", nameof(templateId));
}
