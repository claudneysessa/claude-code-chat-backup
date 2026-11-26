# Contributing to Claude Chat Backup

Thanks for wanting to help. Claude Chat Backup is an MIT-licensed open source project and
contributions are welcome: bug fixes, features, documentation, ideas and problem reports. Issues
and pull requests are accepted in English or Portuguese.

- [Issues](https://github.com/claudneysessa/claude-code-chat-backup/issues)
- [Pull requests](https://github.com/claudneysessa/claude-code-chat-backup/pulls)

## Before you start

1. **Search first.** Someone may already have reported or discussed it.
2. **Open an issue** describing the problem or the idea: what it does, what it should do, the
   steps to reproduce, and your environment — Windows version, Claude Desktop version and whether
   it is the Microsoft Store or the classic install. Claude's file layout changes between
   versions, so the version matters more than usual.
3. **Never attach a real backup or transcript.** They contain your conversations. Describe the
   file layout or build a minimal fake one instead.
4. **Fork and branch** (`fix/restore-remap`, `feat/cowork-listing`), then open a pull request
   against `main`. Small, focused pull requests get reviewed and merged faster.

## Code guide

- **Stack:** C# / WinForms on .NET 10 (`net10.0-windows`), xUnit for tests. **No runtime
  packages beyond the base class library.** Adding one needs a reason in the pull request, not
  just convenience.
- **Layers:** `Domain` is pure and does no I/O; `Application` orchestrates use cases;
  `Infrastructure` does disk, ZIP and process work; `Cli` and `Gui` present. Keep logic out of the
  views — it belongs in `Gui/ViewModels`, where it can be tested.
- **Language convention:** identifiers and class names in **English**; **user-facing messages in
  the CLI and the window in Portuguese (BR)**.
- **GUI:** fixed dark theme, every color comes from `Gui/Theming/Theme.cs`, and never
  `MessageBox` — use `ToastHost`, `DarkDialog` or `ErrorDialog`.
- **Commits:** Conventional Commits (`feat:`, `fix:`, `docs:`, `test:`, `chore:`), one coherent
  change per commit, with a matching entry at the top of `CHANGELOG.md` in
  `YYYY-MM-DD HH:mm:ss Z` format.
- **Tests first.** Write or update the test before the change and commit only a green state.

## Safety rules you must keep

- **Never restore over your real Claude data while developing.** Point the tool at copies with
  `--desktop-dir`, `--claude-dir` and `--home`. The tests already build a fake machine in a
  temporary folder; follow `tests/ClaudeChatBackup.Tests/Fixture.cs`.
- Every restore verifies every SHA-256 before writing, saves what it replaces, and refuses to run
  while Claude Desktop is open. A change that weakens any of these will not be merged.
- Path remapping touches only JSON values that start with a mapped folder. Free conversation text
  is never rewritten.

## Testing

```powershell
dotnet build ClaudeChatBackup.slnx   # must end with 0 errors and 0 warnings
dotnet test ClaudeChatBackup.slnx
```

For a change in the window, also run the app (`dotnet run --project src\ClaudeChatBackup.csproj`)
and check the screen you touched with a real-sized backup made from test data.

## Releases

Releases are cut by the maintainer and built by GitHub Actions, never uploaded by hand:

1. Bump `<Version>` in `src/ClaudeChatBackup.csproj` and add the entry to `CHANGELOG.md`.
2. Commit and push to `main`.
3. `git tag vX.Y.Z` and `git push origin vX.Y.Z`.

The [release workflow](.github/workflows/release.yml) refuses a tag that does not match
`<Version>`, runs the tests, builds the portable exe and publishes it with its SHA-256. The notes
come from [`.github/release-notes.md`](.github/release-notes.md). Pull requests do not need to touch
the version.

## Documentation

Update `README.md` **and** `README.pt-BR.md` in the same commit that changes behavior.
`docs/LEVANTAMENTO.md` records where Claude keeps each file; update it when you discover the
layout changed.

## How this project was built

This project was planned, implemented and tested in Claude Code sessions. That is stated up front
so you know what you are reviewing. Human contributions are equally welcome and are held to the
same standard, not a different one.

## License

By contributing you agree that your contribution is licensed under the
[MIT License](LICENSE) of this project.
