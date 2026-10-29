# MacUI

Makes Windows 11 look and behave like macOS while it runs, and puts everything back when you quit.

## Features

- **Menu bar** across the top: Apple menu (About, System Settings, Sleep, Restart, Shut Down, Lock, Quit), the active app's name, File/Edit/View/Window/Help, battery, Wi-Fi, volume, Spotlight, Control Center and clock.
- **Dock** that hides itself and slides up when the mouse touches the bottom edge: pinned and running apps, icon magnification, bounce on launch, running dots, and the Trash.
- **Window buttons**: red / yellow / green buttons at the top-right of every window. Windows' own buttons are removed (normal apps) or covered (apps that draw their own title bar). Background windows show grey buttons that light up on hover.
- **Genie animation** when minimizing into the Dock and when restoring from it.
- **App switcher** (Alt+Tab) with big app icons. While open: Tab / arrows move, ↓ shows the app's windows, Q quits the app, Esc cancels.
- **Window picker**: click a Dock app with several windows to see live previews of all of them.
- **Alt+`** switches between windows of the current app.
- **Spotlight** (Alt+Space): apps, quick maths, websites and web search.
- Follows the Windows light / dark setting.

## Build

No SDK needed. It uses the C# compiler that ships with Windows (.NET Framework 4.x):

```
build.cmd
```

This produces `MacUI.exe`.

## Use

- Start `MacUI.exe`. Quit from the Apple menu → **Quit MacUI**.
- `MacUI.exe --quit` closes a running MacUI; `MacUI.exe --restore` (or `Restore Windows Taskbar.cmd`) also makes sure the Windows taskbar is back.
- Apple menu → **Open MacUI at Login** toggles starting with Windows.
- Apple menu → **Customize Dock…** edits the Dock's apps (`%APPDATA%\MacUI\dock.txt`).
- Right-click the red/yellow/green buttons to keep the normal Windows buttons in a specific app.

## Limits

- Primary monitor only for the menu bar and Dock.
- Content inside apps (File Explorer's look, dialogs, context menus) is drawn by each app and isn't changed.
- Windows blocks other programs from catching keys in apps running as administrator, so the normal Alt+Tab appears there.
