# ScreenSnip Alpha

## Run
```
cd ScreenSnipAlpha
dotnet restore
dotnet build -c Release
dotnet run -c Release
```
Requires .NET 9 SDK.

## Hotkeys (default)
- **Alt+V** — capture sticky source → clipboard (+ paste if enabled)
- **Alt+S** — window grid (current virtual desktop only). Click a card to share.

## Tray
- Paste after capture (toggle)
- Stop sharing / Exit

Settings file: `%AppData%\ScreenSnipAlpha\settings.json`
