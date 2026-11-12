# Manual checklist MVP cut – Tray and settings

Already automated: menu model (checks, commands, pause), tooltip, save and load settings (including corrupt files). Windows runner smoke test verifies shell accepts the tray icon.

## Start

```bash
dotnet run --project src/Sillfall.App -p:WindowsPackageType=None
```

## 1. Tray icon

- [ ] Icon in notification area (may be in overflow “^”). Hover shows e.g. “Sillfall: clear sky”.
- [ ] Right-click opens menu. Click outside closes.
- [ ] Left-click opens window.
- [ ] Rain → Heavy: it rains; menu shows “Heavy”; tooltip “rain 100 %”.
- [ ] Snow → Light, then “Pause all effects”: all off. Again: back on.
- [ ] Sound on/off from menu.
- [ ] Restart Explorer in Task Manager: icon returns.

## 2. Window

- [ ] Close (X): window hides; weather continues; icon remains.
- [ ] Open via icon: sliders show current state.
- [ ] Quit: icon, window, weather gone; no process left.

## 3. Settings

- [ ] Rain 60%, snow light, sound on, Quit, restart: same state.
- [ ] Corrupt `%LOCALAPPDATA%\<AppInfo.Name>\settings.json`: app starts with defaults.

## 4. Daily use (one workday)

- [ ] Run light rain or snow for hours: no lost clicks, no focus steal, not annoying.
- [ ] Notes on likes and annoyances:

## Results

| Date | Windows version | Result | Notes |
|---|---|---|---|
|  |  |  |  |
