# Contributing

Bug reports and focused pull requests are welcome.

Build on Windows with the stable .NET 10 SDK:

```powershell
.\build.ps1 -CoreTest
```

For the full native and UI checks, first exit any running FocusFence instance:

```powershell
.\build.ps1 -Test
```

These checks use hidden test windows and a substituted cursor backend for state transitions, so they do not take over the pointer. The separate startup check briefly registers the app's global hotkeys.

For PiP fixes, include browser/version, Windows version, expected behavior and reproduction steps. Remove personal information from screenshots and logs. Changes to detection should cover both the intended PiP window and similar normal windows that must not be selected.

The app interface, tray controls and app-owned dialogs are in English. Browser-window detection intentionally retains localized PiP titles. User profile names and process/window titles should never be translated.
