# tools/git — sincronização com o GitHub

Scripts para publicar o qdisk no GitHub e para recuperar uma cópia do projeto a partir do backup em rede.

## Arquivos

| Arquivo | Descrição |
|---|---|
| `github_sync.py` | Faz `git add -A`, commit e push do projeto inteiro para o repositório configurado |
| `git-details.txt` | URL do repositório, usada quando `[git] remote` está vazio no `.ini` |
| `restore_from_backup.ps1` | Copia o projeto do compartilhamento de backup para a pasta local de sincronização e, em seguida, chama o `github_sync.py` |
| `../../config/github_sync.ini` | Configuração do `github_sync.py`. Fica fora do Git (`.gitignore`); o modelo é `config/github_sync.ini.example` |

---

## Configuração (`config/github_sync.ini`)

Copie `config/github_sync.ini.example` para `config/github_sync.ini` e ajuste os valores. Todas as chaves ficam na seção `[git]`:

```ini
[git]
active   = true
remote   = https://github.com/MarceloSobral-Quallit/Q-DISK.git
branch   = main
username = INSIRA_SEU_USUARIO_GITHUB
email    = INSIRA_SEU_EMAIL
project  = qdisk
default_commit_message =
```

| Chave | Descrição |
|---|---|
| `active` | Liga ou desliga o script para esta cópia do projeto. Aceita `true`, `1`, `yes` ou `sim` (sem diferenciar maiúsculas); qualquer outro valor desliga. Sem a chave, vale `true`. Desligado, o script mostra um aviso e termina sem fazer nada, inclusive com `--dry-run`. |
| `remote` | URL do repositório. Se vazia, o script usa a primeira URL `https://github.com/<dono>/<repositório>` encontrada em `git-details.txt`. |
| `branch` | Branch de destino. Padrão: `main`. |
| `username` | Gravado em `git config user.name` (configuração local do repositório) somente se `git config --get user.name` não devolver nada. |
| `email` | Gravado em `git config user.email` somente se `git config --get user.email` não devolver nada. |
| `project` | Rótulo mostrado no cabeçalho e usado na mensagem automática de commit. Se vazio, usa o nome do repositório tirado da URL e, sem URL, o nome da pasta do projeto. |
| `default_commit_message` | Mensagem usada quando o usuário deixa a descrição em branco e o repositório ainda não tem nenhum commit. |

Se `config/github_sync.ini` não existir, o script mostra onde criá-lo e termina com código 1.

---

## Uso

O script trabalha sempre na raiz do projeto (dois níveis acima de `tools/git/`), qualquer que seja a pasta de onde é chamado:

```bash
python tools/git/github_sync.py
```

| Opção | Comportamento |
|---|---|
| (nenhuma) | Pede a descrição, faz `git add -A`, commit e push |
| `--no-push` | Pede a descrição, faz `git add -A` e commit, sem push |
| `--dry-run` | Lista os arquivos alterados (`git status --short`) e não faz commit nem push. Não pede descrição. |
| `--rebuild-git` | Apaga a pasta `.git`, cria um repositório novo com todos os arquivos atuais em um único commit e faz push com `--force`. Exige `remote` configurado. Com `--dry-run` junto, nada é executado. |

---

## Fluxo padrão

1. Mostra o rótulo do projeto, a pasta, o remote e a branch.
2. Pede a descrição do commit:
   - texto digitado: usado como mensagem;
   - Enter em branco, com `default_commit_message` preenchida e repositório sem commits: usa `default_commit_message`;
   - Enter em branco nos demais casos: gera `<project> - sync AAAA-MM-DD HH:MM`;
   - `q`, `quit`, `exit` ou `cancelar`: cancela.
3. Prepara o repositório:
   - se não houver `.git`, executa `git init` e renomeia a branch para a configurada;
   - no Windows, define `credential.helper = manager`;
   - cria o remote `origin` com a URL configurada ou o atualiza se apontar para outra URL.
4. Se não houver alterações, informa que está tudo sincronizado e termina.
5. Executa `git add -A` e `git commit`.
6. Executa `git push -u origin <branch>`.

### Push rejeitado

Quando o push é rejeitado porque o remote tem commits que não existem na cópia local, o script pergunta:

- `1`: sobrescreve o remote com a versão local (`git push --force`);
- `2`: executa `git pull --rebase origin <branch>` e tenta o push de novo; se o pull falhar, os conflitos ficam para resolver manualmente;
- `q` ou qualquer outra tecla: cancela o push (o commit local continua feito).

---

## Autenticação

O script usa HTTPS e, no Windows, o Git Credential Manager (`credential.helper = manager`). Na primeira vez que o Git precisar de credenciais para o push, o Git Credential Manager pede o login no GitHub e guarda a credencial para as próximas execuções.

A leitura de `git-details.txt` reconhece apenas URLs `https://github.com/...`.

---

## `restore_from_backup.ps1`

Conecta ao compartilhamento de backup (`net use`), espelha a pasta do projeto para a pasta local com `robocopy /MIR` e, sem `-NoPush`, executa `tools\git\github_sync.py` no destino.

- Não copia `.git`, `.vs`, `bin`, `obj`, `temp`, `dist`, `tools\log`, `tools\build_log`, arquivos `*.log`, `*.user`, `*.suo`, `Thumbs.db`, `Desktop.ini` nem `github_sync.ini`. O `.git` local, com remote e branch, é preservado.
- Parâmetros: `-BackupHost`, `-BackupUser`, `-BackupPass`, `-BackupShare`, `-BackupSubPath`, `-LocalDest` e `-NoPush`. Os valores padrão estão no bloco `param` do script.
- Senha do compartilhamento, nesta ordem: `-BackupPass`, variável `QUALLIT_BACKUP_PASSWORD`, linha `BackupPassword:` em `config\secret.md` (modelo em `config\secret.md.example`) ou pergunta no terminal.

```powershell
.\tools\git\restore_from_backup.ps1
.\tools\git\restore_from_backup.ps1 -NoPush
```
