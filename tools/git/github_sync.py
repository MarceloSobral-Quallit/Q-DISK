#!/usr/bin/env python
# -*- coding: utf-8 -*-
# ─────────────────────────────────────────────────────────────────────────────
# qdisk  |  Tools / Git  |  github_sync.py
# Script genérico de sincronização com GitHub, reutilizável em qualquer projeto.
#
# Configuração: config/github_sync.ini (raiz do projeto; antes ficava em tools/git/).
# Se [git] remote estiver vazio, tenta obter a URL em tools/git/git-details.txt.
#
# Uso:
#   python tools/git/github_sync.py
#   python tools/git/github_sync.py --no-push
#   python tools/git/github_sync.py --rebuild-git
#   python tools/git/github_sync.py --dry-run
#
# Autenticação: Git Credential Manager (Windows). Na primeira execução com
# push, o Windows abrirá um popup de login. Credenciais salvas automaticamente.
#
# © Quallit — Projeto proprietário. Todos os direitos reservados.
# ─────────────────────────────────────────────────────────────────────────────

from __future__ import annotations

import argparse
import configparser
import os
import re
import shutil
import subprocess
import sys
from datetime import datetime
from pathlib import Path

# ---------------------------------------------------------------------------
# Caminhos
# ---------------------------------------------------------------------------
SCRIPT_DIR   = Path(__file__).resolve().parent
# Raiz do repositório (tools/git/ → dois níveis acima)
PROJECT_ROOT = SCRIPT_DIR.parent.parent
# config/github_sync.ini — padrão Quallit atual (mesmo local em todos os
# projetos, alinhado com o config.ini principal). Antes vivia em tools/git/.
CONFIG_FILE  = PROJECT_ROOT / "config" / "github_sync.ini"
GIT_DETAILS  = SCRIPT_DIR / "git-details.txt"

# ---------------------------------------------------------------------------
# Leitura de configuração
# ---------------------------------------------------------------------------
_cfg = configparser.ConfigParser()
_cfg.read(CONFIG_FILE, encoding="utf-8")

def _c(key: str, fallback: str = "") -> str:
    return _cfg.get("git", key, fallback=fallback).strip()


def _remote_from_git_details(path: Path) -> str:
    """Extrai a primeira URL https://github.com/... de git-details.txt."""
    if not path.is_file():
        return ""
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""
    m = re.search(r"https://github\.com/[\w.-]+/[\w.-]+(?:\.git)?", text)
    return m.group(0).strip() if m else ""


def _project_from_remote(url: str) -> str:
    if not url:
        return ""
    m = re.search(r"github\.com/[^/\s]+/([^/\s]+?)(?:\.git)?$", url, re.I)
    return m.group(1) if m else ""


REMOTE = _c("remote") or _remote_from_git_details(GIT_DETAILS)
BRANCH = _c("branch") or "main"
USER_NAME = _c("username")
USER_EMAIL = _c("email")
PROJECT_LABEL = _c("project") or _project_from_remote(REMOTE) or PROJECT_ROOT.name
DEFAULT_COMMIT_MESSAGE = _c("default_commit_message")
# Sincronização ligada/desligada explicitamente pela config — não mais inferida
# do nome da pasta do projeto. Padrão true (retrocompatível: configs antigas
# sem essa chave continuam sincronizando como sempre fizeram).
ACTIVE = _c("active", "true").strip().lower() in ("true", "1", "yes", "sim")

# Ambiente limpo para git (sem traces verbosos)
_GIT_ENV = os.environ.copy()
for _k in ("GIT_TRACE", "GIT_TRACE_PERFORMANCE", "GIT_TRACE_SETUP"):
    _GIT_ENV.setdefault(_k, "0")

# ---------------------------------------------------------------------------
# Helpers de log e cor
# ---------------------------------------------------------------------------
_IS_WIN = sys.platform == "win32"
if _IS_WIN:
    os.system("")  # habilita ANSI no Windows

class _C:
    R = "\033[0m"
    GREEN  = "\033[92m"
    CYAN   = "\033[96m"
    YELLOW = "\033[93m"
    RED    = "\033[91m"
    BOLD   = "\033[1m"

def _log(msg: str, level: str = "INFO") -> None:
    tag = {
        "INFO":  f"{_C.CYAN}[INFO ]{_C.R}",
        "OK":    f"{_C.GREEN}[OK   ]{_C.R}",
        "WARN":  f"{_C.YELLOW}[AVISO]{_C.R}",
        "ERROR": f"{_C.RED}[FALHA]{_C.R}",
    }.get(level, f"[{level}]")
    print(f"  {tag} {msg}")

def _short(url: str) -> str:
    if not url:
        return "(sem remote)"
    m = re.search(r"[:/]([^/\s]+/[^/\s]+?)(?:\.git)?$", url)
    return m.group(1) if m else url

# ---------------------------------------------------------------------------
# Wrappers git
# ---------------------------------------------------------------------------
def _git(args: list[str], *, capture: bool = False,
         check: bool = False) -> subprocess.CompletedProcess:
    return subprocess.run(
        ["git"] + args,
        cwd=str(PROJECT_ROOT),
        capture_output=capture,
        text=True,
        env=_GIT_ENV,
        check=check,
    )

# ---------------------------------------------------------------------------
# Operações
# ---------------------------------------------------------------------------
def _ensure_repo() -> None:
    if not (PROJECT_ROOT / ".git").exists():
        _log("Inicializando repositório git...", "INFO")
        _git(["init"], check=True)
        _git(["branch", "-M", BRANCH])

    if _IS_WIN:
        _git(["config", "credential.helper", "manager"])

    existing_name  = _git(["config", "--get", "user.name"],  capture=True).stdout.strip()
    existing_email = _git(["config", "--get", "user.email"], capture=True).stdout.strip()

    if not existing_name:
        if USER_NAME:
            _git(["config", "user.name", USER_NAME])
            _log(f"user.name = {USER_NAME}", "INFO")
        else:
            _log("user.name não configurado em git_sync.ini → [git] username", "WARN")

    if not existing_email:
        if USER_EMAIL:
            _git(["config", "user.email", USER_EMAIL])
            _log(f"user.email = {USER_EMAIL}", "INFO")
        else:
            _log("user.email não configurado em git_sync.ini → [git] email", "WARN")


def _ensure_remote() -> None:
    if not REMOTE:
        _log("Remote não configurado em git_sync.ini → [git] remote", "WARN")
        return
    has = _git(["remote", "get-url", "origin"], capture=True).returncode == 0
    if not has:
        _log(f"Adicionando remote origin: {_short(REMOTE)}", "INFO")
        _git(["remote", "add", "origin", REMOTE])
    else:
        current = _git(["remote", "get-url", "origin"], capture=True).stdout.strip()
        if current != REMOTE:
            _log(f"Atualizando remote origin: {_short(REMOTE)}", "INFO")
            _git(["remote", "set-url", "origin", REMOTE])


def _has_any_commit() -> bool:
    """Retorna True se o repositório já tem ao menos um commit."""
    return _git(["rev-parse", "HEAD"], capture=True).returncode == 0


def _has_changes() -> bool:
    return bool(_git(["status", "--porcelain"], capture=True).stdout.strip())


def _commit(message: str) -> bool:
    _git(["add", "-A"], check=True)
    r = _git(["commit", "-m", message], capture=True)
    if r.returncode == 0:
        _log("Commit criado.", "OK")
        return True
    out = (r.stdout + r.stderr).strip()
    if "nothing to commit" in out:
        _log("Nada para commitar.", "INFO")
    else:
        _log(f"git commit: {out}", "WARN")
    return False


def _prompt_push_conflict() -> str:
    """Pergunta ao usuário o que fazer quando o remote está à frente do local.
    Retorna: 'force' | 'pull' | 'cancel'
    """
    print()
    print(f"  {_C.YELLOW}[AVISO]{_C.R} O remote possui commits que não existem localmente.")
    print()
    print(f"  {_C.BOLD}Como deseja prosseguir?{_C.R}")
    print(f"  {_C.CYAN}[1]{_C.R} Sobrescrever o remote com a versão local  {_C.YELLOW}(force-push){_C.R}")
    print(f"  {_C.CYAN}[2]{_C.R} Baixar as alterações do remote e depois enviar  {_C.CYAN}(pull + push){_C.R}")
    print(f"  {_C.CYAN}[q]{_C.R} Cancelar")
    print()
    try:
        choice = input("  > ").strip().lower()
    except (KeyboardInterrupt, EOFError):
        print()
        return "cancel"
    if choice == "1":
        return "force"
    if choice == "2":
        return "pull"
    return "cancel"


def _push() -> bool:
    if not REMOTE:
        _log("Push ignorado: remote não configurado.", "WARN")
        return False
    _log(f"Enviando para {_short(REMOTE)} ({BRANCH})...", "INFO")
    r = _git(["push", "-u", "origin", BRANCH], capture=True)
    if r.returncode == 0:
        _log(f"Push para {_short(REMOTE)} concluído.", "OK")
        return True

    output = (r.stdout + r.stderr).strip()

    # Rejeição por remote à frente (fetch first / non-fast-forward)
    if "fetch first" in output or "non-fast-forward" in output or "[rejected]" in output:
        _log(f"Push rejeitado: o remote contém commits não presentes localmente.", "WARN")
        choice = _prompt_push_conflict()
        print()
        if choice == "force":
            _log(f"Forçando push para {_short(REMOTE)} ({BRANCH})...", "INFO")
            p = _git(["push", "-u", "origin", BRANCH, "--force"], capture=True)
            if p.returncode == 0:
                _log(f"Force-push concluído.", "OK")
                return True
            _log(f"Force-push falhou: {(p.stdout + p.stderr).strip()}", "ERROR")
            return False
        elif choice == "pull":
            _log(f"Baixando alterações do remote ({BRANCH})...", "INFO")
            pl = _git(["pull", "--rebase", "origin", BRANCH], capture=True)
            if pl.returncode != 0:
                _log(f"Pull falhou: {(pl.stdout + pl.stderr).strip()}", "ERROR")
                _log("Resolva os conflitos manualmente e tente novamente.", "WARN")
                return False
            _log("Pull concluído. Enviando...", "OK")
            p2 = _git(["push", "-u", "origin", BRANCH], capture=True)
            if p2.returncode == 0:
                _log(f"Push para {_short(REMOTE)} concluído.", "OK")
                return True
            _log(f"Push falhou após pull: {(p2.stdout + p2.stderr).strip()}", "ERROR")
            return False
        else:
            _log("Push cancelado pelo usuário.", "WARN")
            return False

    _log(f"Push falhou: {output}", "WARN")
    _log("Verifique credenciais (Git Credential Manager) e permissões.", "WARN")
    return False

# ---------------------------------------------------------------------------
# Fluxo principal
# ---------------------------------------------------------------------------
def sync(message: str, *, no_push: bool, dry_run: bool) -> None:
    _ensure_repo()
    _ensure_remote()

    if not _has_changes():
        _log("Nenhuma alteração pendente. Tudo sincronizado.", "OK")
        return

    if dry_run:
        lines = _git(["status", "--short"], capture=True).stdout.strip().splitlines()
        _log("Arquivos que seriam commitados:", "INFO")
        for line in lines:
            print(f"        {line}")
        _log("Dry-run: nenhuma ação executada.", "WARN")
        return

    if not _commit(message):
        return

    if no_push:
        _log("Commit criado localmente. Push ignorado (--no-push).", "INFO")
    else:
        _push()


def rebuild_git(message: str, *, dry_run: bool) -> None:
    if dry_run:
        _log("Dry-run: --rebuild-git não executado.", "WARN")
        return
    if not REMOTE:
        _log("--rebuild-git requer [git] remote configurado em git_sync.ini.", "ERROR")
        return

    _log("Zerando histórico local e recriando repositório...", "WARN")
    git_dir = PROJECT_ROOT / ".git"
    if git_dir.exists():
        try:
            shutil.rmtree(git_dir)
            _log(".git removido.", "INFO")
        except Exception as exc:
            _log(f"Falha ao remover .git: {exc}", "ERROR")
            return

    _git(["init"], check=True)
    _git(["branch", "-M", BRANCH])
    if _IS_WIN:
        _git(["config", "credential.helper", "manager"])
    if USER_NAME:
        _git(["config", "user.name",  USER_NAME])
    if USER_EMAIL:
        _git(["config", "user.email", USER_EMAIL])

    _git(["add", "-A"], check=True)
    r = _git(["commit", "-m", message], capture=True)
    if r.returncode != 0:
        _log(f"Falha no commit: {(r.stdout + r.stderr).strip()}", "ERROR")
        return
    _log("Commit inicial criado.", "OK")

    _git(["remote", "add", "origin", REMOTE])
    _log(f"Force-push para {_short(REMOTE)} ({BRANCH})...", "INFO")
    p = _git(["push", "-u", "origin", BRANCH, "--force"], capture=True)
    if p.returncode == 0:
        _log(f"Force-push concluído.", "OK")
    else:
        _log(f"Force-push falhou: {(p.stdout + p.stderr).strip()}", "WARN")

# ---------------------------------------------------------------------------
# Prompt interativo
# ---------------------------------------------------------------------------
def _default_message_if_blank() -> str:
    if DEFAULT_COMMIT_MESSAGE and not _has_any_commit():
        return DEFAULT_COMMIT_MESSAGE
    ts = datetime.now().strftime("%Y-%m-%d %H:%M")
    return f"{PROJECT_LABEL} - sync {ts}"


def _prompt_message() -> str | None:
    """Exibe prompt para descrição das alterações. Retorna None se cancelado."""
    print()
    print(f"  {_C.BOLD}Descrição das alterações{_C.R}")
    if DEFAULT_COMMIT_MESSAGE and not _has_any_commit():
        hint = f"Enter para usar: {_C.BOLD}{DEFAULT_COMMIT_MESSAGE}{_C.R}"
    else:
        hint = f"Enter para timestamp automático ({PROJECT_LABEL} - sync …)"
    print(f"  {_C.CYAN}({hint} | 'q' para cancelar){_C.R}")
    print()
    try:
        answer = input("  > ").strip()
    except (KeyboardInterrupt, EOFError):
        print()
        return None

    if answer.lower() in ("q", "quit", "exit", "cancelar"):
        return None

    if not answer:
        return _default_message_if_blank()

    return answer

# ---------------------------------------------------------------------------
# Entrypoint
# ---------------------------------------------------------------------------
def main() -> None:
    parser = argparse.ArgumentParser(
        description="Sincronização genérica com GitHub."
    )
    parser.add_argument("--no-push",      action="store_true",
                        help="Commit local sem push.")
    parser.add_argument("--rebuild-git",  action="store_true",
                        help="Zera histórico e faz force-push.")
    parser.add_argument("--dry-run",      action="store_true",
                        help="Mostra o que seria feito, sem executar.")
    args = parser.parse_args()

    if not CONFIG_FILE.exists():
        print(f"\n  {_C.RED}[FALHA]{_C.R} github_sync.ini não encontrado em: {CONFIG_FILE}")
        print(f"  Crie {CONFIG_FILE.name} em {CONFIG_FILE.parent} (copie de config/github_sync.ini.example).")
        sys.exit(1)

    if not ACTIVE:
        print()
        print(f"  {_C.YELLOW}[AVISO]{_C.R} Sincronização desativada — [git] active = false em {CONFIG_FILE}.")
        print(f"  Ajuste essa chave para 'true' quando este projeto estiver pronto para publicar no GitHub.")
        print()
        return

    print()
    print("=" * 56)
    print(f"  {_C.BOLD}{PROJECT_LABEL}{_C.R} — sincronização GitHub")
    print("=" * 56)
    print(f"  Projeto : {PROJECT_ROOT}")
    print(f"  Remote  : {_short(REMOTE)}")
    print(f"  Branch  : {BRANCH}")
    if args.dry_run:
        print(f"  {_C.YELLOW}Modo    : DRY-RUN{_C.R}")
    elif args.rebuild_git:
        print(f"  {_C.YELLOW}Modo    : REBUILD-GIT (force-push){_C.R}")
    elif args.no_push:
        print(f"  Modo    : NO-PUSH (somente commit local)")

    # Prompt de descrição (exceto dry-run, que não precisa)
    if args.dry_run:
        message = _default_message_if_blank()
    else:
        message = _prompt_message()
        if message is None:
            print()
            _log("Operação cancelada pelo usuário.", "WARN")
            print()
            return

    print()
    _log(f'Commit: "{message}"', "INFO")
    print()

    os.chdir(PROJECT_ROOT)

    if args.rebuild_git:
        rebuild_git(message, dry_run=args.dry_run)
    else:
        sync(message, no_push=args.no_push, dry_run=args.dry_run)

    print()
    print("=" * 56)
    print()


if __name__ == "__main__":
    main()
