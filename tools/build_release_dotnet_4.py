#!/usr/bin/env python
# -*- coding: utf-8 -*-
# -----------------------------------------------------------------------------
# qdisk | Tools | build_release_dotnet_4.py
# Pipeline v4 autocontido: build, dist/update, publicacao fallback, Git installer
# e verificacao remota sem depender de outros scripts do diretorio tools/.
#
# Novidade sobre a v3: controle de verbosidade do console (retorno visual ao
# usuario) adotando os switches/padroes de tools/build_unificado_sign.py
# (projeto RD-DEPLOY-3G), adaptados de Nuitka/scons para dotnet/MSBuild:
#   --show-progress      saida ao vivo (projeto a projeto). Padrao: minima.
#   --show-full-info     saida ao vivo + build MSBuild detalhado (-v detailed).
#   --show-progress-bar  barra compacta (% + projetos) na etapa de publish.
# Sem nenhum desses switches o comportamento e o mais silencioso possivel:
# so os marcos [INFO]/[OK]/[AVISO]/[FALHA] no console, saida completa no log,
# e as ultimas linhas sao mostradas automaticamente se um comando falhar.
# -----------------------------------------------------------------------------

from __future__ import annotations

import argparse
import base64
import contextlib
import datetime
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

TOOLS_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = TOOLS_DIR.parent
LOG_DIR = TOOLS_DIR / "log"
PROPS_FILE = PROJECT_ROOT / "Directory.Build.props"
VERSION_INFO = PROJECT_ROOT / "version_info.txt"
# Segredos locais (fora do Git): uma linha `Chave: valor` por segredo. Modelo: config/secret.md.example
SECRET_FILE = PROJECT_ROOT / "config" / "secret.md"
SECRET_LINE_RE = re.compile(r"^\s*[-*]?\s*([A-Za-z][A-Za-z0-9_]*)\s*:\s*(\S.*?)\s*$")
MISSING_SIGN_PASSWORD_MSG = (
    "Senha de assinatura nao encontrada. Defina SignPassword em config/secret.md "
    "(modelo em config/secret.md.example) ou a variavel QUALLIT_SIGN_PASSWORD, ou rode com --no-sign."
)
ENDPOINTS_FILE = PROJECT_ROOT / "config" / "qdisk.endpoints.json"
DIST_DIR = PROJECT_ROOT / "dist"
UPDATE_DIR = DIST_DIR / "update"
DIST_EXE = DIST_DIR / "qdisk.exe"
ADM_DIST_DIR = DIST_DIR / "ADM"
ADM_EXE = ADM_DIST_DIR / "QDisk_Adm.exe"
# Bancada do teste de velocidade: aplicativo autonomo como o QDisk_Adm (fora do instalador)
NETSPEED_PROJECT = PROJECT_ROOT / "tools" / "Quallit.NetSpeedTest" / "Quallit.NetSpeedTest.vbproj"
NETSPEED_DIST_DIR = DIST_DIR / "NetSpeedTest"
NETSPEED_EXE = NETSPEED_DIST_DIR / "Quallit.NetSpeedTest.exe"
# Helper de manifesto de USB do QDisk_Adm, publicado compilado ao lado do QDisk_Adm.exe (antes: dotnet run --project).
ISO_MANIFEST_TOOL_PROJECT = PROJECT_ROOT / "tools" / "Quallit.IsoManifestTool" / "Quallit.IsoManifestTool.csproj"
ISO_MANIFEST_TOOL_EXE = ADM_DIST_DIR / "Quallit.IsoManifestTool.exe"
BACKUP_ROOT = PROJECT_ROOT.parent.parent / "PROJECT_BACKUP"
RELEASE_DIR = PROJECT_ROOT.parent.parent / "PROJECT_RELEASE"
ISS_SCRIPT = TOOLS_DIR / "installer" / "qdisk.iss"

DEFAULT_INSTALLER_URL = "https://installer.quallit.com.br/qdisk/Setup_qdisk.update.exe"
DEFAULT_REMOTE_BASE_URL = "https://installer.quallit.com.br/qdisk"
DEFAULT_SETUP_UPDATE_NAME = "Setup_qdisk.update.exe"
DEFAULT_APP_UPDATE_NAME = "qdisk.update.exe"
DEFAULT_MANIFEST_NAME = "qdisk.update.json"
CATALOG_FILE_NAMES = (
    "qdisk.catalog.pnb.json",
    "qdisk.catalog.rd.json",
    "qdisk.catalog.tools.json",
    "qdisk.catalog.devteam.json",
)
# Manifestos de USB ("Identificar USB"): os catalogos apontam para <base>/manifests/<nome>.
# Publicados junto com o pacote nos passos 14 e 16 (antes o passo 14 apagava a pasta no installer).
USB_MANIFEST_DIR = PROJECT_ROOT / "config" / "manifests"
USB_MANIFEST_SUBDIR = "manifests"
USB_MANIFEST_NAME_RE = re.compile(r"^[A-Za-z0-9._-]+\.manifest\.json$")

DEFAULT_FALLBACK_HOST = "mateweb.ddns.net"
DEFAULT_FALLBACK_PORT = 2205
DEFAULT_FALLBACK_USER = "qdisk-update"
DEFAULT_FALLBACK_REMOTE_PATH = "/store/sites/qdisk/www"
DEFAULT_FALLBACK_HOST_KEY = "SHA256:aBVNSji5CI6pD+nASIXNxUTkCP86DMktrg1LoLiVre4"
DEFAULT_INSTALLER_REPO_URL = "https://github.com/quallitdesenv/quallit-installer.git"
DEFAULT_INSTALLER_TARGET_SUBDIR = "public/qdisk"

DEFAULT_PFX = TOOLS_DIR / "certs" / "quallit_codesign.pfx"
DEFAULT_TIMESTAMP = "http://timestamp.digicert.com"
SIGN_DESCRIPTION = "Q-DISK - Quallit Solucoes em TI"
SIGN_DESCRIPTION_URL = "https://www.quallit.com.br"

SIGNTOOL_CANDIDATES = (
    r"C:\Program Files (x86)\Windows Kits\10\App Certification Kit\signtool.exe",
    r"C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe",
    r"C:\Program Files\Windows Kits\10\bin\x64\signtool.exe",
)
PWSH_CANDIDATES = (
    r"C:\Program Files\PowerShell\7\pwsh.exe",
    r"C:\Program Files\WindowsApps\Microsoft.PowerShell_7.6.0.0_x64__8wekyb3d8bbwe\pwsh.exe",
)
ISCC_CANDIDATES = (
    r"C:\Program Files (x86)\Inno Setup 6\iscc.exe",
    r"C:\Program Files\Inno Setup 6\iscc.exe",
    str(Path(os.environ.get("LOCALAPPDATA", "")) / "Programs" / "Inno Setup 6" / "iscc.exe"),
)

ZIP_SKIP_DIRS = {".git", "obj", "bin", "__pycache__", "node_modules"}
ZIP_SKIP_ROOTS = {"dist", "temp/bck", "tools/log"}
DIST_SKIP_EXTS = {".pdb", ".log", ".xml"}
PROJECT_GIT_EXCLUDE_PATHS = ("secret.txt", "config/secret.md")

# Painel de execucao do build.
# Altere True/False aqui para habilitar ou pular acoes sem precisar lembrar todos os parametros de linha de comando.
RUN_CONFIG = {
    "atualizar_versao": True,
    "atualizar_manual": True,
    "limpeza_artefatos": True,
    "assinatura_digital": True,
    "gerar_instalador": True,
    "backup_zip": True,
    "limpeza_artefatos_pos_build": True,
    "empacotar_distribuicao": True,
    "gerar_pacote_update": True,
    "sincronizar_repositorios_git": True,
    "sincronizar_git_installer": True,
    "verificar_installer_remoto": True,
    "publicar_fallback_remoto": True,
    "sincronizar_git_projeto": True,
    "previsualizar_etapas": False,
}

RUN_CONFIG_KEYS = {
    "atualizar_versao",
    "atualizar_manual",
    "limpeza_artefatos",
    "assinatura_digital",
    "gerar_instalador",
    "backup_zip",
    "limpeza_artefatos_pos_build",
    "empacotar_distribuicao",
    "gerar_pacote_update",
    "sincronizar_repositorios_git",
    "sincronizar_git_installer",
    "verificar_installer_remoto",
    "publicar_fallback_remoto",
    "sincronizar_git_projeto",
    "previsualizar_etapas",
}


def read_backup_exclude_roots_from_props() -> set[str]:
    if not PROPS_FILE.is_file():
        return set()

    text = PROPS_FILE.read_text(encoding="utf-8")
    match = re.search(r"<QuallitBackupExcludePaths>(.*?)</QuallitBackupExcludePaths>", text, re.IGNORECASE | re.DOTALL)
    if not match:
        return set()

    roots: set[str] = set()
    for raw in match.group(1).split(";"):
        value = raw.strip().replace("\\", "/").strip("/")
        if value:
            roots.add(value)
    return roots


ZIP_SKIP_ROOTS.update(read_backup_exclude_roots_from_props())

STEP_TITLES = {
    1: "Validar pre-requisitos",
    2: "Atualizar versao",
    3: "Atualizar manual",
    4: "Limpar artefatos",
    5: "Publicar app principal",
    6: "Compilar QDisk_Adm e NetSpeedTest",
    7: "Assinar executaveis",
    8: "Verificar assinaturas",
    9: "Empacotar distribuicao",
    10: "Gerar e assinar instalador",
    11: "Backup ZIP",
    12: "Limpar artefatos pos-build",
    13: "Preparar pacote dist/update",
    14: "Sincronizar Git installer",
    15: "Verificar installer remoto",
    16: "Publicar fallback remoto",
    17: "Sincronizar Git projeto",
}


class Colors:
    GREEN = "\033[92m"
    YELLOW = "\033[93m"
    RED = "\033[91m"
    CYAN = "\033[96m"
    BOLD = "\033[1m"
    RESET = "\033[0m"


def enable_ansi() -> None:
    if sys.platform == "win32":
        try:
            import ctypes
            kernel = ctypes.windll.kernel32
            kernel.SetConsoleMode(kernel.GetStdHandle(-11), 7)
        except Exception:
            pass


def log_message(message: str, level: str = "INFO", log_file: Path | None = None) -> None:
    prefix = {
        "INFO": f"{Colors.CYAN}[INFO ]{Colors.RESET}",
        "OK": f"{Colors.GREEN}[OK   ]{Colors.RESET}",
        "WARN": f"{Colors.YELLOW}[AVISO]{Colors.RESET}",
        "ERROR": f"{Colors.RED}[FALHA]{Colors.RESET}",
    }.get(level, f"[{level:<5}]")
    print(f"{prefix} {message}")
    if log_file:
        with log_file.open("a", encoding="utf-8") as stream:
            stream.write(f"[{level:<5}] {message}\n")


def banner(title: str, step: int | None = None, total: int | None = None, log_file: Path | None = None) -> None:
    header = f"[{step}/{total}] {title}" if step is not None else title
    text = f"{'=' * 60}\n  {header}  ({datetime.datetime.now().strftime('%H:%M:%S')})\n{'=' * 60}"
    print(f"\n{Colors.BOLD}{text}{Colors.RESET}")
    if log_file:
        with log_file.open("a", encoding="utf-8") as stream:
            stream.write(f"\n{text}\n")


def log_step_summary(status: dict[int, str], duration: dict[int, float], log_file: Path | None) -> None:
    for step in STEP_TITLES:
        status.setdefault(step, "PENDENTE")

    icons = {"OK": "OK", "PULADO": "--", "AVISO": "AV", "FALHOU": "!!", "PENDENTE": "??"}
    colors = {
        "OK": Colors.GREEN,
        "PULADO": Colors.YELLOW,
        "AVISO": Colors.YELLOW,
        "FALHOU": Colors.RED,
        "PENDENTE": Colors.CYAN,
    }
    plain_lines = ["", "=" * 60, "  RESUMO FINAL DE ETAPAS", "=" * 60]
    color_lines = ["", f"{Colors.BOLD}{'=' * 60}", f"  {Colors.CYAN}RESUMO FINAL DE ETAPAS{Colors.RESET}{Colors.BOLD}", f"{'=' * 60}{Colors.RESET}"]
    for step in sorted(STEP_TITLES):
        st = status.get(step, "PENDENTE")
        dt = duration.get(step)
        dur = f"  ({int(dt)}s)" if dt is not None else ""
        icon = icons.get(st, "??")
        color = colors.get(st, Colors.RESET)
        plain_lines.append(f"  [{icon}] [{step}/{len(STEP_TITLES)}] {STEP_TITLES[step]}: {st}{dur}")
        color_lines.append(f"  {color}[{icon}] [{step}/{len(STEP_TITLES)}] {STEP_TITLES[step]}: {st}{dur}{Colors.RESET}")
    plain_lines.extend(["=" * 60, ""])
    color_lines.extend([f"{Colors.BOLD}{'=' * 60}{Colors.RESET}", ""])
    text = "\n".join(plain_lines)
    print("\n".join(color_lines))
    if log_file:
        with log_file.open("a", encoding="utf-8") as stream:
            stream.write(text + "\n")


def print_header(args: argparse.Namespace, log_file: Path) -> None:
    art = [
        r" ,-----.            ,------.   ,--.  ,---.   ,--. ,--.",
        r"'  .-.  '   ,-----. |  .-.  \  |  | '   .-'  |  .'   /",
        r"|  | |  |   '-----' |  |  \  : |  | `.  `-.  |  .   '",
        "'  '-'  '-.         |  '--'  / |  | .-'    | |  |\\   \\",
        r" `-----'--'         `-------'  `--' `-----'  `--' '--'",
    ]
    print()
    for line in art:
        print(f"{Colors.BOLD}{line}{Colors.RESET}")
    print(f"{Colors.CYAN}          Quallit  --  Q-Disk Build + Publicacao + Verificacao{Colors.RESET}")
    print(f"  Projeto          : {PROJECT_ROOT}")
    print(f"  Log              : {log_file}")
    print(f"  Verbosidade      : {verbosity_label(args)}")
    print(f"  Assinatura       : {'desabilitada (--no-sign)' if args.no_sign else 'habilitada'}")
    print(f"  Backup ZIP       : {'desabilitado (--no-backup)' if args.no_backup else 'habilitado'}")
    print(f"  Timestamp        : {'desabilitado (--no-timestamp)' if args.no_timestamp else os.environ.get('QUALLIT_SIGN_TIMESTAMP', DEFAULT_TIMESTAMP)}")
    print(f"  Instalador       : {'desabilitado (--no-installer)' if args.no_installer else 'habilitado'}")
    print(f"  Pacote update    : {'desabilitado (--no-update-package)' if args.no_update_package else 'habilitado'}")
    print(f"  Painel build     : {'ativo' if args.use_run_config else 'inativo'}")
    print(f"  Previsualizacao  : {'habilitada' if args.preview_steps else 'desabilitada'}")
    if args.sync_project and args.sync_only:
        publication_mode = "sync_project + sync_update"
    elif args.sync_project:
        publication_mode = "sync_project"
    elif args.sync_only:
        publication_mode = "sync_update"
    elif args.verify_installer:
        publication_mode = "verify_installer"
    else:
        publication_mode = "Git installer + fallback"
    print(f"  Publicacao       : {publication_mode}")
    disabled_steps = disabled_step_titles(args)
    if disabled_steps:
        print("  Etapas puladas   :")
        for step_title in disabled_steps:
            print(f"    - {step_title}")
    print()

    with log_file.open("a", encoding="utf-8") as stream:
        stream.write("\n".join(art) + "\n")
        stream.write("Quallit -- Build v4 + Publicacao + Verificacao\n")


def validate_run_config() -> None:
    unknown = sorted(set(RUN_CONFIG) - RUN_CONFIG_KEYS)
    if unknown:
        names = ", ".join(unknown)
        raise ValueError(f"Chave(s) desconhecida(s) em RUN_CONFIG: {names}")
    invalid = sorted(name for name, value in RUN_CONFIG.items() if not isinstance(value, bool))
    if invalid:
        names = ", ".join(invalid)
        raise ValueError(f"Chave(s) de RUN_CONFIG devem usar True/False: {names}")


def config_enabled(name: str) -> bool:
    return bool(RUN_CONFIG.get(name, True))


def apply_run_config(args: argparse.Namespace) -> None:
    validate_run_config()
    args.use_run_config = True
    args.no_bump_version = args.no_bump_version or not config_enabled("atualizar_versao")
    args.no_manual_update = not config_enabled("atualizar_manual")
    args.no_clean_artifacts = not config_enabled("limpeza_artefatos")
    args.no_sign = args.no_sign or not config_enabled("assinatura_digital")
    args.no_installer = args.no_installer or not config_enabled("gerar_instalador")
    args.no_backup = args.no_backup or not config_enabled("backup_zip")
    args.no_post_clean = args.no_post_clean or not config_enabled("limpeza_artefatos_pos_build")
    args.no_dist_package = not config_enabled("empacotar_distribuicao")
    args.no_update_package = args.no_update_package or not config_enabled("gerar_pacote_update")
    args.preview_steps = args.preview_steps or config_enabled("previsualizar_etapas")

    sync_git = config_enabled("sincronizar_repositorios_git")
    args.no_installer_git_publish = args.no_installer_git_publish or not sync_git or not config_enabled("sincronizar_git_installer")
    args.no_project_git_publish = args.no_project_git_publish or not sync_git or not config_enabled("sincronizar_git_projeto")
    args.no_verify_remote = args.no_verify_remote or not config_enabled("verificar_installer_remoto")
    args.no_fallback_publish = args.no_fallback_publish or not config_enabled("publicar_fallback_remoto")


def full_build_skip_reason(args: argparse.Namespace, step: int) -> str | None:
    reasons = {
        2: "atualizacao de versao desativada" if args.no_bump_version else None,
        3: "atualizacao do manual desativada" if args.no_manual_update else None,
        4: "limpeza pre-build desativada" if args.no_clean_artifacts else None,
        7: "assinatura digital desativada" if args.no_sign else None,
        8: "assinatura digital desativada" if args.no_sign else None,
        9: "empacotamento de distribuicao desativado" if args.no_dist_package else None,
        10: "geracao de instalador desativada" if args.no_installer else None,
        11: "backup ZIP desativado" if args.no_backup else None,
        12: "limpeza pos-build desativada" if args.no_post_clean else None,
        13: "pacote update desativado" if args.no_update_package else None,
        14: "sincronizacao Git installer desativada" if args.no_installer_git_publish else None,
        15: "verificacao remota desativada" if args.no_verify_remote else None,
        16: "publicacao fallback desativada" if args.no_fallback_publish else None,
        17: "sincronizacao Git projeto desativada" if args.no_project_git_publish else None,
    }
    return reasons.get(step)


def planned_skip_reason(args: argparse.Namespace, step: int) -> str | None:
    if args.verify_installer:
        if step == 15:
            return "verificacao remota desativada" if args.no_verify_remote else None
        return "modo verify-installer executa apenas a verificacao remota"

    if args.sync_project:
        if step == 11:
            return "backup ZIP desativado" if args.no_backup else None
        if step == 17:
            return "sincronizacao Git projeto desativada" if args.no_project_git_publish else None
        if args.sync_only and step == 14:
            return "sincronizacao Git installer desativada" if args.no_installer_git_publish else None
        if args.sync_only and step == 15:
            if args.no_verify_remote or args.no_installer_git_publish:
                return "verificacao remota desativada"
            return None
        return "modo sync-project nao executa esta etapa"

    if args.sync_only:
        if step == 14:
            return "sincronizacao Git installer desativada" if args.no_installer_git_publish else None
        if step == 15:
            if args.no_verify_remote or args.no_installer_git_publish:
                return "verificacao remota desativada"
            return None
        return "modo sync-only executa apenas sync e verificacao do installer"

    if args.verify_only:
        if step == 8:
            return None
        return "modo verify-only executa apenas verificacao de assinatura"

    return full_build_skip_reason(args, step)


def disabled_step_titles(args: argparse.Namespace) -> list[str]:
    return [f"[{step}] {title}" for step, title in STEP_TITLES.items() if planned_skip_reason(args, step)]


def print_execution_preview(args: argparse.Namespace, log_file: Path) -> None:
    banner("Previsualizacao de etapas", log_file=log_file)
    for step, title in STEP_TITLES.items():
        reason = planned_skip_reason(args, step)
        if reason:
            log_message(f"[{step:02d}] PULAR    - {title} ({reason})", "WARN", log_file)
        else:
            log_message(f"[{step:02d}] EXECUTAR - {title}", "OK", log_file)


# ========================== Verbosidade do console ==========================
# Controlado por --show-progress / --show-full-info / --show-progress-bar
# (setado em main() a partir de args); todos False = saida minima (padrao).
SHOW_PROGRESS: bool = False
SHOW_FULL_INFO: bool = False
SHOW_PROGRESS_BAR: bool = False


def verbosity_label(args: argparse.Namespace) -> str:
    if args.show_full_info:
        return "detalhada (--show-full-info)"
    if args.show_progress_bar:
        return "barra de progresso (--show-progress-bar)"
    if args.show_progress:
        return "ao vivo (--show-progress)"
    return "minima (padrao)"


def dotnet_verbosity_args() -> list[str]:
    """Nivel de verbosidade do MSBuild (-v) conforme os switches globais:
    - Nenhum switch (padrao): "minimal" -- ainda emite a linha "Projeto -> saida.dll"
      por projeto (usada pela barra de progresso), mas sem ruido de restore/tasks.
    - --show-progress: "normal" -- projeto a projeto, com um pouco mais de contexto.
    - --show-progress-bar: igual a --show-progress (precisa das linhas "-> " como
      fonte de dados para a barra), mas desenhada como barra em vez de texto bruto.
    - --show-full-info: "detailed" -- build MSBuild alvo a alvo/tarefa a tarefa,
      o mais proximo do equivalente "arquivo a arquivo" do Nuitka/scons que o
      MSBuild oferece (o Vbc compila o projeto inteiro numa unica invocacao,
      entao nao existe uma etapa file-by-file real para expor aqui)."""
    if SHOW_FULL_INFO:
        return ["-v", "detailed"]
    if SHOW_PROGRESS or SHOW_PROGRESS_BAR:
        return ["-v", "normal"]
    return ["-v", "minimal"]


def _tail(text: str, n: int = 40) -> str:
    lines = (text or "").splitlines()
    return "\n".join(lines[-n:]) if len(lines) > n else (text or "")


# Heuristica para "este projeto terminou de compilar": o MSBuild ecoa uma linha
# "NomeDoProjeto -> caminho\Saida.dll" ao concluir cada projeto da grafo de build
# (restore/clean/publish inclusive). Ajustar aqui se a formatacao divergir.
_RE_PROJECT_DONE = re.compile(r"^\s*\S.*?\s+->\s+.+$")


def _draw_progress_bar(label: str, done: int, total: int, width: int = 30) -> None:
    total = max(total, done, 1)
    frac = min(done / total, 1.0)
    filled = int(width * frac)
    bar = "#" * filled + "-" * (width - filled)
    pct = int(frac * 100)
    line = f"\r[{bar}] {pct:3d}%  {label}"
    sys.stdout.write(line.ljust(max(len(line), 90)))
    sys.stdout.flush()


def _run_with_progress_bar(cmd: list[str], env: dict[str, str], cwd: Path | None, total_projects: int, label: str) -> tuple[int, str]:
    """Roda `cmd` (dotnet build/publish/clean) e desenha uma barra compacta a
    partir da contagem de linhas "Projeto -> saida" em vez de despejar a saida
    bruta no console. A saida completa continua indo integralmente para o log."""
    proc = subprocess.Popen(cmd, cwd=str(cwd or PROJECT_ROOT), env=env, stdout=subprocess.PIPE,
                             stderr=subprocess.STDOUT, universal_newlines=True, encoding="utf-8", errors="replace")
    lines: list[str] = []
    done = 0
    assert proc.stdout is not None
    _draw_progress_bar(f"{label} (0/{total_projects} projeto(s))", 0, total_projects)
    for line in proc.stdout:
        lines.append(line)
        if _RE_PROJECT_DONE.match(line.strip()):
            done += 1
            _draw_progress_bar(f"{label} ({min(done, total_projects)}/{total_projects} projeto(s))", done, total_projects)
    proc.wait()
    sys.stdout.write("\n")
    sys.stdout.flush()
    return proc.returncode, "".join(lines)


def run_command(cmd: list[str], log_file: Path | None = None, cwd: Path | None = None, mask_idx: list[int] | None = None,
                 env: dict[str, str] | None = None, progress_bar: bool = False, project_total: int = 1,
                 label: str = "Executando") -> tuple[int, str]:
    shown = list(cmd)
    for idx in mask_idx or []:
        if idx < len(shown):
            shown[idx] = "***"
    shown_cmd = " ".join(shown)
    command_env = os.environ.copy()
    if env:
        command_env.update(env)

    use_bar = progress_bar and SHOW_PROGRESS_BAR
    stream_live = SHOW_PROGRESS or SHOW_FULL_INFO

    if use_bar or stream_live:
        # Saida ao vivo/barra: o comando aparece no console antes de rodar,
        # como marco de atividade.
        log_message("CMD: " + shown_cmd, "INFO", log_file)
    elif log_file:
        # Saida minima (padrao sem switches): o comando em si fica só no log
        # completo -- o console mostra apenas o marco [INFO]/[OK]/[FALHA].
        with log_file.open("a", encoding="utf-8") as stream:
            stream.write(f"[INFO ] CMD: {shown_cmd}\n")

    if use_bar:
        rc, output = _run_with_progress_bar(cmd, command_env, cwd, max(project_total, 1), label)
    else:
        proc = subprocess.Popen(cmd, cwd=str(cwd or PROJECT_ROOT), env=command_env, stdout=subprocess.PIPE,
                                 stderr=subprocess.STDOUT, universal_newlines=True, encoding="utf-8", errors="replace")
        lines: list[str] = []
        assert proc.stdout is not None
        if stream_live:
            # Transmite a saida ao vivo (linha por linha) em vez de esperar o
            # processo terminar para mostrar tudo de uma vez.
            for line in proc.stdout:
                print(line, end="", flush=True)
                lines.append(line)
            proc.wait()
        else:
            # Nao transmite cada linha (tudo continua indo para o log completo),
            # mas marca atividade com um "#" a cada 10s na mesma linha, para nao
            # parecer travado durante comandos longos e silenciosos.
            def _reader() -> None:
                for reader_line in proc.stdout:
                    lines.append(reader_line)

            reader_thread = threading.Thread(target=_reader, daemon=True)
            reader_thread.start()
            sys.stdout.write(f"{Colors.CYAN}[INFO ]{Colors.RESET} {label} ")
            sys.stdout.flush()
            while reader_thread.is_alive():
                reader_thread.join(timeout=10)
                if reader_thread.is_alive():
                    sys.stdout.write("#")
                    sys.stdout.flush()
            sys.stdout.write("\n")
            sys.stdout.flush()
            proc.wait()
        output = "".join(lines)
        rc = proc.returncode

    if log_file:
        with log_file.open("a", encoding="utf-8") as stream:
            stream.write(output)
            if output and not output.endswith("\n"):
                stream.write("\n")

    if rc != 0 and not stream_live and not use_bar:
        # Comando falhou e a saida nao foi transmitida ao vivo: mostra as
        # ultimas linhas no console em vez de deixar so o log completo.
        tail = _tail(output, 40)
        if tail.strip():
            sys.stdout.write(f"{Colors.YELLOW}---- ultimas linhas (comando retornou codigo {rc}) ----{Colors.RESET}\n{tail}\n")
            sys.stdout.flush()

    return rc, output


def find_command(names) -> str | None:
    for name in names:
        path = shutil.which(name)
        if path:
            return path
    return None


def find_powershell() -> str:
    for path in PWSH_CANDIDATES:
        if Path(path).is_file():
            return path
    return find_command(["pwsh", "powershell"]) or "powershell"


def find_signtool() -> str | None:
    for path in SIGNTOOL_CANDIDATES:
        if Path(path).is_file():
            return path
    sdk_bin = Path(r"C:\Program Files (x86)\Windows Kits\10\bin")
    if sdk_bin.exists():
        candidates = [p for p in sorted(sdk_bin.rglob("signtool.exe"), reverse=True) if p.is_file() and "x64" in p.parts]
        if candidates:
            return str(candidates[0])
    return find_command(["signtool.exe", "signtool"])


def find_iscc() -> str | None:
    for path in ISCC_CANDIDATES:
        if Path(path).is_file():
            return path
    return find_command(["iscc.exe", "iscc"])


def read_version() -> str:
    text = PROPS_FILE.read_text(encoding="utf-8")
    match = re.search(r"<Version>(.*?)</Version>", text)
    return match.group(1).strip() if match else "1.0.00.00"


def bump_version() -> str:
    text = PROPS_FILE.read_text(encoding="utf-8")
    match = re.search(r"<Version>(.*?)</Version>", text)
    if not match:
        raise RuntimeError("Tag <Version> nao encontrada em Directory.Build.props")
    now = datetime.datetime.now()
    parts = match.group(1).strip().split(".")
    while len(parts) < 4:
        parts.append("0")
    parts[1] = f"{int(parts[1]) + 1:02d}"
    parts[2] = f"{now.month:02d}"
    parts[3] = now.strftime("%y")
    version = ".".join(parts)
    for tag in ("Version", "AssemblyVersion", "FileVersion", "InformationalVersion"):
        text = re.sub(rf"<{tag}>[^<]*</{tag}>", f"<{tag}>{version}</{tag}>", text)
    PROPS_FILE.write_text(text, encoding="utf-8")
    VERSION_INFO.write_text(f"Versao: {version}\nAtualizado em: {now.strftime('%d/%m/%Y %H:%M:%S')}\n", encoding="utf-8")
    return version


# Nomes das imagens do manual, na mesma ordem em que os <img> aparecem em
# manual.html (ver tools/utils/embed_manual_images.py -- mesma lista, mantida em
# sincronia manualmente sempre que uma secao/imagem for adicionada ao manual).
MANUAL_IMAGE_NAMES = (
    "autenticacao.png",
    "inicio.png",
    "downloads.png",
    "copiar-backup-tibx-2.png",
    "gravar-iso-bootavel-2.png",
    "teste-velocidade.png",
    "logs.png",
    "configuracoes.png",
    "sobre.png",
    "manual.png",
)


def embed_manual_images(log_file: Path | None) -> bool:
    manual_path = PROJECT_ROOT / "assets" / "manual" / "manual.html"
    images_root = PROJECT_ROOT / "assets" / "manual" / "images"
    if not manual_path.is_file():
        log_message(f"Manual nao encontrado: {manual_path}", "WARN", log_file)
        return False
    html = manual_path.read_text(encoding="utf-8")
    original = html

    # Substitui o src de cada <img> na ordem em que aparecem no documento --
    # nao da pra procurar por um marcador tipo src="images/X.png", porque uma
    # vez embutido em base64 (o estado normal do arquivo versionado), esse
    # marcador nunca mais existe; so a posicao no documento e estavel.
    manual_images = [images_root / name for name in MANUAL_IMAGE_NAMES]
    missing_images = [path.name for path in manual_images if not path.is_file()]
    if missing_images:
        log_message(f"Imagens do manual nao encontradas em {images_root}: {', '.join(missing_images)}", "WARN", log_file)
    else:
        image_iter = iter(manual_images)

        def _replace_img_src(match: re.Match) -> str:
            try:
                image_path = next(image_iter)
            except StopIteration:
                return match.group(0)
            data_uri = "data:image/png;base64," + base64.b64encode(image_path.read_bytes()).decode("ascii")
            return f"{match.group(1)}{data_uri}{match.group(3)}"

        html = re.sub(r'(<img\b[^>]*\bsrc=")([^"]+)("[^>]*>)', _replace_img_src, html, count=len(manual_images))
    if VERSION_INFO.exists():
        version = ""
        updated = ""
        for line in VERSION_INFO.read_text(encoding="utf-8").splitlines():
            if line.startswith("Versao:"):
                version = line.replace("Versao:", "").strip()
            elif line.startswith("Atualizado em:"):
                updated = line.replace("Atualizado em:", "").strip()
        if version:
            html = re.sub(r'<div class="meta">Vers(?:ao|ão):.*?</div>', f'<div class="meta">Versão: {version}</div>', html)
        if updated:
            html = re.sub(r'<div class="meta">Atualizado em:.*?</div>', f'<div class="meta">Atualizado em: {updated}</div>', html)
    if html != original:
        manual_path.write_text(html, encoding="utf-8")
    return True


def sign_exe(exe: Path, signtool: str, pfx: Path, password: str, timestamp_url: str | None, log_file: Path | None) -> bool:
    cmd = [signtool, "sign", "/v", "/fd", "sha256", "/f", str(pfx), "/p", password, "/d", SIGN_DESCRIPTION, "/du", SIGN_DESCRIPTION_URL]
    if timestamp_url:
        cmd.extend(["/tr", timestamp_url, "/td", "sha256"])
    cmd.append(str(exe))
    password_index = cmd.index("/p") + 1

    last_output = ""
    for attempt in range(1, 6):
        rc, output = run_command(cmd, log_file=log_file, mask_idx=[password_index], label=f"Assinando {exe.name}")
        if rc == 0:
            return True

        last_output = output
        if "being used by another process" not in output and "usado por outro processo" not in output.lower():
            break

        if attempt < 5:
            log_message(f"Arquivo ainda em uso para assinatura; nova tentativa {attempt + 1}/5 em 2s.", "WARN", log_file)
            time.sleep(2)

    if last_output:
        log_message("Assinatura nao concluida apos as tentativas configuradas.", "WARN", log_file)
    return False


def verify_sign(exe: Path, signtool: str, ps_exe: str, log_file: Path | None) -> None:
    ps_script = "$s = Get-AuthenticodeSignature -LiteralPath '" + str(exe).replace("'", "''") + "'; $c = $s.SignerCertificate; if ($c) { Write-Host ('STATUS:' + $s.Status); Write-Host ('SUBJECT:' + $c.Subject); Write-Host ('ISSUER:' + $c.Issuer); Write-Host ('THUMB:' + $c.Thumbprint); Write-Host ('EXPIRE:' + $c.NotAfter.ToString('dd/MM/yyyy')) }"
    run_command([ps_exe, "-NoProfile", "-NonInteractive", "-Command", ps_script], log_file=log_file, label=f"Consultando assinatura de {exe.name}")
    rc, _ = run_command([signtool, "verify", "/pa", "/v", str(exe)], log_file=log_file, label=f"Verificando cadeia de confianca de {exe.name}")
    log_message("Cadeia de confianca: VALIDA" if rc == 0 else "Cadeia de confianca: falhou na verificacao publica", "OK" if rc == 0 else "WARN", log_file)


def pyinstaller_version_tuple(version: str) -> tuple[int, int, int, int]:
    parts = [int(part) for part in re.findall(r"\d+", version)]
    while len(parts) < 4:
        parts.append(0)
    return tuple(parts[:4])


def write_qdisk_adm_version_file(path: Path, version: str) -> None:
    filevers = pyinstaller_version_tuple(version)
    safe_version = version.replace("\\", "\\\\").replace("'", "\\'")
    path.write_text(
        "# UTF-8\n"
        "VSVersionInfo(\n"
        "  ffi=FixedFileInfo(\n"
        f"    filevers={filevers},\n"
        f"    prodvers={filevers},\n"
        "    mask=0x3f,\n"
        "    flags=0x0,\n"
        "    OS=0x40004,\n"
        "    fileType=0x1,\n"
        "    subtype=0x0,\n"
        "    date=(0, 0)\n"
        "  ),\n"
        "  kids=[\n"
        "    StringFileInfo([\n"
        "      StringTable('040904B0', [\n"
        "        StringStruct('CompanyName', 'Quallit Solucoes em TI'),\n"
        f"        StringStruct('FileDescription', 'QDisk_Adm - Quallit Catalogo ADM {safe_version}'),\n"
        f"        StringStruct('FileVersion', '{safe_version}'),\n"
        "        StringStruct('InternalName', 'QDisk_Adm'),\n"
        "        StringStruct('OriginalFilename', 'QDisk_Adm.exe'),\n"
        "        StringStruct('ProductName', 'Q-DISK'),\n"
        f"        StringStruct('ProductVersion', '{safe_version}')\n"
        "      ])\n"
        "    ]),\n"
        "    VarFileInfo([VarStruct('Translation', [1033, 1200])])\n"
        "  ]\n"
        ")\n",
        encoding="utf-8",
    )


def resolve_config_json(name: str) -> Path:
    return PROJECT_ROOT / "config" / name


def build_qdisk_adm_exe(log_file: Path | None, version: str | None = None) -> bool:
    version = version or read_version()
    source = PROJECT_ROOT / "QDisk_Adm" / "catalog_admin.py"
    if not source.is_file():
        log_message(f"Fonte do QDisk_Adm nao encontrado: {source}", "ERROR", log_file)
        return False

    ADM_DIST_DIR.mkdir(parents=True, exist_ok=True)

    # Limpeza best-effort de sobras de builds anteriores em que o QDisk_Adm.exe
    # estava em uso (ver rename abaixo). Se ainda estiverem travadas (processo
    # antigo ainda rodando), ignora -- serao limpas num build futuro.
    for stale in ADM_DIST_DIR.glob(f"{ADM_EXE.stem}.locked-*{ADM_EXE.suffix}"):
        try:
            stale.unlink()
        except OSError:
            pass

    if ADM_EXE.exists():
        try:
            ADM_EXE.unlink()
        except OSError:
            # Provavelmente em uso -- cenario tipico quando o proprio
            # QDisk_Adm.exe compilado dispara o build completo (botao
            # "Executar build completo"): o Windows nao deixa apagar/
            # sobrescrever um executavel em execucao, mas deixa renomea-lo.
            # O processo antigo continua rodando normalmente a partir do
            # arquivo renomeado (o handle aberto nao se importa com o nome),
            # e o caminho original fica livre para o PyInstaller escrever a
            # versao nova -- sem pular a etapa nem abortar o pipeline.
            stale_name = ADM_EXE.with_name(f"{ADM_EXE.stem}.locked-{datetime.datetime.now().strftime('%Y%m%d%H%M%S')}{ADM_EXE.suffix}")
            try:
                ADM_EXE.rename(stale_name)
                log_message(f"QDisk_Adm.exe estava em uso; renomeado para {stale_name.name} para liberar o build.", "WARN", log_file)
            except OSError as exc:
                log_message(f"Nao foi possivel substituir {ADM_EXE}: {exc}", "ERROR", log_file)
                return False

    # Workpath descartavel do PyInstaller: usa um diretorio temporario real do
    # SO (nao uma pasta fixa dentro do projeto) -- e limpo automaticamente ao
    # sair do "with", igual ao clone temporario usado em sync_installer_git.
    with tempfile.TemporaryDirectory(prefix="quallit-pyinstaller-qdisk-adm-") as temp_root:
        work_dir = Path(temp_root)
        spec_dir = work_dir / "spec"
        build_dir = work_dir / "build"
        spec_dir.mkdir(parents=True, exist_ok=True)
        build_dir.mkdir(parents=True, exist_ok=True)
        version_file = work_dir / "QDisk_Adm.version"
        write_qdisk_adm_version_file(version_file, version)

        rc, _ = run_command([
            sys.executable,
            "-m",
            "PyInstaller",
            "--noconfirm",
            "--clean",
            "--onefile",
            "--name",
            "QDisk_Adm",
            "--distpath",
            str(ADM_DIST_DIR),
            "--workpath",
            str(build_dir),
            "--specpath",
            str(spec_dir),
            "--version-file",
            str(version_file),
            str(source),
        ], log_file=log_file, cwd=PROJECT_ROOT, label="Compilando QDisk_Adm (PyInstaller)")

        if rc != 0 or not ADM_EXE.is_file():
            log_message("Falha ao gerar dist/ADM/QDisk_Adm.exe com PyInstaller.", "ERROR", log_file)
            return False

    log_message(f"QDisk_Adm gerado: {ADM_EXE} (versao {version})", "OK", log_file)
    return True


def build_iso_manifest_tool_exe(log_file: Path | None) -> bool:
    """Publica tools/Quallit.IsoManifestTool como exe unico em dist/ADM/, usado pelo QDisk_Adm para gerar manifestos."""
    if not ISO_MANIFEST_TOOL_PROJECT.is_file():
        log_message(f"Projeto do helper de manifesto nao encontrado: {ISO_MANIFEST_TOOL_PROJECT}", "ERROR", log_file)
        return False
    ADM_DIST_DIR.mkdir(parents=True, exist_ok=True)
    if ISO_MANIFEST_TOOL_EXE.exists():
        try:
            ISO_MANIFEST_TOOL_EXE.unlink()
        except OSError as exc:
            log_message(f"Nao foi possivel substituir {ISO_MANIFEST_TOOL_EXE}: {exc}", "ERROR", log_file)
            return False
    rc, _ = run_command(
        ["dotnet", "publish", str(ISO_MANIFEST_TOOL_PROJECT), "-c", "Release", "-r", "win-x64", "--self-contained", "true",
         "/p:PublishSingleFile=true", "/p:EnableCompressionInSingleFile=true", "/p:IncludeNativeLibrariesForSelfExtract=true",
         "-o", str(ADM_DIST_DIR), *dotnet_verbosity_args()],
        log_file=log_file, cwd=PROJECT_ROOT, label="Publicando helper de manifesto (dotnet publish)",
    )
    if rc != 0 or not ISO_MANIFEST_TOOL_EXE.is_file():
        log_message("Falha ao gerar dist/ADM/Quallit.IsoManifestTool.exe.", "ERROR", log_file)
        return False
    log_message(f"Helper de manifesto gerado: {ISO_MANIFEST_TOOL_EXE}", "OK", log_file)
    return True


def build_netspeedtest_exe(log_file: Path | None, version: str | None = None) -> bool:
    """Publica a bancada do teste de velocidade (tools/Quallit.NetSpeedTest) como exe
    unico autonomo em dist/NetSpeedTest/, como o QDisk_Adm em dist/ADM/. A versao vem do
    Directory.Build.props, como a do app principal. Nao entra no instalador nem no update."""
    version = version or read_version()
    if not NETSPEED_PROJECT.is_file():
        log_message(f"Projeto da bancada NetSpeedTest nao encontrado: {NETSPEED_PROJECT}", "ERROR", log_file)
        return False

    NETSPEED_DIST_DIR.mkdir(parents=True, exist_ok=True)
    for stale in NETSPEED_DIST_DIR.glob(f"{NETSPEED_EXE.stem}.locked-*{NETSPEED_EXE.suffix}"):
        try:
            stale.unlink()
        except OSError:
            pass
    if NETSPEED_EXE.exists():
        try:
            NETSPEED_EXE.unlink()
        except OSError:
            # Em uso (bancada aberta): renomeia para liberar o caminho, como no QDisk_Adm.
            stale_name = NETSPEED_EXE.with_name(f"{NETSPEED_EXE.stem}.locked-{datetime.datetime.now().strftime('%Y%m%d%H%M%S')}{NETSPEED_EXE.suffix}")
            try:
                NETSPEED_EXE.rename(stale_name)
                log_message(f"Quallit.NetSpeedTest.exe estava em uso; renomeado para {stale_name.name} para liberar o build.", "WARN", log_file)
            except OSError as exc:
                log_message(f"Nao foi possivel substituir {NETSPEED_EXE}: {exc}", "ERROR", log_file)
                return False

    rc, _ = run_command(
        ["dotnet", "publish", str(NETSPEED_PROJECT), "-c", "Release", "-r", "win-x64", "--self-contained", "true",
         "/p:PublishSingleFile=true", "/p:EnableCompressionInSingleFile=true", "/p:IncludeNativeLibrariesForSelfExtract=true",
         "/p:IncludeAllContentForSelfExtract=true", "-o", str(NETSPEED_DIST_DIR), *dotnet_verbosity_args()],
        log_file=log_file, cwd=PROJECT_ROOT, label="Publicando NetSpeedTest (dotnet publish)",
    )
    if rc != 0 or not NETSPEED_EXE.is_file():
        log_message("Falha ao gerar dist/NetSpeedTest/Quallit.NetSpeedTest.exe.", "ERROR", log_file)
        return False

    log_message(f"NetSpeedTest gerado: {NETSPEED_EXE} (versao {version})", "OK", log_file)
    return True


def should_skip_zip(rel: Path) -> bool:
    if any(part in ZIP_SKIP_DIRS for part in rel.parts):
        return True
    rel_posix = rel.as_posix()
    return any(rel_posix == root or rel_posix.startswith(root + "/") for root in ZIP_SKIP_ROOTS)


def create_dist_zip(version: str, log_file: Path | None) -> Path | None:
    RELEASE_DIR.mkdir(parents=True, exist_ok=True)
    zip_path = RELEASE_DIR / f"qdisk_release-{version}-{datetime.datetime.now().strftime('%Y-%m-%d')}.zip"
    try:
        with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
            for path in DIST_DIR.rglob("*"):
                if path.is_file() and path.suffix.lower() not in DIST_SKIP_EXTS and path.suffix.lower() != ".zip" and ".locked-" not in path.name:
                    archive.write(path, path.relative_to(DIST_DIR).as_posix())
        log_message(f"ZIP dist criado: {zip_path.name}", "OK", log_file)
        return zip_path
    except Exception as exc:
        log_message(f"Falha ao criar ZIP de distribuicao: {exc}", "ERROR", log_file)
        return None


def create_zip_backup(version: str, log_file: Path | None) -> Path | None:
    BACKUP_ROOT.mkdir(parents=True, exist_ok=True)
    zip_path = BACKUP_ROOT / f"qdisk_backup-{version}-{datetime.datetime.now().strftime('%Y-%m-%d')}.zip"
    try:
        files_to_zip: list[tuple[Path, Path]] = []
        for path in PROJECT_ROOT.rglob("*"):
            if path.is_file():
                rel = path.relative_to(PROJECT_ROOT)
                if not should_skip_zip(rel):
                    files_to_zip.append((path, rel))

        log_message(f"Arquivos a incluir no backup: {len(files_to_zip)}", "INFO", log_file)

        with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
            for path, rel in files_to_zip:
                archive.write(path, f"qdisk/{rel.as_posix()}")
        log_message(f"ZIP backup criado: {zip_path.name}", "OK", log_file)
        return zip_path
    except Exception as exc:
        log_message(f"Falha ao criar ZIP backup: {exc}", "ERROR", log_file)
        return None


def build_installer(version: str, iscc: str, signtool: str | None, pfx: Path, password: str, ts_url: str | None, no_sign: bool, log_file: Path | None) -> Path | None:
    if not ISS_SCRIPT.is_file():
        log_message(f"Script ISS nao encontrado: {ISS_SCRIPT}", "ERROR", log_file)
        return None
    setup = DIST_DIR / f"Setup_qdisk_{version}.exe"
    rc, _ = run_command([iscc, f"/DAppVersion={version}", str(ISS_SCRIPT)], log_file=log_file, label="Gerando instalador (Inno Setup)")
    if rc != 0 or not setup.is_file():
        log_message("Falha ao gerar instalador Inno Setup.", "ERROR", log_file)
        return None
    if not no_sign and signtool:
        sign_exe(setup, signtool, pfx, password, ts_url, log_file)
    return setup


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def string_sha256(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def shell_quote(value: str) -> str:
    return "'" + value.replace("'", "'\"'\"'") + "'"


def json_semantic_sha256(path: Path) -> str:
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    return string_sha256(json.dumps(data, ensure_ascii=False, sort_keys=True, separators=(",", ":")))


def usb_manifest_files() -> list[Path]:
    """Manifestos de USB de config/manifests (mesma regra de nome do publish_catalogs.ps1)."""
    if not USB_MANIFEST_DIR.is_dir():
        return []
    files = sorted(path for path in USB_MANIFEST_DIR.glob("*.manifest.json") if path.is_file())
    for path in files:
        if not USB_MANIFEST_NAME_RE.match(path.name):
            raise ValueError(f"Manifesto com nome invalido para publicacao: {path.name}")
    return files


def get_manifest_payload(local_update_dir: Path) -> tuple[dict, list[Path]]:
    manifest = local_update_dir / DEFAULT_MANIFEST_NAME
    if not manifest.is_file():
        raise FileNotFoundError(f"Manifesto nao encontrado: {manifest}")
    manifest_json = json.loads(manifest.read_text(encoding="utf-8-sig"))
    artifact_name = str(manifest_json.get("fileName") or "").strip()
    if not artifact_name:
        raise ValueError(f"Campo fileName ausente em {manifest}")
    files = []
    for name in [DEFAULT_MANIFEST_NAME, artifact_name, *CATALOG_FILE_NAMES]:
        path = local_update_dir / name
        if not path.is_file():
            raise FileNotFoundError(f"Arquivo do pacote nao encontrado: {path}")
        files.append(path)
    return manifest_json, files


def prepare_update_dir() -> None:
    UPDATE_DIR.mkdir(parents=True, exist_ok=True)
    for pattern in ("Setup_qdisk_*.exe", "Setup_qdisk.update.exe", "qdisk.exe", "qdisk.update.exe", "update.json", "qdisk.update.json", *CATALOG_FILE_NAMES):
        for path in UPDATE_DIR.glob(pattern):
            if path.is_file():
                path.unlink()


def resolve_release_artifact(version: str, prefer_setup: bool) -> Path:
    setup = DIST_DIR / f"Setup_qdisk_{version}.exe"
    if prefer_setup and setup.is_file():
        return setup
    if DIST_EXE.is_file():
        return DIST_EXE
    if setup.is_file():
        return setup
    raise FileNotFoundError(f"Nenhum artefato encontrado: {setup.name} ou {DIST_EXE.name}")


def create_update_package(args: argparse.Namespace) -> tuple[Path, list[Path], list[Path]]:
    version = read_version()
    artifact = resolve_release_artifact(version, args.prefer_setup)
    prepare_update_dir()
    generated_at_utc = datetime.datetime.now(datetime.UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")
    build_metadata = {"product": "Q-DISK", "version": version, "buildDateUtc": generated_at_utc, "generatedBy": "tools/build_release_dotnet_4.py"}
    update_name = args.update_artifact_name.strip() if args.update_artifact_name else ""
    if not update_name:
        update_name = DEFAULT_SETUP_UPDATE_NAME if artifact.name.startswith("Setup_") else DEFAULT_APP_UPDATE_NAME
    update_artifact = UPDATE_DIR / update_name
    shutil.copy2(artifact, update_artifact)
    installer_url = args.update_base_url.rstrip("/") + "/" + urllib.parse.quote(update_artifact.name) if args.update_base_url else (args.update_installer_url.strip() or DEFAULT_INSTALLER_URL)
    manifest = {
        "version": version,
        "releaseDate": datetime.date.today().isoformat(),
        "installerUrl": installer_url,
        "sha256": file_sha256(update_artifact),
        "mandatory": bool(args.update_mandatory),
        "minimumSupportedVersion": args.update_minimum_supported_version or "",
        "notes": args.update_notes or ["Atualizacao do Q-DISK."],
        "fileName": update_artifact.name,
        "sizeBytes": update_artifact.stat().st_size,
        "build": build_metadata,
    }
    manifest_paths = []
    manifest_names = [DEFAULT_MANIFEST_NAME]
    if args.update_manifest_name and args.update_manifest_name not in manifest_names:
        manifest_names.insert(0, args.update_manifest_name)
    for name in manifest_names:
        path = UPDATE_DIR / name
        path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        manifest_paths.append(path)
    catalog_paths = []
    for name in CATALOG_FILE_NAMES:
        source = resolve_config_json(name)
        data = json.loads(source.read_text(encoding="utf-8-sig"))
        items = data["items"] if isinstance(data, dict) and isinstance(data.get("items"), list) else data
        if not isinstance(items, list):
            raise ValueError(f"Catalogo invalido: {source}")
        destination = UPDATE_DIR / name
        destination.write_text(json.dumps({"build": build_metadata, "items": items}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        catalog_paths.append(destination)
    return update_artifact, manifest_paths, catalog_paths


def load_env_file(path: Path) -> None:
    if not path.is_file():
        return
    for line in path.read_text(encoding="utf-8", errors="ignore").splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#") or "=" not in stripped:
            continue
        name, value = stripped.split("=", 1)
        os.environ.setdefault(name.strip(), value.strip().strip('"').strip("'"))


def read_secrets() -> dict[str, str]:
    """Le config/secret.md: uma linha `Chave: valor` por segredo (crases opcionais).
    Devolve as chaves em minusculas. Valores de modelo (`<...>`) sao ignorados."""
    if not SECRET_FILE.is_file():
        return {}
    values: dict[str, str] = {}
    for line in SECRET_FILE.read_text(encoding="utf-8", errors="ignore").splitlines():
        match = SECRET_LINE_RE.match(line)
        if not match:
            continue
        value = match.group(2).strip().strip("`").strip('"').strip("'").strip()
        if value and not (value.startswith("<") and value.endswith(">")):
            values[match.group(1).lower()] = value
    return values


def read_secret(name: str, *env_names: str) -> str:
    """Variavel de ambiente tem prioridade; depois config/secret.md."""
    for env_name in env_names:
        value = os.environ.get(env_name)
        if value:
            return value
    return read_secrets().get(name.lower(), "")


@contextlib.contextmanager
def password_file(password: str):
    """Grava a senha num arquivo temporario do usuario para o -pwfile do PuTTY
    (>= 0.77), evitando expor a senha na lista de processos. Apaga ao sair."""
    if not password:
        yield None
        return
    fd, pw_path = tempfile.mkstemp(prefix="quallit-pw-", suffix=".txt")
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="") as handle:
            handle.write(password)
        yield pw_path
    finally:
        try:
            os.remove(pw_path)
        except OSError:
            pass


def is_connection_or_auth_failure(output: str) -> bool:
    lowered = output.lower()
    markers = (
        "password was not accepted",
        "access denied",
        "permission denied",
        "connection refused",
        "connection timed out",
        "network error",
        "host does not exist",
        "no route to host",
        "connection reset",
    )
    return any(marker in lowered for marker in markers)


def read_publish_script_defaults() -> dict[str, str]:
    path = TOOLS_DIR / "utils" / "publish_update_package.ps1"
    if not path.is_file():
        return {}

    text = path.read_text(encoding="utf-8", errors="ignore")
    defaults: dict[str, str] = {}
    for name in ("HostName", "Port", "UserName", "RemotePath"):
        match = re.search(rf"\${name}\s*=\s*'([^']*)'", text)
        if match:
            defaults[name] = match.group(1)
    return defaults


def read_endpoint_config_defaults() -> dict[str, str]:
    endpoints_file = resolve_config_json(ENDPOINTS_FILE.name)
    if not endpoints_file.is_file():
        return {}

    try:
        data = json.loads(endpoints_file.read_text(encoding="utf-8-sig"))
    except Exception:
        return {}

    fallback = data.get("fallback") if isinstance(data, dict) else {}
    installer_git = data.get("installerGit") if isinstance(data, dict) else {}
    primary = data.get("primary") if isinstance(data, dict) else {}
    defaults: dict[str, str] = {}
    if isinstance(fallback, dict):
        mapping = {
            "publicBaseUrl": "FallbackPublicBaseUrl",
            "host": "HostName",
            "port": "Port",
            "user": "UserName",
            "remotePath": "RemotePath",
            "hostKey": "HostKey",
        }
        for source, target in mapping.items():
            value = fallback.get(source)
            if value not in (None, ""):
                defaults[target] = str(value)
    if isinstance(installer_git, dict):
        if installer_git.get("repositoryUrl"):
            defaults["RepositoryUrl"] = str(installer_git["repositoryUrl"])
        if installer_git.get("targetSubdir"):
            defaults["TargetSubDir"] = str(installer_git["targetSubdir"])
    if isinstance(primary, dict) and primary.get("baseUrl"):
        defaults["RemoteBaseUrl"] = str(primary["baseUrl"]).rstrip("/")
    return defaults


def apply_publish_configuration(args: argparse.Namespace) -> None:
    """Valores de publicacao: opcao explicita na linha de comando > variavel de ambiente > config/qdisk.endpoints.json
    > padroes do publish_update_package.ps1 local > padrao deste script. (Antes a opcao era sobrescrita pelo endpoints.json.)"""
    endpoint_defaults = read_endpoint_config_defaults()
    defaults = read_publish_script_defaults()

    def pick(cli_value, env_names, *fallbacks):
        if cli_value not in (None, ""):
            return cli_value
        for name in env_names:
            if os.environ.get(name):
                return os.environ[name]
        for value in fallbacks:
            if value not in (None, ""):
                return value
        return fallbacks[-1] if fallbacks else cli_value

    args.fallback_host = pick(args.fallback_host, ("QUALLIT_UPDATE_HOST", "QUALLIT_PUBLISH_HOST"), endpoint_defaults.get("HostName"), defaults.get("HostName"), DEFAULT_FALLBACK_HOST)
    args.fallback_user = pick(args.fallback_user, ("QUALLIT_UPDATE_USER", "QUALLIT_PUBLISH_USER"), endpoint_defaults.get("UserName"), defaults.get("UserName"), DEFAULT_FALLBACK_USER)
    args.fallback_remote_path = pick(args.fallback_remote_path, ("QUALLIT_UPDATE_REMOTE_PATH", "QUALLIT_PUBLISH_REMOTE_PATH"), endpoint_defaults.get("RemotePath"), defaults.get("RemotePath"), DEFAULT_FALLBACK_REMOTE_PATH)
    args.fallback_host_key = pick(args.fallback_host_key, ("QUALLIT_UPDATE_HOST_KEY", "QUALLIT_PUBLISH_HOST_KEY"), endpoint_defaults.get("HostKey"), DEFAULT_FALLBACK_HOST_KEY)
    args.fallback_public_base_url = pick(args.fallback_public_base_url, ("QUALLIT_FALLBACK_PUBLIC_BASE_URL",), endpoint_defaults.get("FallbackPublicBaseUrl"), "")
    args.fallback_port = int(pick(args.fallback_port, ("QUALLIT_UPDATE_PORT", "QUALLIT_PUBLISH_PORT"), endpoint_defaults.get("Port"), defaults.get("Port"), DEFAULT_FALLBACK_PORT))

    args.fallback_password = args.fallback_password or read_secret("SshPassword", "QUALLIT_UPDATE_SSH_PASSWORD", "QUALLIT_PUBLISH_SSH_PASSWORD")
    args.repository_url = pick(args.repository_url, ("QUALLIT_INSTALLER_REPO_URL",), endpoint_defaults.get("RepositoryUrl"), DEFAULT_INSTALLER_REPO_URL)
    args.target_subdir = pick(args.target_subdir, ("QUALLIT_INSTALLER_TARGET_SUBDIR",), endpoint_defaults.get("TargetSubDir"), DEFAULT_INSTALLER_TARGET_SUBDIR)
    args.remote_base_url = pick(args.remote_base_url, ("QUALLIT_INSTALLER_REMOTE_BASE_URL",), endpoint_defaults.get("RemoteBaseUrl"), DEFAULT_REMOTE_BASE_URL)


def publish_fallback(args: argparse.Namespace, log_file: Path | None) -> None:
    local_dir = Path(args.local_update_dir or UPDATE_DIR).resolve()
    _, payload_files = get_manifest_payload(local_dir)
    used_password = args.fallback_password
    with tempfile.TemporaryDirectory(prefix="quallit-update-publish-") as temp_dir:
        payload_dir = Path(temp_dir)
        for path in payload_files:
            shutil.copy2(path, payload_dir / path.name)
        target = f"{args.fallback_user}@{args.fallback_host}:{args.fallback_remote_path}/"
        pscp = find_command(["pscp.exe", "pscp"])
        scp = find_command(["scp.exe", "scp"])
        if args.dry_run:
            log_message(f"DryRun fallback: arquivos preparados em {payload_dir}", "WARN", log_file)
            return
        if pscp:
            def run_pscp(password: str) -> tuple[int, str]:
                cmd = [pscp, "-batch", "-P", str(args.fallback_port)]
                if args.fallback_host_key:
                    cmd.extend(["-hostkey", args.fallback_host_key])
                with password_file(password) as pw_path:
                    if pw_path:
                        cmd.extend(["-pwfile", pw_path])
                    cmd.extend([str(path) for path in payload_dir.iterdir() if path.is_file()])
                    cmd.append(target)
                    return run_command(cmd, log_file=log_file, label="Publicando pacote fallback (pscp)")

            rc, output = run_pscp(args.fallback_password)
            secret_password = read_secrets().get("sshpassword", "")
            if rc != 0 and secret_password and secret_password != args.fallback_password and is_connection_or_auth_failure(output):
                log_message("Fallback remoto falhou por conexao/autenticacao; tentando credencial de config/secret.md.", "WARN", log_file)
                rc, _ = run_pscp(secret_password)
                if rc == 0:
                    used_password = secret_password
        elif scp:
            rc, _ = run_command([scp, "-o", "BatchMode=yes", "-P", str(args.fallback_port), *[str(path) for path in payload_dir.iterdir() if path.is_file()], target], log_file=log_file, label="Publicando pacote fallback (scp)")
        else:
            raise RuntimeError("Nenhum cliente scp encontrado. Instale PuTTY/pscp ou OpenSSH Client.")
        if rc != 0:
            raise RuntimeError(f"Publicacao fallback falhou com codigo {rc}")
        usb_manifests = usb_manifest_files()
        if usb_manifests:
            manifest_target = f"{args.fallback_user}@{args.fallback_host}:{args.fallback_remote_path}/{USB_MANIFEST_SUBDIR}/"
            if pscp:
                cmd = [pscp, "-batch", "-P", str(args.fallback_port)]
                if args.fallback_host_key:
                    cmd.extend(["-hostkey", args.fallback_host_key])
                with password_file(used_password) as pw_path:
                    if pw_path:
                        cmd.extend(["-pwfile", pw_path])
                    cmd.extend([str(path) for path in usb_manifests])
                    cmd.append(manifest_target)
                    rc, _ = run_command(cmd, log_file=log_file, label="Publicando manifestos de USB no fallback (pscp)")
            else:
                rc, _ = run_command([scp, "-o", "BatchMode=yes", "-P", str(args.fallback_port), *[str(path) for path in usb_manifests], manifest_target],
                                    log_file=log_file, label="Publicando manifestos de USB no fallback (scp)")
            if rc != 0:
                raise RuntimeError(f"Publicacao dos manifestos de USB no fallback falhou com codigo {rc}")
    verify_fallback_files_over_ssh(args, payload_files, used_password, log_file)
    public_base = args.fallback_public_base_url or f"https://{args.fallback_host}/qdisk"
    for path in payload_files:
        verify_remote_file(path, public_base, verify_hash=path.suffix.lower() == ".json", timeout=60, log_file=log_file)
    for path in usb_manifest_files():
        verify_remote_bytes(path, public_base.rstrip("/") + "/" + USB_MANIFEST_SUBDIR, timeout=60, log_file=log_file)


def verify_fallback_files_over_ssh(args: argparse.Namespace, payload_files: list[Path], password: str, log_file: Path | None) -> None:
    plink = find_command(["plink.exe", "plink"])
    if not plink:
        log_message("Verificacao SSH do fallback ignorada: plink nao encontrado.", "WARN", log_file)
        return
    if not password:
        log_message("Verificacao SSH do fallback ignorada: senha nao resolvida para plink.", "WARN", log_file)
        return

    remote_root = args.fallback_remote_path.rstrip("/")
    checks = []
    for path in payload_files:
        remote_path = f"{remote_root}/{path.name}"
        expected_size = path.stat().st_size
        expected_hash = file_sha256(path)
        quoted_remote_path = shell_quote(remote_path)
        quoted_name = shell_quote(path.name)
        checks.append(
            "f={remote}; name={name}; expected_size={size}; expected_hash={hash}; "
            "if [ ! -f \"$f\" ]; then echo \"MISSING $name $f\"; exit 20; fi; "
            "actual_size=$(wc -c < \"$f\" 2>/dev/null | tr -d ' '); "
            "if [ \"$actual_size\" != \"$expected_size\" ]; then echo \"SIZE_MISMATCH $name expected=$expected_size actual=$actual_size\"; exit 21; fi; "
            "if command -v sha256sum >/dev/null 2>&1; then "
            "actual_hash=$(sha256sum \"$f\" | awk '{{print $1}}'); "
            "if [ \"$actual_hash\" != \"$expected_hash\" ]; then echo \"HASH_MISMATCH $name expected=$expected_hash actual=$actual_hash\"; exit 22; fi; "
            "echo \"OK remoto SSH: $name bytes=$actual_size sha256=$actual_hash\"; "
            "else echo \"OK remoto SSH: $name bytes=$actual_size sha256=nao_disponivel\"; fi"
            .format(remote=quoted_remote_path, name=quoted_name, size=expected_size, hash=expected_hash)
        )
    remote_command = " && ".join(checks)

    cmd = [plink, "-batch", "-P", str(args.fallback_port)]
    if args.fallback_host_key:
        cmd.extend(["-hostkey", args.fallback_host_key])
    with password_file(password) as pw_path:
        cmd.extend(["-pwfile", pw_path, "-l", args.fallback_user, args.fallback_host, remote_command])
        rc, _ = run_command(cmd, log_file=log_file, label="Verificando arquivos publicados via SSH (plink)")
    if rc != 0:
        raise RuntimeError(f"Verificacao SSH dos arquivos publicados no fallback falhou com codigo {rc}")
    log_message(f"Verificacao SSH do fallback concluida: {len(payload_files)} arquivo(s).", "OK", log_file)


def git_checked(arguments: list[str], cwd: Path | None = None, log_file: Path | None = None) -> str:
    label = f"git {arguments[0]}" if arguments else "git"
    rc, output = run_command(["git", *arguments], log_file=log_file, cwd=cwd, env={"GIT_TERMINAL_PROMPT": "0", "GCM_INTERACTIVE": "Never"}, label=label)
    if rc != 0:
        raise RuntimeError(f"git {' '.join(arguments)} falhou com codigo {rc}")
    return output


def sync_installer_git(args: argparse.Namespace, log_file: Path | None) -> None:
    local_dir = Path(args.local_update_dir or UPDATE_DIR).resolve()
    manifest_json, payload_files = get_manifest_payload(local_dir)
    temp_repo = None
    repo_dir = Path(args.repository_dir).resolve() if args.repository_dir else None
    try:
        if repo_dir is None:
            temp_repo = tempfile.TemporaryDirectory(prefix="quallit-installer-publish-")
            repo_dir = Path(temp_repo.name)
            clone_args = ["clone", "--depth", "1"]
            if args.branch:
                clone_args.extend(["--branch", args.branch])
            clone_args.extend([args.repository_url, str(repo_dir)])
            git_checked(clone_args, log_file=log_file)
        elif not (repo_dir / ".git").exists():
            raise RuntimeError(f"Diretorio nao e um repositorio Git: {repo_dir}")
        elif args.branch:
            git_checked(["checkout", args.branch], cwd=repo_dir, log_file=log_file)
            git_checked(["pull", "--ff-only"], cwd=repo_dir, log_file=log_file)
        target_dir = repo_dir / args.target_subdir
        target_dir.mkdir(parents=True, exist_ok=True)
        for child in target_dir.iterdir():
            if child.name == ".gitkeep":
                continue
            if child.is_dir():
                shutil.rmtree(child)
            else:
                child.unlink()
        for path in payload_files:
            shutil.copy2(path, target_dir / path.name)
        usb_manifests = usb_manifest_files()
        if usb_manifests:
            manifest_target = target_dir / USB_MANIFEST_SUBDIR
            manifest_target.mkdir(parents=True, exist_ok=True)
            for path in usb_manifests:
                shutil.copy2(path, manifest_target / path.name)
            # Sem conversao de fim de linha: o catalogo guarda o SHA-256 dos bytes exatos (manifestSha256)
            # e o Git do installer convertia CRLF em LF, fazendo o app rejeitar o manifesto baixado.
            (manifest_target / ".gitattributes").write_text("*.manifest.json -text\n", encoding="utf-8", newline="\n")
            log_message(f"Manifestos de USB incluidos: {len(usb_manifests)} em {args.target_subdir}/{USB_MANIFEST_SUBDIR}/", "INFO", log_file)
        status = git_checked(["-C", str(repo_dir), "status", "--short", "--", args.target_subdir], log_file=log_file)
        if not status.strip():
            log_message("Nenhuma alteracao para publicar no repositorio quallit-installer.", "WARN", log_file)
            return
        print(status)
        if args.dry_run:
            log_message("DryRun ativo: commit/push nao executados.", "WARN", log_file)
            return
        commit_message = args.commit_message or f"chore: publica pacote qdisk {manifest_json.get('version', '')}"
        git_checked(["add", "--", args.target_subdir], cwd=repo_dir, log_file=log_file)
        git_checked(["commit", "-m", commit_message], cwd=repo_dir, log_file=log_file)
        if args.no_push:
            log_message("NoPush ativo: commit criado, push nao executado.", "WARN", log_file)
            return
        git_checked(["push"], cwd=repo_dir, log_file=log_file)
    finally:
        if temp_repo is not None:
            temp_repo.cleanup()


def sync_project_git(args: argparse.Namespace, log_file: Path | None) -> None:
    if not (PROJECT_ROOT / ".git").exists():
        raise RuntimeError(f"Diretorio do projeto nao e um repositorio Git: {PROJECT_ROOT}")

    status = git_checked(["status", "--short"], cwd=PROJECT_ROOT, log_file=log_file)
    if not status.strip():
        log_message("Nenhuma alteracao para salvar no Git do projeto.", "WARN", log_file)
        return

    print(status)
    if args.dry_run:
        log_message("DryRun ativo: commit/push do projeto nao executados.", "WARN", log_file)
        return

    version = read_version()
    commit_message = args.commit_message or f"chore: sincroniza projeto {version}"
    git_checked(["add", "-A", "--", "."], cwd=PROJECT_ROOT, log_file=log_file)
    for path in PROJECT_GIT_EXCLUDE_PATHS:
        run_command(["git", "reset", "-q", "--", path], cwd=PROJECT_ROOT, log_file=log_file, env={"GIT_TERMINAL_PROMPT": "0", "GCM_INTERACTIVE": "Never"})
    git_checked(["commit", "-m", commit_message], cwd=PROJECT_ROOT, log_file=log_file)
    if args.no_push:
        log_message("NoPush ativo: commit do projeto criado, push nao executado.", "WARN", log_file)
        return
    git_checked(["push"], cwd=PROJECT_ROOT, log_file=log_file)


def download_url(url: str, timeout: int = 180) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": "qdiskBuild/4.0"})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return response.read()


def remote_content_length(url: str, timeout: int = 60) -> int | None:
    request = urllib.request.Request(url, method="HEAD", headers={"User-Agent": "qdiskBuild/4.0"})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            value = response.headers.get("Content-Length")
            return int(value) if value else None
    except Exception:
        return None


def verify_remote_file(local_file: Path, base_url: str, verify_hash: bool, timeout: int = 180, log_file: Path | None = None) -> None:
    url = base_url.rstrip("/") + "/" + urllib.parse.quote(local_file.name)
    if verify_hash:
        data = download_url(url, timeout=timeout)
        if local_file.suffix.lower() == ".json":
            remote_json = json.loads(data.decode("utf-8-sig"))
            remote_hash = string_sha256(json.dumps(remote_json, ensure_ascii=False, sort_keys=True, separators=(",", ":")))
            local_hash = json_semantic_sha256(local_file)
        else:
            remote_hash = hashlib.sha256(data).hexdigest()
            local_hash = file_sha256(local_file)
        if remote_hash != local_hash:
            raise RuntimeError(f"SHA-256 remoto divergente para {url}")
    else:
        length = remote_content_length(url, timeout=timeout)
        if length is not None and length != local_file.stat().st_size:
            raise RuntimeError(f"Tamanho remoto divergente para {url}. Esperado={local_file.stat().st_size} Remoto={length}")
    log_message(f"OK remoto: {url}", "OK", log_file)


def verify_remote_bytes(local_file: Path, base_url: str, timeout: int = 180, log_file: Path | None = None) -> None:
    """SHA-256 dos bytes exatos (sem interpretar JSON): e assim que o app confere o manifesto de USB
    contra o manifestSha256 do catalogo; uma conversao de fim de linha passaria na comparacao semantica."""
    url = base_url.rstrip("/") + "/" + urllib.parse.quote(local_file.name)
    if hashlib.sha256(download_url(url, timeout=timeout)).hexdigest() != file_sha256(local_file):
        raise RuntimeError(f"SHA-256 remoto (bytes) divergente para {url}")
    log_message(f"OK remoto (bytes): {url}", "OK", log_file)


def verify_installer_remote(args: argparse.Namespace, log_file: Path | None) -> None:
    local_dir = Path(args.local_update_dir or UPDATE_DIR).resolve()
    _, payload_files = get_manifest_payload(local_dir)
    attempts = max(1, args.verify_attempts)
    delay = max(0, args.verify_delay_seconds)
    for attempt in range(1, attempts + 1):
        try:
            log_message(f"Verificando arquivos em {args.remote_base_url} (tentativa {attempt}/{attempts})", "INFO", log_file)
            for path in payload_files:
                verify_hash = path.suffix.lower() == ".json" or args.verify_installer_hash
                verify_remote_file(path, args.remote_base_url, verify_hash=verify_hash, log_file=log_file)
            manifest_base = args.remote_base_url.rstrip("/") + "/" + USB_MANIFEST_SUBDIR
            for path in usb_manifest_files():
                verify_remote_bytes(path, manifest_base, log_file=log_file)
            return
        except Exception as exc:
            if attempt >= attempts:
                raise
            log_message(f"Verificacao ainda nao confirmou a publicacao: {exc}", "WARN", log_file)
            if delay:
                time.sleep(delay)


def validate_prerequisites(args: argparse.Namespace, pfx: Path, log_file: Path | None) -> tuple[bool, str | None, str, str | None]:
    ok = True
    log_message(f"Python        : {sys.version.split()[0]}", "OK", log_file)
    rc, _ = run_command(["dotnet", "--version"], log_file=log_file)
    if rc != 0:
        log_message("dotnet SDK nao encontrado.", "ERROR", log_file)
        ok = False
    ps_exe = find_powershell()
    signtool = None if args.no_sign else find_signtool()
    if not args.no_sign:
        if signtool:
            log_message(f"signtool      : {signtool}", "OK", log_file)
        else:
            log_message("signtool.exe nao encontrado. Use --no-sign para pular assinatura.", "ERROR", log_file)
            ok = False
        if not pfx.is_file():
            log_message(f"PFX nao encontrado: {pfx}", "ERROR", log_file)
            ok = False
    if not PROPS_FILE.is_file():
        log_message(f"Directory.Build.props nao encontrado: {PROPS_FILE}", "ERROR", log_file)
        ok = False
    iscc = find_iscc()
    if not args.no_installer:
        if iscc:
            log_message(f"iscc          : {iscc}", "OK", log_file)
        else:
            log_message("iscc.exe nao encontrado. Instalador sera pulado.", "WARN", log_file)
    log_message(f"PowerShell    : {ps_exe}", "OK", log_file)
    # Testes unitarios antes de qualquer alteracao ou publicacao: falha aqui interrompe o build.
    if ok and not getattr(args, "no_tests", False):
        tests_project = PROJECT_ROOT / "tests" / "qdisk.Tests"
        rc, _ = run_command(["dotnet", "test", str(tests_project), "-c", "Release", "-nologo", "-v", "q"], log_file=log_file,
                            cwd=PROJECT_ROOT, label="Testes unitarios")
        if rc != 0:
            log_message("Testes unitarios falharam: build interrompido (use --no-tests so para diagnostico).", "ERROR", log_file)
            ok = False
        else:
            log_message("Testes unitarios: OK", "OK", log_file)
    return ok, signtool, ps_exe, iscc


def run_sign_only(args: argparse.Namespace, log_file: Path, status: dict[int, str], duration: dict[int, float]) -> int:
    """Reassina o(s) executavel(is) ja publicado(s) em dist/ sem rebuildar
    nada -- util depois de trocar/renovar o certificado de assinatura, sem
    precisar rodar `dotnet publish`/PyInstaller de novo. Assina o que ja
    existir de DIST_EXE/ADM_EXE/NETSPEED_EXE (precisa de pelo menos um)."""
    pfx = Path(os.environ.get("QUALLIT_SIGN_PFX", str(DEFAULT_PFX)))
    password = read_secret("SignPassword", "QUALLIT_SIGN_PASSWORD")
    if not password:
        log_message(MISSING_SIGN_PASSWORD_MSG, "ERROR", log_file)
        return 1
    ts_url = None if args.no_timestamp else os.environ.get("QUALLIT_SIGN_TIMESTAMP", DEFAULT_TIMESTAMP)

    banner(STEP_TITLES[7], 7, len(STEP_TITLES), log_file)
    t0 = time.monotonic()
    signtool = find_signtool()
    if not signtool:
        log_message("signtool.exe nao encontrado.", "ERROR", log_file)
        status[7] = "FALHOU"
        duration[7] = time.monotonic() - t0
        return 1
    if not pfx.is_file():
        log_message(f"PFX nao encontrado: {pfx}", "ERROR", log_file)
        status[7] = "FALHOU"
        duration[7] = time.monotonic() - t0
        return 1

    targets = [p for p in (DIST_EXE, ADM_EXE, NETSPEED_EXE, ISO_MANIFEST_TOOL_EXE) if p.is_file()]
    if not targets:
        log_message(f"Nenhum executavel encontrado para assinar ({DIST_EXE}, {ADM_EXE} ou {NETSPEED_EXE}). Rode o build antes.", "ERROR", log_file)
        status[7] = "FALHOU"
        duration[7] = time.monotonic() - t0
        return 1

    all_signed = True
    for exe in targets:
        all_signed = sign_exe(exe, signtool, pfx, password, ts_url, log_file) and all_signed
    status[7] = "OK" if all_signed else "FALHOU"
    duration[7] = time.monotonic() - t0
    if not all_signed:
        return 1

    banner(STEP_TITLES[8], 8, len(STEP_TITLES), log_file)
    t0 = time.monotonic()
    ps_exe = find_powershell()
    for exe in targets:
        verify_sign(exe, signtool, ps_exe, log_file)
    status[8] = "OK"
    duration[8] = time.monotonic() - t0
    return 0


def run_full_build(args: argparse.Namespace, log_file: Path, status: dict[int, str], duration: dict[int, float]) -> int:
    pfx = Path(os.environ.get("QUALLIT_SIGN_PFX", str(DEFAULT_PFX)))
    password = read_secret("SignPassword", "QUALLIT_SIGN_PASSWORD")
    if not args.no_sign and not password:
        log_message(MISSING_SIGN_PASSWORD_MSG, "ERROR", log_file)
        return 1
    ts_url = None if args.no_timestamp else os.environ.get("QUALLIT_SIGN_TIMESTAMP", DEFAULT_TIMESTAMP)
    version = read_version() if PROPS_FILE.is_file() else "0.0.00.00"

    banner(STEP_TITLES[1], 1, len(STEP_TITLES), log_file)
    t0 = time.monotonic()
    ok, signtool, ps_exe, iscc = validate_prerequisites(args, pfx, log_file)
    status[1] = "OK" if ok else "FALHOU"
    duration[1] = time.monotonic() - t0
    if not ok:
        return 1

    banner(STEP_TITLES[2], 2, len(STEP_TITLES), log_file)
    t0 = time.monotonic()
    if args.no_bump_version:
        log_message(f"Versao atual: {version}", "INFO", log_file)
        status[2] = "PULADO"
    else:
        version = bump_version()
        log_message(f"Versao nova: {version}", "OK", log_file)
        status[2] = "OK"
    # Verificadores do token diario embutidos no app (sem internal_id), com janela contada a partir deste build.
    verifier_script = TOOLS_DIR / "utils" / "generate_token_verifiers.py"
    verifier_run = subprocess.run([sys.executable, str(verifier_script)], cwd=str(PROJECT_ROOT), capture_output=True, text=True)
    if verifier_run.returncode != 0:
        log_message(f"Falha ao gerar os verificadores do token: {verifier_run.stderr.strip() or verifier_run.stdout.strip()}", "ERROR", log_file)
        status[2] = "FALHOU"
        duration[2] = time.monotonic() - t0
        return 1
    log_message(verifier_run.stdout.strip(), "OK", log_file)
    duration[2] = time.monotonic() - t0

    banner(STEP_TITLES[3], 3, len(STEP_TITLES), log_file)
    t0 = time.monotonic()
    if args.no_manual_update:
        status[3] = "PULADO"
    else:
        status[3] = "OK" if embed_manual_images(log_file) else "AVISO"
    duration[3] = time.monotonic() - t0

    banner(STEP_TITLES[4], 4, len(STEP_TITLES), log_file)
    t0 = time.monotonic()
    if args.no_clean_artifacts:
        status[4] = "PULADO"
    else:
        clean_ok = True
        for project in ("qdisk.UI", "qdisk.Core", "qdisk.Infrastructure"):
            rc, _ = run_command(
                ["dotnet", "clean", f"src/{project}", "-c", "Release", *dotnet_verbosity_args()],
                log_file=log_file, label=f"Limpando {project}",
            )
            clean_ok = clean_ok and rc == 0
        status[4] = "OK" if clean_ok else "AVISO"
    duration[4] = time.monotonic() - t0

    banner(STEP_TITLES[5], 5, len(STEP_TITLES), log_file)
    t0 = time.monotonic()
    rc, _ = run_command(
        ["dotnet", "publish", "src/qdisk.UI", "-c", "Release", "-r", "win-x64", "--self-contained", "true",
         "/p:PublishSingleFile=true", "/p:EnableCompressionInSingleFile=true", "/p:IncludeNativeLibrariesForSelfExtract=true",
         *dotnet_verbosity_args()],
        log_file=log_file, progress_bar=True, project_total=3, label="Publicando app principal (dotnet publish)",
    )
    status[5] = "OK" if rc == 0 and DIST_EXE.is_file() else "FALHOU"
    duration[5] = time.monotonic() - t0
    if status[5] == "FALHOU":
        return 1

    banner(STEP_TITLES[6], 6, len(STEP_TITLES), log_file)
    t0 = time.monotonic()
    status[6] = "OK" if build_qdisk_adm_exe(log_file, version) and build_iso_manifest_tool_exe(log_file) and build_netspeedtest_exe(log_file, version) else "FALHOU"
    duration[6] = time.monotonic() - t0
    if status[6] == "FALHOU":
        return 1

    banner(STEP_TITLES[7], 7, len(STEP_TITLES), log_file)
    t0 = time.monotonic()
    if args.no_sign:
        status[7] = "PULADO"
    else:
        main_signed = sign_exe(DIST_EXE, signtool, pfx, password, ts_url, log_file)
        adm_signed = sign_exe(ADM_EXE, signtool, pfx, password, ts_url, log_file)
        netspeed_signed = sign_exe(NETSPEED_EXE, signtool, pfx, password, ts_url, log_file)
        helper_signed = sign_exe(ISO_MANIFEST_TOOL_EXE, signtool, pfx, password, ts_url, log_file) if ISO_MANIFEST_TOOL_EXE.is_file() else True
        status[7] = "OK" if main_signed and adm_signed and netspeed_signed and helper_signed else "FALHOU"
    duration[7] = time.monotonic() - t0

    banner(STEP_TITLES[8], 8, len(STEP_TITLES), log_file)
    t0 = time.monotonic()
    if status.get(7) == "OK":
        verify_sign(DIST_EXE, signtool, ps_exe, log_file)
        verify_sign(ADM_EXE, signtool, ps_exe, log_file)
        verify_sign(NETSPEED_EXE, signtool, ps_exe, log_file)
        if ISO_MANIFEST_TOOL_EXE.is_file():
            verify_sign(ISO_MANIFEST_TOOL_EXE, signtool, ps_exe, log_file)
        status[8] = "OK"
    else:
        status[8] = "PULADO"
    duration[8] = time.monotonic() - t0

    banner(STEP_TITLES[9], 9, len(STEP_TITLES), log_file)
    t0 = time.monotonic()
    if args.no_dist_package:
        status[9] = "PULADO"
    else:
        status[9] = "OK" if create_dist_zip(version, log_file) else "FALHOU"
    duration[9] = time.monotonic() - t0

    banner(STEP_TITLES[10], 10, len(STEP_TITLES), log_file)
    t0 = time.monotonic()
    if args.no_installer or not iscc:
        status[10] = "PULADO"
    else:
        status[10] = "OK" if build_installer(version, iscc, signtool, pfx, password, ts_url, args.no_sign, log_file) else "FALHOU"
    duration[10] = time.monotonic() - t0

    banner(STEP_TITLES[11], 11, len(STEP_TITLES), log_file)
    t0 = time.monotonic()
    if args.no_backup:
        status[11] = "PULADO"
    else:
        status[11] = "OK" if create_zip_backup(version, log_file) else "FALHOU"
    duration[11] = time.monotonic() - t0

    banner(STEP_TITLES[12], 12, len(STEP_TITLES), log_file)
    t0 = time.monotonic()
    if args.no_post_clean:
        status[12] = "PULADO"
    else:
        for directory in sorted((PROJECT_ROOT / "src").rglob("obj")) + sorted((PROJECT_ROOT / "src").rglob("bin")):
            if directory.is_dir():
                shutil.rmtree(directory, ignore_errors=True)
        status[12] = "OK"
    duration[12] = time.monotonic() - t0
    return 0


def new_log_file(argv: list[str]) -> Path:
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    path = LOG_DIR / f"build_v4_{datetime.datetime.now().strftime('%Y%m%d_%H%M%S')}.log"
    path.write_text("Q-DISK - Build Release v4\n" f"Iniciado em : {datetime.datetime.now().strftime('%d/%m/%Y %H:%M:%S')}\n" f"Argumentos  : {argv}\n" + "=" * 60 + "\n\n", encoding="utf-8")
    return path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Build release v4 autocontido.")
    parser.add_argument("--no-sign", action="store_true")
    parser.add_argument("--no-tests", action="store_true", help="Nao roda os testes unitarios na etapa 1.")
    parser.add_argument("--no-backup", action="store_true")
    parser.add_argument("--no-bump-version", action="store_true")
    parser.add_argument("--no-timestamp", action="store_true")
    parser.add_argument("--no-post-clean", action="store_true")
    parser.add_argument("--no-installer", action="store_true")
    parser.add_argument("--verify-only", action="store_true")
    parser.add_argument(
        "--sign-only", action="store_true",
        help="Reassina o(s) executavel(is) ja publicado(s) em dist/ (app principal, QDisk_Adm e/ou NetSpeedTest) sem rebuildar nada. Util apos trocar/renovar o certificado.",
    )
    parser.add_argument("--no-update-package", action="store_true")
    parser.add_argument("--no-fallback-publish", action="store_true")
    parser.add_argument("--no-installer-git-publish", action="store_true")
    parser.add_argument("--no-project-git-publish", action="store_true")
    parser.add_argument("--verify_installer", "--verify-installer", action="store_true")
    parser.add_argument("--sync_only", "--sync-only", "--sync-update", dest="sync_only", action="store_true")
    parser.add_argument("--sync-project", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--local-update-dir", default="")
    parser.add_argument("--update-installer-url", default=os.environ.get("QUALLIT_UPDATE_INSTALLER_URL", DEFAULT_INSTALLER_URL))
    parser.add_argument("--update-base-url", default=os.environ.get("QUALLIT_UPDATE_BASE_URL", "").strip() or None)
    parser.add_argument("--update-manifest-name", default=DEFAULT_MANIFEST_NAME)
    parser.add_argument("--update-artifact-name", default="")
    parser.add_argument("--update-notes", action="append", default=[])
    parser.add_argument("--update-mandatory", action="store_true")
    parser.add_argument("--update-minimum-supported-version", default="")
    parser.add_argument("--fallback-host", default=None)
    parser.add_argument("--fallback-port", type=int, default=None)
    parser.add_argument("--fallback-user", default=None)
    parser.add_argument("--fallback-remote-path", default=None)
    parser.add_argument("--fallback-host-key", default=None)
    parser.add_argument("--fallback-public-base-url", default=None)
    parser.add_argument("--fallback-password", default="")
    parser.add_argument("--repository-url", default=None)
    parser.add_argument("--repository-dir", default="")
    parser.add_argument("--branch", default="")
    parser.add_argument("--target-subdir", default=None)
    parser.add_argument("--commit-message", default="")
    parser.add_argument("--no-push", action="store_true")
    parser.add_argument("--no-verify-remote", action="store_true")
    parser.add_argument("--remote-base-url", default=None)
    parser.add_argument("--verify-attempts", type=int, default=5)
    parser.add_argument("--verify-delay-seconds", type=int, default=10)
    parser.add_argument("--verify-installer-hash", action="store_true")
    parser.add_argument("--env-file", default="")
    parser.add_argument("--preview", "--preview-steps", dest="preview_steps", action="store_true")
    parser.add_argument(
        "--show-progress", action="store_true", default=False,
        help="Mostra progresso do dotnet (projeto a projeto) e transmite a saida ao vivo no console. Padrao: saida minima.",
    )
    parser.add_argument(
        "--show-full-info", action="store_true", default=False,
        help="Mostra o maximo de detalhe possivel (progresso + build MSBuild detalhado, alvo/tarefa a tarefa via -v detailed) e transmite tudo ao vivo. Padrao: saida minima.",
    )
    parser.add_argument(
        "--show-progress-bar", action="store_true", default=False,
        help="Mostra uma barra de progresso compacta (%% + contagem de projetos) para a etapa de build/publish do dotnet, em vez de texto bruto.",
    )
    args = parser.parse_args()
    args.no_manual_update = False
    args.no_clean_artifacts = False
    args.no_dist_package = False
    args.use_run_config = False
    return args


def run_step(step: int, status: dict[int, str], duration: dict[int, float], log_file: Path, func, skipped: bool = False) -> int:
    banner(STEP_TITLES[step], step, len(STEP_TITLES), log_file)
    t0 = time.monotonic()
    if skipped:
        status[step] = "PULADO"
        duration[step] = time.monotonic() - t0
        return 0
    try:
        func()
        status[step] = "OK"
        return 0
    except Exception as exc:
        log_message(str(exc), "ERROR", log_file)
        status[step] = "FALHOU"
        return 1
    finally:
        duration[step] = time.monotonic() - t0


def main() -> int:
    enable_ansi()
    args = parse_args()

    global SHOW_PROGRESS, SHOW_FULL_INFO, SHOW_PROGRESS_BAR
    SHOW_PROGRESS = args.show_progress
    SHOW_FULL_INFO = args.show_full_info
    SHOW_PROGRESS_BAR = args.show_progress_bar

    if args.env_file:
        load_env_file(Path(args.env_file))
    for candidate in (TOOLS_DIR / ".env", TOOLS_DIR / "publish_update.env", PROJECT_ROOT / ".env", PROJECT_ROOT / "config" / "publish_update.env"):
        load_env_file(candidate)
    apply_publish_configuration(args)
    apply_run_config(args)

    log_file = new_log_file(sys.argv[1:])
    status: dict[int, str] = {}
    duration: dict[int, float] = {}
    print_header(args, log_file)
    banner("Pipeline v4 autocontido", log_file=log_file)
    log_message(f"Projeto: {PROJECT_ROOT}", "INFO", log_file)
    log_message(f"Log    : {log_file}", "INFO", log_file)
    if args.preview_steps:
        print_execution_preview(args, log_file)
        log_message(f"Log completo: {log_file}", "INFO", log_file)
        return 0

    try:
        if args.verify_installer:
            for step in range(1, 18):
                status[step] = "PULADO"
            rc = run_step(15, status, duration, log_file, lambda: verify_installer_remote(args, log_file), skipped=args.no_verify_remote)
            log_step_summary(status, duration, log_file)
            log_message(f"Log completo: {log_file}", "INFO", log_file)
            return rc

        if args.sync_project:
            for step in range(1, 18):
                status[step] = "PULADO"
            rc = run_step(11, status, duration, log_file, lambda: create_zip_backup(read_version(), log_file), skipped=args.no_backup)
            if rc != 0:
                log_step_summary(status, duration, log_file)
                return rc
            rc = run_step(17, status, duration, log_file, lambda: sync_project_git(args, log_file), skipped=args.no_project_git_publish)
            if rc != 0:
                log_step_summary(status, duration, log_file)
                return rc
            if args.sync_only:
                rc = run_step(14, status, duration, log_file, lambda: sync_installer_git(args, log_file), skipped=args.no_installer_git_publish)
                if rc != 0:
                    log_step_summary(status, duration, log_file)
                    return rc
                if not args.no_verify_remote and not args.no_installer_git_publish:
                    rc = run_step(15, status, duration, log_file, lambda: verify_installer_remote(args, log_file))
                else:
                    status[15] = "PULADO"
            log_step_summary(status, duration, log_file)
            log_message(f"Log completo: {log_file}", "INFO", log_file)
            return rc

        if args.sync_only:
            for step in range(1, 14):
                status[step] = "PULADO"
            status[16] = "PULADO"
            status[17] = "PULADO"
            rc = run_step(14, status, duration, log_file, lambda: sync_installer_git(args, log_file), skipped=args.no_installer_git_publish)
            if rc == 0 and not args.no_verify_remote and not args.no_installer_git_publish:
                rc = run_step(15, status, duration, log_file, lambda: verify_installer_remote(args, log_file))
            else:
                status[15] = "PULADO"
            log_step_summary(status, duration, log_file)
            log_message(f"Log completo: {log_file}", "INFO", log_file)
            return rc

        if args.verify_only:
            for step in range(1, 18):
                status[step] = "PULADO"
            signtool = find_signtool()
            if not signtool or not DIST_EXE.is_file():
                log_message("Nao foi possivel verificar assinatura existente.", "ERROR", log_file)
                status[8] = "FALHOU"
                log_step_summary(status, duration, log_file)
                return 1
            verify_sign(DIST_EXE, signtool, find_powershell(), log_file)
            status[8] = "OK"
            log_step_summary(status, duration, log_file)
            log_message(f"Log completo: {log_file}", "INFO", log_file)
            return 0

        if args.sign_only:
            for step in range(1, 18):
                status[step] = "PULADO"
            rc = run_sign_only(args, log_file, status, duration)
            log_step_summary(status, duration, log_file)
            log_message(f"Log completo: {log_file}", "INFO", log_file)
            return rc

        rc = run_full_build(args, log_file, status, duration)
        if rc != 0:
            log_step_summary(status, duration, log_file)
            return rc

        args.prefer_setup = not args.no_installer
        rc = run_step(13, status, duration, log_file, lambda: create_update_package(args), skipped=args.no_update_package)
        if rc != 0:
            log_step_summary(status, duration, log_file)
            return rc
        rc = run_step(14, status, duration, log_file, lambda: sync_installer_git(args, log_file), skipped=args.no_installer_git_publish)
        if rc != 0:
            log_step_summary(status, duration, log_file)
            return rc
        if not args.no_verify_remote:
            rc = run_step(15, status, duration, log_file, lambda: verify_installer_remote(args, log_file))
            if rc != 0:
                log_step_summary(status, duration, log_file)
                return rc
        else:
            status[15] = "PULADO"
        fallback_rc = run_step(16, status, duration, log_file, lambda: publish_fallback(args, log_file), skipped=args.no_fallback_publish)
        if fallback_rc != 0:
            log_message("Publicacao fallback remoto falhou; prosseguindo porque o Git installer e o destino principal.", "WARN", log_file)
        rc = run_step(17, status, duration, log_file, lambda: sync_project_git(args, log_file), skipped=args.no_project_git_publish)
        if rc != 0:
            log_step_summary(status, duration, log_file)
            return rc

        log_step_summary(status, duration, log_file)
        log_message(f"Log completo: {log_file}", "INFO", log_file)
        return 0
    except KeyboardInterrupt:
        log_message("Operacao cancelada pelo usuario.", "WARN", log_file)
        return 130


if __name__ == "__main__":
    sys.exit(main())
