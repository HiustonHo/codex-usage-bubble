# Codex Usage Bubble

A Windows desktop overlay that shows the signed-in user's remaining seven-day Codex usage.

The bubble is always on top, draggable, refreshed every minute, and launched silently at Windows sign-in. Its water level follows the remaining percentage, while its color blends continuously from green through amber to red. Click it to refresh immediately or right-click it to refresh or exit.

## Install as a Codex plugin

Add this repository as a local Codex marketplace, then install `codex-usage-bubble`. In Codex, ask:

> Install my Codex usage bubble.

The included skill runs the deterministic installer from the plugin package.

## Manual install

From the plugin directory, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

This installs the GUI under `%LOCALAPPDATA%\CodexUsageBubble` and creates a current-user Windows login startup entry. Administrator access is not required.

## Requirements

- Windows 10 or 11
- A signed-in Codex installation discoverable through `CODEX_CLI_PATH`, `PATH`, or the VS Code OpenAI extension
- .NET Framework 4.x

## Build

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\plugins\codex-usage-bubble\scripts\build.ps1
```

## Privacy

The utility starts the local `codex app-server` process and reads the account rate-limit response. It does not operate a separate server or send usage data to the repository publisher.

## License

MIT
