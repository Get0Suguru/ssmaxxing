# ScreenSnip Alpha

ScreenSnip is a small Windows utility that captures a screen or window and pastes
the image at the current cursor location. It runs locally from the system tray.
There is no account, cloud upload, settings service, or automatic updater.

## Requirements

- Windows 10 version 19041 or later
- .NET 9 SDK
- An x64 Windows build environment
- A target application that accepts an image paste

Windows 10 19041 is required for `Windows.Graphics.Capture`. The project uses
the Windows SDK and the `Vortice.Direct3D11` and `Vortice.DXGI` packages restored
by NuGet.

## Setup for development

Open PowerShell in the directory containing `ScreenSnipAlpha.csproj`, then run:

```powershell
dotnet restore
dotnet build -c Release -p:Platform=x64
dotnet run -c Release -p:Platform=x64
```

The application has no main window. A tray icon appears when it starts, and the
global hotkeys are active immediately. Stop it from the tray menu or with
`Ctrl+C` when running through `dotnet run`.

To confirm the installed SDK:

```powershell
dotnet --version
```

## How to use it

### Capture and paste

1. Put the cursor in the application where the image should be inserted.
2. Hold `CapsLock` and press `V`.
3. ScreenSnip captures the current source and sends `Ctrl+V` to the focused
   application.

The initial source is **Entire Screen**, which includes the full Windows virtual
desktop and all connected monitors.

### Choose a source

1. Hold `CapsLock` and press `S`.
2. Use the Windows picker to select a window or screen.
3. Alternatively, choose **Entire screen** in the ScreenSnip picker.

The selected source is sticky: it remains active until another source is chosen,
the tray menu selects **Stop sharing**, or the shared window closes. A yellow
border is shown by Windows while a window is being shared. Press `Esc` to cancel
source selection without changing the current source.

`CapsLock` is used as a modifier and does not toggle Caps Lock while ScreenSnip
is running. Other combinations such as `Ctrl+V`, `Alt+V`, and `Win+V` are passed
through normally.

## Build a distributable folder

For a machine that already has the .NET 9 runtime installed:

```powershell
dotnet publish -c Release -p:Platform=x64 --self-contained false -o .\publish
```

For a self-contained build that does not require a separate .NET runtime:

```powershell
dotnet publish -c Release -p:Platform=x64 -r win-x64 --self-contained true -o .\publish
```

Run `ScreenSnipAlpha.exe` from the resulting `publish` directory. Keep the
whole published directory together; do not copy only the executable.

## Tray menu

- **Change source** opens source selection.
- **Stop sharing** returns to the entire-screen source.
- **Exit** stops the keyboard hook, releases the capture session, and removes
  the tray icon.

## Troubleshooting

### The hotkeys do nothing

Confirm that the process is running and that its tray icon is visible. Another
utility may already be using a low-level keyboard hook or the `CapsLock` key.
Run the published executable directly from PowerShell to see startup errors.

### A window cannot be captured

The window may have closed, be protected by Windows, or belong to an application
that blocks screen capture. Choose the source again with `CapsLock+S`.

### Paste does not appear

Make sure the destination application is focused and accepts images. Some
applications handle simulated `Ctrl+V` differently; test first in Paint or
another standard image editor.

### Build errors in WinRT or COM interop

Check that the Windows 10 SDK and .NET 9 SDK are installed. The interop code in
`CaptureInterop.cs` depends on the Windows SDK projections and is the first place
to inspect when those APIs change between SDK versions.

## Project structure

| File | Responsibility |
|---|---|
| `App.xaml.cs` | Tray icon, sticky source state, and application lifecycle |
| `HotkeyHook.cs` | Global `CapsLock+V` and `CapsLock+S` keyboard hook |
| `SourcePicker.xaml` | Source-selection window and Windows capture picker |
| `CaptureService.cs` | Entire virtual desktop capture through GDI |
| `LiveWgcSession.cs` | Live window capture through `Windows.Graphics.Capture` |
| `CaptureInterop.cs` | WinRT, Direct3D, and DXGI interop |
| `ClipboardPaste.cs` | Clipboard image setup and simulated paste |
| `WindowEnumerator.cs` | Window discovery helpers |

## Current scope

This is an alpha utility. It intentionally does not include persistence, a
settings UI, startup registration, accounts, cloud storage, telemetry, updates,
or installer packaging. The selected source resets to **Entire Screen** when the
application restarts.

## What a normal README should contain

A useful project README usually answers these questions in this order:

1. **What is it?** A short description and the main use case.
2. **What is required?** Supported operating systems, runtimes, SDKs, and tools.
3. **How do I install or build it?** Copy-pasteable commands.
4. **How do I use it?** The smallest complete workflow, including controls.
5. **How do I troubleshoot it?** Common failures and practical checks.
6. **How is it organized?** Important files or architecture, when useful.
7. **What are the limits?** Known gaps, unsupported cases, and project status.
8. **How can I contribute or get help?** Add contribution, issue, license, and
   contact sections when the project accepts outside users or contributors.

For this alpha, the first seven sections are relevant. Contribution, license,
and support details should be added once the project has a public contribution
workflow and a chosen license.
