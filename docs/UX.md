# SHIFT UX

## Navigation map

```text
First run → onboarding → demo OR empty overview
Overview → workspace detail → launch queue → history
Workspaces → search → detail → edit metadata / add/edit item
Favorites → workspace detail
History → expandable run → per-item result / error
Settings → theme / language / behavior / backup / data / logs
Tray → restore SHIFT / explicit quit
```

The fixed sidebar is 224 DIPs. Content uses 36 DIP horizontal margins, 14 DIP card radii, 8–16 DIP control gaps. Workspace cards are arranged in 2 columns at normal desktop widths, 1 at narrow width when rendering. Typography uses Segoe UI Variable Text; native Segoe Fluent Icons and an original SHIFT arrow mark. The app keeps the native Windows title bar, caption buttons and standard window behavior.

## Visual system

| Theme | Canvas | Surface | Accent |
|---|---|---|---|
| Dark (default) | #17181E | #202128 | #BCADFF |
| Light | #F5F5F8 | #FFFFFF | #B6A5FF |
| Midnight | #0D1423 | #151F32 | #98BEFF |
| Forest | #101B18 | #192722 | #A9D4A5 |

Cards show real name, description, tags, enabled/total item counts, star, details and launch. Overview uses an introductory panel, actual counts and recent runs. No fabricated success indicators or sample activity. Demo consists of a real documentation URL and the current user's local home folder; it does not launch on creation.

## State and interaction

- Workspace editor saves metadata in a dialog. Item editor changes its fields based on item type.
- NumberBox uses milliseconds and supports 0–300000. Reordering uses named up/down buttons, accessible without drag-and-drop.
- Primary actions: New, Launch, Save. Delete is in workspace overflow and asks for confirmation.
- Import shows every item's target, type and enable state before save. Commands are reviewed again before every actual launch.
- Queue includes a progress indicator, status rows and cancellation. Success is labelled «Открытие запрошено» / «Open requested» to avoid readiness promises.
- Errors from storage and actions appear in an InfoBar. Form validation keeps the dialog open. Item launch errors remain in durable history.
- Close-to-tray defaults on; explicit quit from tray. If closing during launch with tray disabled, stop-and-quit confirmation precedes cancellation.
- Native WinUI controls supply standard focus, hover and press feedback. No hand-written animation loops; reduced-motion and high contrast behavior require verification on Windows.

## Accessibility / localization

RU defaults, EN can be saved in Settings without restart. Domain/OS error details may be in English; UI introduces them with a localized message. UI does not translate user names, paths or command text. Native controls participate in keyboard navigation. Icon-only star/detail/reorder buttons carry automation names/tooltips.

Manual gates: Narrator, contrast (especially disabled rows), high contrast, 200% text scaling, DPI 100/150/200%, narrow window clipping, dialog picker focus, keyboard-only launch/cancel. Color is accompanied by text status. Current themes use explicit palette brushes, so high contrast support must be verified and improved before accessibility claims.
