# Security policy

## Supported versions

Claude Chat Backup is a local Windows tool. Security fixes target the `main` branch.

## What the tool touches

- **Reads** the Claude Desktop data folder (`%LOCALAPPDATA%\Claude-Data\claude_*` for the
  Microsoft Store install, `%APPDATA%\Claude` for the classic one) and the Claude Code folder
  (`%USERPROFILE%\.claude`, or `CLAUDE_CONFIG_DIR` when set).
- **Writes** backups to `backups\` next to the executable, falling back to
  `%LOCALAPPDATA%\ClaudeChatBackup\backups`. The folder is created only when a backup is written.
- **On restore, writes** into the same Claude folders, after saving whatever it replaces to
  `antes-da-restauracao-<date>.zip`.
- **May close Claude Desktop**, only when you confirm it before a restore.
- **Sends deleted backups to the Windows Recycle Bin**, never deletes them outright.

There is no network traffic, no account, no telemetry and no analytics. No third-party package
runs inside the application.

## Backups are sensitive

A backup holds the full transcripts of your Code and Cowork sessions: every prompt, every answer,
every tool output and file snapshot. Anything you ever pasted into a session — keys, customer
data, internal code — can be in there. Backups are **not encrypted**.

- Keep backups where you would keep the source code they talk about.
- Do not attach a backup to an issue or a pull request. Send a minimal reproduction instead.
- `backups/` is in `.gitignore`. Never commit one.

What the tool **leaves out on purpose**: the Desktop `config.json` (login token cache),
`ccd-ids.json` (device salt), `~\.claude\.credentials.json`, cookies and caches. Those are tied
to the Windows user through DPAPI, would not work on another machine and would turn a backup into
a credential leak.

## Reporting a vulnerability

Do not open a public issue for anything that could be exploited before a fix exists. Use
GitHub's private vulnerability reporting under this repository's **Security** tab.

Include the version, your Windows version, the Claude Desktop version, reproduction steps, the
impact and a proof of concept if you have one.
