# Levantamento — ClaudeChatBackup (.NET portable)

Aplicação Windows portable (um único `.exe`, sem instalar runtime) para fazer backup e restaurar os chats do Claude Desktop.

**Agora:** o básico funcional, ou seja, backup em ZIP e restore (na mesma máquina e em outra).

**Objetivo futuro (guardado, não fazer agora):** nunca perder um chat, sincronizando continuamente com um repositório Git privado. Ver seção 3.5.

Levantamento feito em 2026-10-22, inspecionando a instalação local do Claude Desktop (versão MSIX / Microsoft Store) nesta máquina.

---

## 1. Ponto crítico: existem dois tipos de "chat"

| Tipo | Onde vive | Backup | Restore |
|---|---|---|---|
| **Chat normal** (aba Chat, Projects, conversas do claude.ai) | **Servidor da Anthropic**. Localmente só há cache (IndexedDB/LevelDB, ~3,6 MB, incompleto). | Sim, via export oficial (ou API interna, ver 4.3) | **Não é possível devolver ao servidor.** Não existe API de importação. Restore = visualizador offline. |
| **Sessões do Code / Cowork** (aba Code, agent mode) | **Disco local** (JSON + JSONL) | Sim, cópia de arquivos | **Sim**, copiando os arquivos de volta (com o app fechado) |

Consequência para o escopo: o "restore" completo só existe para sessões do Code/Cowork. Para chats normais, o produto entrega arquivamento + leitura/busca offline + exportação (HTML/Markdown/PDF).

---

## 2. Mapa dos dados no disco (Windows)

### 2.1 Raiz de dados do Claude Desktop

```
%LOCALAPPDATA%\Claude-Data\claude_<packageSuffix>\     (ex.: claude_pzs8sxrjxfjjc)
```

- Instalações MSIX também têm `%LOCALAPPDATA%\Packages\Claude_<suffix>\LocalCache\Roaming\Claude` (aqui só contém um marcador vazio).
- Instalações antigas/não-Store usam `%APPDATA%\Claude`. O app precisa **detectar os três caminhos**.
- O `README.txt` da pasta avisa: não mover/renomear enquanto o app estiver instalado.

### 2.2 O que copiar

| Caminho (relativo à raiz) | Conteúdo | Tamanho aqui | Prioridade |
|---|---|---|---|
| `claude-code-sessions\<accountUuid>\<orgUuid>\local_<id>.json` | Metadados de cada sessão do Code: título, `cwd`, modelo, `cliSessionId`, `isArchived`, datas (epoch ms), permissões | 8,3 MB / 46 arq. | **Alta** |
| `claude-code-sessions\...\deleted_<id>` | Marcadores (13 bytes) de sessões apagadas | — | Média (preservar) |
| `claude-code-sessions\...\scheduled-tasks.json` | Tarefas agendadas | — | Média |
| `local-agent-mode-sessions\<accountUuid>\<orgUuid>\...` | Sessões Cowork / agent mode | 3,9 MB / 229 arq. | **Alta** |
| `ccd-session-presets.json`, `ccd-ids.json`, `git-worktrees.json` | Presets e IDs das sessões do Code | pequeno | Alta |
| `claude_desktop_config.json`, `config.json` | Config de MCP servers e app | pequeno | Alta |
| `Claude Extensions\`, `Claude Extensions Settings\`, `extensions-installations.json`, `mcp-user-tool-toggles.json` | Extensões / MCP | variável | Opcional |
| `IndexedDB\https_claude.ai_0.indexeddb.leveldb` | Cache do chat web (incompleto) | 3,6 MB | Baixa (só forense) |
| `Local Storage\`, `Session Storage\` | Estado da UI | 0,6 MB | Baixa |

### 2.3 O que NÃO copiar (lixo regenerável ou enorme)

`vm_bundles` (9,6 GB), `claude-code` (536 MB, binários), `claude-code-vm` (275 MB), `Cache`, `Code Cache`, `GPUCache`, `DawnGraphiteCache`, `DawnWebGPUCache`, `GrShaderCache`, `ShaderCache`, `GPUPersistentCache`, `Crashpad`, `sentry`, `logs`, `blob_storage`, `lockfile`, `DIPS*`, `SharedStorage*`.

### 2.4 Segredos — excluir por padrão

- `Network\Cookies` (cookie de sessão, criptografado com DPAPI + chave em `Local State`)
- `buddy-tokens.json`, `ant-did`, `ant-device-registry.json`
- `%USERPROFILE%\.claude\.credentials.json`

Esses arquivos dependem do usuário/máquina (DPAPI): restaurá-los em outro PC não funciona e, num backup, viram risco de vazamento. Oferecer, no máximo, uma opção avançada "incluir login", desligada por padrão, só para o mesmo usuário Windows.

### 2.5 Transcripts (conteúdo real das conversas do Code)

O JSON da sessão do Desktop guarda só metadados. As mensagens ficam no Claude Code CLI:

```
%USERPROFILE%\.claude\projects\<cwd-codificado>\<cliSessionId>.jsonl      ← transcript
%USERPROFILE%\.claude\projects\<cwd-codificado>\<cliSessionId>\tool-results\   ← saídas grandes de ferramentas
%USERPROFILE%\.claude\file-history\<cliSessionId>\                         ← snapshots de arquivos (checkpoints/rewind)
%USERPROFILE%\.claude\history.jsonl, settings.json, CLAUDE.md, skills\, plugins\
```

- Vínculo confirmado: `local_<id>.json` → campo `cliSessionId` → `<cliSessionId>.jsonl`.
- Codificação da pasta: `D:\Work\ServerApp` → `D--Work-ServerApp` (`:` e `\` viram `-`).
- Formato JSONL: uma linha por evento. Campos vistos: `type` (`user`/`assistant`/…), `uuid`, `parentUuid` (árvore de mensagens), `isSidechain`, `message`, `timestamp`, `cwd`, `sessionId`, `gitBranch`, `version`.
- Total aqui: 157 MB / 245 arquivos. Um único transcript chegou a 37 MB, então o leitor precisa ser **streaming** (linha a linha), nunca carregar tudo em memória.

---

## 3. Funcionalidades

### 3.1 Backup
1. Detectar instalação(ões) do Claude Desktop e a pasta `.claude`.
2. Listar sessões (título, projeto/`cwd`, data, tamanho, arquivada sim/não) para seleção total ou parcial.
3. Avisar ou exigir que o Claude Desktop esteja fechado (processo `claude.exe`); oferecer "Fechar Claude". Com o app aberto, copiar com `FileShare.ReadWrite` e validar o JSON depois.
4. Gerar um `.zip` (sugestão de extensão: `.ccbak`) com:
   - `manifest.json`: versão do formato, data, máquina, usuário, versão do Claude, lista de arquivos com SHA-256, caminhos originais.
   - `desktop\...` e `dotclaude\...` espelhando a estrutura original.
5. Opcional: senha (AES-256 via `System.Security.Cryptography`, chave derivada com PBKDF2/Argon2).
6. Backup incremental/agendado (Task Scheduler via `schtasks.exe`, ou modo `--silent` por linha de comando).

### 3.2 Restore
1. Ler o manifesto e validar os hashes.
2. Preview: quais sessões serão criadas, sobrescritas ou ignoradas.
3. Exigir Claude Desktop fechado (aqui é obrigatório: o app reescreve os JSONs ao fechar).
4. Antes de sobrescrever, fazer backup automático do estado atual (rollback).
5. Remapeamento quando o restore é em outra máquina/usuário:
   - `accountUuid` / `orgUuid` diferentes: mover para a pasta da conta atual (a conta logada é descoberta pelas subpastas existentes).
   - `cwd` diferente (outra letra de drive ou pasta): reescrever `cwd`/`originCwd`/`gitAnchors` no JSON e renomear a pasta codificada em `projects\`.
   - Caminho do usuário diferente (`C:\Users\fulano`): substituir nos JSONs.
6. Merge, sem duplicar: chave = `sessionId`/`cliSessionId`. Em conflito, manter o mais recente por `lastActivityAt`, ou perguntar.

### 3.3 Chats normais (claude.ai)
- Importar o `.zip` do export oficial (Settings → Privacy → Export data; o link chega por e-mail). Contém `conversations.json`, `projects.json`, `users.json`.
- Visualizador offline: lista, busca full-text, render de Markdown e código, exportação para HTML/MD/PDF.

### 3.4 Visualizador (também útil para sessões do Code)
- Abrir o transcript `.jsonl` sem o Claude instalado; seguir `parentUuid` para montar o fio principal e ocultar sidechains.
- Busca entre todas as sessões do backup (índice SQLite FTS5).

### 3.5 Sincronização com Git (fase futura, não fazer agora)

> Guardado para depois do básico funcional. O formato do backup do MVP já deve ser compatível com este layout (arquivos brutos + manifesto), para a migração ser simples.

O repositório Git é o destino do backup. Cada sincronização vira um commit, o histórico é o versionamento, e o remoto (GitHub, Azure DevOps, Gitea etc.) é a cópia fora da máquina.

**Layout do repositório**

```
claude-chats/                         (repo privado)
├─ manifest.json                      versão do formato, máquinas conhecidas
├─ machines/<hostname>/               uma pasta por máquina: evita conflito de merge
│  ├─ desktop/claude-code-sessions/<account>/<org>/local_<id>.json
│  ├─ desktop/local-agent-mode-sessions/...
│  ├─ desktop/config/                 claude_desktop_config.json, presets
│  └─ dotclaude/projects/<cwd-codificado>/<cliSessionId>/
│     ├─ chunks/000001.jsonl ...      transcript fatiado (ver abaixo)
│     └─ tool-results/
├─ claude-ai-exports/<data>/          zips do export oficial (manual)
└─ index/sessions.json                índice leve: título, máquina, projeto, datas
```

**Quando sincronizar**
1. Hook do Claude Code: `Stop` / `SessionEnd` no `%USERPROFILE%\.claude\settings.json` chama `ClaudeChatBackup.exe sync --quiet`. Sincroniza ao fim de cada turno ou sessão, sem processo residente. Confirmar na prática se a aba Code do Desktop dispara esses hooks.
2. Rede de segurança: `FileSystemWatcher` em modo bandeja, com debounce (ex.: 2 min sem escrita), ou tarefa agendada a cada N minutos.
3. Commit local sempre. Push quando houver rede; se falhar, tenta de novo na próxima rodada. Nunca perder porque o remoto está fora.

**Tamanho do repositório: principal risco técnico**
- Transcripts são JSONL append-only e grandes (aqui 157 MB; um arquivo com 37 MB). Commitar o arquivo inteiro a cada turno faz o repo crescer rápido. O delta do packfile ajuda, mas não resolve sozinho.
- GitHub recusa arquivo acima de 100 MB e avisa acima de 50 MB.
- Solução: fatiar cada transcript em chunks de tamanho fixo (ex.: 2 MB ou N linhas). Como o arquivo só cresce no fim, só o último chunk muda por commit. O restore concatena os chunks.
- Só gravar linhas completas: ignorar a última linha se ela estiver sendo escrita (sem `\n` final ou JSON inválido).
- `git gc` periódico. Git LFS não compensa aqui (cota paga e perde o delta).
- Não comprimir nem criptografar os arquivos antes do commit, porque isso acaba com o delta.

**Segredos e privacidade**
- Repo obrigatoriamente privado.
- Transcripts podem conter tokens e senhas colados no chat ou lidos de arquivos. O push protection do GitHub pode recusar o push inteiro quando detecta um segredo.
- Mitigação: etapa de redação antes do commit (regex para padrões comuns: `ghp_`, `sk-`, `AKIA`, chaves privadas PEM, connection strings), com relatório do que foi mascarado. Opcional: servidor Git próprio (Gitea na rede da empresa) para não depender do GitHub.
- Nunca commitar cookies, tokens e `.credentials.json` (seção 2.4).
- A criptografia do repo inteiro (estilo git-crypt) fica como opção, sabendo que ela mata o delta e faz o repo crescer muito mais.

**Autenticação no remoto**
- HTTPS + Personal Access Token, guardado no Windows Credential Manager (DPAPI). Reaproveitar o Git Credential Manager se estiver instalado.
- SSH: o LibGit2Sharp não tem SSH embutido. Se precisar de SSH, usar o `git.exe` do sistema.

**Restore a partir do Git**
1. Clonar ou dar pull do repo.
2. Escolher a máquina de origem, as sessões e o ponto no tempo (commit ou data).
3. Remontar os transcripts a partir dos chunks e aplicar o remapeamento da seção 3.2 (usuário, drive, `cwd`, conta/org).

**Chats normais (claude.ai) no Git**
- Não há como capturar automaticamente sem usar a API não oficial (ver risco 5.3). O fluxo suportado é o usuário baixar o export oficial de tempos em tempos e o app versioná-lo em `claude-ai-exports/`, extraído como JSON para dar diff.
- A garantia de "nunca perder" é total só para Code/Cowork. Para o chat do claude.ai ela depende do export manual.

---

## 4. Stack .NET portable

### 4.1 Runtime e publicação
- **.NET 10** (SDK 10.0.301 já instalado nesta máquina).
- Publicar self-contained em arquivo único:

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows</TargetFramework>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
  <InvariantGlobalization>false</InvariantGlobalization>
  <ApplicationManifest>app.manifest</ApplicationManifest>
</PropertyGroup>
```

```
dotnet publish -c Release -r win-x64
```

- Gerar também `win-arm64` (Windows on ARM).
- Tamanho esperado: WinForms ~60–70 MB comprimido; WPF ~70–90 MB; Avalonia ~40–60 MB.
- **Native AOT**: não suporta WinForms/WPF. Só serve com Avalonia (com ressalvas) ou com a versão CLI.
- Portable de verdade: config e logs ao lado do `.exe` (`settings.json` local), nada no registro, sem instalar nada.
- `app.manifest`: `asInvoker` (não exige admin; tudo fica no perfil do usuário), DPI awareness `PerMonitorV2`, long paths habilitado.

### 4.2 UI — escolher uma
| Opção | Prós | Contras |
|---|---|---|
| **WinForms** | Mais simples, single-file estável, rápido | Visual datado |
| **WPF** | Visual melhor, MVVM, `WebView2` para render de chat | Exe maior |
| **Avalonia 11** | Cross-platform (macOS/Linux no futuro), AOT possível | Curva de aprendizado; macOS usa outros caminhos (`~/Library/Application Support/Claude`) |

Recomendação: **WPF + CommunityToolkit.Mvvm**, ou Avalonia se houver chance de Mac. Um projeto **Core (class library)** separado + **CLI** (`System.CommandLine`) permite backup agendado sem UI.

Render do visualizador: `Microsoft.Web.WebView2` depende do runtime Edge WebView2. Ele já vem no Windows 10/11, mas é uma dependência externa. Alternativa 100% embutida: renderizar HTML (Markdig) e abrir no navegador padrão, ou usar um controle de texto rico.

### 4.3 Bibliotecas (todas NuGet, embutidas no exe)
| Necessidade | Pacote |
|---|---|
| JSON / JSONL streaming | `System.Text.Json` (built-in) — `Utf8JsonReader`, `JsonDocument`, leitura por linha com `StreamReader` |
| Git | `LibGit2Sharp` (embute o `libgit2` nativo; funciona em single-file com `IncludeNativeLibrariesForSelfExtract`; HTTPS sim, SSH não). Fallback: `git.exe` do sistema se existir. |
| Credencial do Git | `Meziantou.Framework.Win32.CredentialManager` ou P/Invoke direto em `CredWrite`/`CredRead` |
| ZIP | `System.IO.Compression` (built-in) |
| ZIP com senha (AES) | `SharpZipLib` ou criptografia própria do stream com `AesGcm` (built-in) |
| Hash | `System.Security.Cryptography.SHA256` (built-in) |
| Índice / busca | `Microsoft.Data.Sqlite` (inclui o `e_sqlite3` nativo; FTS5 suportado) |
| Markdown → HTML | `Markdig` |
| Syntax highlight | `ColorCode.Core` |
| PDF export | `QuestPDF` (licença Community; verificar se a empresa se enquadra) |
| CLI | `System.CommandLine` |
| MVVM | `CommunityToolkit.Mvvm` |
| Logs | `Serilog` + `Serilog.Sinks.File` |
| Leitura do LevelDB (opcional, forense) | Não há pacote .NET maduro; precisaria de parser próprio (formato SST + Snappy: `Snappier`). Baixa prioridade. |

### 4.4 Integração com o Windows (sem dependências extras)
- Detectar o processo: `Process.GetProcessesByName("claude")`, filtrando pelo caminho do pacote, porque o CLI `claude.exe` também existe.
- Fechar com elegância: `CloseMainWindow()`, aguardar e só então oferecer `Kill`.
- Pasta do pacote MSIX: enumerar `%LOCALAPPDATA%\Claude-Data\claude_*` e `%LOCALAPPDATA%\Packages\Claude_*`.
- Agendamento: `schtasks.exe /Create ...` apontando para `ClaudeChatBackup.exe --backup --silent --dest X`.

---

## 5. Riscos e cuidados

1. **Formato não documentado.** Os JSONs do Desktop e o JSONL do Code são internos e mudam entre versões. Mitigação: o backup copia arquivos brutos (byte a byte) e o parsing serve só para UI/preview e remapeamento. Gravar a versão do Claude no manifesto e avisar no restore quando ela for diferente.
2. **App aberto.** O Claude reescreve os JSONs de sessão ao fechar, o que pode anular um restore. Exigir app fechado no restore.
3. **Chat do claude.ai não volta para o servidor.** Deixar claro na UI. Não usar a API interna (`claude.ai/api/organizations/{org}/chat_conversations`) com cookie extraído: não é oficial, pode violar os termos, quebra sem aviso e exige descriptografar o cookie DPAPI, ou seja, manipular credencial. Ficar no export oficial.
4. **Dados sensíveis.** Transcripts contêm código, caminhos, possivelmente segredos colados no chat. Oferecer criptografia do backup e avisar ao salvar em nuvem/pendrive.
5. **Arquivos grandes.** Transcripts de dezenas de MB. Usar streaming e progresso com cancelamento (`IProgress<T>`, `CancellationToken`).
6. **Caminhos longos.** Pastas codificadas podem passar de 260 caracteres. Habilitar `longPathAware` no manifest.
7. **Conta diferente.** Restaurar sessões de outra org/conta pode não aparecer ou gerar conflito. Detectar e alertar.
8. **Licenciamento.** Revisar as licenças dos pacotes para distribuição (QuestPDF em especial).

---

## 6. Estrutura sugerida da solução

```
ClaudeChatBackup.sln
├─ src/ClaudeChatBackup.Core        # descoberta de caminhos, modelos, backup/restore, manifesto, remap
├─ src/ClaudeChatBackup.Viewer      # parser JSONL/export oficial, indexação SQLite, render HTML
├─ src/ClaudeChatBackup.Cli         # System.CommandLine: backup | restore | list | verify
├─ src/ClaudeChatBackup.App         # WPF (ou Avalonia)
└─ tests/ClaudeChatBackup.Tests     # xUnit + fixtures com cópias anonimizadas dos JSON/JSONL
```

## 7. Fases sugeridas

1. **MVP (básico funcional)**: Core + CLI. Descobrir caminhos, listar sessões, backup ZIP com manifesto e hashes, restore com checagem do app fechado, rollback automático e remapeamento entre máquinas (usuário, drive, `cwd`).
2. **UI**: lista de sessões, backup/restore seletivo.
3. **Visualizador**: JSONL do Code + import do export oficial do claude.ai, busca FTS, export HTML/MD/PDF.
4. **Git (futuro, seção 3.5)**: comando `sync` (snapshot, chunking, redação de segredos, commit, push), hook `Stop`/`SessionEnd`, tarefa agendada, restore a partir do repo.
5. **Extras**: criptografia, build ARM64, versão macOS (Avalonia).

## 8. Pendências para confirmar antes de codar

- Formato interno de `local-agent-mode-sessions` (Cowork): ainda não detalhado.
- Se o Claude Desktop relê `claude-code-sessions` ao iniciar ou mantém outro índice (testar: restaurar uma sessão apagada e abrir o app).
- Comportamento dos marcadores `deleted_<id>`: remover o marcador ressuscita a sessão?
- Estrutura atual do zip do export oficial do claude.ai (baixar um export real como fixture).
- Se as sessões da aba Code do Desktop disparam os hooks `Stop`/`SessionEnd` do `settings.json`.
- Onde hospedar o repo (GitHub privado, Azure DevOps ou Gitea interno) e se o push protection do provedor vai bloquear transcripts.
- Tamanho real de crescimento: medir uma semana de uso com chunking para estimar o tamanho do repo por mês.
