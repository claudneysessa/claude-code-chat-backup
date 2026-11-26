# AGENTS.md — instruções para agentes de IA neste repositório

## Propósito do projeto

Ferramenta em .NET para fazer **backup e restauração das sessões do Claude Desktop** (aba Code e Cowork) no Windows, inclusive levando de um computador para outro. Tem **interface visual** (WinForms — sem argumentos, `gui` ou com um `.zip` arrastado para o exe) e **linha de comando** (com argumentos). As conversas da aba Chat (claude.ai) ficam no servidor da Anthropic e estão fora do escopo.

Contexto técnico de referência (onde o Claude guarda cada coisa, formato dos arquivos, riscos): ver `docs/LEVANTAMENTO.md`. Não há documentação oficial envolvida — tudo foi obtido por inspeção local do Claude Desktop (MSIX) e do Claude Code.

## Stack

- .NET 10 (SDK 10.0.x), C# moderno, WinForms (`src/`).
- xUnit para testes (`tests/`).
- Sem dependências de runtime além da BCL: ZIP com `System.IO.Compression`, JSON com `System.Text.Json`, hash com `System.Security.Cryptography`.

## Estrutura principal

```
./
├── AGENTS.md / CLAUDE.md    # este arquivo
├── CHANGELOG.md             # registro contínuo de fases concluídas
├── README.md                # documentação pública em inglês
├── README.pt-BR.md          # espelho em português do README.md
├── LICENSE                  # MIT
├── CONTRIBUTING.md / SECURITY.md / CODE_OF_CONDUCT.md
├── .github/
│   ├── workflows/release.yml  # tag vX.Y.Z → testes, exe portable e release no GitHub
│   └── release-notes.md       # modelo das notas da release ({VERSION}, {SHA256})
├── docs/
│   └── LEVANTAMENTO.md      # onde o Claude guarda os dados e decisões do projeto
├── src/                     # código-fonte — SEM subpastas de projeto (sem src/<Projeto>/)
│   ├── ClaudeChatBackup.csproj
│   ├── Program.cs           # entrada híbrida: GUI (sem args/gui/.zip) ou CLI (anexa console)
│   ├── Domain/              # modelos e regras de layout do backup (puro, sem I/O)
│   ├── Application/         # casos de uso: BackupService, RestoreService
│   ├── Infrastructure/      # descoberta de pastas, catálogo, zip, remapeamento, processo do Claude, pasta de backups
│   ├── Cli/                 # parsing de argumentos e apresentação (pt-BR)
│   ├── Gui/                 # interface visual: Theming (tema escuro, controles, toasts/diálogos), Views (telas), ViewModels (lógica testável)
│   └── assets/              # PNG de origem + claude-chat-backup.ico (gerado)
├── tests/
│   └── ClaudeChatBackup.Tests/  # testes xUnit
├── build-icon.ps1           # regenera src\assets\claude-chat-backup.ico
├── build-release.ps1        # publica release em dist/release
├── build-portable.ps1       # publica portable (self-contained) em dist/portable
└── dist/                    # saída dos scripts (não versionado)
```

## Onde o Claude guarda o que a ferramenta copia

| Dado | Caminho | Observação |
|---|---|---|
| Metadados das sessões do Code (título, pasta, modelo) | `%LOCALAPPDATA%\Claude-Data\claude_*\claude-code-sessions\<conta>\<org>\local_*.json` | MSIX. Instalação clássica: `%APPDATA%\Claude` |
| Sessões do Cowork | `...\local-agent-mode-sessions\` | formato não detalhado; copiado byte a byte |
| Transcripts (mensagens) | `%USERPROFILE%\.claude\projects\<cwd-codificado>\<cliSessionId>.jsonl` | `cwd` codificado: todo caractere não alfanumérico vira `-` |
| Resultados de ferramentas, histórico de arquivos | `projects\<dir>\<id>\tool-results\`, `.claude\file-history\<id>\` | restaurados sem alteração |

Nunca entram no backup: `config.json` do Desktop (cache do token OAuth), `ccd-ids.json` (salt do dispositivo), `.claude\.credentials.json`, cookies, caches e binários.

## Comandos

- Restaurar/buildar: `dotnet build ClaudeChatBackup.slnx`
- Testes: `dotnet test ClaudeChatBackup.slnx`
- Publicar portable: `powershell -ExecutionPolicy Bypass -File .\build-portable.ps1`
- Publicar release: `powershell -ExecutionPolicy Bypass -File .\build-release.ps1`
- Lançar versão: subir `<Version>` em `src/ClaudeChatBackup.csproj`, registrar no CHANGELOG, commitar, então `git tag vX.Y.Z` e `git push origin vX.Y.Z`. O GitHub Actions roda os testes, gera o portable e cria a release; tag diferente do `<Version>` falha de propósito.
- Regenerar ícone: `powershell -ExecutionPolicy Bypass -File .\build-icon.ps1`
- Rodar a partir do código: `dotnet run --project src\ClaudeChatBackup.csproj -- <comandos>` (sem argumentos abre a janela)

## Convenções de código

- SOLID + DDD: Domain puro (sem I/O), Application orquestra casos de uso, Infrastructure implementa I/O, Cli e Gui apresentam.
- TDD: escrever/rodar o teste antes da implementação; só commitar estado verde.
- Nomes de tipos/membros em inglês; mensagens de interface (CLI e GUI) em pt-BR.
- Conventional Commits: `feat:`, `fix:`, `docs:`, `chore:`, `test:`.
- XML docs apenas em membros públicos com propósito não óbvio; sem comentários desnecessários.
- Sem subpastas de projeto dentro de `src/`.
- GUI: tema escuro fixo, toda cor vem de `Gui/Theming/Theme.cs`; nunca `MessageBox` (usar `ToastHost` e `DarkDialog`/`ErrorDialog`).
- Seguir as convenções do arquivo editado.

## Fluxo de trabalho com Git

1. Dividir o trabalho em fases pequenas, coerentes e verificáveis.
2. Ao concluir cada fase: validar (`dotnet build`/`dotnet test`) → atualizar `CHANGELOG.md` → revisar os arquivos → commit único da fase.
3. Não agrupar alterações independentes no mesmo commit.
4. Não fazer push, não criar nem alterar remote sem autorização explícita.
5. Não reescrever o histórico Git.

## Obrigação de atualizar o CHANGELOG.md

Cada fase concluída entra no `CHANGELOG.md` antes do commit, com data e hora `YYYY-MM-DD HH:mm:ss Z`, descrição objetiva, arquivos/áreas afetadas, validações realizadas e limitações conhecidas. Nunca registrar como concluído o que não foi implementado ou validado.

## Regras de segurança da ferramenta (não negociáveis)

- **Nunca** restaurar sobre os dados reais do Claude durante desenvolvimento/testes — usar `--desktop-dir`, `--claude-dir` e `--home` apontando para cópias.
- Restaurar é bloqueado com o Claude Desktop aberto (ele regrava as sessões ao sair); a GUI oferece fechá-lo, a CLI exige `--close-claude` ou `--force`.
- Todo restore verifica o SHA-256 de cada arquivo antes de gravar qualquer coisa; backup corrompido não restaura nada.
- Arquivos sobrescritos são copiados antes para `antes-da-restauracao-<data>.zip` na pasta de backups (restaurável pela própria ferramenta).
- Remapeamento de caminhos só troca valores JSON que **começam** com a pasta mapeada; texto livre das conversas não é alterado; linhas sem nada a trocar mantêm os bytes originais.
- Backups ficam em `backups\` **ao lado do executável** (fallback: `%LOCALAPPDATA%\ClaudeChatBackup\backups`); a pasta só é criada quando um backup é gravado.
- Backups contêm as conversas: nunca versionar (`backups/` no `.gitignore`).

## Decisões, restrições e limitações relevantes

- Stack: .NET 10 + WinForms + xUnit (2026-11-10), no mesmo padrão do app `opencode-projects-admin` do autor.
- Sincronização com Git está planejada (ver `docs/LEVANTAMENTO.md`, seção 3.5), **não** implementada.
- Formato dos arquivos do Claude não é documentado e pode mudar entre versões; o backup guarda arquivos brutos e a versão do Claude no manifesto.
- Licença MIT (2026-11-26). Repositório público: `claudneysessa/claude-code-chat-backup`, branch `main`.
- `README.md` (inglês) e `README.pt-BR.md` mudam juntos, no mesmo commit da mudança de comportamento.
- Exemplos em código, testes e documentos usam caminhos e nomes fictícios (`D:\Work`, `C:\Users\ana`); nunca nomes reais de máquina, cliente, empresa ou sessão.
- Fuso local: UTC-03:00.

## Escopo

Não alterar o escopo do projeto nem criar infraestrutura (pipelines, CI, serviços externos, dependências) sem autorização explícita. Preservar arquivos e alterações existentes do usuário; não usar comandos destrutivos.
