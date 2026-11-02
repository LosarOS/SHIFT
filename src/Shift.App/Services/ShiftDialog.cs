namespace Shift.App.Services;

/// <summary>Give command buttons stable semantic IDs without replacing their theme styles.</summary>
internal sealed class ShiftDialog : ContentDialog
{
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        // ContentDialog sets local automation properties on its template parts.
        // Local values take precedence over Style setters.
        foreach (var (part, id) in new[] { ("PrimaryButton", "Dialog.Primary"),
            ("SecondaryButton", "Dialog.Secondary"), ("CloseButton", "Dialog.Close") })
            if (GetTemplateChild(part) is Button button)
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, id);
    }
}
