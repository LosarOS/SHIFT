# Studio widget host

Status at implementation checkpoint: IMPLEMENTED / NOT RUNTIME VERIFIED. A Linux Windows-reference C# compile is not a native HWND, transparency or GUI acceptance result.

`StudioWidgetRuntime` owns one dedicated STA thread and message pump for ordinary managed WinForms tool windows. The main application remains WinUI 3. The existing Windows Forms framework reference is reused; `UseWindowsForms` is not enabled on the WinUI project. There is no new process, Explorer parenting, WorkerW probing, injection, shell extension, global input hook or elevation.

The host exposes `ApplyAsync`, `StopAsync`, `SetInteractionAsync`, `GatherAsync`, `PreviewAsync`, `ClosePreviewAsync`, `WindowCount`, RU/EN `Language` and `EditRequested`, `NotificationRequested`, `RuntimeFaulted` events. The shell must marshal events to its own dispatcher and keep the main-window Edit/Gather/Disable controls reachable. Preview windows have ephemeral instance IDs and perform no state/layout persistence.

## Window behavior

- Normal top-level tool windows; not in the taskbar; initial display does not take focus. Their z-order follows normal Windows rules. Always on top is explicit and defaults off. No guarantee of being below every application while above desktop icons is made. Windows+D/fullscreen/RDP behavior requires native acceptance.
- Edit exposes title/menu and resize margins; Desktop hides helper chrome but leaves notes/timer controls interactive. Click-through explicitly uses documented layered-window/transparent extended styles and is reversible through the main shell. Do not enable click-through unless the shell recovery controls are available.
- The actual Form `Opacity` affects the entire native window, including text. Solid/translucent are distinct from Acrylic/Mica. Acrylic/Mica selections use a visible solid/alpha fallback notice; no glass simulation or unsupported composition trick is used. High contrast/system transparency restrictions force opaque rendering. Regions implement rounded corners; class drop-shadow is platform-dependent.
- Movement/resizing captures normalized work-area position plus monitor ID and logical size. Dirty layouts merge into the latest stored profile rather than overwriting unrelated editor fields. Missing monitors fall back through the shared layout service. Display/resume notifications trigger topology inspection; there is no perpetual topology poll.

## Data and scheduling

One 1-second scheduler updates only due/visible clock and monitor views, samples system metrics on a worker, and evaluates timer deadlines. Notes have no data polling; text changes schedule a cancellable 150ms local-draft write and a database save on the next eligible scheduler tick. SQLite failures back off instead of tight retry loops. No content is sent over the network.

Clock uses actual local Windows time. CPU/RAM use the injected documented native provider; unavailable readings remain unavailable. Focus uses the shared tested monotonic/deadline controller and persists start/pause/reset/completion; no timer is calculated by counting UI ticks. Completion is notified once locally. Notification display follows shell/user notification settings.

Notes/timer state uses Revision CAS and bounded per-instance JSON recovery drafts under the current SHIFT data directory, `Studio/RecoveryNotes`. A differing base revision remains an explicit conflict across restart; recovered text is not silently written over a newer database value. Notes offer a user-selected text export. Close/profile switch/disable flushes state. When SQLite is unavailable, a current local recovery draft permits safe close with a visible warning; when both database and draft writes fail, the unsaved window stays open and the caller receives the error rather than silently losing text. Power loss before a debounce finishes cannot be claimed lossless. Orphaned drafts are retained rather than automatically deleting user text.

Stop closes all owned windows, disposes the scheduler/context, removes display/power/error subscriptions and awaits thread exit. An ended host is safely forgotten. Concurrent apply/preview/stop/dispose and scheduler mutation use gates; repeated disposal is idempotent. No automatic wallpaper change occurs in this component.

## Required native evidence

On disposable Windows only: four widget HWNDs, live clock and CPU/RAM, actual window alpha, edit drag/resize, Desktop/Click-through recovery, no duplicate windows on reapply, Notes/timer restart, preview Cancel without writes, profile switch, Gather, Disable/reenable and Quit/pump exit. Physical Windows10/11, multi-monitor mixed DPI, fullscreen, Narrator/high contrast and sleep/RDP remain manual gates unless actually executed. Memory/CPU and opening/switch timings are NOT MEASURED at this checkpoint.

## RC4 runtime changes

Widget titles and accessible labels follow RU/EN live without recreating windows or editing user titles. Clock and metrics formatting uses the selected language even when Windows uses another culture. CPU/RAM becomes ЦП/ОЗУ in Russian; memory units are explicitly GiB/ГиБ. Used and free memory have separate lines, with ellipsis/tooltips for small surfaces. The native timer uses three equal columns, switches to accessible compact symbols at narrow widths, and can explicitly restart a completed session. Controls and context menus follow the widget palette with a high-contrast system fallback.

Layout acknowledgments compare the geometry/mode actually written to SQLite with the current native window. Dragging just before a mode switch or during an awaited storage write leaves unsaved changes dirty instead of clearing them. The merge keeps concurrent profile metadata and unrelated layouts. Five pure helper regressions cover culture-independent readings, unavailable versus measured-zero values, latest-profile merging and stale acknowledgments. Native visual/DPI validation of these RC4 changes still belongs to the Windows acceptance workflow.

## RC5 reliability and keyboard changes

Profile replacement, hiding and shutdown suspend window input with nested leases until their final flush/read/close hand-off finishes. A user cannot edit a note in the asynchronous gap after it was flushed but before its old HWND closes. Failure restores input; an overlapping conflict decision cannot prematurely re-enable a window still being closed.

Host shutdown acknowledges its final operation before posting message-pump exit. Every marshalled operation races its completion against host exit, so a callback abandoned by an unexpected STA exit fails explicitly instead of leaving the main UI awaiting indefinitely.

Notes and timer recovery conflicts both offer explicit local-versus-database state decisions. Discard drains and invalidates delayed drafts before deletion; the recovered timer controller is reconstructed from the chosen deadline. Neither decision runs automatically. `CaptureLiveStatesAsync(profileId)` is a read-only host snapshot for explicit note export; it does not infer that a successful close flush means the database was updated, and it does not resolve a conflict.

Keyboard access on focused widget windows: Ctrl+S saves now, Ctrl+Shift+S exports a note copy, Shift+F10 or the Context Menu key opens widget actions even in Desktop mode. These additions require native Windows validation; core tests only prove the host-operation completion policy.
