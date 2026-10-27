using System.Runtime.InteropServices;
using System.Text.Json;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Shift.App.Studio;

/// <summary>Ordinary top-level layered tool window; alpha affects the whole native window.</summary>
internal sealed class WidgetWindow : Forms.Form
{
    private readonly WidgetDefinition definition;
    private readonly IStudioStore store;
    private readonly IAppLog log;
    private readonly bool preview;
    private readonly Action edit;
    private readonly Action<string> notify;
    private readonly Forms.Label heading = new() { Name = "Widget.Heading", AutoEllipsis = true, Dock = Forms.DockStyle.Fill, TextAlign = Drawing.ContentAlignment.MiddleLeft };
    private readonly Forms.Label status = new() { Name = "Widget.Status", AutoEllipsis = true, Dock = Forms.DockStyle.Bottom, AutoSize = false, Height = 34 };
    private readonly Forms.Label value = new() { Name = "Widget.Value", AutoEllipsis = true, Dock = Forms.DockStyle.Fill, TextAlign = Drawing.ContentAlignment.MiddleCenter };
    private readonly Forms.Label detail = new() { Name = "Widget.Detail", AutoEllipsis = true, Dock = Forms.DockStyle.Bottom, Height = 25, TextAlign = Drawing.ContentAlignment.MiddleCenter };
    private readonly Forms.Label secondaryDetail = new() { Name = "Widget.SecondaryDetail", AutoEllipsis = true, Dock = Forms.DockStyle.Bottom, Height = 23, TextAlign = Drawing.ContentAlignment.MiddleCenter };
    private readonly Forms.ToolTip tips = new();
    private SystemMetrics? lastMetrics;
    private readonly Forms.TextBox notes = new() { Name = "Widget.Notes", Multiline = true, Dock = Forms.DockStyle.Fill, ScrollBars = Forms.ScrollBars.Vertical, MaxLength = 20000, BorderStyle = Forms.BorderStyle.None, AcceptsReturn = true, AcceptsTab = true };
    private readonly Forms.TableLayoutPanel timerButtons = new() { Dock = Forms.DockStyle.Bottom, Height = 38, ColumnCount = 3, RowCount = 1, Padding = Forms.Padding.Empty, Margin = Forms.Padding.Empty };
    private readonly Forms.Button start = new() { AutoSize = true, Name = "Timer.Start" };
    private readonly Forms.Button pause = new() { AutoSize = true, Name = "Timer.Pause" };
    private readonly Forms.Button reset = new() { AutoSize = true, Name = "Timer.Reset" };
    private readonly Forms.Panel header = new() { Dock = Forms.DockStyle.Top, Height = 34 };
    private readonly Forms.Button menuButton = new() { Dock = Forms.DockStyle.Right, Width = 34, Text = "⋯", FlatStyle = Forms.FlatStyle.Flat, Name = "Widget.Menu" };
    private readonly Forms.Panel body = new() { Dock = Forms.DockStyle.Fill };
    private readonly SemaphoreSlim saving = new(1, 1);
    private readonly SemaphoreSlim draftGate = new(1, 1);
    private FocusTimerController timer;
    private readonly string draftPath;
    private WidgetRuntimeState state;
    private IReadOnlyList<MonitorIdentity> monitors;
    private string language;
    private DateTimeOffset nextUpdate;
    private DateTimeOffset nextSave;
    private bool dirty;
    private bool loading = true;
    private bool ownedClose;
    private bool positioning;
    private bool saveConflict;
    private long? conflictBaseRevision;
    private bool draftAvailable;
    private int generation;
    private CancellationTokenSource? draftDelay;
    private Task draftWrite = Task.CompletedTask;
    private readonly List<Drawing.Font> ownedFonts = [];
    public WidgetInstance Instance { get; private set; }
    public bool LayoutDirty { get; private set; }
    public event EventHandler? HideRequested;
    protected override bool ShowWithoutActivation => true;
    protected override Forms.CreateParams CreateParams
    {
        get { var value = base.CreateParams; value.ExStyle |= 0x00000080; if (definition is not null && definition.Style.Shadow && !Forms.SystemInformation.HighContrast) value.ClassStyle |= 0x00020000; return value; } // WS_EX_TOOLWINDOW
    }
    private string L(string ru, string en) => language == "en" ? en : ru;
    public WidgetWindow(WidgetInstance instance, WidgetDefinition definition, WidgetRuntimeState? saved,
        IReadOnlyList<MonitorIdentity> monitors, string language, bool preview, IStudioStore store, IAppLog log, Action edit, Action<string> notify)
    {
        Validate(definition, instance.Layout);
        Instance = instance; this.definition = definition; this.monitors = monitors; this.language = language;
        this.preview = preview; this.store = store; this.log = log; this.edit = edit; this.notify = notify;
        state = saved ?? new WidgetRuntimeState { InstanceId = instance.Id, Notes = definition.Content.InitialNotes,
            Timer = new FocusTimerState { RemainingSeconds = definition.Content.FocusMinutes * 60 } };
        timer = new FocusTimerController(state.Timer);
        draftPath = Path.Combine(new AppPaths().DataDirectory, "Studio", "RecoveryNotes", instance.Id.ToString("N") + ".json");
        Text = WidgetPresentation.WindowTitle(definition.Title, language);
        Name = "Studio.Widget." + instance.Id.ToString("N");
        AccessibleName = Text;
        FormBorderStyle = Forms.FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = Forms.FormStartPosition.Manual;
        AutoScaleMode = Forms.AutoScaleMode.Dpi;
        MinimumSize = new Drawing.Size(160, 100);
        MaximumSize = new Drawing.Size(3200, 2400);
        DoubleBuffered = true;
        header.Controls.Add(heading); header.Controls.Add(menuButton);
        body.Controls.Add(value); body.Controls.Add(detail);
        if (definition.Kind == WidgetKind.SystemMonitor) body.Controls.Add(secondaryDetail);
        if (definition.Kind == WidgetKind.Notes) { body.Controls.Clear(); body.Controls.Add(notes); notes.Text = state.Notes; }
        if (definition.Kind == WidgetKind.FocusTimer)
        {
            detail.Visible = false;
            for (var column = 0; column < 3; column++) timerButtons.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 100f / 3));
            timerButtons.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 100));
            timerButtons.Controls.Add(start, 0, 0); timerButtons.Controls.Add(pause, 1, 0); timerButtons.Controls.Add(reset, 2, 0); body.Controls.Add(timerButtons);
            timerButtons.SizeChanged += (_, _) => UpdateTimerButtons();
            start.Click += async (_, _) => await ChangeTimerAsync(() => { if (timer.State.RemainingSeconds <= 0) timer.Reset(TimeSpan.FromMinutes(definition.Content.FocusMinutes)); timer.Start(); });
            pause.Click += async (_, _) => await ChangeTimerAsync(() => timer.Pause());
            reset.Click += async (_, _) => await ChangeTimerAsync(() => timer.Reset(TimeSpan.FromMinutes(definition.Content.FocusMinutes)));
        }
        Controls.Add(body); Controls.Add(status); Controls.Add(header);
        menuButton.Click += (_, _) => ShowMenu();
        heading.MouseDown += (_, e) => { if (e.Button == Forms.MouseButtons.Left && Instance.Interaction == WidgetInteraction.Edit) { Native.ReleaseCapture(); Native.SendMessage(Handle, 0xA1, 2, 0); } };
        notes.TextChanged += (_, _) => { if (!loading && !preview) MarkDirty(); };
        LocationChanged += (_, _) => { if (!positioning && !loading && !preview) LayoutDirty = true; };
        SizeChanged += (_, _) => { if (!positioning && !loading && !preview) LayoutDirty = true; SetCorners(); UpdateTimerButtons(); };
        FormClosing += (_, e) => { if (!ownedClose) { e.Cancel = true; HideRequested?.Invoke(this, EventArgs.Empty); } };
        ApplyStyle(); SetLanguage(language); Reposition(instance.Layout, monitors);
        loading = false;
    }
    internal static void Validate(WidgetDefinition definition, WidgetLayout layout)
    {
        StudioValidator.Validate(definition);
        StudioValidator.Validate(layout);
    }
    private void ApplyStyle()
    {
        var style = definition.Style;
        var oldFonts = ownedFonts.ToArray(); ownedFonts.Clear();
        var accessible = Forms.SystemInformation.HighContrast;
        var background = accessible ? Drawing.SystemColors.Window : Drawing.ColorTranslator.FromHtml(style.Background);
        var foreground = accessible ? Drawing.SystemColors.WindowText : Drawing.ColorTranslator.FromHtml(style.Foreground);
        BackColor = background; ForeColor = foreground;
        foreach (Forms.Control control in new Forms.Control[] { header, body, heading, menuButton, status, value, detail, secondaryDetail, notes, timerButtons, start, pause, reset })
        { control.BackColor = background; control.ForeColor = foreground; }
        var family = style.FontFamily == "Segoe UI Variable" ? "Segoe UI" : style.FontFamily;
        Drawing.Font Own(float size, Drawing.FontStyle fontStyle = Drawing.FontStyle.Regular)
        { var font = new Drawing.Font(family, size, fontStyle, Drawing.GraphicsUnit.Point); ownedFonts.Add(font); return font; }
        Font = Own(10); heading.Font = Own(10, Drawing.FontStyle.Bold); status.Font = Own(9);
        value.Font = Own((float)style.FontSize, Drawing.FontStyle.Bold);
        detail.Font = Own(Math.Min(12, Math.Max(10, (float)style.FontSize * .5f)));
        secondaryDetail.Font = Own(10);
        notes.Font = Own(Math.Max(10, (float)style.FontSize * .65f));
        body.Padding = new Forms.Padding((int)style.Padding);
        Opacity = accessible || !TransparencyEnabled() ? 1 : style.Opacity;
        TopMost = Instance.Layout.AlwaysOnTop;
        var accent = accessible ? Drawing.SystemColors.Highlight : Drawing.ColorTranslator.FromHtml(style.Accent);
        foreach (var button in new[] { start, pause, reset, menuButton })
        {
            button.FlatStyle = Forms.FlatStyle.Flat; button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderSize = 1; button.FlatAppearance.BorderColor = accessible ? Drawing.SystemColors.WindowText : accent;
            button.FlatAppearance.MouseOverBackColor = accessible ? Drawing.SystemColors.Highlight : Forms.ControlPaint.Light(background, .15f);
            button.FlatAppearance.MouseDownBackColor = accessible ? Drawing.SystemColors.Highlight : Forms.ControlPaint.Light(background, .25f);
            button.AutoSize = false; button.Dock = button == menuButton ? Forms.DockStyle.Right : Forms.DockStyle.Fill;
            button.Margin = new Forms.Padding(2); button.Padding = Forms.Padding.Empty;
        }
        start.BackColor = accent; start.ForeColor = accessible ? Drawing.SystemColors.HighlightText : background;
        start.FlatAppearance.MouseOverBackColor = Forms.ControlPaint.Light(accent, .1f);
        start.FlatAppearance.MouseDownBackColor = Forms.ControlPaint.Dark(accent, .1f);
        UpdateTimerButtons();
        foreach (var font in oldFonts) font.Dispose();
    }
    private static bool TransparencyEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch (System.Security.SecurityException) { return false; }
    }
    public void SetLanguage(string value)
    {
        language = value == "en" ? "en" : "ru";
        Text = WidgetPresentation.WindowTitle(definition.Title, language); AccessibleName = Text;
        heading.Text = (preview ? L("Предпросмотр · ", "Preview · ") : "") + definition.Title;
        heading.AccessibleName = heading.Text; tips.SetToolTip(heading, heading.Text);
        menuButton.AccessibleName = L("Действия виджета", "Widget actions"); tips.SetToolTip(menuButton, menuButton.AccessibleName);
        notes.AccessibleName = L("Текст заметки, автосохранение", "Note text, autosave");
        notes.AccessibleDescription = L("Заметка хранится локально. Изменения сохраняются автоматически.", "This note stays local. Changes are saved automatically.");
        start.AccessibleName = L("Старт", "Start"); pause.AccessibleName = L("Пауза", "Pause"); reset.AccessibleName = L("Сброс", "Reset");
        UpdateTimerButtons(); RenderReading(); ShowSurfaceStatus();
    }
    private void UpdateTimerButtons()
    {
        if (definition.Kind != WidgetKind.FocusTimer || timer is null) return;
        var state = timer.State; var compact = timerButtons.ClientSize.Width < (int)(185 * DeviceDpi / 96d);
        var restart = state.RemainingSeconds <= 0;
        start.Text = compact ? "▶" : restart ? L("Ещё раз", "Restart") : L("Старт", "Start");
        pause.Text = compact ? "Ⅱ" : L("Пауза", "Pause"); reset.Text = compact ? "↺" : L("Сброс", "Reset");
        start.AccessibleName = restart ? L("Запустить новый таймер", "Restart timer") : L("Старт", "Start");
        start.Enabled = !state.Running; pause.Enabled = state.Running;
        tips.SetToolTip(start, start.AccessibleName); tips.SetToolTip(pause, L("Пауза", "Pause")); tips.SetToolTip(reset, L("Сброс", "Reset"));
    }
    private void RenderReading()
    {
        if (definition.Kind == WidgetKind.Clock)
        {
            var local = DateTime.Now;
            value.Text = WidgetPresentation.Clock(local, definition.Content, language);
            detail.Text = local.ToString(definition.Content.DateFormat, WidgetPresentation.Culture(language));
        }
        else if (definition.Kind == WidgetKind.SystemMonitor)
        {
            var reading = WidgetPresentation.Metrics(lastMetrics, language);
            value.Text = reading.Cpu; detail.Text = reading.UsedMemory; secondaryDetail.Text = reading.FreeMemory;
        }
        else if (definition.Kind == WidgetKind.FocusTimer) value.Text = FormatDuration(timer.State.RemainingSeconds);
        value.AccessibleName = value.Text; detail.AccessibleName = detail.Text; secondaryDetail.AccessibleName = secondaryDetail.Text;
        tips.SetToolTip(value, value.Text); tips.SetToolTip(detail, detail.Text); tips.SetToolTip(secondaryDetail, secondaryDetail.Text);
    }
    private void ShowSurfaceStatus()
    {
        if (saveConflict) { status.Text = L("Конфликт · сохраните копию / выберите текст в меню", "Conflict · save a copy / choose text in menu"); status.Visible = true; return; }
        if (dirty) { status.Text = draftAvailable ? L("Локальный черновик сохранён", "Local draft retained") : L("Сохранение…", "Saving…"); status.Visible = true; return; }
        status.Text = preview ? L("Черновик: изменения не сохраняются", "Draft: changes are not saved")
            : definition.Style.Surface is WidgetSurface.Acrylic or WidgetSurface.Mica
                ? L("Эффект недоступен: обычный фон / alpha", "Effect unavailable: solid / alpha fallback")
                : Instance.Interaction == WidgetInteraction.ClickThrough ? L("Сквозной режим · управление в SHIFT", "Click-through · control from SHIFT") : "";
        status.Visible = status.Text.Length > 0;
    }
    protected override void OnHandleCreated(EventArgs e)
    { base.OnHandleCreated(e); SetInteraction(Instance.Interaction); SetCorners(); }
    public void SetInteraction(WidgetInteraction mode, bool persisted = false)
    {
        if (!persisted && !preview && !loading && Instance.Interaction != mode) LayoutDirty = true;
        Instance = Instance with { Interaction = mode };
        header.Visible = mode == WidgetInteraction.Edit || preview;
        Padding = new Forms.Padding(mode == WidgetInteraction.Edit ? 8 : (int)Math.Ceiling(definition.Style.BorderWidth));
        if (IsHandleCreated)
        {
            var style = Native.GetWindowLongPtr(Handle, -20).ToInt64();
            if (mode == WidgetInteraction.ClickThrough) style |= 0x00000020 | 0x00080000;
            else style &= ~0x00000020L;
            Native.SetWindowLongPtr(Handle, -20, new nint(style));
            if ((style & 0x00080000) != 0) Native.SetLayeredWindowAttributes(Handle, 0, (byte)Math.Round(Opacity * 255), 2);
            Native.SetWindowPos(Handle, 0, 0, 0, 0, 0, 0x0020 | 0x0010 | 0x0001 | 0x0002 | 0x0004);
        }
        ShowSurfaceStatus(); Invalidate();
    }
    public void Reposition(WidgetLayout layout, IReadOnlyList<MonitorIdentity> topology, bool persist = false)
    {
        if (topology.Count == 0) return;
        monitors = topology; Instance = Instance with { Layout = layout };
        var bounds = WidgetLayoutService.Resolve(layout, topology);
        positioning = true;
        try
        {
            var monitor = topology.First(m => m.DeviceId == bounds.MonitorId);
            MinimumSize = new Drawing.Size(Math.Min(monitor.WorkWidth, (int)Math.Round(160 * monitor.DpiScale)), Math.Min(monitor.WorkHeight, (int)Math.Round(100 * monitor.DpiScale)));
            Bounds = new Drawing.Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height); TopMost = layout.AlwaysOnTop;
        }
        finally { positioning = false; }
        if (persist && !preview) LayoutDirty = true;
    }
    public WidgetLayout CaptureLayout()
    {
        var centerX = Left + Width / 2d; var centerY = Top + Height / 2d;
        var monitor = monitors.FirstOrDefault(m => centerX >= m.Left && centerX < m.Left + m.Width && centerY >= m.Top && centerY < m.Top + m.Height)
            ?? monitors.FirstOrDefault(m => m.DeviceId == Instance.Layout.MonitorId) ?? monitors.FirstOrDefault();
        var captured = WidgetLayoutService.Capture(new WidgetBounds(Left, Top, Width, Height, monitor?.DeviceId ?? Instance.Layout.MonitorId), monitors);
        return captured with { AlwaysOnTop = Instance.Layout.AlwaysOnTop };
    }
    public void LayoutSaved(WidgetInstance saved)
    {
        if (!LayoutDirty) return; // A fallback display is not a user edit.
        var current = CaptureLayout();
        // Native move/resize events can arrive while the database save is awaiting IO.
        // Acknowledge only the exact geometry/mode that actually reached storage.
        LayoutDirty = !WidgetPresentation.LayoutWasSaved(saved, current, Instance.Interaction);
        Instance = Instance with { Layout = current };
    }
    private bool DisplayActive => Visible && WindowState != Forms.FormWindowState.Minimized;
    public bool MetricsDue(DateTimeOffset now) => definition.Kind == WidgetKind.SystemMonitor && DisplayActive && now >= nextUpdate;
    public async Task TickAsync(DateTimeOffset now, SystemMetrics? metrics)
    {
        if (IsDisposed) return;
        if (Forms.SystemInformation.HighContrast && Opacity != 1) { Opacity = 1; ApplyStyle(); }
        if (DisplayActive && now >= nextUpdate)
        {
            nextUpdate = now.AddSeconds(definition.Kind == WidgetKind.Clock ? definition.Content.ShowSeconds ? 1 : Math.Max(1, 60 - now.Second) : definition.Content.UpdateIntervalSeconds);
            if (metrics is not null) lastMetrics = metrics;
            RenderReading();
        }
        if (definition.Kind == WidgetKind.FocusTimer)
        {
            var completed = timer.Tick();
            if (DisplayActive) { value.Text = FormatDuration(timer.State.RemainingSeconds); value.AccessibleName = value.Text; UpdateTimerButtons(); }
            if (completed)
            {
                if (!preview)
                {
                    MarkDirty();
                    try { await FlushAsync(); }
                    catch (Exception ex) { nextSave = now.AddSeconds(30); ShowError(ex); log.Error("Completed timer state could not be saved", ex); }
                    // Completion is useful even during a database outage. The controller
                    // and local draft retain its one-shot flag; no tick-count retry is used.
                    notify(L("Фокус-сессия завершена", "Focus session complete"));
                }
            }
        }
        if (!preview && dirty && !saveConflict && now >= nextSave)
        {
            try { await FlushAsync(); }
            catch (Exception ex) { nextSave = now.AddSeconds(30); ShowError(ex); log.Error("Widget autosave failed; recovery draft retained", ex); }
        }
    }
    private static string FormatDuration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling(seconds)));
        return duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}" : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }
    private async Task ChangeTimerAsync(Action change)
    {
        change(); RenderReading(); UpdateTimerButtons();
        if (preview) return;
        MarkDirty();
        try { await FlushAsync(); }
        catch (Exception ex) { ShowError(ex); log.Error("Timer state could not be saved", ex); }
    }
    private void MarkDirty()
    {
        dirty = true; draftAvailable = false; generation++; nextSave = DateTimeOffset.UtcNow.AddMilliseconds(750);
        draftDelay?.Cancel(); draftDelay?.Dispose(); draftDelay = new();
        var token = draftDelay.Token;
        draftWrite = SaveDraftDelayedAsync(token);
        status.Text = saveConflict ? L("Конфликт сохранения · меню виджета", "Save conflict · widget menu") : L("Сохранение…", "Saving…"); status.Visible = true;
    }
    private async Task SaveDraftDelayedAsync(CancellationToken token)
    {
        try { await Task.Delay(150, token); await WriteDraftAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError(ex); log.Error("Widget recovery draft could not be written", ex); }
    }
    private WidgetRuntimeState Snapshot() => state with { Notes = notes.Text, Timer = timer.State };
    private async Task WriteDraftAsync()
    {
        if (preview) return;
        var captured = Snapshot();
        if (saveConflict && conflictBaseRevision is long baseRevision) captured = captured with { Revision = baseRevision };
        var capturedGeneration = generation;
        await draftGate.WaitAsync();
        try
        {
            if (capturedGeneration != generation) return;
            draftAvailable = false;
            var directory = Path.GetDirectoryName(draftPath)!;
            Directory.CreateDirectory(directory);
            var temporary = draftPath + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(captured));
            File.Move(temporary, draftPath, true);
            if (capturedGeneration == generation) draftAvailable = true;
        }
        finally { draftGate.Release(); }
    }
    public async Task RestoreDraftAsync()
    {
        if (preview || !File.Exists(draftPath)) return;
        try
        {
            var info = new FileInfo(draftPath);
            if (info.Length > 256 * 1024) throw new InvalidDataException(L("Черновик превышает допустимый размер.", "Recovery draft exceeds its size limit."));
            var restored = JsonSerializer.Deserialize<WidgetRuntimeState>(await File.ReadAllTextAsync(draftPath), new JsonSerializerOptions { MaxDepth = 8 });
            if (restored is null || restored.InstanceId != Instance.Id || restored.Notes.Length > 20000 || restored.Timer.RemainingSeconds is < 0 or > 86400 || !double.IsFinite(restored.Timer.RemainingSeconds))
                throw new InvalidDataException(L("Черновик восстановления повреждён.", "Invalid widget recovery draft."));
            if (restored.Notes == state.Notes && restored.Timer == state.Timer) { File.Delete(draftPath); return; }
            StudioValidator.Validate(restored);
            if (restored.Revision != state.Revision) { saveConflict = true; conflictBaseRevision = restored.Revision; }
            loading = true; notes.Text = restored.Notes; loading = false;
            // Rebuild using the persisted deadline, never resume by counting UI ticks.
            timer = new FocusTimerController(restored.Timer);
            dirty = true; draftAvailable = true; generation++;
            status.Text = saveConflict ? L("Восстановлен черновик с конфликтом · меню", "Recovered draft has a conflict · menu") : L("Восстановлен локальный черновик", "Local draft recovered"); status.Visible = true;
        }
        catch (Exception ex) { ShowError(ex); log.Error("Recovery draft is unreadable; file retained", ex); }
    }
    public async Task FlushAsync()
    {
        if (preview || !dirty) return;
        await saving.WaitAsync();
        try
        {
            if (!dirty) return;
            draftDelay?.Cancel(); await draftWrite; await WriteDraftAsync();
            if (saveConflict) throw new InvalidOperationException(L("Сначала разрешите конфликт через меню виджета.", "Resolve the conflict from the widget menu first."));
            var currentGeneration = generation;
            var captured = Snapshot();
            try { state = await store.SaveWidgetStateAsync(captured); }
            catch (InvalidOperationException)
            {
                var latest = await store.GetWidgetStateAsync(Instance.Id);
                if (latest is not null && latest.Revision != captured.Revision) { state = latest; saveConflict = true; conflictBaseRevision = captured.Revision; }
                throw;
            }
            if (currentGeneration == generation)
            {
                dirty = false; draftAvailable = false;
                await draftGate.WaitAsync();
                try { if (currentGeneration == generation && File.Exists(draftPath)) File.Delete(draftPath); }
                finally { draftGate.Release(); }
                ShowSurfaceStatus();
            }
            else await WriteDraftAsync();
        }
        finally { saving.Release(); }
    }
    public async Task FlushForCloseAsync()
    {
        try { await FlushAsync(); }
        catch (Exception databaseError)
        {
            // A database outage must not make disabling Studio impossible if the latest text
            // is already durable in our local journal. A conflict keeps its original base revision.
            draftDelay?.Cancel(); await draftWrite;
            await WriteDraftAsync();
            if (!draftAvailable) throw;
            log.Error("Widget state retained in recovery journal during close", databaseError);
            notify(L("Состояние виджета сохранено в локальном черновике; база не обновлена.", "Widget state retained in a local recovery draft; database was not updated."));
        }
    }
    public void ShowError(Exception error)
    {
        status.Text = saveConflict ? L("Конфликт · сохраните копию / выберите текст в меню", "Conflict · save a copy / choose text in menu") : draftAvailable ? L("Не сохранено в базе · черновик сохранён, повторите", "Database save failed · draft retained, retry") : L("Не сохранено · оставьте окно открытым, сохраните копию", "Not saved · keep window open, save a copy");
        status.Visible = true;
    }
    private void ShowMenu()
    {
        var accessible = Forms.SystemInformation.HighContrast;
        var accent = accessible ? Drawing.SystemColors.Highlight : Drawing.ColorTranslator.FromHtml(definition.Style.Accent);
        var menu = new Forms.ContextMenuStrip { BackColor = BackColor, ForeColor = ForeColor, Font = Font, ShowImageMargin = false };
        if (!accessible) menu.Renderer = new Forms.ToolStripProfessionalRenderer(new MenuColors(BackColor, accent));
        menu.Closed += (_, _) => menu.Dispose();
        void Add(string text, Action action)
        {
            var item = menu.Items.Add(text); item.ForeColor = ForeColor;
            item.Click += (_, _) => { try { action(); } catch (Exception ex) { ShowError(ex); log.Error("Widget action failed", ex); } };
        }
        void AddAsync(string text, Func<Task> action)
        {
            var item = menu.Items.Add(text); item.ForeColor = ForeColor;
            item.Click += async (_, _) => { try { await action(); } catch (Exception ex) { ShowError(ex); log.Error("Widget action failed", ex); } };
        }
        Add(L("Редактировать в SHIFT", "Edit in SHIFT"), edit);
        Add(L("Режим редактирования", "Edit mode"), () => SetInteraction(WidgetInteraction.Edit));
        Add(L("Закрепить", "Desktop mode"), () => SetInteraction(WidgetInteraction.Desktop));
        Add(L("Сквозной режим (вернуть через SHIFT)", "Click-through (restore from SHIFT)"), () => SetInteraction(WidgetInteraction.ClickThrough));
        Add(L("Всегда поверх: ", "Always on top: ") + (TopMost ? L("да", "on") : L("нет", "off")), () => { TopMost = !TopMost; Instance = Instance with { Layout = Instance.Layout with { AlwaysOnTop = TopMost } }; if (!preview) LayoutDirty = true; });
        if (!preview)
        {
            AddAsync(L("Сохранить сейчас", "Save now"), async () => { try { await FlushAsync(); } catch (Exception ex) { ShowError(ex); } });
            if (definition.Kind == WidgetKind.Notes)
            {
                Add(L("Сохранить текст в файл…", "Save text copy…"), ExportNote);
                if (saveConflict)
                {
                    AddAsync(L("Использовать текст этого окна…", "Use this window's text…"), async () =>
                    {
                        if (Forms.MessageBox.Show(this, L("Заменить сохранённый текст текущим черновиком?", "Replace the saved text with this draft?"), "SHIFT", Forms.MessageBoxButtons.YesNo, Forms.MessageBoxIcon.Warning) != Forms.DialogResult.Yes) return;
                        state = await store.GetWidgetStateAsync(Instance.Id) ?? state; saveConflict = false; conflictBaseRevision = null;
                        try { await FlushAsync(); } catch (Exception ex) { ShowError(ex); }
                    });
                    AddAsync(L("Оставить текст из базы…", "Keep database text…"), async () =>
                    {
                        if (Forms.MessageBox.Show(this, L("Отбросить локальный черновик? Сначала можно сохранить копию.", "Discard this local draft? You can save a copy first."), "SHIFT", Forms.MessageBoxButtons.YesNo, Forms.MessageBoxIcon.Warning) != Forms.DialogResult.Yes) return;
                        state = await store.GetWidgetStateAsync(Instance.Id) ?? state; saveConflict = false; conflictBaseRevision = null;
                        loading = true; notes.Text = state.Notes; loading = false; dirty = false;
                        await draftGate.WaitAsync(); try { if (File.Exists(draftPath)) File.Delete(draftPath); } finally { draftGate.Release(); }
                        ShowSurfaceStatus();
                    });
                }
            }
        }
        Add(preview ? L("Закрыть предпросмотр", "Close preview") : L("Скрыть виджет", "Hide widget"), () => HideRequested?.Invoke(this, EventArgs.Empty));
        menu.Show(menuButton, 0, menuButton.Height);
    }
    private sealed class MenuColors(Drawing.Color background, Drawing.Color accent) : Forms.ProfessionalColorTable
    {
        public override Drawing.Color ToolStripDropDownBackground => background;
        public override Drawing.Color MenuBorder => accent;
        public override Drawing.Color MenuItemBorder => accent;
        public override Drawing.Color MenuItemSelected => Forms.ControlPaint.Light(background, .2f);
        public override Drawing.Color MenuItemSelectedGradientBegin => MenuItemSelected;
        public override Drawing.Color MenuItemSelectedGradientEnd => MenuItemSelected;
        public override Drawing.Color MenuItemPressedGradientBegin => MenuItemSelected;
        public override Drawing.Color MenuItemPressedGradientMiddle => MenuItemSelected;
        public override Drawing.Color MenuItemPressedGradientEnd => MenuItemSelected;
    }
    private void ExportNote()
    {
        using var dialog = new Forms.SaveFileDialog { Filter = L("Текстовый файл (*.txt)|*.txt", "Text file (*.txt)|*.txt"), Title = L("Сохранить копию заметки", "Save a note copy"), FileName = L("SHIFT-заметка.txt", "SHIFT-note.txt"), AddExtension = true, DefaultExt = "txt", RestoreDirectory = true };
        if (dialog.ShowDialog(this) != Forms.DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, notes.Text); }
        catch (Exception ex) { ShowError(ex); log.Error("Note copy could not be saved", ex); }
    }
    public void CloseOwned()
    {
        ownedClose = true; draftDelay?.Cancel(); Close();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { draftDelay?.Cancel(); draftDelay?.Dispose(); tips.Dispose(); foreach (var font in ownedFonts) font.Dispose(); }
        base.Dispose(disposing);
    }
    protected override void WndProc(ref Forms.Message message)
    {
        if (message.Msg == 0x84 && Instance.Interaction == WidgetInteraction.Edit)
        {
            var point = PointToClient(new Drawing.Point(unchecked((short)(message.LParam.ToInt64() & 0xffff)), unchecked((short)((message.LParam.ToInt64() >> 16) & 0xffff))));
            var border = Math.Max(6, DeviceDpi / 16);
            var left = point.X < border; var right = point.X >= Width - border; var top = point.Y < border; var bottom = point.Y >= Height - border;
            if (left || right || top || bottom) { message.Result = new nint(top ? left ? 13 : right ? 14 : 12 : bottom ? left ? 16 : right ? 17 : 15 : left ? 10 : 11); return; }
        }
        if (message.Msg is 0x001A or 0x031A) { ApplyStyle(); SetInteraction(Instance.Interaction); }
        base.WndProc(ref message);
    }
    protected override void OnPaint(Forms.PaintEventArgs e)
    {
        base.OnPaint(e);
        if (definition.Style.BorderWidth > 0)
        {
            using var pen = new Drawing.Pen(Forms.SystemInformation.HighContrast ? Drawing.SystemColors.WindowText : Drawing.ColorTranslator.FromHtml(definition.Style.Accent), (float)definition.Style.BorderWidth);
            e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        }
    }
    private void SetCorners()
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
        var radius = Forms.SystemInformation.HighContrast ? 0 : (int)(definition.Style.CornerRadius * DeviceDpi / 96d);
        var region = radius == 0 ? Native.CreateRectRgn(0, 0, Width, Height) : Native.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, radius * 2, radius * 2);
        if (region != 0 && Native.SetWindowRgn(Handle, region, true) == 0) Native.DeleteObject(region); // Success transfers region ownership to Windows.
    }
}

internal static class Native
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")] internal static extern nint SendMessage(nint window, uint message, nint wparam, nint lparam);
    [DllImport("gdi32.dll")] internal static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] internal static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll")] internal static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteObject(nint value);
}
