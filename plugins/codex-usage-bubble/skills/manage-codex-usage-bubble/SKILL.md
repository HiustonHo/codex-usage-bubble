---
name: manage-codex-usage-bubble
description: Install, start, stop, inspect, or uninstall the Codex weekly usage bubble on Windows. Use when the user asks to manage this plugin's desktop bubble; do not use for general Codex billing or subscription questions.
---

# Manage Codex Usage Bubble

This plugin ships a signed-in-user Windows utility that shows the Codex seven-day usage window as a draggable, always-on-top glossy bubble.

## Boundaries

- Windows only.
- The utility reads usage locally through `codex app-server`; it does not add a separate network service.
- Installing creates `%LOCALAPPDATA%\CodexUsageBubble`, writes the current user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexUsageBubble` login entry, and starts the GUI.
- Run install, stop, or uninstall only when the user explicitly asks for that state change.
- Uninstall removes only this utility's process, Run value, and `%LOCALAPPDATA%\CodexUsageBubble` directory.

## Workflow

Resolve the plugin root as two directories above this `SKILL.md`, then use its deterministic PowerShell scripts:

- Install or update: `scripts/install.ps1`
- Start: `scripts/start.ps1`
- Stop: `scripts/stop.ps1`
- Uninstall: `scripts/uninstall.ps1`

Run scripts with Windows PowerShell using `-NoProfile -ExecutionPolicy Bypass -File <script-path>`. Use the user's normal security and approval flow for persistent registry writes or deletion.

After installing or starting, verify all of the following:

1. `CodexUsageBubble` is running.
2. `%LOCALAPPDATA%\CodexUsageBubble\codex-weekly-usage-bubble.status.json` reports `isVisible: true`, `topmost: true`, and `available: true`.
3. The `CodexUsageBubble` Run value points to `%LOCALAPPDATA%\CodexUsageBubble\CodexUsageBubble.exe`.

If `available` is false, report the status file's `error`. The executable discovers Codex through `CODEX_CLI_PATH`, `PATH`, or the newest VS Code `openai.chatgpt-*` extension.
