# ADR-0015: Localization and accessibility

- Status: accepted
- Date: 2026-11-12
- Milestone: M12

## Context

PLAN.md, M12 requires:

- Narrator labels,
- high contrast handling,
- reduced motion,
- states not conveyed by color alone,
- `.resw` strings in English and German.

Sign-off: Accessibility Insights with no critical findings.

## Decisions

### 1. Strings: `.resw` without Windows MRT

All UI strings live in `src/Weatherly.Core/Localization/Strings/{en-US,de-DE}/Resources.resw`. `Texts` (Core) reads them with a small ResX reader from embedded files. We do not use WinUI MRT because:

- same behavior packaged, unpackaged, and in tests,
- Core strings are translatable too (tray menu, pause reasons, template names, achievements),
- no `resources.pri` generation in build.

Format stays `.resw` so translation tools can edit it.

### 2. Language

- **Choice:** Follows Windows display language or Advanced → Language.
- **Switch:** Immediate. Open page rebuilds; menus and flyout update on next open.
- **Missing:** English is fallback. Missing German shows English. Unknown keys show as themselves to surface mistakes.
- **Stay English:** Diagnostics window and smoke report (developer tools).

### 3. Tests keep languages aligned

`LocalizationTests` checks:

- same keys in both files,
- no empty strings,
- same placeholders,
- German actually translated,
- every key used via `Texts.Get`/`Texts.Format` exists (parsed from sources),
- every enum-built key exists.

A new string without translation breaks the build.

### 4. Narrator

- **Names:** Every control gets `AutomationProperties.Name`. Sliders, toggles, and combos carry their labels. Flyout sliders: “Rain, intensity”. Effect expanders carry effect name.
- **Images:** Visitor images carry character name. Decorative icons are hidden from screen readers.
- **Check:** Smoke test walks every settings page and the flyout. Any tab-focusable control without a name fails (`AccessibilityCheck`).
- **Visitors:** Path for Narrator from M11 and ADR-0014: tooltip, tray menu, Ctrl+Alt+V, hint.

### 5. High contrast

UI uses only theme resources. In high contrast, WinUI uses system colors.

Semi-transparent weather over the screen lowers contrast users enabled. Weather pauses in high contrast. Can be disabled under Advanced; detection via `SPI_GETHIGHCONTRAST`. Reduced motion since M9.

### 6. State beyond color

Every state has text or shape:

- Toggles show On/Off.
- Achievements show check or circle and “(not yet)”.
- Collection says “Not met yet”.
- Sync status is text.
- Basket grows on hit, not only brighter.

### 7. Share

Share card now opens Windows share UI. For desktop apps via window handle (`DataTransferManagerInterop`). Image passed as PNG file and bitmap.

## Consequences

- New strings always via `Texts.Get("Area.Name")` in both `.resw` files.
- Accessibility Insights needs real Windows. Smoke test covers the most common critical class (unnamed controls). Rest is checklist M12.
