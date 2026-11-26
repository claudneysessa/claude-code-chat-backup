## Download

**`ClaudeChatBackup.exe`** — portable, self-contained Windows x64 executable. No installer and no
.NET runtime needed: download it and double-click. The interface is in Portuguese (BR).

> **Windows SmartScreen will warn you.** The executable is not code-signed, so Windows may show
> *"Windows protected your PC"*. Click **More info → Run anyway** if you trust this release, or
> build it yourself from the source below.

Verify the download in PowerShell — the hash must match:

```powershell
(Get-FileHash .\ClaudeChatBackup.exe -Algorithm SHA256).Hash.ToLower()
```

```
{SHA256}
```

Built and tested by GitHub Actions from the tagged commit. What changed is in the
[CHANGELOG](https://github.com/claudneysessa/claude-code-chat-backup/blob/{VERSION}/CHANGELOG.md).

**Backups contain your full conversations and are not encrypted.** Read
[SECURITY.md](https://github.com/claudneysessa/claude-code-chat-backup/blob/{VERSION}/SECURITY.md)
before you copy one anywhere.

---

## Download (português)

**`ClaudeChatBackup.exe`** — executável portable para Windows x64, sem instalador e sem precisar do
runtime .NET: baixe e dê dois cliques.

> **O Windows SmartScreen vai avisar.** O executável não é assinado digitalmente, então o Windows
> pode mostrar *"O Windows protegeu o computador"*. Clique em **Mais informações → Executar assim
> mesmo** se confiar nesta release, ou compile a partir do código.

O SHA-256 acima confere o download. **Os backups contêm as conversas inteiras e não são
criptografados.**
