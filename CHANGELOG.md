# Changelog

## 2.0.0 — 2026-11-13

- Migrated from .NET Framework 4.8 to .NET 10 (`net10.0-windows`).
- Self-contained Windows x64 single-file release; no separate runtime installation.
- Rebuilt the interface with new typography, spacing, controls, navigation and protection states.
- Translated the full app interface, tray actions, messages and app-owned dialog titles to English.
- Retained existing settings and profiles, automatic discovery and recovery behavior.
- Updated Windows CI to build and test with the .NET 10 SDK.

## 1.2.0 — 2026-11-13

First public release.

- Compact WPF interface with automatic PiP and known-game discovery.
- Foreground-only mouse confinement with window and monitor modes.
- PiP click-through, automatic reacquisition after browser restart and manual override.
- Game profiles, configurable hotkeys, 30-second pause and emergency release.
- Tray controls, optional startup, local settings and profile import/export.
- Recovery companion for an unexpected main-process exit.
- Windows CI build with headless core checks; native integration checks available locally.
