# Building the packaged installer

Two tools, both free, both one-time installs on your Windows dev machine:
- **.NET 9 SDK** — you already have this from before.
- **Inno Setup** — https://jrsoftware.org/isdl.php (grab the latest stable installer, install it normally).

Everything below runs from the folder that contains both the `ScreenSnipAlpha\`
project folder and `installer.iss` (i.e. wherever you unzipped this delivery).

## 1. Publish a self-contained single-file exe

```
cd ScreenSnipAlpha
dotnet publish -c Release -r win-x64 --self-contained true
cd ..
```

`-r win-x64 --self-contained true` is what triggers the single-file/self-contained
settings in the .csproj (they're deliberately scoped to only apply here, so your
everyday `dotnet build` / `dotnet run` while tweaking code stays a fast, ordinary
framework-dependent build). Output lands at:

```
ScreenSnipAlpha\bin\Release\net9.0-windows10.0.19041.0\win-x64\publish\ScreenSnipAlpha.exe
```

That one .exe has the .NET runtime baked in — it'll run on a PC with no .NET
installed at all.

## 2. Compile the installer

Right-click `installer.iss` → **Compile**, or from a terminal:

```
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
```

This produces:

```
installer_output\ScreenSnipAlphaSetup.exe
```

That's your real installer — double-click it and you get a Setup Wizard,
Start Menu entry, optional desktop shortcut, optional "start with Windows"
checkbox, and a proper uninstaller listed in Windows' "Installed apps".

## Notes

- The installer installs per-user to `%LocalAppData%\Programs\ScreenSnipAlpha`
  by default, so it does **not** need admin rights / a UAC prompt to install.
- Settings still live at `%AppData%\ScreenSnipAlpha\settings.json` regardless
  of where the app itself is installed, and survive an uninstall.
- Bumping the version: edit `<Version>`/`<FileVersion>` in `ScreenSnipAlpha.csproj`
  **and** `#define MyAppVersion` in `installer.iss` to match, then repeat steps 1–2.
  Inno Setup will upgrade in place (same AppId) rather than installing side-by-side.
- Icon is `ScreenSnipAlpha\app.ico` — swap that file for your own art anytime,
  same filename, and both the .exe and the installer will pick it up next build.
