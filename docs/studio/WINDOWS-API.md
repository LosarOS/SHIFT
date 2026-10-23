# SHIFT Studio Windows adapter boundary

## Supported API and scope

`WindowsWallpaperService` implements `IWallpaperService` through the documented `IDesktopWallpaper` COM interface. Its complete 16-method SDK vtable is declared in order, although Studio uses only wallpaper/monitor/position/status methods. Each operation creates a private STA, initializes COM, owns one object and releases it before completing. No Explorer injection, WorkerW parenting, elevation, policies, global hooks or registry writes are used.

Capture records each current monitor device path, image path and the global wallpaper placement. Set operations target one explicitly enumerated connected monitor, never a null/all-monitor shorthand. Cancellation is observed before an OS mutation; a successful synchronous OS call is acknowledged even if cancellation arrives during it. The application layer remains responsible for consent, serialized apply, pre-mutation journal persistence and conflict-aware restore.

`CanRestore=true` means the captured static **image values and position** are accessible, not that a provider or Windows theme is fully restorable. Slideshow, disabled/remote-session status, missing/unreadable/nonlocal original images and empty/solid-color backgrounds return `CanRestore=false` with a limitation. Background colors and slideshow options are never changed. `IDesktopWallpaper` cannot reliably detect/capture/restore Windows Spotlight provider configuration. Studio therefore requires a current static-picture prerequisite confirmation for each manual wallpaper apply and does not auto-apply wallpaper-bearing workspace profiles. No undocumented registry detection is substituted.

Original image paths are retained, not deleted or silently moved. An original removed after capture can make restore fail explicitly. Selected new images are copied to managed persistent user-data storage. Uninstall must retain that storage while Windows or a recovery journal can reference it. This adapter does not delete managed resources; safe cleanup must be reference-aware. Restore does not promise theme/accent/Spotlight/slideshow restoration.

## Local image library

`WindowsStudioImageLibrary(string managedAssetsDirectory)` accepts only explicitly chosen absolute local PNG, JPEG and BMP files. It rejects UNC/device/URL paths, mapped network drives, alternate streams, linked/reparse/offline paths and non-files. Files are opened without write/delete sharing; the opened final Windows path is compared with the requested path to reject a link-swap escape.

Limits: 32 MiB source bytes, 32 million decoded pixels and 16384 pixels on either edge. Built-in GDI+ decoding validates the actual format and draws all pixels into a bounded bitmap; a successful header-only probe is insufficient. External GDI+ codecs are disabled. Native GDI+ calls are bounded by these size limits but cannot be interrupted mid-call. Cancellation cleans up only the uniquely created temporary import file.

Images are copied, flushed and atomically moved to a SHA256-based canonical name. Existing content at that name must match its hash; it is never overwritten silently. The original selected file is untouched. A moved portable directory can resolve a **missing** old managed reference only when its basename is exactly our lowercase 64-hex SHA256 plus `.png`/`.jpg`/`.bmp`, the same basename exists under the current managed root, and its real bytes match that hash. Arbitrary filenames/folders are not rebased. Validation repeats at apply time. No network download or shell handler is invoked.

## Display topology

`WindowsMonitorTopology` uses `EnumDisplayMonitors`/`GetMonitorInfoW` for monitor/work rectangles, `QueryDisplayConfig` plus source/target device information for matching GDI display names to target device paths, and `GetScaleFactorForMonitor` for scale. Target paths normally match `IDesktopWallpaper` IDs. Monitor IDs can change after reconnect. Application layout recovery must match/fallback and clamp, not assume physical permanence.

When target identity is unavailable (including some RDP configurations), a GDI display-name fallback is returned and recorded in the public `Warnings` property. Such an ID is not silently accepted by the wallpaper setter: disconnected/unrecognized targets fail safely. A scale-query failure uses 1.0 only for safe placement and records that it is **not a measured 100% DPI**. The WinUI executable must retain its existing per-monitor DPI-awareness manifest. Physical mixed-DPI/RDP/hotplug behavior requires native acceptance; a Linux compile does not verify it.

## System monitor values

`WindowsSystemMetricsProvider` uses `GetSystemTimes` deltas. Kernel time includes idle, so busy percentage is `(deltaKernel + deltaUser - deltaIdle) / (deltaKernel + deltaUser)`. On ordinary systems with at most 64 logical processors this covers all processors; on larger Windows processor-group configurations the API reports the calling processor group. It is not per-process CPU utilization or a GPU measurement.

The first sample, a failed API call, counter regression or an invalid delta yields CPU `null`. `GlobalMemoryStatusEx` supplies total/available physical bytes, with `null` on failure. UI must render unavailable values as unavailable, not 0%. There is no timer or polling loop in this provider; the central widget scheduler controls frequency. Non-Windows samples are explicitly unavailable.

## Executed evidence and outstanding gates

Focused Linux .NET10 adapter contract suite: **16 passed, 0 failed/skipped**. Linux exercises explicit platform boundaries and input/cancellation contracts; it does not execute COM, GDI+, display APIs or real metrics. Windows branches in the same tests perform real PNG/JPEG/BMP decode/import, bad/oversize rejection, Unicode and portable rebase/hash tampering, read-only wallpaper capture, topology and metrics. Wallpaper mutation is deliberately excluded from unit tests and must run only in the parent's disposable Windows acceptance harness, preserving and confirming the initial state.

No Windows runtime, mixed-DPI, Spotlight restoration, real wallpaper mutation or performance PASS is claimed by this document. Native/GUI results must be added from the actual completed Windows run.

Official references: [IDesktopWallpaper](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-idesktopwallpaper), [SetWallpaper](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-setwallpaper), [QueryDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig), [GetScaleFactorForMonitor](https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getscalefactorformonitor), [GetSystemTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes), [GlobalMemoryStatusEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex).
