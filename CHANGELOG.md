# CHANGELOG

## 2026-11-26 01:24:11 -03:00 — Repositório preparado para publicação open source

**Alterações**
- Licença MIT (`LICENSE`), antes indefinida.
- `README.md` reescrito em inglês e `README.pt-BR.md` novo, espelhando o primeiro; ambos com aviso de que o projeto foi feito em sessões do Claude Code e de que não tem vínculo com a Anthropic.
- `CONTRIBUTING.md`, `SECURITY.md` (o que a ferramenta lê e grava, e por que backups são dados sensíveis) e `CODE_OF_CONDUCT.md`.
- Nomes reais de máquina, empresa, clientes e de uma sessão trocados por exemplos fictícios em testes, comentário de `BackupsViewModel`, `docs/LEVANTAMENTO.md` e neste changelog.
- `AGENTS.md`/`CLAUDE.md`: licença, repositório público, regra dos dois READMEs e regra de exemplos fictícios.

**Arquivos/áreas**: `LICENSE`, `README.md`, `README.pt-BR.md`, `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`, `AGENTS.md`, `CLAUDE.md`, `CHANGELOG.md`, `docs/LEVANTAMENTO.md`, `src/Gui/ViewModels/BackupsViewModel.cs`, `tests/ClaudeChatBackup.Tests/*`.

**Validações**: `dotnet build` sem avisos; `dotnet test`: 45 aprovados com os exemplos trocados.

**Limitações**
- O histórico Git anterior a este commit ainda contém os nomes reais trocados aqui.
- README sem captura de tela: a tela Sessões mostraria conversas reais.

## 2026-11-10 15:00:31 -03:00 — Títulos completos com quebra de linha (v0.6.0)

**Alterações**
- O título das sessões de terminal (primeiro pedido) era cortado em 80 caracteres com "…" já no catálogo. Agora guarda até 300.
- Backups antigos, com o título cortado no manifesto, recuperam o texto completo do primeiro pedido lendo o transcript de dentro do próprio zip ao abrir (diálogo de restauração e `list --from`); o zip não é alterado.
- Tabelas: o título quebra em até 3 linhas e a linha cresce para caber; linhas curtas ficam como antes. Título com mais de 60 caracteres mostra o texto inteiro no tooltip.

**Arquivos/áreas**: `src/Infrastructure/SessionCatalog.cs` (`FirstPrompt(Stream)`, `MaxTitleLength`), `src/Infrastructure/ArchiveReader.cs` (`RecoverFullTitles`), `src/Gui/Theming/Table.cs`, `src/Gui/Views/RestoreDialog.cs`, `src/Cli/App.cs`, testes, versão 0.6.0.

**Validações**
- TDD: 2 testes novos (título longo no catálogo; recuperação a partir de backup com título cortado), o primeiro falhou antes da mudança; `dotnet test`: 45 aprovados; build sem avisos.
- Renderizado com o backup real de 120 sessões: uma sessão com primeiro pedido longo aparece em 3 linhas com o texto recuperado; demais linhas inalteradas.

## 2026-11-10 14:56:26 -03:00 — Restauração começa com tudo desmarcado (v0.5.1)

**Alterações**
- No diálogo de restauração as sessões começam **desmarcadas**, para evitar restaurar sem querer; o usuário marca as que quer (ou usa "Marcar todas" / a caixa do cabeçalho). Texto inicial do plano orienta a marcar antes de analisar.

**Arquivos/áreas**: `src/Gui/Views/RestoreDialog.cs`, versão 0.5.1.

**Validações**: build sem avisos; `dotnet test`: 43 aprovados. Analisar sem nenhuma marcada continua recusando com "Marque ao menos uma sessão para restaurar.".

## 2026-11-10 14:22:24 -03:00 — Diálogo de restauração usável com backups grandes (v0.5.0)

**Alterações**
- Com um backup real de 120 sessões o diálogo ficava espremido: 3 sessões visíveis, botões e trocas de pasta escondidos atrás de rolagem dupla. Agora ele abre quase do tamanho da tela, pode ser redimensionado e maximizado, e tem duas colunas: sessões à esquerda (ocupam a altura toda) e, à direita, "Se o arquivo já existir", "Trocar pastas" e o plano, sempre visível.
- Sessões: busca por título ou pasta, botões "Marcar todas" / "Desmarcar todas" visíveis, contador "N de M marcada(s)" e sessões sem conversa marcadas como "vazia".
- Tabelas (Sessões, Backups, Restaurar): caixa no cabeçalho marca ou desmarca todas (mostra "–" quando só algumas estão marcadas), Shift+clique marca um intervalo, e a roda do mouse rola a lista com o cursor sobre as linhas e botões (antes só rolava com o foco no painel).
- Trocar pastas: cada troca em duas linhas (origem em cima, destino embaixo) para caber na coluna; a sugestão não propõe mais uma pasta-mãe que existe neste computador, só os projetos que faltam.
- Botões do rodapé dos diálogos acompanham o redimensionamento.

**Arquivos/áreas**: `src/Gui/Views/RestoreDialog.cs` (reescrito), `src/Gui/Theming/Table.cs`, `src/Gui/Theming/Dialogs.cs`, `src/Gui/ViewModels/BackupsViewModel.cs`, `tests/ClaudeChatBackup.Tests/ViewModelTests.cs`, versão 0.5.0.

**Validações**
- TDD na sugestão de trocas (teste novo falhou com `["D:\Code"]`, depois verde); `dotnet test`: 43 aprovados; build sem avisos.
- Diálogo renderizado com o backup real `claude-<máquina>-20261110-141320.zip` (120 sessões, 906 MB): Shift+clique marcou 5 sessões, rolagem pela roda deslocou a lista, Analisar mostrou "262 arquivo(s) novo(s)"; nada foi gravado.

**Limitações**
- Restauração desse backup real ainda não executada.

## 2026-11-10 14:07:57 -03:00 — Restaurar sessões escolhidas de um backup (v0.4.0)

**Alterações**
- O diálogo **Restaurar backup** lista cada sessão do pacote (mesmo de um backup "tudo") com caixa de seleção, "Marcar todas" / "Desmarcar todas" e contador. Com todas marcadas, a restauração é completa; com só algumas, entram apenas os arquivos dessas sessões (metadados do Desktop, transcript, resultados de ferramentas e histórico de arquivos) e as configurações compartilhadas do destino ficam como estão.
- O plano agora fica fixo acima dos botões, visível sem rolar, e diz se a restauração é completa ou parcial.
- CLI: `restore --session <id>` (repetível, aceita prefixo do id Desktop ou do Claude Code).
- Core: `RestoreOptions.SessionFilter` e `DataLayout.BelongsToSession`, que decide a que sessão pertence cada arquivo do backup.

**Arquivos/áreas**: `src/Domain/DataLayout.cs`, `src/Application/RestoreService.cs`, `src/Cli/App.cs`, `src/Gui/Views/RestoreDialog.cs`, `tests/ClaudeChatBackup.Tests/BackupRestoreTests.cs`, versão 0.4.0.

**Validações**
- TDD: 10 testes novos escritos antes (falharam por falta de `SessionFilter`/`BelongsToSession`), depois verdes; `dotnet test`: 42 aprovados; build sem avisos.
- Restaurar só uma de duas sessões de um backup completo: cria só os arquivos dela; os da outra, `history.jsonl` e presets não são gravados. Filtro pelo id do Claude Code funciona; id desconhecido é recusado.
- Diálogo conferido por renderização com um backup de 3 sessões, uma desmarcada: "2 de 3 marcada(s)", plano parcial e botão Restaurar habilitado depois de Analisar.

**Limitações**
- Sessões do Cowork não aparecem na lista e não entram numa restauração parcial (só na completa).

## 2026-11-10 14:04:23 -03:00 — Atualizar listas, excluir backup e rodapé legível (v0.3.0)

**Alterações**
- Botão **Atualizar** nas telas Sessões e Backups, e tecla **F5** na janela: relê o disco e tira da tela o que já não existe (backup apagado, sessão nova). Busca e seleção são mantidas; os contadores da barra lateral são recalculados.
- Botão **Excluir** em cada linha da tela Backups, com diálogo de confirmação que mostra o arquivo. O backup vai para a **Lixeira do Windows** (recuperável), não é apagado direto. Funciona também nos `.zip` inválidos.
- Rodapé da barra lateral: em vez do caminho completo cortado, mostra "PASTAS DO CLAUDE" com `Desktop` (só a pasta final) e `Code` (`~\.claude`); caminho completo no tooltip e clique abre no Explorer. A barra de status deixou de repetir o caminho e mostra a versão.
- Colunas da tela Backups reajustadas para caber os três botões.

**Arquivos/áreas**: `src/Gui/MainForm.cs`, `src/Gui/Views/SessionsView.cs`, `src/Gui/Views/BackupsView.cs`, `src/Gui/Views/ConfirmDeleteBackupDialog.cs` (novo), `src/Infrastructure/RecycleBin.cs` (novo), `src/ClaudeChatBackup.csproj` (versão 0.3.0).

**Validações**
- `dotnet build` sem avisos; `dotnet test`: 32 aprovados.
- Telas Sessões e Backups e o diálogo de exclusão conferidos por renderização fora da tela, com um backup válido e um `.zip` inválido.

**Limitações**
- Envio real para a Lixeira não foi exercitado em teste automatizado (deixaria arquivos na Lixeira do usuário); usa `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile` com `SendToRecycleBin`.
- A lista não se atualiza sozinha quando arquivos mudam fora do app; é preciso Atualizar ou F5.

## 2026-11-10 12:58:19 -03:00 — Interface visual e reestruturação no padrão dos apps OpenCode

**Alterações**
- O exe agora abre uma janela (WinForms, tema escuro) quando executado sem argumentos, com `gui` ou com um `.zip` arrastado sobre ele. Com argumentos continua sendo a linha de comando, anexada ao console do terminal.
- Tela **Sessões**: lista as sessões do Code/Cowork com busca, seleção por clique, "Backup das selecionadas" e "Backup de tudo".
- Tela **Backups**: lista os `.zip` da pasta de backups (inválidos aparecem marcados), com Verificar e Restaurar; botões "Abrir backup…" e "Abrir pasta de backups"; aceita `.zip` arrastado para a janela.
- Diálogo **Restaurar backup**: trocas de pasta pré-preenchidas com as pastas do backup que não existem neste computador (agrupadas pela pasta-mãe), escolha do que fazer quando o arquivo já existe, botão Analisar (mostra o plano sem gravar) e Restaurar; oferece fechar o Claude Desktop quando ele está aberto.
- Barra lateral com aviso "Claude Desktop aberto / Pronto para restaurar", atualizado a cada 3 s.
- Pasta de backups: `backups\` ao lado do exe, com fallback para `%LOCALAPPDATA%\ClaudeChatBackup\backups`; a cópia de segurança do restore passou a se chamar `antes-da-restauracao-<data>.zip` e fica na mesma pasta.
- Estrutura: projeto único `src/ClaudeChatBackup.csproj` com `Domain/`, `Application/`, `Infrastructure/`, `Cli/`, `Gui/` (removidos `ClaudeChatBackup.Core` e `ClaudeChatBackup.Cli`); tema copiado de `opencode-projects-admin` com o laranja do Claude.
- Ícone do app (`src/assets`), scripts `build-portable.ps1`, `build-release.ps1`, `build-icon.ps1`; `CLAUDE.md`/`AGENTS.md`; `LEVANTAMENTO.md` movido para `docs/`.
- Tamanhos na CLI passam a usar vírgula decimal.

**Arquivos/áreas**: `src/**`, `tests/ClaudeChatBackup.Tests/**`, `ClaudeChatBackup.slnx`, scripts `.ps1`, `README.md`, `CLAUDE.md`, `AGENTS.md`, `docs/LEVANTAMENTO.md`, `.gitignore`.

**Validações**
- `dotnet build` sem erros nem avisos; `dotnet test`: 32 testes aprovados (7 novos de ViewModels: sugestão de trocas, busca, zip inválido, formatação).
- Portable publicado (`dist\portable\ClaudeChatBackup.exe`, ~49 MB): abre a janela com duplo clique e lista as 21 sessões reais; `ClaudeChatBackup.exe paths` funciona no terminal.
- Telas Sessões, Backups e o diálogo de restauração conferidos por captura de tela; a sugestão de troca foi conferida com um backup simulado de outro computador (`X:\Antigo` agrupando dois projetos).

**Limitações**
- O fluxo completo de restauração pela janela (Analisar → Restaurar) não foi clicado de ponta a ponta; a lógica é a mesma da CLI, coberta pelos testes e pelo teste com dados reais de 2026-10-22.
- Seletor de pasta e "Abrir backup…" usam os diálogos padrão do Windows (claros).
- Sem trimming: WinForms não suporta, por isso o portable tem ~49 MB.

## 2026-10-22 15:40:00 -03:00 — MVP: núcleo + linha de comando

**Alterações**
- Descoberta das pastas do Claude Desktop (MSIX e instalação clássica) e do Claude Code.
- Catálogo de sessões (Desktop ligado ao transcript pelo `cliSessionId`; sessões só de terminal também listadas).
- Backup em ZIP com `manifest.json` (SHA-256 por arquivo, máquina, usuário, versão do Claude, contas/orgs, pastas de projeto); transcripts sendo gravados entram só até a última linha completa.
- Restore com verificação prévia de hash, plano (novo/substituir/ignorar), política de conflito, cópia dos arquivos sobrescritos, remapeamento de perfil, pastas do Claude, `--map` e conta/org, e renomeação de `projects\<dir>` pelo novo `cwd`.
- CLI: `paths`, `list`, `backup`, `verify`, `restore`.

**Validações**
- 25 testes xUnit; backup real (1.212 arquivos, 214 MB → 75 MB) verificado; restore simulado em outra pasta/usuário com troca `D:\Work → E:\Code` conferido linha a linha num transcript de 17.682 linhas.

**Limitações**
- Chats da aba Chat (claude.ai) não estão em disco; fora do escopo.
- Conteúdo do Cowork é copiado, mas não listado.
