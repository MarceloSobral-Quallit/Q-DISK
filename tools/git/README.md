# tools/git — GitHub Sync

Script genérico de sincronização com GitHub, reutilizável em qualquer projeto.

## Arquivos

| Arquivo | Descrição |
|---|---|
| `github_sync.py` | Script principal |
| `../../config/github_sync.ini` | Configuração do repositório, credenciais e permissão de publicação — **nunca no git** |
| `git-details.txt` | Fonte alternativa para a URL do repositório |

---

## Configuração (`config/github_sync.ini`)

```ini
[git]
active   = true
remote   = https://github.com/usuario/REPOSITORIO.git
branch   = main
username = Nome-GitHub
email    = email@dominio.com
project  = NOME-DO-PROJETO
default_commit_message = Initial Release
```

| Chave | Descrição |
|---|---|
| `active` | `true`/`false` — liga/desliga commit e push para **esta cópia**. `false` (ou arquivo ausente) bloqueia qualquer operação, inclusive `--dry-run`. Padrão `true` se a chave não existir. |
| `remote` | URL HTTPS do repositório. Se vazio, lida de `git-details.txt` |
| `branch` | Branch de destino (padrão: `main`) |
| `username` | Nome do usuário Git (usado apenas se não configurado globalmente) |
| `email` | E-mail do usuário Git (usado apenas se não configurado globalmente) |
| `project` | Rótulo exibido nos logs. Se vazio, usa o nome do repositório |
| `default_commit_message` | Mensagem usada no **primeiro commit** ao pressionar Enter em branco. Commits subsequentes geram timestamp automático |

---

## Uso

Execute a partir da **raiz do projeto** ou de qualquer pasta:

```bash
python tools/git/github_sync.py
```

### Modos disponíveis

| Comando | Comportamento |
|---|---|
| `python tools/git/github_sync.py` | Prompt interativo → add + commit + push |
| `python tools/git/github_sync.py --no-push` | Commit local sem push |
| `python tools/git/github_sync.py --rebuild-git` | Zera histórico e faz force-push (uso pontual) |
| `python tools/git/github_sync.py --dry-run` | Mostra o que seria feito, sem executar |

---

## Fluxo padrão

1. Exibe resumo do projeto, remote e branch
2. Solicita a descrição do commit:
   - **Digite uma mensagem** → usa a mensagem digitada
   - **Enter em branco (primeiro commit)** → usa `default_commit_message` do `.ini`
   - **Enter em branco (commits seguintes)** → gera `PROJETO - sync AAAA-MM-DD HH:MM`
   - **`q`** → cancela a operação
3. Executa `git add -A` + `git commit` + `git push`

---

## Autenticação

Este script usa exclusivamente **HTTPS** — não use URLs SSH (`git@github.com:...`).

Utiliza o **Git Credential Manager** (Windows). Na primeira execução com push, o Windows abrirá um popup de login GitHub. As credenciais são armazenadas automaticamente para usos futuros.

> **Por que HTTPS e não SSH?**
> - O GCM gerencia tokens OAuth automaticamente, sem necessidade de gerar/cadastrar chaves SSH
> - A detecção automática de URL em `git-details.txt` reconhece apenas o formato `https://github.com/...`
> - Em ambientes corporativos, HTTPS tende a funcionar sem bloqueios de firewall

---

## `--rebuild-git`

Remove o diretório `.git`, reinicializa o repositório e faz um `force-push` com todos os arquivos atuais em um único commit. Use apenas quando quiser **zerar o histórico** do repositório remoto.

> **Atenção:** esta operação é irreversível no remote.
