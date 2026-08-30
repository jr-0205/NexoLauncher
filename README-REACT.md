# NEXA React UI migration

The production-facing UI is moving from WPF composition to a local React renderer hosted by WebView2.

## Principles

- C# remains authoritative for profiles, Minecraft, Java, filesystem operations, downloads, hashes and process control.
- React never receives unrestricted filesystem access or credentials.
- WebView2 navigation is restricted to the packaged `https://app.nexa/` virtual host (or localhost during development).
- Legacy WPF stays available during the transition; it is not removed until React reaches functional parity.
- Product-facing branding is **NEXA**. Existing `NexoLauncher.*` namespaces and `%LOCALAPPDATA%\NexoLauncher` remain temporarily for compatibility.

## Development

```powershell
cd src\NexaLauncher.UI
npm install
npm run build
cd ..\..
dotnet run --project src\NexaLauncher.Desktop
```

For hot reload:

```powershell
cd src\NexaLauncher.UI
npm run dev
$env:NEXA_UI_DEV_URL = "http://127.0.0.1:5173"
dotnet run --project ..\NexaLauncher.Desktop
```

End users will not need Node/npm. Production packaging will publish .NET self-contained and bundle the compiled React `dist` plus the WebView2 bootstrap/runtime strategy.
