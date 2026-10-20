from __future__ import annotations

import argparse
import copy
import datetime as dt
import hashlib
import json
import logging
import os
import queue
import re
import shutil
import subprocess
import sys
import threading
from dataclasses import dataclass, field
from pathlib import Path
from tkinter import BOTH, END, LEFT, RIGHT, VERTICAL, X, Y, filedialog, messagebox, ttk
import tkinter as tk
import tkinter.font as tkfont


APP_TITLE = "Quallit Catalogo ADM"
def get_app_dir() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent


def get_repo_root(app_dir: Path) -> Path:
    env_root = os.environ.get("QDISK_ROOT", "").strip()
    if env_root:
        return Path(env_root).resolve()
    if getattr(sys, "frozen", False):
        if app_dir.name.lower() == "adm" and app_dir.parent.name.lower() == "dist":
            return app_dir.parent.parent
        return app_dir.parent
    return app_dir.parent


def get_config_dir(app_dir: Path, repo_root: Path) -> Path:
    env_config = os.environ.get("QDISK_CONFIG_DIR", "").strip()
    if env_config:
        return Path(env_config).resolve()
    return repo_root / "config"


APP_DIR = get_app_dir()
REPO_ROOT = get_repo_root(APP_DIR)
PROJECT_CONFIG_DIR = REPO_ROOT / "config"
CONFIG_DIR = get_config_dir(APP_DIR, REPO_ROOT)
VERSION_FILE = REPO_ROOT / "version_info.txt"
PUBLISH_CATALOGS_SCRIPT = REPO_ROOT / "tools" / "utils" / "publish_catalogs.ps1"
BUILD_SCRIPT = REPO_ROOT / "tools" / "utils" / "Build-OneFile-Sign_v4.bat"
ISO_MANIFEST_TOOL_PROJECT = REPO_ROOT / "tools" / "Quallit.IsoManifestTool" / "Quallit.IsoManifestTool.csproj"
LOG_DIR = APP_DIR / "log"
DEBUG_LOG_DIR = APP_DIR / "log_debug"
CATALOG_PATTERN = "qdisk.quallit.*.json"
HASH_CHUNK_SIZE = 1024 * 1024
ISO_MANIFEST_HELPER_TIMEOUT_SECONDS = 10 * 60
IMAGE_FILETYPES = (
    ("Imagens TIBX/ISO", "*.tibx *.iso"),
    ("Acronis TIBX", "*.tibx"),
    ("Imagens ISO", "*.iso"),
    ("Todos os arquivos", "*.*"),
)

MD5_RE = re.compile(r"^[a-fA-F0-9]{32}$")
SHA256_RE = re.compile(r"^[a-fA-F0-9]{64}$")
URL_RE = re.compile(r"^https?://", re.IGNORECASE)
NAME_CLEANUP_RE = re.compile(r"[^A-Za-z0-9._-]+")
# Cores ANSI do script de build (feitas para o terminal); o console do build no ADM nao as interpreta.
ANSI_ESCAPE_RE = re.compile(r"\x1b\[[0-9;]*m")
ENDPOINTS_FILE = CONFIG_DIR / "qdisk.endpoints.json"
TOKENS_FILE = CONFIG_DIR / "qdisk.tokens.json"
ENDPOINT_DEFAULTS = {
    "primary.baseUrl": "https://installer.quallit.com.br/qdisk/",
    "fallback.publicBaseUrl": "https://mateweb.ddns.net/qdisk/",
    "fallback.host": "mateweb.ddns.net",
    "fallback.port": "2222",
    "fallback.hostKey": "",
    "fallback.user": "qdisk-update",
    "fallback.remotePath": "/store/repositorio/qdisk",
    "installerGit.repositoryUrl": "https://github.com/quallitdesenv/quallit-installer.git",
    "installerGit.targetSubdir": "public/qdisk",
}
TOKEN_PROFILES = ("RD", "PNB", "DevTeam")
TOKEN_DEFAULTS = {
    "RD.displayName": "RD",
    "RD.internalId": "010911",
    "RD.catalogFile": "qdisk.quallit.rd.json",
    "PNB.displayName": "PNB",
    "PNB.internalId": "060171",
    "PNB.catalogFile": "qdisk.quallit.pnb.json",
    "DevTeam.displayName": "DevTeam",
    "DevTeam.internalId": "270777",
    "DevTeam.catalogFile": "qdisk.quallit.devteam.json",
    "downloader.profile": "RD",
    "downloader.baseDate": "1970-10-01",
}
UPLOAD_TARGET_LABELS = {
    "Both": "Installer Git + Fallback mateweb",
    "InstallerGit": "Somente installer Git",
    "Fallback": "Somente fallback mateweb",
}
EDITABLE_ITEM_FIELDS = {"enabled", "name", "group", "fileUrl", "fileName", "md5", "sha256", "imageSizeBytes", "usbIdentification", "usbWritePolicy", "description"}
# Metodos de gravacao USB (mesmos nomes de UsbWriteMethods no Core e dos manifestos).
WRITE_METHODS = ("logical-fat32", "uefi-ntfs", "raw", "fat32-split-wim")
WRITE_METHOD_LABELS = {
    "logical-fat32": "Cópia lógica FAT32",
    "uefi-ntfs": "UEFI:NTFS",
    "raw": "RAW/DD",
    "fat32-split-wim": "FAT32 com split do WIM",
}
TRANSFORMING_WRITE_METHODS = {"fat32-split-wim"}
# Analise do helper .NET (regra do Core). Outro valor, ou ausente, e analise parcial (fallback Python).
FULL_ANALYSIS_ENGINE = "core-1"
# Descricao funcional da imagem, exibida no app (lista de USB e dica em Downloads).
DESCRIPTION_MAX_LENGTH = 600


@dataclass
class CatalogDocument:
    path: Path | None = None
    items: list[dict] = field(default_factory=list)
    build: dict | None = None
    source_format: str = "array"


def read_version() -> str:
    if not VERSION_FILE.exists():
        return "0.0.0.0"
    content = VERSION_FILE.read_text(encoding="utf-8", errors="ignore")
    for line in content.splitlines():
        match = re.match(r"^\s*Vers[aã]o\s*:\s*(?P<version>\S+)", line, re.IGNORECASE)
        if match:
            return match.group("version").strip()
    match = re.search(r"\d+(?:\.\d+){1,3}", content)
    if match:
        return match.group(0)
    return "0.0.0.0"


def load_catalog(path: Path) -> CatalogDocument:
    raw = path.read_text(encoding="utf-8-sig")
    if not raw.strip():
        raise ValueError("JSON vazio.")

    data = json.loads(raw)
    if isinstance(data, list):
        return CatalogDocument(path=path, items=[normalize_item(item) for item in data], source_format="array")

    if isinstance(data, dict) and isinstance(data.get("items"), list):
        return CatalogDocument(
            path=path,
            items=[normalize_item(item) for item in data["items"]],
            build=data.get("build") if isinstance(data.get("build"), dict) else None,
            source_format="package",
        )

    raise ValueError("Formato invalido. Use array direto ou objeto com 'items'.")


def normalize_item(item: object) -> dict:
    if not isinstance(item, dict):
        raise ValueError("Item de catalogo deve ser objeto JSON.")
    enabled_value = item.get("enabled", True)
    if isinstance(enabled_value, str):
        enabled = enabled_value.strip().lower() not in ("false", "0", "no", "nao", "não", "inativo", "desativado")
    else:
        enabled = bool(enabled_value)
    normalized = {
        "enabled": enabled,
        "name": str(item.get("name", "")).strip(),
        "group": str(item.get("group", "")).strip(),
        "fileUrl": str(item.get("fileUrl", "")).strip(),
        "fileName": str(item.get("fileName", "")).strip(),
        "md5": str(item.get("md5", "")).strip().lower(),
        "sha256": str(item.get("sha256", "")).strip().lower(),
        "imageSizeBytes": item.get("imageSizeBytes", ""),
    }
    if normalized["imageSizeBytes"] in (None, ""):
        normalized["imageSizeBytes"] = ""
    else:
        normalized["imageSizeBytes"] = str(normalized["imageSizeBytes"]).strip()
    if "usbIdentification" in item:
        normalized["usbIdentification"] = item.get("usbIdentification")
    if item.get("usbWritePolicy") not in (None, ""):
        normalized["usbWritePolicy"] = item.get("usbWritePolicy")
    description = str(item.get("description") or "").strip()
    if description:
        normalized["description"] = description
    extra = {key: value for key, value in item.items() if key not in EDITABLE_ITEM_FIELDS and key != "_extra"}
    if isinstance(item.get("_extra"), dict):
        extra = {**item["_extra"], **extra}
    if extra:
        normalized["_extra"] = extra
    return normalized


def compact_item(item: dict) -> dict:
    output = dict(item.get("_extra", {})) if isinstance(item.get("_extra"), dict) else {}
    output.update({
        "name": item["name"].strip(),
        "group": item["group"].strip(),
        "fileUrl": item["fileUrl"].strip(),
    })
    if item.get("enabled") is False:
        output["enabled"] = False
    elif "enabled" in output:
        output.pop("enabled", None)
    md5 = item.get("md5", "").strip().lower()
    sha256 = item.get("sha256", "").strip().lower()
    file_name = item.get("fileName", "").strip()
    size = str(item.get("imageSizeBytes", "")).strip()
    if file_name:
        output["fileName"] = file_name
    if md5:
        output["md5"] = md5
    if sha256:
        output["sha256"] = sha256
    if size:
        output["imageSizeBytes"] = int(size)
    if "usbIdentification" in item and item.get("usbIdentification") not in (None, ""):
        output["usbIdentification"] = item.get("usbIdentification")
    if item.get("usbWritePolicy") not in (None, ""):
        output["usbWritePolicy"] = item.get("usbWritePolicy")
    description = str(item.get("description") or "").strip()
    if description:
        output["description"] = description
    return output


def usb_identification_modes(usb_id: object) -> list[dict]:
    if not isinstance(usb_id, dict):
        return []
    modes = usb_id.get("modes")
    if isinstance(modes, list):
        return [mode for mode in modes if isinstance(mode, dict)]
    if isinstance(usb_id.get("mode"), str):
        return [usb_id]
    return []


def usb_identification_status(usb_id: object) -> str:
    if usb_id in (None, ""):
        return ""
    modes = usb_identification_modes(usb_id)
    if not modes:
        return "erro"
    values: list[str] = []
    for mode in modes:
        mode_name = str(mode.get("mode", "")).strip().lower()
        if mode_name in ("logical", "raw") and mode_name not in values:
            values.append(mode_name)
        elif mode_name and mode_name not in values:
            values.append(mode_name)
    return "+".join(values) if values else "erro"


def usb_identification_summary(usb_id: object) -> str:
    if usb_id in (None, ""):
        return "Sem usbIdentification."
    modes = usb_identification_modes(usb_id)
    if not modes:
        return "usbIdentification invalido."
    summaries: list[str] = []
    for mode in modes:
        mode_name = str(mode.get("mode", "")).strip().lower() or "?"
        if mode_name == "logical":
            sentinels = mode.get("sentinelFiles") if isinstance(mode.get("sentinelFiles"), list) else []
            summaries.append(
                f"logical: arquivos={mode.get('fileCount', '?')} bytes={mode.get('totalBytes', '?')} "
                f"sentinelas={len(sentinels)} manifesto={mode.get('manifestUrl', '')}"
            )
        elif mode_name == "raw":
            summaries.append(f"raw: bytes={mode.get('sizeBytes', '?')} sha256={mode.get('imageSha256', '')}")
        else:
            summaries.append(mode_name)
    return " | ".join(summaries)


def validate_usb_identification(label: str, item: dict) -> list[str]:
    errors: list[str] = []
    usb_id = item.get("usbIdentification")
    if usb_id in (None, ""):
        return errors
    if not isinstance(usb_id, dict):
        return [f"{label}: usbIdentification deve ser objeto JSON."]
    modes = usb_identification_modes(usb_id)
    if not modes:
        return [f"{label}: usbIdentification deve informar mode ou modes."]

    for mode_index, mode in enumerate(modes, start=1):
        prefix = f"{label}: usbIdentification[{mode_index}]"
        schema = mode.get("schema", usb_id.get("schema"))
        if schema is not None:
            try:
                if int(schema) <= 0:
                    errors.append(f"{prefix}: schema deve ser maior que zero.")
            except (TypeError, ValueError):
                errors.append(f"{prefix}: schema deve ser numerico.")

        mode_name = str(mode.get("mode", "")).strip().lower()
        if mode_name == "logical":
            tree_hash = str(mode.get("treeHash", "")).strip()
            manifest_url = str(mode.get("manifestUrl", "")).strip()
            if not SHA256_RE.match(tree_hash):
                errors.append(f"{prefix}: treeHash invalido.")
            if not manifest_url:
                errors.append(f"{prefix}: manifestUrl obrigatorio.")
            elif not (URL_RE.match(manifest_url) or not re.match(r"^[a-zA-Z][a-zA-Z0-9+.-]*:", manifest_url)):
                errors.append(f"{prefix}: manifestUrl deve ser HTTP/HTTPS ou caminho relativo.")
            manifest_sha256 = str(mode.get("manifestSha256", "")).strip()
            if manifest_sha256 and not SHA256_RE.match(manifest_sha256):
                errors.append(f"{prefix}: manifestSha256 invalido.")
            for field_name in ("fileCount", "totalBytes"):
                try:
                    if int(mode.get(field_name, 0)) <= 0:
                        errors.append(f"{prefix}: {field_name} deve ser maior que zero.")
                except (TypeError, ValueError):
                    errors.append(f"{prefix}: {field_name} deve ser numerico.")
            sentinels = mode.get("sentinelFiles", [])
            if sentinels is not None and not isinstance(sentinels, list):
                errors.append(f"{prefix}: sentinelFiles deve ser lista.")
            elif isinstance(sentinels, list):
                for sentinel_index, sentinel in enumerate(sentinels, start=1):
                    if not isinstance(sentinel, dict):
                        errors.append(f"{prefix}: sentinelFiles[{sentinel_index}] deve ser objeto.")
                        continue
                    if not str(sentinel.get("path", "")).strip():
                        errors.append(f"{prefix}: sentinelFiles[{sentinel_index}].path obrigatorio.")
                    try:
                        if int(sentinel.get("size", 0)) < 0:
                            errors.append(f"{prefix}: sentinelFiles[{sentinel_index}].size invalido.")
                    except (TypeError, ValueError):
                        errors.append(f"{prefix}: sentinelFiles[{sentinel_index}].size deve ser numerico.")
                    if not SHA256_RE.match(str(sentinel.get("sha256", "")).strip()):
                        errors.append(f"{prefix}: sentinelFiles[{sentinel_index}].sha256 invalido.")
        elif mode_name == "raw":
            if not SHA256_RE.match(str(mode.get("imageSha256", "")).strip()):
                errors.append(f"{prefix}: imageSha256 invalido.")
            try:
                if int(mode.get("sizeBytes", 0)) <= 0:
                    errors.append(f"{prefix}: sizeBytes deve ser maior que zero.")
            except (TypeError, ValueError):
                errors.append(f"{prefix}: sizeBytes deve ser numerico.")
        else:
            errors.append(f"{prefix}: mode deve ser logical ou raw.")
    return errors


def technical_analysis_of(item: dict) -> dict:
    usb_id = item.get("usbIdentification")
    for mode in usb_identification_modes(usb_id):
        analysis = mode.get("technicalAnalysis")
        if isinstance(analysis, dict):
            return analysis
    return {}


def write_policy_status(item: dict) -> str:
    policy = item.get("usbWritePolicy")
    if policy in (None, ""):
        return ""
    if not isinstance(policy, dict):
        return "erro"
    preferred = str(policy.get("preferredMethod", "")).strip()
    item_sha256 = str(item.get("sha256", "")).strip().lower()
    if item_sha256 and str(policy.get("imageSha256", "")).strip().lower() != item_sha256:
        return f"{preferred} (outra imagem)"
    return preferred


def suggest_write_policy(item: dict) -> tuple[dict | None, str]:
    """Monta a politica a partir da analise gravada no item. Devolve (politica, mensagem)."""
    analysis = technical_analysis_of(item)
    if not analysis:
        return None, "Item sem analise tecnica: gere o manifesto USB da ISO primeiro."
    if analysis.get("analysisEngine") != FULL_ANALYSIS_ENGINE:
        return None, (
            "Analise parcial (gerada sem o helper .NET): nao detecta El Torito, Syslinux, GRUB, Bootwiz nem ISO hibrida. "
            "Gere o manifesto novamente com o helper ou defina o metodo manualmente."
        )
    recommended = str(analysis.get("recommendedMethod", "")).strip()
    methods = [entry for entry in analysis.get("methods") or [] if isinstance(entry, dict) and entry.get("method") in WRITE_METHODS]
    allowed = [entry["method"] for entry in methods if not entry.get("transformsContent")]
    if recommended not in allowed:
        return None, f"A analise nao encontrou metodo sem transformar a imagem (recomendado: {recommended or 'nenhum'})."
    reasons = [str(reason) for reason in analysis.get("reasons") or []]
    preferred_targets = next((list(entry.get("bootTargets") or []) for entry in methods if entry["method"] == recommended), [])
    policy = {
        "imageSha256": str(item.get("sha256", "")).strip().lower(),
        "preferredMethod": recommended,
        "preferredBootTargets": preferred_targets,
        "allowedMethods": allowed,
        "preserveSourceImage": True,
        "allowContentTransformations": False,
        "note": " ".join(reasons),
    }
    targets = {entry["method"]: "+".join(entry.get("bootTargets") or []) for entry in methods}
    summary = ", ".join(f"{method} ({targets.get(method) or '-'})" for method in allowed)
    return policy, f"Sugerido {recommended}; permitidos: {summary}."


def validate_usb_write_policy(label: str, item: dict) -> list[str]:
    policy = item.get("usbWritePolicy")
    if policy in (None, ""):
        return []
    prefix = f"{label}: usbWritePolicy"
    if not isinstance(policy, dict):
        return [f"{prefix} deve ser objeto JSON."]
    errors: list[str] = []
    file_name = str(item.get("fileName", "")).strip()
    if file_name and Path(file_name).suffix.lower() != ".iso":
        errors.append(f"{prefix} so se aplica a imagem ISO (fileName {file_name}).")

    allowed = policy.get("allowedMethods")
    if not isinstance(allowed, list) or not allowed:
        errors.append(f"{prefix}.allowedMethods deve ser lista nao vazia.")
        allowed = []
    unknown = [str(method) for method in allowed if method not in WRITE_METHODS]
    if unknown:
        errors.append(f"{prefix}.allowedMethods com metodo desconhecido: {', '.join(unknown)}.")
    if len(set(map(str, allowed))) != len(allowed):
        errors.append(f"{prefix}.allowedMethods com metodo repetido.")
    preferred = policy.get("preferredMethod")
    if preferred not in WRITE_METHODS:
        errors.append(f"{prefix}.preferredMethod invalido: {preferred}.")
    elif preferred not in allowed:
        errors.append(f"{prefix}.preferredMethod ({preferred}) deve estar em allowedMethods.")

    for flag in ("preserveSourceImage", "allowContentTransformations"):
        if not isinstance(policy.get(flag), bool):
            errors.append(f"{prefix}.{flag} deve ser true ou false.")
    if policy.get("preserveSourceImage") is True and policy.get("allowContentTransformations") is True:
        errors.append(f"{prefix}: preserveSourceImage e allowContentTransformations nao podem ser ambos true.")
    if policy.get("allowContentTransformations") is False:
        transforming = [method for method in allowed if method in TRANSFORMING_WRITE_METHODS]
        if transforming:
            errors.append(f"{prefix}: {', '.join(transforming)} altera a imagem e exige allowContentTransformations=true.")

    policy_sha256 = str(policy.get("imageSha256", "")).strip().lower()
    item_sha256 = str(item.get("sha256", "")).strip().lower()
    if not SHA256_RE.match(policy_sha256):
        errors.append(f"{prefix}.imageSha256 invalido.")
    elif item_sha256 and policy_sha256 != item_sha256:
        errors.append(f"{prefix} se refere a outra imagem (imageSha256 diferente do sha256 do item); refaca a analise.")
    if "note" in policy and not isinstance(policy.get("note"), str):
        errors.append(f"{prefix}.note deve ser texto.")
    targets = policy.get("preferredBootTargets")
    if targets is not None and (not isinstance(targets, list) or any(target not in ("bios", "uefi") for target in targets)):
        errors.append(f"{prefix}.preferredBootTargets deve ser lista com bios e/ou uefi.")
    errors.extend(validate_volume_label(prefix, policy.get("volumeLabel"), allowed))
    return errors


VOLUME_LABEL_RE = re.compile(r"^[A-Za-z0-9_-]+$")
FAT32_WRITE_METHODS = ("logical-fat32", "fat32-split-wim")


def validate_volume_label(prefix: str, label: object, allowed: list) -> list[str]:
    """Rotulo do pendrive na copia logica / UEFI:NTFS. RAW/DD copia a imagem byte a byte e mantem o rotulo da ISO."""
    if label is None:
        return []
    if not isinstance(label, str) or not VOLUME_LABEL_RE.match(label):
        return [f"{prefix}.volumeLabel deve ter so letras, digitos, '_' e '-' (sem espaco)."]
    errors = []
    if any(method in FAT32_WRITE_METHODS for method in allowed) and len(label) > 11:
        errors.append(f"{prefix}.volumeLabel com {len(label)} caracteres: FAT32 aceita no maximo 11.")
    elif len(label) > 32:
        errors.append(f"{prefix}.volumeLabel com {len(label)} caracteres: NTFS aceita no maximo 32.")
    if allowed and all(method == "raw" for method in allowed):
        errors.append(f"{prefix}.volumeLabel nao se aplica a RAW/DD (o pendrive fica com o rotulo da propria ISO).")
    return errors


def make_build_metadata() -> dict:
    return {
        "product": "Q-DISK",
        "version": read_version(),
        "buildDateUtc": dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
        "generatedBy": "GUI_ADM/catalog_admin.py",
    }


def validate_items(items: list[dict], allow_empty_devteam: bool = False) -> list[str]:
    errors: list[str] = []
    names: dict[str, int] = {}

    if not items and not allow_empty_devteam:
        errors.append("Catalogo sem itens.")

    for index, item in enumerate(items, start=1):
        name = item.get("name", "").strip()
        group = item.get("group", "").strip()
        file_url = item.get("fileUrl", "").strip()
        file_name = item.get("fileName", "").strip()
        md5 = item.get("md5", "").strip()
        sha256 = item.get("sha256", "").strip()
        size = str(item.get("imageSizeBytes", "")).strip()

        label = name or f"item {index}"
        if not name:
            errors.append(f"Item {index}: campo name obrigatorio.")
        elif name in names:
            errors.append(f"{label}: name duplicado com item {names[name]}.")
        else:
            names[name] = index

        if not group:
            errors.append(f"{label}: campo group obrigatorio.")
        if not file_url:
            errors.append(f"{label}: campo fileUrl obrigatorio.")
        elif not URL_RE.match(file_url):
            errors.append(f"{label}: fileUrl deve iniciar com http:// ou https://.")
        if file_name:
            if Path(file_name).name != file_name:
                errors.append(f"{label}: fileName deve conter apenas o nome do arquivo, sem pasta.")
            elif Path(file_name).suffix.lower() not in (".tibx", ".iso", ".img"):
                errors.append(f"{label}: fileName deve terminar com .tibx, .iso ou .img.")
        if md5 and not MD5_RE.match(md5):
            errors.append(f"{label}: md5 invalido.")
        if sha256 and not SHA256_RE.match(sha256):
            errors.append(f"{label}: sha256 invalido.")
        if sha256:
            try:
                if int(size) <= 0:
                    errors.append(f"{label}: imageSizeBytes obrigatorio quando sha256 estiver preenchido.")
            except ValueError:
                errors.append(f"{label}: imageSizeBytes deve ser numerico.")
        elif size:
            try:
                if int(size) <= 0:
                    errors.append(f"{label}: imageSizeBytes deve ser maior que zero.")
            except ValueError:
                errors.append(f"{label}: imageSizeBytes deve ser numerico.")
        errors.extend(validate_usb_identification(label, item))
        errors.extend(validate_usb_write_policy(label, item))
        if len(str(item.get("description") or "")) > DESCRIPTION_MAX_LENGTH:
            errors.append(f"{label}: description com mais de {DESCRIPTION_MAX_LENGTH} caracteres.")

    return errors


def write_catalog(path: Path, items: list[dict], package_format: bool) -> None:
    compacted = [compact_item(item) for item in items]
    if package_format:
        payload: object = {"build": make_build_metadata(), "items": compacted}
    else:
        payload = compacted
    path.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def sync_local_jsons_to_project_config() -> tuple[int, int]:
    if CONFIG_DIR.resolve() == PROJECT_CONFIG_DIR.resolve():
        return 0, 0
    if not CONFIG_DIR.exists():
        return 0, 0

    copied = 0
    skipped = 0
    for source in sorted(CONFIG_DIR.rglob("*.json")):
        relative = source.relative_to(CONFIG_DIR)
        target = PROJECT_CONFIG_DIR / relative
        try:
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
            copied += 1
        except OSError:
            skipped += 1
    return copied, skipped


def calculate_file_metadata(path: Path) -> tuple[str, str, int]:
    md5_hash = hashlib.md5()
    sha256_hash = hashlib.sha256()
    with path.open("rb") as file_handle:
        for chunk in iter(lambda: file_handle.read(HASH_CHUNK_SIZE), b""):
            md5_hash.update(chunk)
            sha256_hash.update(chunk)
    return md5_hash.hexdigest(), sha256_hash.hexdigest(), path.stat().st_size


def calculate_file_sha256(path: Path) -> str:
    sha256_hash = hashlib.sha256()
    with path.open("rb") as file_handle:
        for chunk in iter(lambda: file_handle.read(HASH_CHUNK_SIZE), b""):
            sha256_hash.update(chunk)
    return sha256_hash.hexdigest()


def should_skip_manifest_hash(relative_path: str) -> bool:
    normalized = relative_path.replace("\\", "/").strip("/").lower()
    return normalized.endswith("programs/produkey/produkey.exe")


def suggest_name_from_image(path: Path) -> str:
    name = NAME_CLEANUP_RE.sub("-", path.stem.strip()).strip("-_.")
    return name or path.stem.strip() or "novo-item"


def powershell_quote(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


def run_powershell(command: str) -> str:
    completed = subprocess.run(
        ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
        cwd=str(REPO_ROOT),
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    if completed.returncode != 0:
        raise RuntimeError((completed.stderr or completed.stdout or "Falha ao executar PowerShell.").strip())
    return completed.stdout.strip()


def mount_iso_for_manifest(iso_path: Path) -> Path:
    quoted = powershell_quote(str(iso_path.resolve()))
    command = (
        "$ErrorActionPreference='Stop';"
        f"$image=Mount-DiskImage -ImagePath {quoted} -PassThru;"
        "$volume=$image | Get-Volume;"
        f"if(-not $volume -or -not $volume.DriveLetter){{$volume=Get-DiskImage -ImagePath {quoted} | Get-Volume}};"
        "if(-not $volume -or -not $volume.DriveLetter){throw 'ISO montada sem letra de unidade.'};"
        "$volume.DriveLetter"
    )
    drive_letter = run_powershell(command).splitlines()[-1].strip().rstrip(":")
    if not drive_letter:
        raise RuntimeError("Nao foi possivel obter a letra da ISO montada.")
    return Path(f"{drive_letter}:\\")


def dismount_iso_for_manifest(iso_path: Path) -> None:
    quoted = powershell_quote(str(iso_path.resolve()))
    run_powershell(f"$ErrorActionPreference='Stop'; Dismount-DiskImage -ImagePath {quoted}")


def normalized_manifest_path(path: Path) -> str:
    return path.as_posix().strip("/")


def choose_sentinel_files(files: list[dict]) -> list[dict]:
    preferred_paths = ("bootmgr", "bootmgr.efi", "EFI/BOOT/BOOTX64.EFI", "boot/bcd", "sources/boot.wim")
    hashable_files = [file_item for file_item in files if str(file_item.get("sha256") or "").strip()]
    by_path = {str(file_item["path"]).lower(): file_item for file_item in hashable_files}
    sentinels: list[dict] = []
    for preferred in preferred_paths:
        match = by_path.get(preferred.lower())
        if match:
            sentinels.append({"path": match["path"], "size": match["size"], "sha256": match["sha256"]})
    if not sentinels and hashable_files:
        first = hashable_files[0]
        sentinels.append({"path": first["path"], "size": first["size"], "sha256": first["sha256"]})
    return sentinels


def build_manifest_analysis(files: list[dict]) -> dict:
    largest_file = max(files, key=lambda item: int(item["size"]), default=None)
    has_over_4gb = any(int(item["size"]) > 4 * 1024**3 for item in files)
    paths = {str(item["path"]).lower() for item in files}
    boot_artifacts = {
        "biosBootmgr": "bootmgr" in paths,
        "uefiBootx64": "efi/boot/bootx64.efi" in paths,
        "bootBcd": "boot/bcd" in paths,
        "bootWim": "sources/boot.wim" in paths,
    }
    boot_targets = []
    if boot_artifacts["biosBootmgr"]:
        boot_targets.append("bios")
    if boot_artifacts["uefiBootx64"]:
        boot_targets.append("uefi")

    reasons: list[str] = []
    alternative_methods = ["raw"]
    if has_over_4gb:
        recommended = "uefi-ntfs"
        alternative_methods.insert(0, "fat32-split-wim")
        reasons.append("Ha arquivo maior que 4 GB; FAT32 puro nao comporta a copia sem transformacao.")
    else:
        recommended = "logical-fat32"
        reasons.append("Todos os arquivos cabem em FAT32 sem divisao.")

    return {
        "largestFile": largest_file,
        "hasFilesOver4Gb": has_over_4gb,
        "bootArtifacts": boot_artifacts,
        "filesystemCompatibility": {"fat32": not has_over_4gb, "ntfs": True},
        "recommendedMethod": recommended,
        "alternativeMethods": alternative_methods,
        "bootTargets": boot_targets,
        "reasons": reasons,
        # Fallback sem El Torito, Syslinux/GRUB/Bootwiz nem ISO hibrida: nao serve para sugerir usbWritePolicy.
        "analysisEngine": "python-fallback",
    }


def build_manifest_summary(manifest: dict, manifest_path: Path, manifest_url_base: str) -> dict:
    logical = manifest.get("logical") if isinstance(manifest.get("logical"), dict) else {}
    analysis = logical.get("analysis") if isinstance(logical.get("analysis"), dict) else {}
    manifest_url = "/".join(part.strip("/") for part in (manifest_url_base, manifest_path.name) if part.strip("/"))
    return {
        "schema": 1,
        "mode": "logical",
        "bootTargets": analysis.get("bootTargets") or [],
        "fileCount": logical.get("fileCount"),
        "totalBytes": logical.get("totalBytes"),
        "treeHash": logical.get("treeHash"),
        "manifestUrl": manifest_url,
        "manifestSha256": calculate_file_sha256(manifest_path),
        "sentinelFiles": logical.get("sentinelFiles") or [],
        "technicalAnalysis": {
            "largestFile": analysis.get("largestFile"),
            "hasFilesOver4Gb": analysis.get("hasFilesOver4Gb"),
            "recommendedMethod": analysis.get("recommendedMethod"),
            "alternativeMethods": analysis.get("alternativeMethods") or [],
            "reasons": analysis.get("reasons") or [],
            "analysisEngine": analysis.get("analysisEngine"),
            "imageBootTargets": analysis.get("imageBootTargets") or [],
            "methods": analysis.get("methods") or [],
        },
    }


def generate_iso_manifest(iso_path: Path, out_dir: Path, manifest_url_base: str = "manifests") -> tuple[Path, dict]:
    """Manifesto pelo helper .NET (analise completa). Se o helper falhar, o erro sobe para quem chamou: a analise
    parcial em Python (sem sugestao de gravacao) so e usada quando o helper nao existe nesta maquina."""
    try:
        iso_manifest_tool_command()
    except FileNotFoundError as exc:
        logging.getLogger("catalog_admin").warning("Helper de manifesto ausente; usando analise parcial em Python: %s", exc)
        return generate_iso_manifest_with_windows_mount(iso_path, out_dir, manifest_url_base)
    return generate_iso_manifest_with_helper(iso_path, out_dir, manifest_url_base)


def publish_readiness(item: dict) -> list[tuple[bool, str]]:
    """O que falta para o item ficar pronto para publicar (exibido no painel do item)."""
    file_name = str(item.get("fileName", "")).strip()
    sha256 = str(item.get("sha256", "")).strip().lower()
    checks = [
        (bool(str(item.get("fileUrl", "")).strip()), "URL do arquivo"),
        (bool(file_name), "Nome do arquivo"),
        (bool(item.get("md5")) and bool(SHA256_RE.match(sha256)) and int(item.get("imageSizeBytes") or 0) > 0, "Hashes e tamanho"),
        (bool(str(item.get("description") or "").strip()), "Descricao"),
    ]
    if not file_name.lower().endswith(".iso"):
        return checks
    analysis = technical_analysis_of(item)
    checks.append((bool(item.get("usbIdentification")) and analysis.get("analysisEngine") == FULL_ANALYSIS_ENGINE,
                   "Manifesto USB (analise completa)" if item.get("usbIdentification") else "Manifesto USB"))
    policy = item.get("usbWritePolicy") if isinstance(item.get("usbWritePolicy"), dict) else None
    if policy is None:
        checks.append((False, "Gravacao USB"))
    else:
        label = policy.get("volumeLabel") or "rotulo da ISO"
        same_image = str(policy.get("imageSha256", "")).lower() == sha256
        checks.append((same_image, f"Gravacao USB: {policy.get('preferredMethod', '-')} ({label})" if same_image else "Gravacao USB de outra imagem: refazer"))
    scan = get_image_scan(item)
    if scan is None or str(scan.get("imageSha256", "")).lower() not in ("", sha256):
        checks.append((False, "Verificacao no Defender"))
    else:
        threats = len(scan.get("threats") or [])
        checks.append((threats == 0, "Defender: sem ameacas" if threats == 0 else f"Defender: {threats} ameaca(s)"))
    return checks


def iso_manifest_tool_command() -> list[str]:
    """Helper compilado (dist/ADM/Quallit.IsoManifestTool.exe, publicado pelo pipeline); senao dotnet run do projeto."""
    for candidate in (APP_DIR / "Quallit.IsoManifestTool.exe", REPO_ROOT / "dist" / "ADM" / "Quallit.IsoManifestTool.exe"):
        if candidate.is_file():
            return [str(candidate)]
    if not ISO_MANIFEST_TOOL_PROJECT.exists():
        raise FileNotFoundError(f"Helper de manifesto nao encontrado (nem compilado, nem o projeto): {ISO_MANIFEST_TOOL_PROJECT}")
    return ["dotnet", "run", "--project", str(ISO_MANIFEST_TOOL_PROJECT), "-c", "Release", "--"]


def reanalyze_iso_manifest(iso_path: Path, manifest_path: Path, manifest_url_base: str = "manifests") -> tuple[Path, dict]:
    """Refaz so a analise de um manifesto existente (mesma imagem): lista de arquivos e treeHash nao mudam."""
    return generate_iso_manifest_with_helper(iso_path, manifest_path.parent, manifest_url_base, reanalyze_manifest=manifest_path)


def generate_iso_manifest_with_helper(
    iso_path: Path, out_dir: Path, manifest_url_base: str = "manifests", reanalyze_manifest: Path | None = None
) -> tuple[Path, dict]:
    helper_command = iso_manifest_tool_command()
    if not iso_path.exists():
        raise FileNotFoundError(f"ISO nao encontrada: {iso_path}")
    out_dir.mkdir(parents=True, exist_ok=True)

    summary_path = out_dir / f"{suggest_name_from_image(iso_path)}.summary.json"
    command = [
        *helper_command,
        "--iso",
        str(iso_path),
        *(["--reanalyze", str(reanalyze_manifest)] if reanalyze_manifest is not None else ["--out", str(out_dir)]),
        "--manifest-url-base",
        manifest_url_base,
        "--summary-out",
        str(summary_path),
        "--quiet",
    ]
    try:
        helper_timeout = int(os.environ.get("QUALLIT_ISO_MANIFEST_HELPER_TIMEOUT_SECONDS", ISO_MANIFEST_HELPER_TIMEOUT_SECONDS))
    except ValueError:
        helper_timeout = ISO_MANIFEST_HELPER_TIMEOUT_SECONDS
    completed = subprocess.run(
        command,
        cwd=str(REPO_ROOT),
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        encoding="utf-8",
        errors="replace",
        check=False,
        timeout=max(30, helper_timeout),
    )
    if completed.returncode != 0:
        raise RuntimeError((completed.stderr or completed.stdout or "Falha ao executar Quallit.IsoManifestTool.").strip())
    manifest_path = reanalyze_manifest or out_dir / f"{suggest_name_from_image(iso_path)}.manifest.json"
    if not manifest_path.exists():
        raise FileNotFoundError(f"Helper nao gerou o manifesto esperado: {manifest_path}")
    if not summary_path.exists():
        raise FileNotFoundError(f"Helper nao gerou o resumo esperado: {summary_path}")
    summary = json.loads(summary_path.read_text(encoding="utf-8-sig"))
    try:
        summary_path.unlink()
    except OSError:
        pass
    if not isinstance(summary, dict):
        raise ValueError("Resumo gerado pelo helper deve ser objeto JSON.")
    return manifest_path, summary


def generate_iso_manifest_with_windows_mount(iso_path: Path, out_dir: Path, manifest_url_base: str = "manifests") -> tuple[Path, dict]:
    if not iso_path.exists():
        raise FileNotFoundError(f"ISO nao encontrada: {iso_path}")
    out_dir.mkdir(parents=True, exist_ok=True)

    md5, image_sha256, image_size = calculate_file_metadata(iso_path)
    mounted_root: Path | None = None
    try:
        mounted_root = mount_iso_for_manifest(iso_path)
        files: list[dict] = []
        total_bytes = 0
        sorted_files = sorted(
            (path for path in mounted_root.rglob("*") if path.is_file()),
            key=lambda path: normalized_manifest_path(path.relative_to(mounted_root)).lower(),
        )
        total_files = len(sorted_files)
        for file_index, file_path in enumerate(sorted_files, start=1):
            relative_path = normalized_manifest_path(file_path.relative_to(mounted_root))
            size = file_path.stat().st_size
            entry = {"path": relative_path, "size": size}
            if should_skip_manifest_hash(relative_path):
                entry["sha256"] = None
                entry["sha256Error"] = "Arquivo ignorado para hash por historico de bloqueio de antivirus."
            else:
                try:
                    entry["sha256"] = calculate_file_sha256(file_path)
                except OSError as exc:
                    entry["sha256"] = None
                    entry["sha256Error"] = str(exc)
            files.append(entry)
            total_bytes += size
            if file_index == 1 or file_index % 250 == 0 or file_index == total_files:
                print(f"Manifesto ISO: {file_index}/{total_files} arquivos processados", flush=True)
    finally:
        if mounted_root is not None:
            dismount_iso_for_manifest(iso_path)

    tree_hash_input = "\n".join(f"{item['path'].lower()}|{item['size']}|{item.get('sha256') or ''}" for item in files)
    tree_hash = hashlib.sha256(tree_hash_input.encode("utf-8")).hexdigest()
    analysis = build_manifest_analysis(files)
    unhashed_files = [
        {"path": item["path"], "size": item["size"], "reason": item.get("sha256Error", "Arquivo nao pode ser lido para hash.")}
        for item in files
        if not str(item.get("sha256") or "").strip()
    ]
    manifest_name = f"{suggest_name_from_image(iso_path)}.manifest.json"
    manifest_url = "/".join(part.strip("/") for part in (manifest_url_base, manifest_name) if part.strip("/"))
    manifest = {
        "schema": 1,
        "generatedAtUtc": dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
        "image": {"fileName": iso_path.name, "sizeBytes": image_size, "md5": md5, "sha256": image_sha256},
        "logical": {
            "fileCount": len(files),
            "totalBytes": total_bytes,
            "treeHash": tree_hash,
            "sentinelFiles": choose_sentinel_files(files),
            "unhashedFiles": unhashed_files,
            "analysis": analysis,
        },
        "files": files,
    }
    manifest_path = out_dir / manifest_name
    manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    summary = build_manifest_summary(manifest, manifest_path, manifest_url_base)
    return manifest_path, summary


def find_mpcmdrun() -> Path | None:
    """MpCmdRun.exe da plataforma mais nova do Microsoft Defender."""
    platform = Path(os.environ.get("ProgramData", r"C:\ProgramData")) / "Microsoft" / "Windows Defender" / "Platform"
    candidates = sorted(platform.glob("*/MpCmdRun.exe"), key=lambda path: path.parent.name, reverse=True) if platform.exists() else []
    fallback = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Windows Defender" / "MpCmdRun.exe"
    for candidate in [*candidates, fallback]:
        if candidate.is_file():
            return candidate
    return None


def find_oscdimg() -> Path | None:
    """oscdimg.exe do Windows ADK (Deployment Tools), usado para gerar a ISO limpa."""
    kits = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Windows Kits"
    for candidate in sorted(kits.glob("*/Assessment and Deployment Kit/Deployment Tools/amd64/Oscdimg/oscdimg.exe"), reverse=True):
        if candidate.is_file():
            return candidate
    found = shutil.which("oscdimg")
    return Path(found) if found else None


def parse_defender_scan_output(text: str, root: str) -> tuple[list[dict], list[str]]:
    """Ameacas (nome + arquivo relativo a raiz da ISO) e itens 'Unknown' (fora da contagem de ameacas)."""
    threats: list[dict] = []
    unclassified: list[str] = []
    current = ""
    prefix = root.rstrip("\\") + "\\"
    for raw_line in text.splitlines():
        line = raw_line.strip()
        if line.lower().startswith("threat"):
            current = line.split(":", 1)[1].strip() if ":" in line else ""
        elif line.lower().startswith("file") and ":" in line:
            path = line.split(":", 1)[1].strip()
            relative = path[len(prefix):] if path.lower().startswith(prefix.lower()) else path
            if current and current.lower() != "unknown":
                threats.append({"name": current, "path": relative})
            else:
                unclassified.append(relative)
    return threats, unclassified


def find_system_tool_copies(root: Path) -> list[str]:
    """Executaveis com o mesmo nome de um .exe do Windows (System32/SysWOW64): a regra ASR 'ferramentas do sistema
    copiadas ou falsificadas' costuma barrar a gravacao/abertura dessas copias. Heuristica: nem todos sao barrados."""
    windir = Path(os.environ.get("windir", r"C:\Windows"))
    system_names = {path.name.lower() for folder in (windir / "System32", windir / "SysWOW64") if folder.exists() for path in folder.glob("*.exe")}
    copies = []
    for path in root.rglob("*.exe"):
        if path.name.lower() in system_names:
            copies.append(str(path.relative_to(root)))
    return sorted(copies, key=str.lower)


def scan_iso_with_defender(iso_path: Path, image_sha256: str = "") -> dict:
    """Varre a ISO montada so leitura com o Microsoft Defender, sem remediacao (nada vai para a quarentena)."""
    mpcmdrun = find_mpcmdrun()
    if mpcmdrun is None:
        raise FileNotFoundError("MpCmdRun.exe (Microsoft Defender) nao encontrado nesta maquina.")
    root = mount_iso_for_manifest(iso_path)
    try:
        completed = subprocess.run(
            [str(mpcmdrun), "-Scan", "-ScanType", "3", "-File", str(root), "-DisableRemediation"],
            text=True, capture_output=True, encoding="utf-8", errors="replace", check=False, timeout=60 * 60,
        )
        threats, unclassified = parse_defender_scan_output(completed.stdout, str(root))
        tool_copies = find_system_tool_copies(root)
    finally:
        dismount_iso_for_manifest(iso_path)
    return {
        "engine": "Microsoft Defender (MpCmdRun -DisableRemediation)",
        "scannedAtUtc": dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
        "imageFileName": iso_path.name,
        "imageSha256": image_sha256,
        "threats": threats,
        "unclassified": unclassified,
        "systemToolCopies": tool_copies,
    }


def image_scan_summary(scan: object, item_sha256: str = "") -> str:
    if not isinstance(scan, dict):
        return "Defender: imagem nao verificada."
    threats = scan.get("threats") or []
    copies = scan.get("systemToolCopies") or []
    when = str(scan.get("scannedAtUtc", ""))[:10]
    stale = item_sha256 and str(scan.get("imageSha256", "")).lower() not in ("", item_sha256.lower())
    text = f"Defender ({when}): {len(threats)} ameaca(s)"
    if threats:
        text += " — " + ", ".join(f"{Path(t['path']).name} ({t['name']})" for t in threats[:3])
    text += f"; {len(copies)} copia(s) de ferramenta do Windows (ASR)."
    if stale:
        text += " Verificacao de outra imagem: refaca."
    return text


def get_image_scan(item: dict) -> dict | None:
    """Resultado da ultima verificacao do Defender (campo imageScan; preservado em _extra pelo GUI_ADM)."""
    scan = item.get("imageScan")
    if not isinstance(scan, dict) and isinstance(item.get("_extra"), dict):
        scan = item["_extra"].get("imageScan")
    return scan if isinstance(scan, dict) else None


def scan_publish_warnings(items: list[dict]) -> list[str]:
    """Avisos antes de publicar: imagem com ameaca detectada na ultima verificacao do Defender."""
    warnings = []
    for item in items:
        scan = get_image_scan(item)
        if scan is None or item.get("enabled") is False:
            continue
        sha = str(item.get("sha256", "")).lower()
        if scan.get("imageSha256") and sha and str(scan.get("imageSha256")).lower() != sha:
            continue
        threats = scan.get("threats") or []
        if threats:
            names = ", ".join(f"{Path(t['path']).name} ({t['name']})" for t in threats[:3])
            warnings.append(f"{item.get('name', '')}: {len(threats)} ameaca(s) — {names}")
    return warnings


def build_clean_iso(iso_path: Path, output_path: Path, exclude: list[str], log=print) -> Path:
    """Gera uma ISO sem os arquivos de 'exclude' (caminhos relativos a raiz), no mesmo layout: UDF, mesmo rotulo,
    boot BIOS (boot\\etfsboot.com) e/ou UEFI (efi\\microsoft\\boot\\efisys.bin) quando presentes. Exige oscdimg (ADK)."""
    oscdimg = find_oscdimg()
    if oscdimg is None:
        raise FileNotFoundError("oscdimg.exe nao encontrado: instale o Windows ADK (Deployment Tools).")
    work = Path(os.environ.get("TEMP", ".")) / f"quallit_clean_{dt.datetime.now():%Y%m%d%H%M%S}"
    root = mount_iso_for_manifest(iso_path)
    try:
        label = run_powershell(f"(Get-Volume -DriveLetter {str(root)[0]}).FileSystemLabel").strip() or iso_path.stem[:32]
        excludes = [str(root / relative) for relative in exclude]
        log(f"Copiando a ISO sem {len(exclude)} arquivo(s)...")
        completed = subprocess.run(
            ["robocopy", str(root), str(work), "/E", "/COPY:DAT", "/DCOPY:DAT", "/R:1", "/W:1", "/NFL", "/NDL", "/NP", "/XF", *excludes],
            text=True, capture_output=True, encoding="utf-8", errors="replace", check=False,
        )
        if completed.returncode >= 8:
            raise RuntimeError(f"robocopy falhou (codigo {completed.returncode}).")
    finally:
        dismount_iso_for_manifest(iso_path)
    try:
        bios = work / "boot" / "etfsboot.com"
        uefi = work / "efi" / "microsoft" / "boot" / "efisys.bin"
        entries = []
        if bios.is_file():
            entries.append(f"p0,e,b{bios}")
        if uefi.is_file():
            entries.append(f"pEF,e,b{uefi}")
        if not entries:
            raise RuntimeError("A ISO nao tem boot\\etfsboot.com nem efi\\microsoft\\boot\\efisys.bin: layout de boot nao suportado para gerar a ISO limpa.")
        bootdata = f"-bootdata:{len(entries)}#" + "#".join(entries)
        log(f"Gerando {output_path.name} com oscdimg...")
        completed = subprocess.run(
            [str(oscdimg), "-m", "-o", "-u2", "-udfver102", f"-l{label}", bootdata, str(work), str(output_path)],
            text=True, capture_output=True, encoding="utf-8", errors="replace", check=False,
        )
        if completed.returncode != 0 or not output_path.is_file():
            raise RuntimeError((completed.stderr or completed.stdout or "oscdimg falhou.").strip()[-500:])
    finally:
        shutil.rmtree(work, ignore_errors=True)
    return output_path


def find_local_image_for_item(item: dict, image_dir: Path) -> Path | None:
    if not image_dir.exists():
        return None
    candidates = sorted(
        [path for path in image_dir.iterdir() if path.is_file() and path.suffix.lower() in (".tibx", ".iso", ".img")],
        key=lambda path: path.name.lower(),
    )
    if not candidates:
        return None

    expected_file_name = str(item.get("fileName", "")).strip()
    if expected_file_name:
        name_matches = [path for path in candidates if path.name.lower() == expected_file_name.lower()]
        if len(name_matches) == 1:
            return name_matches[0]

    expected_size = str(item.get("imageSizeBytes", "")).strip()
    size_matches: list[Path] = []
    if expected_size.isdigit():
        expected_size_int = int(expected_size)
        size_matches = [path for path in candidates if path.stat().st_size == expected_size_int]
        if len(size_matches) == 1:
            return size_matches[0]

    expected_sha256 = str(item.get("sha256", "")).strip().lower()
    if expected_sha256 and size_matches:
        for path in size_matches:
            if calculate_file_sha256(path) == expected_sha256:
                return path

    item_name = str(item.get("name", "")).strip().lower()
    if item_name:
        name_tokens = [token for token in re.split(r"[^a-z0-9]+", item_name) if len(token) >= 3]
        name_matches = []
        for path in candidates:
            stem = path.stem.lower()
            if all(token in stem for token in name_tokens[:3]):
                name_matches.append(path)
        if len(name_matches) == 1:
            return name_matches[0]

    return None


def generate_missing_manifests_for_catalog(
    catalog_path: Path, image_dir: Path, out_dir: Path, manifest_url_base: str, refresh: bool = False
) -> tuple[int, list[str]]:
    document = load_catalog(catalog_path)
    changed = 0
    messages: list[str] = []
    # Com refresh, cada imagem e analisada uma vez por execucao (itens -sharepoint/-dropbox apontam para a mesma ISO).
    refreshed: dict[str, tuple[Path, dict]] = {}

    for item in document.items:
        if refresh and item.get("usbIdentification") and str(item.get("fileName", "")).lower().endswith(".iso"):
            image_path = find_local_image_for_item(item, image_dir)
            if image_path is None:
                messages.append(f"PENDENTE imagem local nao encontrada: {item.get('name', '')}")
                continue
            key = str(image_path).lower()
            if key not in refreshed:
                existing_manifest = out_dir / f"{suggest_name_from_image(image_path)}.manifest.json"
                if existing_manifest.exists():
                    refreshed[key] = reanalyze_iso_manifest(image_path, existing_manifest, manifest_url_base)
                else:
                    refreshed[key] = generate_iso_manifest(image_path, out_dir, manifest_url_base)
            manifest_path, summary = refreshed[key]
            item["usbIdentification"] = json.loads(json.dumps(summary))
            changed += 1
            messages.append(f"OK reanalisado {item.get('name', '')}: {image_path.name} -> {manifest_path.name}")
            continue
        if item.get("usbIdentification"):
            usb_id = item.get("usbIdentification")
            modes = usb_identification_modes(usb_id)
            updated_sha = False
            for mode in modes:
                if str(mode.get("mode", "")).strip().lower() != "logical" or mode.get("manifestSha256"):
                    continue
                manifest_url = str(mode.get("manifestUrl", "")).strip()
                manifest_name = Path(manifest_url.rstrip("/").replace("\\", "/").split("/")[-1]).name
                manifest_path = out_dir / manifest_name if manifest_name else Path()
                if manifest_name and manifest_path.exists():
                    mode["manifestSha256"] = calculate_file_sha256(manifest_path)
                    updated_sha = True
            if updated_sha:
                changed += 1
                messages.append(f"OK manifestSha256 atualizado: {item.get('name', '')}")
                continue
            messages.append(f"SKIP usbIdentification existente: {item.get('name', '')}")
            continue
        image_path = find_local_image_for_item(item, image_dir)
        if image_path is None:
            messages.append(f"PENDENTE imagem local nao encontrada: {item.get('name', '')}")
            continue
        manifest_path = out_dir / f"{suggest_name_from_image(image_path)}.manifest.json"
        if manifest_path.exists():
            manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
            summary = build_manifest_summary(manifest, manifest_path, manifest_url_base)
        else:
            manifest_path, summary = generate_iso_manifest(image_path, out_dir, manifest_url_base)
        item["usbIdentification"] = summary
        changed += 1
        messages.append(f"OK {item.get('name', '')}: {image_path.name} -> {manifest_path.name}")

    if changed > 0:
        write_catalog(catalog_path, document.items, package_format=document.source_format == "package")
    return changed, messages


def suggest_write_policies_for_catalog(catalog_path: Path, apply: bool, overwrite: bool) -> tuple[int, list[str]]:
    document = load_catalog(catalog_path)
    changed = 0
    messages: list[str] = []
    for item in document.items:
        name = item.get("name", "")
        if not str(item.get("fileName", "")).lower().endswith(".iso"):
            messages.append(f"SKIP {name}: nao e ISO.")
            continue
        current = item.get("usbWritePolicy")
        if current and not overwrite:
            messages.append(f"SKIP {name}: ja tem usbWritePolicy ({write_policy_status(item)}).")
            continue
        policy, message = suggest_write_policy(item)
        if policy is None:
            messages.append(f"PENDENTE {name}: {message}")
            continue
        messages.append(f"{'OK' if apply else 'SUGESTAO'} {name}: {message}")
        if apply:
            item["usbWritePolicy"] = policy
            changed += 1

    if changed > 0:
        errors = validate_items(document.items, allow_empty_devteam=catalog_path.name == "qdisk.quallit.devteam.json")
        if errors:
            raise ValueError("Politica sugerida deixou o catalogo invalido:\n" + "\n".join(errors))
        write_catalog(catalog_path, document.items, package_format=document.source_format == "package")
    return changed, messages


def read_endpoint_base_urls() -> tuple[str, str]:
    primary_base_url = "https://installer.quallit.com.br/qdisk"
    fallback_base_url = "https://mateweb.ddns.net/repositorio/qdisk"
    if not ENDPOINTS_FILE.exists():
        return primary_base_url, fallback_base_url

    try:
        data = json.loads(ENDPOINTS_FILE.read_text(encoding="utf-8-sig"))
    except Exception:
        return primary_base_url, fallback_base_url

    primary = data.get("primary") if isinstance(data, dict) else {}
    fallback = data.get("fallback") if isinstance(data, dict) else {}
    if isinstance(primary, dict) and str(primary.get("baseUrl", "")).strip():
        primary_base_url = str(primary["baseUrl"]).strip().rstrip("/")
    if isinstance(fallback, dict) and str(fallback.get("publicBaseUrl", "")).strip():
        fallback_base_url = str(fallback["publicBaseUrl"]).strip().rstrip("/")
    return primary_base_url, fallback_base_url


def load_endpoint_config_values() -> dict[str, str]:
    values = dict(ENDPOINT_DEFAULTS)
    if not ENDPOINTS_FILE.exists():
        return values

    try:
        data = json.loads(ENDPOINTS_FILE.read_text(encoding="utf-8-sig"))
    except Exception:
        return values

    if not isinstance(data, dict):
        return values

    for section_name, section in data.items():
        if not isinstance(section, dict):
            continue
        for key, value in section.items():
            dotted_key = f"{section_name}.{key}"
            if dotted_key in values and value is not None:
                values[dotted_key] = str(value)
    return values


def write_endpoint_config_values(values: dict[str, str]) -> None:
    port_text = values["fallback.port"].strip()
    try:
        port = int(port_text)
    except ValueError as exc:
        raise ValueError("Porta do fallback deve ser numerica.") from exc
    if port <= 0 or port > 65535:
        raise ValueError("Porta do fallback deve estar entre 1 e 65535.")

    for key in ("primary.baseUrl", "fallback.publicBaseUrl", "fallback.host", "fallback.user", "fallback.remotePath", "installerGit.repositoryUrl", "installerGit.targetSubdir"):
        if not values[key].strip():
            raise ValueError(f"Campo obrigatorio vazio: {key}")

    payload = {
        "primary": {
            "baseUrl": values["primary.baseUrl"].strip(),
        },
        "fallback": {
            "publicBaseUrl": values["fallback.publicBaseUrl"].strip(),
            "host": values["fallback.host"].strip(),
            "port": port,
            "hostKey": values["fallback.hostKey"].strip(),
            "user": values["fallback.user"].strip(),
            "remotePath": values["fallback.remotePath"].strip(),
        },
        "installerGit": {
            "repositoryUrl": values["installerGit.repositoryUrl"].strip(),
            "targetSubdir": values["installerGit.targetSubdir"].strip(),
        },
    }
    ENDPOINTS_FILE.parent.mkdir(parents=True, exist_ok=True)
    ENDPOINTS_FILE.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def load_token_config_values() -> dict[str, str]:
    values = dict(TOKEN_DEFAULTS)
    if not TOKENS_FILE.exists():
        return values

    try:
        data = json.loads(TOKENS_FILE.read_text(encoding="utf-8-sig"))
    except Exception:
        return values

    if not isinstance(data, dict):
        return values

    profiles = data.get("profiles")
    if isinstance(profiles, list):
        for profile in profiles:
            if not isinstance(profile, dict):
                continue
            profile_name = str(profile.get("profile", "")).strip()
            if profile_name not in TOKEN_PROFILES:
                continue
            for key in ("displayName", "internalId", "catalogFile"):
                value = profile.get(key)
                if value is not None:
                    values[f"{profile_name}.{key}"] = str(value).strip()

    downloader = data.get("downloader")
    if isinstance(downloader, dict):
        profile = str(downloader.get("profile", "")).strip()
        base_date = str(downloader.get("baseDate", "")).strip()
        if profile in TOKEN_PROFILES:
            values["downloader.profile"] = profile
        if base_date:
            values["downloader.baseDate"] = base_date
    return values


def write_token_config_values(values: dict[str, str]) -> None:
    profiles = []
    for profile_name in TOKEN_PROFILES:
        display_name = values[f"{profile_name}.displayName"].strip()
        internal_id = values[f"{profile_name}.internalId"].strip()
        catalog_file = values[f"{profile_name}.catalogFile"].strip()

        if not display_name:
            raise ValueError(f"Nome exibido vazio para {profile_name}.")
        if not re.match(r"^\d{6}$", internal_id):
            raise ValueError(f"ID interno de {profile_name} deve ter exatamente 6 digitos.")
        if not catalog_file or not catalog_file.lower().endswith(".json"):
            raise ValueError(f"Catalogo associado de {profile_name} deve ser um arquivo .json.")

        profiles.append(
            {
                "profile": profile_name,
                "displayName": display_name,
                "internalId": internal_id,
                "catalogFile": catalog_file,
            }
        )

    downloader_profile = values["downloader.profile"].strip()
    if downloader_profile not in TOKEN_PROFILES:
        raise ValueError("Perfil downloader invalido.")

    downloader_base_date = values["downloader.baseDate"].strip()
    try:
        dt.date.fromisoformat(downloader_base_date)
    except ValueError as exc:
        raise ValueError("Data base downloader deve usar o formato AAAA-MM-DD.") from exc

    payload = {
        "profiles": profiles,
        "downloader": {
            "profile": downloader_profile,
            "baseDate": downloader_base_date,
        },
    }
    TOKENS_FILE.parent.mkdir(parents=True, exist_ok=True)
    TOKENS_FILE.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def regenerate_token_verifiers() -> str:
    """Refaz config/qdisk.tokens.app.json (verificadores embutidos no app) depois de mudar os IDs internos."""
    script = REPO_ROOT / "tools" / "utils" / "generate_token_verifiers.py"
    python = shutil.which("python") or shutil.which("py")
    if not script.exists() or not python:
        return "Verificadores do token nao regerados aqui (sem Python ou sem o script); o proximo build do pipeline regera."
    completed = subprocess.run([python, str(script)], cwd=str(REPO_ROOT), text=True, capture_output=True, encoding="utf-8", errors="replace", check=False)
    if completed.returncode != 0:
        raise RuntimeError((completed.stderr or completed.stdout or "Falha ao gerar os verificadores do token.").strip())
    return completed.stdout.strip()


def build_upload_summary(output: str, success: bool, debug_log_path: Path) -> str:
    lines = [line.strip() for line in output.splitlines() if line.strip()]
    primary_base_url, fallback_base_url = read_endpoint_base_urls()
    catalog_names = []
    for line in lines:
        match = re.search(r"qdisk\.quallit\.[A-Za-z0-9_-]+\.json", line)
        if match:
            catalog_names.append(match.group(0))
    catalog_name = catalog_names[0] if catalog_names else "catalogo selecionado"

    fallback_ok = any(fallback_base_url in line and line.startswith("OK remoto:") for line in lines)
    installer_ok = any(primary_base_url in line and line.startswith("OK remoto:") for line in lines)
    no_git_changes = any("Nenhuma alteracao de catalogo" in line for line in lines)
    git_pushed = any("Publicacao de catalogos no quallit-installer concluida" in line for line in lines)
    verify_attempts = [line for line in lines if line.startswith("Verificando catalogos publicados em")]

    summary = [f"Catalogo: {catalog_name}", ""]
    if success:
        summary.append("Publicacao concluida.")
        summary.append("")
        summary.append(f"Fallback direto: {'OK' if fallback_ok else 'nao verificado'}")
        if no_git_changes:
            summary.append("Git installer: sem alteracao para publicar")
        elif git_pushed:
            summary.append("Git installer: commit/push OK")
        else:
            summary.append("Git installer: nao verificado")
        summary.append(f"Installer remoto: {'OK' if installer_ok else 'nao verificado'}")
        if verify_attempts:
            summary.append(f"Verificacao remota: {len(verify_attempts)} tentativa(s)")
    else:
        summary.append("Publicacao nao concluida.")
        summary.append("")
        error_lines = [line for line in lines if "falhou" in line.lower() or "divergente" in line.lower() or "erro" in line.lower()]
        if error_lines:
            summary.append("Motivo principal:")
            summary.append(error_lines[-1][:500])
        else:
            summary.append("Consulte o log detalhado para identificar o motivo.")

    summary.append("")
    summary.append(f"Detalhes completos: {debug_log_path}")
    return "\n".join(summary)


def build_process_summary(lines: list[str], exit_code: int, console_log_path: Path | None, official_log_path: Path | None) -> str:
    version = "n/d"
    step_lines: list[str] = []
    for line in lines:
        line = ANSI_ESCAPE_RE.sub("", line)
        version_match = re.search(r"Versao nova:\s*(\S+)", line)
        if version_match:
            version = version_match.group(1)
        if re.match(r"\s*\[(OK|!!|--)\]\s+\[\d+/\d+\]", line):
            step_lines.append(line.strip())

    def step_status(name: str) -> str:
        for line in step_lines:
            if name in line:
                if ": OK" in line:
                    return "OK"
                if ": FALHOU" in line:
                    return "FALHOU"
                if ": PULADO" in line:
                    return "PULADO"
        return "n/d"

    summary = [
        "Resumo do build completo",
        f"Codigo de saida: {exit_code}",
        f"Versao: {version}",
        f"Git installer: {step_status('Sincronizar Git installer')}",
        f"Fallback SSH/HTTPS: {step_status('Publicar fallback remoto')}",
        f"Git projeto: {step_status('Sincronizar Git projeto')}",
    ]
    if official_log_path is not None:
        summary.append(f"Log oficial: {official_log_path}")
    if console_log_path is not None:
        summary.append(f"Console GUI: {console_log_path}")
    return "\n".join(summary)


def setup_logging() -> tuple[logging.Logger, Path, Path]:
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    DEBUG_LOG_DIR.mkdir(parents=True, exist_ok=True)

    log_path = LOG_DIR / "catalog_admin.log"
    debug_path = DEBUG_LOG_DIR / f"catalog_admin_debug_{dt.datetime.now().strftime('%Y%m%d_%H%M%S')}.log"

    logger = logging.getLogger("catalog_admin")
    logger.setLevel(logging.DEBUG)
    logger.handlers.clear()

    formatter = logging.Formatter("%(asctime)s [%(levelname)s] %(message)s")
    app_handler = logging.FileHandler(log_path, encoding="utf-8")
    app_handler.setLevel(logging.INFO)
    app_handler.setFormatter(formatter)

    debug_handler = logging.FileHandler(debug_path, encoding="utf-8")
    debug_handler.setLevel(logging.DEBUG)
    debug_handler.setFormatter(formatter)

    logger.addHandler(app_handler)
    logger.addHandler(debug_handler)
    logger.propagate = False
    logger.info("GUI_ADM iniciado.")
    logger.debug("Log operacional: %s", log_path)
    logger.debug("Log debug: %s", debug_path)
    return logger, log_path, debug_path


UI_COLORS = {
    "app": "#eceef1",
    "surface": "#ffffff",
    "subtle": "#f7f8fa",
    "line": "#d4d7dd",
    "border": "#c4c9d1",
    "text": "#1f2328",
    "muted": "#5b6270",
    "muted2": "#8a909b",
    "accent": "#2f5d8a",
    "accent_hover": "#284f76",
    "accent_press": "#21425f",
    "danger": "#a12a2a",
    "console": "#1e2329",
}
UI_RADIUS = 6


def _mix_color(color_a: str, color_b: str, amount: float) -> str:
    a = [int(color_a[i:i + 2], 16) for i in (1, 3, 5)]
    b = [int(color_b[i:i + 2], 16) for i in (1, 3, 5)]
    return "#%02x%02x%02x" % tuple(round(a[k] + (b[k] - a[k]) * amount) for k in range(3))


def make_rounded_image(master: tk.Misc, fill: str, border: str, bg: str = "#ffffff", radius: int = UI_RADIUS, size: int = 24) -> tk.PhotoImage:
    """Gera um retangulo arredondado (com antialias) usado como borda dos elementos ttk."""
    image = tk.PhotoImage(master=master, width=size, height=size)
    rows = []
    for y in range(size):
        row = []
        for x in range(size):
            px, py = x + 0.5, y + 0.5
            cx = min(max(px, radius), size - radius)
            cy = min(max(py, radius), size - radius)
            distance = ((px - cx) ** 2 + (py - cy) ** 2) ** 0.5
            outer = max(0.0, min(1.0, radius - distance + 0.5))
            inner = max(0.0, min(1.0, radius - 1 - distance + 0.5))
            row.append(_mix_color(_mix_color(bg, border, outer), fill, inner))
        rows.append("{" + " ".join(row) + "}")
    image.put(" ".join(rows))
    return image


def apply_app_theme(app: tk.Tk) -> dict[str, tk.PhotoImage]:
    c = UI_COLORS
    style = ttk.Style(app)
    style.theme_use("clam")
    if sys.platform == "win32":
        for font_name in ("TkDefaultFont", "TkTextFont", "TkMenuFont", "TkHeadingFont"):
            try:
                tkfont.nametofont(font_name).configure(family="Segoe UI", size=9)
            except tk.TclError:
                pass
    app.configure(background=c["app"])
    app.option_add("*TCombobox*Listbox.background", c["surface"])
    app.option_add("*TCombobox*Listbox.selectBackground", "#e3ecf6")
    app.option_add("*TCombobox*Listbox.selectForeground", c["text"])

    images: dict[str, tk.PhotoImage] = {}

    def img(name: str, fill: str, border: str, bg: str = c["surface"]) -> tk.PhotoImage:
        images[name] = make_rounded_image(app, fill, border, bg)
        return images[name]

    common = {"border": UI_RADIUS + 1, "padding": 1, "sticky": "nsew"}
    style.element_create(
        "Rounded.Button.border", "image", img("btn", c["surface"], c["border"]),
        ("disabled", img("btn_dis", c["subtle"], "#dde0e5")),
        ("pressed", img("btn_press", "#e8ebf0", c["border"])),
        ("active", img("btn_hover", "#f3f5f8", "#b3b9c3")),
        **common,
    )
    style.element_create(
        "Primary.Button.border", "image", img("pri", c["accent"], c["accent"]),
        ("disabled", img("pri_dis", "#9fb3c8", "#9fb3c8")),
        ("pressed", img("pri_press", c["accent_press"], c["accent_press"])),
        ("active", img("pri_hover", c["accent_hover"], c["accent_hover"])),
        **common,
    )
    style.element_create(
        "Danger.Button.border", "image", img("dan", c["surface"], "#e3b4b4"),
        ("disabled", img("dan_dis", c["subtle"], "#dde0e5")),
        ("pressed", img("dan_press", "#f6e2e2", "#d99c9c")),
        ("active", img("dan_hover", "#fbf0f0", "#d99c9c")),
        **common,
    )
    style.element_create(
        "Console.Button.border", "image", img("con", "#2a3038", "#3a424d", c["console"]),
        ("pressed", img("con_press", "#20252c", "#3a424d", c["console"])),
        ("active", img("con_hover", "#323943", "#4a535f", c["console"])),
        **common,
    )
    style.element_create(
        "Rounded.field", "image", img("field", c["surface"], c["border"]),
        ("disabled", img("field_dis", c["subtle"], "#dde0e5")),
        ("focus", img("field_focus", c["surface"], c["accent"])),
        **common,
    )

    def button_layout(border_element: str) -> list:
        return [(border_element, {"sticky": "nsew", "children": [
            ("Button.padding", {"sticky": "nsew", "children": [("Button.label", {"sticky": "nsew"})]})
        ]})]

    style.configure(".", background=c["surface"], foreground=c["text"], bordercolor=c["line"])
    for style_name, element in (("TButton", "Rounded.Button.border"), ("Primary.TButton", "Primary.Button.border"),
                                ("Danger.TButton", "Danger.Button.border"), ("Console.TButton", "Console.Button.border")):
        style.layout(style_name, button_layout(element))
    # Mesmo padding vertical em botoes e campos: todos ficam com a mesma altura.
    style.configure("TButton", padding=(12, 6), anchor="center", foreground=c["text"])
    style.map("TButton", foreground=[("disabled", c["muted2"])])
    style.configure("Primary.TButton", foreground="#ffffff")
    style.map("Primary.TButton", foreground=[("disabled", "#eef2f6")])
    style.configure("Danger.TButton", foreground=c["danger"])
    style.configure("Console.TButton", padding=(10, 2), foreground="#d6dde6")

    style.layout("TEntry", [("Rounded.field", {"sticky": "nsew", "children": [
        ("Entry.padding", {"sticky": "nsew", "children": [("Entry.textarea", {"sticky": "nsew"})]})
    ]})])
    style.configure("TEntry", padding=(9, 6), foreground=c["text"], selectbackground="#cfdcec", selectforeground=c["text"], insertcolor=c["text"])
    style.map("TEntry", foreground=[("disabled", c["muted2"])])

    style.layout("TCombobox", [("Rounded.field", {"sticky": "nsew", "children": [
        ("Combobox.downarrow", {"side": "right", "sticky": "ns"}),
        ("Combobox.padding", {"sticky": "nsew", "children": [("Combobox.textarea", {"sticky": "nsew"})]}),
    ]})])
    flat = c["surface"]
    style.configure(
        "TCombobox", padding=(9, 6, 4, 6), arrowsize=11, arrowcolor=c["muted"], foreground=c["text"],
        background=flat, bordercolor=flat, lightcolor=flat, darkcolor=flat, fieldbackground=flat,
        selectbackground="#cfdcec", selectforeground=c["text"],
    )
    style.map(
        "TCombobox",
        background=[("active", flat), ("pressed", flat), ("disabled", c["subtle"])],
        bordercolor=[("active", flat), ("pressed", flat), ("disabled", c["subtle"])],
        lightcolor=[("active", flat), ("pressed", flat)],
        darkcolor=[("active", flat), ("pressed", flat)],
        arrowcolor=[("disabled", c["muted2"])],
        foreground=[("disabled", c["muted2"])],
        selectbackground=[("readonly", flat)],
        selectforeground=[("readonly", c["text"])],
    )

    style.configure("App.TFrame", background=c["app"])
    style.configure("Card.TFrame", background=c["surface"], bordercolor=c["line"], relief="solid", borderwidth=1)
    style.configure("Subtle.TFrame", background=c["subtle"])
    style.configure("Subtle.TLabel", background=c["subtle"], foreground=c["muted"])
    style.configure("Muted.TLabel", foreground=c["muted"])
    style.configure("Title.TLabel", font=("Segoe UI Semibold", 11))
    style.configure("Section.TLabel", font=("Segoe UI Semibold", 8), foreground=c["muted"])
    style.configure("Console.TFrame", background=c["console"])
    style.configure("ConsoleTitle.TLabel", background=c["console"], foreground="#aeb7c2", font=("Segoe UI Semibold", 9))
    style.configure("TCheckbutton", background=c["surface"])
    style.map("TCheckbutton", background=[("active", c["surface"])])
    style.configure("TSeparator", background=c["line"])
    style.configure("TPanedwindow", background=c["app"])
    style.configure("Sash", sashthickness=10, gripcount=0, background=c["app"], lightcolor=c["app"], bordercolor=c["app"])

    style.configure(
        "Treeview", rowheight=26, background=c["surface"], fieldbackground=c["surface"], foreground=c["text"],
        bordercolor=c["surface"], lightcolor=c["surface"], darkcolor=c["surface"],
    )
    style.map("Treeview", background=[("selected", "#e3ecf6")], foreground=[("selected", c["text"])])
    style.configure(
        "Treeview.Heading", background=c["subtle"], foreground="#3b414c", relief="flat", padding=(8, 5),
        font=("Segoe UI Semibold", 9), bordercolor=c["line"], lightcolor=c["subtle"], darkcolor=c["line"],
    )
    style.map("Treeview.Heading", background=[("active", "#eef0f3")])

    style.configure(
        "TScrollbar", troughcolor=c["surface"], background="#d4d7dd", bordercolor=c["surface"],
        lightcolor="#d4d7dd", darkcolor="#d4d7dd", arrowcolor=c["muted"], gripcount=0,
    )
    style.configure(
        "Console.Vertical.TScrollbar", troughcolor=c["console"], background="#3a424d", bordercolor=c["console"],
        lightcolor="#3a424d", darkcolor="#3a424d", arrowcolor="#aeb7c2", gripcount=0,
    )
    style.configure(
        "Thin.Horizontal.TProgressbar", thickness=4, background=c["accent"], troughcolor="#e6e8ec",
        bordercolor="#e6e8ec", lightcolor=c["accent"], darkcolor=c["accent"],
    )
    return images


class ScrollableFrame(ttk.Frame):
    """Frame com rolagem vertical; a barra so aparece quando o conteudo nao cabe."""

    def __init__(self, master: tk.Misc, width: int = 360, padding: tuple = (14, 12)) -> None:
        super().__init__(master)
        self.canvas = tk.Canvas(self, width=width, highlightthickness=0, borderwidth=0, background=UI_COLORS["surface"])
        self.scrollbar = ttk.Scrollbar(self, orient=VERTICAL, command=self.canvas.yview)
        self.body = ttk.Frame(self.canvas, padding=padding)
        self._window = self.canvas.create_window((0, 0), window=self.body, anchor="nw")
        self.canvas.configure(yscrollcommand=self._on_scroll)
        self.canvas.pack(side=LEFT, fill=BOTH, expand=True)
        self.body.bind("<Configure>", lambda _event: self.canvas.configure(scrollregion=self.canvas.bbox("all")))
        self.canvas.bind("<Configure>", lambda event: self.canvas.itemconfigure(self._window, width=event.width))
        self.bind_all("<MouseWheel>", self._on_wheel, add="+")

    def _on_scroll(self, first: str, last: str) -> None:
        self.scrollbar.set(first, last)
        if float(first) <= 0.0 and float(last) >= 1.0:
            if self.scrollbar.winfo_ismapped():
                self.scrollbar.pack_forget()
        elif not self.scrollbar.winfo_ismapped():
            self.scrollbar.pack(side=RIGHT, fill=Y, before=self.canvas)

    def _on_wheel(self, event: tk.Event) -> None:
        widget = self.winfo_containing(event.x_root, event.y_root)
        if widget is None or not str(widget).startswith(str(self)) or not self.scrollbar.winfo_ismapped():
            return
        self.canvas.yview_scroll(int(-event.delta / 120), "units")


class CatalogAdminApp(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.main_version = read_version()
        self.document_dirty = False
        self.form_dirty = False
        self._suppress_form_dirty = False
        self._suppress_tree_select = False
        self.title(self.window_title())
        self.geometry("1280x800")
        self.minsize(1100, 640)

        self.document = CatalogDocument()
        self.selected_index: int | None = None
        self.form_vars = {
            "name": tk.StringVar(),
            "group": tk.StringVar(),
            "fileUrl": tk.StringVar(),
            "fileName": tk.StringVar(),
            "md5": tk.StringVar(),
            "sha256": tk.StringVar(),
            "imageSizeBytes": tk.StringVar(),
        }
        self.enabled_var = tk.BooleanVar(value=True)
        self.usb_identification_var = tk.StringVar(value="Sem usbIdentification.")
        self.policy_enabled_var = tk.BooleanVar(value=False)
        self.policy_preferred_var = tk.StringVar()
        self.policy_allowed_vars = {method: tk.BooleanVar(value=False) for method in WRITE_METHODS}
        self.policy_transform_var = tk.BooleanVar(value=False)
        self.policy_note_var = tk.StringVar()
        self.policy_label_var = tk.StringVar()
        self.policy_analysis_var = tk.StringVar(value="")
        # SHA-256 da imagem a que a politica do formulario se refere (vem da politica ou da sugestao aplicada).
        self.policy_image_sha256 = ""
        self.status_var = tk.StringVar(value=f"Pronto. Versao principal: {self.main_version}")
        self.path_var = tk.StringVar()
        # A caixa "Catalogo" mostra so a identificacao (ex.: pnb); o caminho real fica neste mapa.
        self.catalog_paths: dict[str, Path] = {}
        self.package_format_var = tk.BooleanVar(value=False)
        self.upload_target_var = tk.StringVar(value="Both")
        self.hash_progress_var = tk.DoubleVar(value=0)
        self.hash_queue: queue.Queue[tuple[str, object]] = queue.Queue()
        self.hash_worker: threading.Thread | None = None
        self.manifest_queue: queue.Queue[tuple[str, object]] = queue.Queue()
        self.manifest_worker: threading.Thread | None = None
        self.upload_queue: queue.Queue[tuple[str, object]] = queue.Queue()
        self.upload_worker: threading.Thread | None = None
        self.build_queue: queue.Queue[tuple[str, object]] = queue.Queue()
        self.build_worker: threading.Thread | None = None
        self.build_process: subprocess.Popen[str] | None = None
        self.build_window: tk.Toplevel | None = None
        self.build_console_text: tk.Text | None = None
        self.manifest_button: ttk.Button | None = None
        self.build_button: ttk.Button | None = None
        self.build_cancel_button: ttk.Button | None = None
        self.build_log_var = tk.StringVar(value="Log oficial: n/d")
        self.build_console_log_path: Path | None = None
        self.build_official_log_path: Path | None = None
        self.logger, self.log_path, self.debug_log_path = setup_logging()

        self._build_ui()
        self._refresh_policy_widgets()
        self._wire_change_tracking()
        self.protocol("WM_DELETE_WINDOW", self.on_close)
        self.append_console(f"Log: {self.log_path}")
        self.append_console(f"Log debug: {self.debug_log_path}")
        self.append_console(f"Versao principal: {self.main_version}")
        self._load_default_catalog_list()

    def window_title(self) -> str:
        suffix = " *" if self.document_dirty or self.form_dirty else ""
        return f"{APP_TITLE} v{self.main_version}{suffix}"

    def _wire_change_tracking(self) -> None:
        for value in self.form_vars.values():
            value.trace_add("write", self._on_form_changed)
        self.enabled_var.trace_add("write", self._on_form_changed)
        for value in (self.policy_enabled_var, self.policy_preferred_var, self.policy_transform_var, self.policy_note_var, self.policy_label_var, *self.policy_allowed_vars.values()):
            value.trace_add("write", self._on_form_changed)
        self.package_format_var.trace_add("write", self._on_catalog_option_changed)

    def _on_form_changed(self, *_args: object) -> None:
        if self._suppress_form_dirty:
            return
        self.form_dirty = True
        self._refresh_dirty_status()

    def _on_catalog_option_changed(self, *_args: object) -> None:
        if self._suppress_form_dirty:
            return
        self.mark_document_dirty("Formato de salvamento alterado.")

    def mark_document_dirty(self, message: str | None = None) -> None:
        self.document_dirty = True
        if message:
            self.status_var.set(message)
        self._refresh_dirty_status()

    def mark_clean(self) -> None:
        self.document_dirty = False
        self.form_dirty = False
        self._refresh_dirty_status()

    def _refresh_dirty_status(self) -> None:
        self.title(self.window_title())
        self._refresh_form_header()

    def _build_ui(self) -> None:
        self._theme_images = apply_app_theme(self)
        root = ttk.Frame(self, style="App.TFrame")
        root.pack(fill=BOTH, expand=True)

        def add_button(parent: tk.Misc, text: str, command, style: str = "TButton", padx: tuple = (6, 0)) -> ttk.Button:
            button = ttk.Button(parent, text=text, command=command, style=style)
            button.pack(side=LEFT, padx=padx)
            return button

        def add_divider(parent: tk.Misc) -> None:
            ttk.Separator(parent, orient=VERTICAL).pack(side=LEFT, fill=Y, padx=(10, 4), pady=4)

        # Barra superior: catalogo, arquivo, publicacao e projeto.
        toolbar = ttk.Frame(root, padding=(12, 10))
        toolbar.pack(fill=X)
        ttk.Separator(root).pack(fill=X)
        ttk.Label(toolbar, text="Catálogo").pack(side=LEFT, padx=(0, 6))
        self.catalog_combo = ttk.Combobox(toolbar, textvariable=self.path_var, width=40, state="readonly")
        self.catalog_combo.pack(side=LEFT, fill=X, expand=True)
        self.catalog_combo.bind("<<ComboboxSelected>>", lambda _event: self.open_selected_path())

        add_button(toolbar, "Abrir", self.open_file, padx=(8, 0))
        add_button(toolbar, "Novo", self.new_catalog)
        self.save_menu = tk.Menu(self, tearoff=False)
        self.save_menu.add_command(label="Salvar", accelerator="Ctrl+S", command=self.save_file)
        self.save_menu.add_command(label="Salvar como…", accelerator="Ctrl+Shift+S", command=self.save_file_as)
        self.save_menu.add_separator()
        self.save_menu.add_checkbutton(label="Salvar no formato publicado com build/items", variable=self.package_format_var)
        save_button = add_button(toolbar, "Salvar ▾", None)
        save_button.configure(command=lambda: self._post_menu(self.save_menu, save_button))
        self.bind_all("<Control-s>", lambda _event: self.save_file())
        self.bind_all("<Control-S>", lambda _event: self.save_file_as())

        add_divider(toolbar)
        add_button(toolbar, "Validar", self.validate_current_catalog)
        ttk.Label(toolbar, text="Destino").pack(side=LEFT, padx=(10, 6))
        self.upload_target_combo = ttk.Combobox(
            toolbar,
            textvariable=self.upload_target_var,
            values=list(UPLOAD_TARGET_LABELS.keys()),
            state="readonly",
            width=13,
        )
        self.upload_target_combo.pack(side=LEFT)
        self.upload_target_combo.bind(
            "<<ComboboxSelected>>",
            lambda _event: self.status_var.set(f"Destino do upload: {UPLOAD_TARGET_LABELS.get(self.upload_target_var.get(), '')}"),
        )
        self.upload_button = add_button(toolbar, "Upload", self.upload_current_catalog)

        add_divider(toolbar)
        add_button(toolbar, "Configurações", self.open_project_settings)
        add_button(toolbar, "Build completo", self.open_build_window, style="Primary.TButton")

        # Barra de status (empacotada antes do corpo para nunca ser cortada).
        footer = ttk.Frame(root, padding=(12, 5))
        footer.pack(side=tk.BOTTOM, fill=X)
        ttk.Separator(root).pack(side=tk.BOTTOM, fill=X)
        ttk.Label(footer, textvariable=self.status_var, style="Muted.TLabel").pack(side=LEFT, fill=X, expand=True)
        ttk.Label(footer, text=f"Versão principal: {self.main_version}", style="Muted.TLabel").pack(side=RIGHT)

        body = ttk.PanedWindow(root, orient=tk.HORIZONTAL)
        body.pack(fill=BOTH, expand=True, padx=12, pady=10)

        # Coluna esquerda: lista e console com a mesma largura (divisoria ajustavel entre eles).
        left_split = ttk.PanedWindow(body, orient=VERTICAL)
        body.add(left_split, weight=1)
        self._left_split = left_split

        list_card = ttk.Frame(left_split, style="Card.TFrame", padding=1)
        left_split.add(list_card, weight=1)
        list_toolbar = ttk.Frame(list_card, padding=(10, 8))
        list_toolbar.pack(fill=X)
        ttk.Label(list_toolbar, text="Filtrar").pack(side=LEFT, padx=(0, 6))
        self.filter_var = tk.StringVar()
        ttk.Entry(list_toolbar, textvariable=self.filter_var, width=28).pack(side=LEFT)
        self.filter_var.trace_add("write", lambda *_args: self.refresh_tree())
        for text, command, style in reversed((
            ("Adicionar", self.add_item, "TButton"),
            ("Duplicar", self.duplicate_item, "TButton"),
            ("Ativar/desativar", self.toggle_selected_item, "TButton"),
            ("Remover", self.remove_item, "Danger.TButton"),
        )):
            ttk.Button(list_toolbar, text=text, command=command, style=style).pack(side=RIGHT, padx=(6, 0))
        ttk.Separator(list_card).pack(fill=X)

        tree_frame = ttk.Frame(list_card)
        tree_frame.pack(fill=BOTH, expand=True)
        tree_frame.rowconfigure(0, weight=1)
        tree_frame.columnconfigure(0, weight=1)
        columns = ("status", "usbId", "writeMethod", "name", "group", "fileName", "fileUrl", "sha256", "size")
        self.tree = ttk.Treeview(tree_frame, columns=columns, show="headings", selectmode="browse")
        for column, heading, width, anchor in (
            ("status", "Status", 70, tk.W),
            ("usbId", "USB ID", 90, tk.W),
            ("writeMethod", "Gravação", 110, tk.W),
            ("name", "Name", 210, tk.W),
            ("group", "Group", 120, tk.W),
            ("fileName", "Arquivo", 170, tk.W),
            ("fileUrl", "URL", 230, tk.W),
            ("sha256", "SHA-256", 170, tk.W),
            ("size", "Bytes", 90, tk.E),
        ):
            self.tree.heading(column, text=heading, anchor=anchor)
            self.tree.column(column, width=width, minwidth=50, anchor=anchor, stretch=column in ("name", "fileName", "fileUrl"))
        self.tree.tag_configure("inactive", foreground=UI_COLORS["muted2"])
        self.tree.grid(row=0, column=0, sticky=tk.NSEW)
        self.tree.bind("<<TreeviewSelect>>", self.on_tree_select)
        tree_scroll_y = ttk.Scrollbar(tree_frame, orient=VERTICAL, command=self.tree.yview)
        tree_scroll_y.grid(row=0, column=1, sticky=tk.NS)
        tree_scroll_x = ttk.Scrollbar(tree_frame, orient=tk.HORIZONTAL, command=self.tree.xview)
        tree_scroll_x.grid(row=1, column=0, sticky=tk.EW)
        self.tree.configure(yscrollcommand=tree_scroll_y.set, xscrollcommand=tree_scroll_x.set)

        console_card = ttk.Frame(left_split, style="Console.TFrame")
        left_split.add(console_card, weight=0)
        self._console_header = ttk.Frame(console_card, style="Console.TFrame", padding=(10, 6))
        self._console_header.pack(fill=X)
        ttk.Label(self._console_header, text="Console", style="ConsoleTitle.TLabel").pack(side=LEFT)
        self.console_toggle_button = ttk.Button(self._console_header, text="Recolher", style="Console.TButton", command=self.toggle_console)
        self.console_toggle_button.pack(side=RIGHT)
        ttk.Button(self._console_header, text="Limpar", style="Console.TButton", command=self.clear_console).pack(side=RIGHT, padx=(0, 6))
        self.console_body = ttk.Frame(console_card, style="Console.TFrame", padding=(10, 0, 4, 8))
        self.console_body.pack(fill=BOTH, expand=True)
        self.console_text = tk.Text(
            self.console_body,
            height=9,
            wrap=tk.WORD,
            state=tk.DISABLED,
            background=UI_COLORS["console"],
            foreground="#d6dde6",
            insertbackground="#d6dde6",
            selectbackground=UI_COLORS["accent"],
            relief=tk.FLAT,
            borderwidth=0,
            highlightthickness=0,
            font=("Consolas", 9),
            pady=4,
        )
        self.console_text.tag_configure("WARNING", foreground="#e8c06a")
        self.console_text.tag_configure("ERROR", foreground="#f08c8c")
        self.console_text.tag_configure("DEBUG", foreground="#8a96a3")
        self.console_text.pack(side=LEFT, fill=BOTH, expand=True)
        console_scrollbar = ttk.Scrollbar(self.console_body, orient=VERTICAL, command=self.console_text.yview, style="Console.Vertical.TScrollbar")
        console_scrollbar.pack(side=RIGHT, fill=Y)
        self.console_text.configure(yscrollcommand=console_scrollbar.set)
        self._console_collapsed = False
        self._console_last_sash: int | None = None

        # Painel direito: somente os dados do item selecionado.
        form_card = ttk.Frame(body, style="Card.TFrame", padding=1)
        body.add(form_card, weight=0)

        self.form_title_var = tk.StringVar(value="Novo item")
        self.form_subtitle_var = tk.StringVar()
        form_header = ttk.Frame(form_card, padding=(14, 12, 14, 10))
        form_header.pack(fill=X)
        form_header.columnconfigure(0, weight=1)
        ttk.Label(form_header, textvariable=self.form_title_var, style="Title.TLabel").grid(row=0, column=0, sticky=tk.W)
        ttk.Label(form_header, textvariable=self.form_subtitle_var, style="Muted.TLabel").grid(row=1, column=0, sticky=tk.W)
        ttk.Button(form_header, text="?", width=3, command=self.show_help).grid(row=0, column=1, rowspan=2, sticky=tk.E)
        ttk.Separator(form_card).pack(fill=X)

        form_footer = ttk.Frame(form_card, style="Subtle.TFrame", padding=(14, 10))
        form_footer.pack(side=tk.BOTTOM, fill=X)
        form_footer.columnconfigure(0, weight=1, uniform="footer")
        form_footer.columnconfigure(1, weight=1, uniform="footer")
        ttk.Button(form_footer, text="Limpar", command=self.clear_form).grid(row=0, column=0, sticky=tk.EW, padx=(0, 3))
        ttk.Button(form_footer, text="Atualizar item", command=self.update_item, style="Primary.TButton").grid(row=0, column=1, sticky=tk.EW, padx=(3, 0))
        ttk.Separator(form_card).pack(side=tk.BOTTOM, fill=X)

        form_scroll = ScrollableFrame(form_card, width=360)
        form_scroll.pack(fill=BOTH, expand=True)
        form = form_scroll.body
        form.columnconfigure(0, weight=1, uniform="form")
        form.columnconfigure(1, weight=1, uniform="form")

        ttk.Label(form, text="Name").grid(row=0, column=0, columnspan=2, sticky=tk.W, pady=(0, 3))
        ttk.Entry(form, textvariable=self.form_vars["name"], width=1).grid(row=1, column=0, columnspan=2, sticky=tk.EW)
        ttk.Label(form, text="Group").grid(row=2, column=0, sticky=tk.W, pady=(10, 3))
        ttk.Label(form, text="Status").grid(row=2, column=1, sticky=tk.W, pady=(10, 3), padx=(8, 0))
        ttk.Entry(form, textvariable=self.form_vars["group"], width=1).grid(row=3, column=0, sticky=tk.EW)
        self.enabled_label_var = tk.StringVar(value="Ativo")
        ttk.Combobox(form, textvariable=self.enabled_label_var, values=("Ativo", "Inativo"), state="readonly", width=1).grid(
            row=3, column=1, sticky=tk.EW, padx=(8, 0)
        )
        self.enabled_label_var.trace_add("write", self._sync_enabled_from_label)
        self.enabled_var.trace_add("write", self._sync_label_from_enabled)

        ttk.Label(form, text="File URL").grid(row=4, column=0, columnspan=2, sticky=tk.W, pady=(10, 3))
        ttk.Entry(form, textvariable=self.form_vars["fileUrl"], width=1).grid(row=5, column=0, columnspan=2, sticky=tk.EW)

        ttk.Label(form, text="Arquivo de imagem").grid(row=6, column=0, columnspan=2, sticky=tk.W, pady=(10, 3))
        file_row = ttk.Frame(form)
        file_row.grid(row=7, column=0, columnspan=2, sticky=tk.EW)
        ttk.Entry(file_row, textvariable=self.form_vars["fileName"], width=1).pack(side=LEFT, fill=X, expand=True)
        self.hash_button = ttk.Button(file_row, text="Hashes…", command=self.fill_hashes_from_file)
        self.hash_button.pack(side=LEFT, padx=(6, 0))
        self.hash_progress = ttk.Progressbar(form, variable=self.hash_progress_var, maximum=100, style="Thin.Horizontal.TProgressbar")
        self.hash_progress.grid(row=8, column=0, columnspan=2, sticky=tk.EW, pady=(6, 0))

        hash_box = ttk.Frame(form, style="Subtle.TFrame", padding=(10, 8))
        hash_box.grid(row=9, column=0, columnspan=2, sticky=tk.EW, pady=(8, 0))
        hash_box.columnconfigure(1, weight=1)
        for hash_row, (field_name, label) in enumerate((("md5", "MD5"), ("sha256", "SHA-256"), ("imageSizeBytes", "Bytes"))):
            ttk.Label(hash_box, text=label, style="Subtle.TLabel").grid(row=hash_row, column=0, sticky=tk.W, padx=(0, 8), pady=2)
            ttk.Entry(hash_box, textvariable=self.form_vars[field_name], width=1, font=("Consolas", 9)).grid(
                row=hash_row, column=1, sticky=tk.EW, pady=2
            )

        ttk.Label(form, text="Descrição (exibida no app)").grid(row=10, column=0, columnspan=2, sticky=tk.W, pady=(12, 3))
        self.description_text = tk.Text(form, height=3, wrap=tk.WORD, relief=tk.SOLID, borderwidth=1, font=("Segoe UI", 9))
        self.description_text.grid(row=11, column=0, columnspan=2, sticky=tk.EW)
        self.description_text.bind("<<Modified>>", self._on_description_modified)
        ttk.Separator(form).grid(row=12, column=0, columnspan=2, sticky=tk.EW, pady=(14, 10))
        policy_header = ttk.Frame(form)
        policy_header.grid(row=13, column=0, columnspan=2, sticky=tk.EW)
        ttk.Label(policy_header, text="GRAVAÇÃO USB", style="Section.TLabel").pack(side=LEFT)
        ttk.Button(policy_header, text="Editar…", command=self.open_policy_window).pack(side=RIGHT)
        self.policy_summary_var = tk.StringVar()
        wrap_labels = [
            ttk.Label(form, textvariable=self.policy_summary_var, justify=LEFT, wraplength=330),
            ttk.Label(form, textvariable=self.usb_identification_var, style="Muted.TLabel", justify=LEFT, wraplength=330),
        ]
        wrap_labels[0].grid(row=14, column=0, columnspan=2, sticky=tk.W, pady=(6, 2))
        wrap_labels[1].grid(row=15, column=0, columnspan=2, sticky=tk.W)
        form.bind("<Configure>", lambda event: [label.configure(wraplength=max(120, event.width - 32)) for label in wrap_labels])
        self.manifest_button = ttk.Button(form, text="Preparar ISO…", command=self.prepare_iso_from_form, style="Primary.TButton")
        self.manifest_button.grid(row=16, column=0, columnspan=2, sticky=tk.EW, pady=(10, 0))
        prepare_hint = ttk.Label(
            form,
            text="Escolha a ISO uma vez: hashes, manifesto USB, gravação sugerida e verificação no Defender, em sequência. "
                 "Revise e clique em Atualizar item.",
            style="Muted.TLabel", justify=LEFT, wraplength=330,
        )
        prepare_hint.grid(row=17, column=0, columnspan=2, sticky=tk.W, pady=(4, 0))
        wrap_labels.append(prepare_hint)
        ttk.Separator(form).grid(row=20, column=0, columnspan=2, sticky=tk.EW, pady=(12, 8))
        ttk.Label(form, text="PRONTO PARA PUBLICAR", style="Section.TLabel").grid(row=21, column=0, columnspan=2, sticky=tk.W)
        self.readiness_var = tk.StringVar(value="")
        readiness_label = ttk.Label(form, textvariable=self.readiness_var, justify=LEFT, wraplength=330)
        readiness_label.grid(row=22, column=0, columnspan=2, sticky=tk.W, pady=(4, 0))
        wrap_labels.append(readiness_label)
        scan_row = ttk.Frame(form)
        scan_row.grid(row=18, column=0, columnspan=2, sticky=tk.EW, pady=(8, 0))
        scan_row.columnconfigure(0, weight=1, uniform="scan")
        scan_row.columnconfigure(1, weight=1, uniform="scan")
        self.scan_button = ttk.Button(scan_row, text="Verificar no Defender", command=self.scan_iso_from_form)
        self.scan_button.grid(row=0, column=0, sticky=tk.EW, padx=(0, 3))
        self.clean_button = ttk.Button(scan_row, text="Gerar ISO limpa…", command=self.clean_iso_from_form)
        self.clean_button.grid(row=0, column=1, sticky=tk.EW, padx=(3, 0))
        self.scan_summary_var = tk.StringVar(value="")
        scan_label = ttk.Label(form, textvariable=self.scan_summary_var, style="Muted.TLabel", justify=LEFT, wraplength=330)
        scan_label.grid(row=19, column=0, columnspan=2, sticky=tk.W, pady=(6, 0))
        wrap_labels.append(scan_label)
        self.scan_queue: queue.Queue[tuple[str, object]] = queue.Queue()
        self.scan_worker: threading.Thread | None = None

        self._build_policy_window()
        for value in (self.policy_enabled_var, self.policy_preferred_var, self.policy_transform_var, self.policy_label_var, *self.policy_allowed_vars.values()):
            value.trace_add("write", lambda *_args: self._refresh_policy_summary())
        self.form_vars["name"].trace_add("write", lambda *_args: self._refresh_form_header())
        self.form_vars["fileName"].trace_add("write", lambda *_args: self._refresh_policy_summary())
        self._refresh_policy_summary()
        self._refresh_form_header()

    def _build_policy_window(self) -> None:
        window = tk.Toplevel(self)
        window.withdraw()
        window.title("Gravação USB")
        window.transient(self)
        window.resizable(False, False)
        window.configure(background=UI_COLORS["surface"])
        window.protocol("WM_DELETE_WINDOW", window.withdraw)
        self.policy_window = window

        frame = ttk.Frame(window, padding=14)
        frame.pack(fill=BOTH, expand=True)
        frame.columnconfigure(1, weight=1)
        ttk.Label(frame, textvariable=self.form_title_var, style="Title.TLabel").grid(row=0, column=0, columnspan=2, sticky=tk.W, pady=(0, 8))
        ttk.Checkbutton(
            frame,
            text="Definir o método de gravação deste item",
            variable=self.policy_enabled_var,
            command=self._refresh_policy_widgets,
        ).grid(row=1, column=0, columnspan=2, sticky=tk.W, pady=(0, 8))
        ttk.Label(frame, text="Preferido").grid(row=2, column=0, sticky=tk.W, padx=(0, 10))
        self.policy_preferred_combo = ttk.Combobox(
            frame, textvariable=self.policy_preferred_var, values=list(WRITE_METHODS), state="readonly", width=20
        )
        self.policy_preferred_combo.grid(row=2, column=1, sticky=tk.W)
        ttk.Label(frame, text="Permitidos").grid(row=3, column=0, sticky=tk.NW, padx=(0, 10), pady=(10, 0))
        checks_frame = ttk.Frame(frame)
        checks_frame.grid(row=3, column=1, sticky=tk.W, pady=(10, 0))
        self.policy_method_checks: list[ttk.Checkbutton] = []
        for method in WRITE_METHODS:
            check = ttk.Checkbutton(checks_frame, text=f"{method}  ({WRITE_METHOD_LABELS[method]})", variable=self.policy_allowed_vars[method])
            check.pack(anchor=tk.W, pady=1)
            self.policy_method_checks.append(check)
        self.policy_transform_check = ttk.Checkbutton(
            frame,
            text="Permitir alterar a imagem (ex.: dividir install.wim)",
            variable=self.policy_transform_var,
        )
        self.policy_transform_check.grid(row=4, column=0, columnspan=2, sticky=tk.W, pady=(10, 0))
        ttk.Label(frame, text="Motivo").grid(row=5, column=0, sticky=tk.W, padx=(0, 10), pady=(10, 0))
        self.policy_note_entry = ttk.Entry(frame, textvariable=self.policy_note_var, width=48)
        self.policy_note_entry.grid(row=5, column=1, sticky=tk.EW, pady=(10, 0))
        ttk.Label(frame, text="Rótulo do pendrive").grid(row=6, column=0, sticky=tk.W, padx=(0, 10), pady=(10, 0))
        self.policy_label_entry = ttk.Entry(frame, textvariable=self.policy_label_var, width=34)
        self.policy_label_entry.grid(row=6, column=1, sticky=tk.W, pady=(10, 0))
        ttk.Label(
            frame,
            text="Cópia lógica e UEFI:NTFS (FAT32: até 11; NTFS: até 32). Vazio: rótulo da ISO. RAW/DD mantém o da ISO.",
            style="Muted.TLabel", wraplength=440, justify=LEFT,
        ).grid(row=7, column=1, sticky=tk.W)
        ttk.Label(frame, textvariable=self.policy_analysis_var, style="Muted.TLabel", wraplength=440, justify=LEFT).grid(
            row=8, column=0, columnspan=2, sticky=tk.W, pady=(10, 0)
        )
        buttons = ttk.Frame(frame)
        buttons.grid(row=9, column=0, columnspan=2, sticky=tk.EW, pady=(14, 0))
        ttk.Button(buttons, text="Sugerir pela análise", command=self.suggest_policy_for_form).pack(side=LEFT)
        ttk.Button(buttons, text="Concluir", command=window.withdraw, style="Primary.TButton").pack(side=RIGHT)
        # As escolhas ficam no formulario do item, como os demais campos: so valem apos "Atualizar item".
        ttk.Label(frame, text="As escolhas valem depois de \"Atualizar item\".", style="Muted.TLabel").grid(
            row=10, column=0, columnspan=2, sticky=tk.W, pady=(8, 0)
        )

    def open_policy_window(self) -> None:
        window = self.policy_window
        window.deiconify()
        window.update_idletasks()
        x = self.winfo_rootx() + max(0, (self.winfo_width() - window.winfo_width()) // 2)
        y = self.winfo_rooty() + max(0, (self.winfo_height() - window.winfo_height()) // 2)
        window.geometry(f"+{x}+{y}")
        window.lift()
        window.focus_set()

    def _refresh_policy_summary(self) -> None:
        if not hasattr(self, "policy_summary_var"):
            return
        if not self.policy_enabled_var.get():
            if self.form_vars["fileName"].get().strip().lower().endswith(".tibx"):
                self.policy_summary_var.set("Não se aplica: o TIBX é copiado para o USB (NTFS).")
            else:
                self.policy_summary_var.set("Método de gravação não definido para este item.")
            return
        allowed = [method for method in WRITE_METHODS if self.policy_allowed_vars[method].get()]
        text = f"{self.policy_preferred_var.get() or '-'}  ·  permitidos: {', '.join(allowed) or 'nenhum'}"
        if self.policy_transform_var.get():
            text += "  ·  altera a imagem"
        label = self.policy_label_var.get().strip() if hasattr(self, "policy_label_var") else ""
        text += f"  ·  rótulo: {label or 'o da ISO'}"
        self.policy_summary_var.set(text)

    def _refresh_form_header(self) -> None:
        if not hasattr(self, "form_title_var"):
            return
        title = self.form_vars["name"].get().strip() or "Novo item"
        if len(title) > 42:
            title = title[:41] + "…"
        if self.selected_index is not None:
            subtitle = f"Item {self.selected_index + 1} de {len(self.document.items)}"
        else:
            subtitle = "Novo item (use Adicionar na lista)"
        if self.form_dirty:
            subtitle += "  ·  alterações não aplicadas"
        self.form_title_var.set(title)
        self.form_subtitle_var.set(subtitle)

    def _sync_enabled_from_label(self, *_args: object) -> None:
        enabled = self.enabled_label_var.get() == "Ativo"
        if self.enabled_var.get() != enabled:
            self.enabled_var.set(enabled)

    def _sync_label_from_enabled(self, *_args: object) -> None:
        label = "Ativo" if self.enabled_var.get() else "Inativo"
        if self.enabled_label_var.get() != label:
            self.enabled_label_var.set(label)

    def _post_menu(self, menu: tk.Menu, widget: tk.Widget) -> None:
        try:
            menu.tk_popup(widget.winfo_rootx(), widget.winfo_rooty() + widget.winfo_height())
        finally:
            menu.grab_release()

    def _safe_selection_set(self, iid: str) -> None:
        if self.tree.exists(iid):
            self.tree.selection_set(iid)

    def toggle_console(self) -> None:
        split = self._left_split
        if not self._console_collapsed:
            self._console_last_sash = split.sashpos(0)
            self.console_body.pack_forget()
            split.update_idletasks()
            split.sashpos(0, split.winfo_height() - self._console_header.winfo_reqheight())
            self.console_toggle_button.configure(text="Expandir")
            self._console_collapsed = True
        else:
            self.console_body.pack(fill=BOTH, expand=True)
            if self._console_last_sash is not None:
                split.sashpos(0, self._console_last_sash)
            self.console_toggle_button.configure(text="Recolher")
            self._console_collapsed = False

    def clear_console(self) -> None:
        self.console_text.configure(state=tk.NORMAL)
        self.console_text.delete("1.0", END)
        self.console_text.configure(state=tk.DISABLED)

    def show_help(self) -> None:
        messagebox.showinfo(
            APP_TITLE,
            "Fonte do repo: salve como array direto em config/.\n"
            "Publicacao: em Salvar ▾, marque o formato build/items para gerar metadados.\n"
            "ISO/TIBX (botao Hashes…) preenche name, fileName, md5, sha256 e imageSizeBytes.\n"
            "Preencha manualmente group e fileUrl.\n"
            "Itens inativos ficam no JSON, mas nao aparecem no Q-DISK.\n"
            "Upload usa tools/utils/publish_catalogs.ps1.",
        )

    def append_console(self, message: str, level: str = "INFO") -> None:
        timestamp = dt.datetime.now().strftime("%H:%M:%S")
        line = f"[{timestamp}] [{level}] {message}"
        if hasattr(self, "console_text"):
            self.console_text.configure(state=tk.NORMAL)
            self.console_text.insert(END, line + "\n", level)
            self.console_text.see(END)
            self.console_text.configure(state=tk.DISABLED)
        if level == "ERROR":
            self.logger.error(message)
        elif level == "DEBUG":
            self.logger.debug(message)
        elif level == "WARNING":
            self.logger.warning(message)
        else:
            self.logger.info(message)

    def open_project_settings(self) -> None:
        endpoint_values = load_endpoint_config_values()
        token_values = load_token_config_values()
        dialog = tk.Toplevel(self)
        dialog.title("Configurações do projeto")
        dialog.transient(self)
        dialog.grab_set()
        dialog.resizable(True, True)

        container = ttk.Frame(dialog, padding=12)
        container.pack(fill=BOTH, expand=True)

        notebook = ttk.Notebook(container)
        notebook.pack(fill=BOTH, expand=True)

        endpoint_tab = ttk.Frame(notebook, padding=10)
        token_tab = ttk.Frame(notebook, padding=10)
        endpoint_tab.columnconfigure(1, weight=1)
        token_tab.columnconfigure(1, weight=1)
        notebook.add(endpoint_tab, text="Endpoints")
        notebook.add(token_tab, text="Tokens")

        fields = (
            ("primary.baseUrl", "URL principal"),
            ("fallback.publicBaseUrl", "URL pública fallback"),
            ("fallback.host", "Host SSH fallback"),
            ("fallback.port", "Porta SSH fallback"),
            ("fallback.hostKey", "Host key SSH fallback"),
            ("fallback.user", "Usuário SSH fallback"),
            ("fallback.remotePath", "Caminho remoto fallback"),
            ("installerGit.repositoryUrl", "Repositório Git installer"),
            ("installerGit.targetSubdir", "Subpasta Git installer"),
        )
        field_vars: dict[str, tk.StringVar] = {}
        for row, (key, label) in enumerate(fields):
            ttk.Label(endpoint_tab, text=label).grid(row=row, column=0, sticky=tk.W, pady=(0, 6), padx=(0, 8))
            field_vars[key] = tk.StringVar(value=endpoint_values.get(key, ""))
            entry = ttk.Entry(endpoint_tab, textvariable=field_vars[key], width=72)
            entry.grid(row=row, column=1, sticky=tk.EW, pady=(0, 6))

        token_vars: dict[str, tk.StringVar] = {}
        token_row = 0
        for profile_name in TOKEN_PROFILES:
            ttk.Label(token_tab, text=profile_name, font=("TkDefaultFont", 9, "bold")).grid(
                row=token_row, column=0, columnspan=2, sticky=tk.W, pady=(0, 4)
            )
            token_row += 1
            for key, label in (("displayName", "Nome exibido"), ("internalId", "ID interno"), ("catalogFile", "Catálogo associado")):
                value_key = f"{profile_name}.{key}"
                ttk.Label(token_tab, text=label).grid(row=token_row, column=0, sticky=tk.W, pady=(0, 6), padx=(0, 8))
                token_vars[value_key] = tk.StringVar(value=token_values.get(value_key, ""))
                ttk.Entry(token_tab, textvariable=token_vars[value_key], width=48).grid(row=token_row, column=1, sticky=tk.EW, pady=(0, 6))
                token_row += 1

        ttk.Separator(token_tab).grid(row=token_row, column=0, columnspan=2, sticky=tk.EW, pady=(6, 10))
        token_row += 1

        token_vars["downloader.profile"] = tk.StringVar(value=token_values.get("downloader.profile", "RD"))
        token_vars["downloader.baseDate"] = tk.StringVar(value=token_values.get("downloader.baseDate", "1970-10-01"))
        ttk.Label(token_tab, text="Perfil downloader").grid(row=token_row, column=0, sticky=tk.W, pady=(0, 6), padx=(0, 8))
        ttk.Combobox(
            token_tab,
            textvariable=token_vars["downloader.profile"],
            values=TOKEN_PROFILES,
            state="readonly",
            width=16,
        ).grid(row=token_row, column=1, sticky=tk.W, pady=(0, 6))
        token_row += 1
        ttk.Label(token_tab, text="Data base downloader").grid(row=token_row, column=0, sticky=tk.W, pady=(0, 6), padx=(0, 8))
        ttk.Entry(token_tab, textvariable=token_vars["downloader.baseDate"], width=18).grid(row=token_row, column=1, sticky=tk.W, pady=(0, 6))

        button_row = ttk.Frame(container)
        button_row.pack(fill=X, pady=(10, 0))

        def save_settings() -> None:
            new_endpoint_values = {key: var.get() for key, var in field_vars.items()}
            new_token_values = {key: var.get() for key, var in token_vars.items()}
            tokens_changed = new_token_values != load_token_config_values()
            try:
                write_endpoint_config_values(new_endpoint_values)
                write_token_config_values(new_token_values)
                verifier_message = regenerate_token_verifiers() if tokens_changed else ""
            except Exception as exc:
                messagebox.showerror(APP_TITLE, f"Falha ao salvar configurações:\n{exc}", parent=dialog)
                return
            self.append_console(f"Configurações do projeto salvas: {ENDPOINTS_FILE}")
            self.append_console(f"Configurações de tokens salvas: {TOKENS_FILE}")
            if verifier_message:
                self.append_console(verifier_message)
            self.status_var.set("Configurações do projeto salvas.")
            dialog.destroy()

        def restore_defaults() -> None:
            for key, default_value in ENDPOINT_DEFAULTS.items():
                field_vars[key].set(default_value)
            for key, default_value in TOKEN_DEFAULTS.items():
                token_vars[key].set(default_value)

        ttk.Button(button_row, text="Restaurar padrões", command=restore_defaults).pack(side=LEFT)
        ttk.Button(button_row, text="Salvar", command=save_settings).pack(side=RIGHT, padx=(6, 0))
        ttk.Button(button_row, text="Cancelar", command=dialog.destroy).pack(side=RIGHT)

        dialog.update_idletasks()
        x = self.winfo_rootx() + max(0, (self.winfo_width() - dialog.winfo_width()) // 2)
        y = self.winfo_rooty() + max(0, (self.winfo_height() - dialog.winfo_height()) // 2)
        dialog.geometry(f"+{x}+{y}")
        dialog.wait_window()

    def open_build_window(self) -> None:
        if self.build_window is not None and self.build_window.winfo_exists():
            self.build_window.lift()
            return

        dialog = tk.Toplevel(self)
        dialog.title("Build completo")
        dialog.transient(self)
        dialog.geometry("980x620")
        dialog.minsize(820, 480)
        self.build_window = dialog

        container = ttk.Frame(dialog, padding=10)
        container.pack(fill=BOTH, expand=True)

        toolbar = ttk.Frame(container)
        toolbar.pack(fill=X, pady=(0, 8))
        ttk.Label(toolbar, text=f"Script: {BUILD_SCRIPT}").pack(side=LEFT, fill=X, expand=True)
        self.build_cancel_button = ttk.Button(toolbar, text="Cancelar build", command=self.cancel_build, state=tk.DISABLED)
        self.build_cancel_button.pack(side=RIGHT, padx=(6, 0))
        self.build_button = ttk.Button(toolbar, text="Executar build completo", command=self.start_full_build)
        self.build_button.pack(side=RIGHT)

        ttk.Label(container, textvariable=self.build_log_var).pack(fill=X, pady=(0, 8))

        console_frame = ttk.LabelFrame(container, text="Console do build", padding=6)
        console_frame.pack(fill=BOTH, expand=True)
        self.build_console_text = tk.Text(console_frame, wrap=tk.WORD, state=tk.DISABLED)
        self.build_console_text.pack(side=LEFT, fill=BOTH, expand=True)
        scrollbar = ttk.Scrollbar(console_frame, orient=VERTICAL, command=self.build_console_text.yview)
        scrollbar.pack(side=RIGHT, fill=Y)
        self.build_console_text.configure(yscrollcommand=scrollbar.set)

        self.append_build_console("Pronto para executar o build completo com bump e sincronizacao.")

        def on_close() -> None:
            if self.build_worker is not None and self.build_worker.is_alive():
                messagebox.showwarning(APP_TITLE, "Build em execucao. Aguarde a conclusao antes de fechar esta janela.", parent=dialog)
                return
            self.build_window = None
            self.build_console_text = None
            self.build_button = None
            self.build_cancel_button = None
            dialog.destroy()

        dialog.protocol("WM_DELETE_WINDOW", on_close)

    def append_build_console(self, message: str) -> None:
        if self.build_console_text is None:
            return
        self.build_console_text.configure(state=tk.NORMAL)
        self.build_console_text.insert(END, ANSI_ESCAPE_RE.sub("", message) + "\n")
        self.build_console_text.see(END)
        self.build_console_text.configure(state=tk.DISABLED)

    def start_full_build(self) -> None:
        if self.build_worker is not None and self.build_worker.is_alive():
            messagebox.showinfo(APP_TITLE, "Build ja esta em execucao.", parent=self.build_window)
            return
        if not BUILD_SCRIPT.exists():
            messagebox.showerror(APP_TITLE, f"Script de build nao encontrado:\n{BUILD_SCRIPT}", parent=self.build_window)
            return
        if not messagebox.askyesno(
            APP_TITLE,
            "Executar build completo com bump de versao, publicacao do Git installer, fallback e Git do projeto?",
            parent=self.build_window,
        ):
            return
        try:
            copied, skipped = sync_local_jsons_to_project_config()
        except Exception as exc:
            messagebox.showerror(APP_TITLE, f"Falha ao sincronizar JSONs para config antes do build:\n{exc}", parent=self.build_window)
            return
        if copied or skipped:
            self.append_build_console(f"JSONs sincronizados para config antes do build: {copied} copiado(s), {skipped} falha(s).")
        if skipped:
            messagebox.showerror(APP_TITLE, "Nem todos os JSONs foram sincronizados para config antes do build.", parent=self.build_window)
            return

        if self.build_button is not None:
            self.build_button.configure(state=tk.DISABLED)
        if self.build_cancel_button is not None:
            self.build_cancel_button.configure(state=tk.NORMAL)
        self.build_console_log_path = DEBUG_LOG_DIR / f"build_console_{dt.datetime.now().strftime('%Y%m%d_%H%M%S')}.log"
        self.build_official_log_path = None
        self.build_log_var.set("Log oficial: aguardando inicio do build...")
        self.status_var.set("Build completo em execucao...")
        self.append_build_console("")
        self.append_build_console(f"Iniciando build completo: {dt.datetime.now().strftime('%d/%m/%Y %H:%M:%S')}")
        self.append_build_console(f"Comando: cmd.exe /c {BUILD_SCRIPT}")
        self.append_build_console(f"Console GUI: {self.build_console_log_path}")
        self.build_worker = threading.Thread(target=self.build_worker_run, daemon=True)
        self.build_worker.start()
        self.after(200, self.poll_build_queue)

    def cancel_build(self) -> None:
        if self.build_process is None or self.build_process.poll() is not None:
            return
        if not messagebox.askyesno(APP_TITLE, "Cancelar o build em execucao?", parent=self.build_window):
            return
        self.append_build_console("Cancelamento solicitado. Encerrando processo de build...")
        try:
            subprocess.run(["taskkill", "/PID", str(self.build_process.pid), "/T", "/F"], cwd=str(REPO_ROOT), capture_output=True, text=True)
        except Exception as exc:
            self.append_build_console(f"Falha ao cancelar build: {exc}")

    def build_worker_run(self) -> None:
        command = ["cmd.exe", "/c", str(BUILD_SCRIPT)]
        output_lines: list[str] = []
        try:
            DEBUG_LOG_DIR.mkdir(parents=True, exist_ok=True)
            console_log_path = self.build_console_log_path or (DEBUG_LOG_DIR / f"build_console_{dt.datetime.now().strftime('%Y%m%d_%H%M%S')}.log")
            self.build_queue.put(("console_log", console_log_path))
            creation_flags = subprocess.CREATE_NEW_PROCESS_GROUP if hasattr(subprocess, "CREATE_NEW_PROCESS_GROUP") else 0
            process = subprocess.Popen(
                command,
                cwd=str(REPO_ROOT),
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                encoding="utf-8",
                errors="replace",
                creationflags=creation_flags,
            )
            self.build_process = process
            assert process.stdout is not None
            with console_log_path.open("w", encoding="utf-8") as log_stream:
                for line in process.stdout:
                    clean_line = line.rstrip()
                    if clean_line:
                        output_lines.append(clean_line)
                        log_stream.write(clean_line + "\n")
                        log_stream.flush()
                        self.build_queue.put(("progress", clean_line))
                        log_match = re.search(r"Log\s*:\s*(.+build_v\d+_\d{8}_\d{6}\.log)", clean_line)
                        if log_match:
                            self.build_queue.put(("official_log", Path(log_match.group(1).strip())))
                exit_code = process.wait()
            self.build_queue.put(("done", (exit_code, output_lines, console_log_path)))
        except Exception as exc:
            self.build_queue.put(("error", exc))
        finally:
            self.build_process = None

    def poll_build_queue(self) -> None:
        keep_polling = True
        while True:
            try:
                message_type, payload = self.build_queue.get_nowait()
            except queue.Empty:
                break

            if message_type == "progress":
                self.append_build_console(str(payload))
            elif message_type == "console_log":
                self.build_console_log_path = Path(payload)  # type: ignore[arg-type]
            elif message_type == "official_log":
                self.build_official_log_path = Path(payload)  # type: ignore[arg-type]
                self.build_log_var.set(f"Log oficial: {self.build_official_log_path}")
            elif message_type == "done":
                keep_polling = False
                if self.build_button is not None:
                    self.build_button.configure(state=tk.NORMAL)
                if self.build_cancel_button is not None:
                    self.build_cancel_button.configure(state=tk.DISABLED)
                exit_code, output_lines, console_log_path = payload  # type: ignore[misc]
                summary = build_process_summary(list(output_lines), int(exit_code), Path(console_log_path), self.build_official_log_path)
                self.append_build_console("")
                self.append_build_console(summary)
                if exit_code == 0:
                    self.status_var.set("Build completo concluido.")
                    self.append_build_console("Build completo concluido com sucesso.")
                    messagebox.showinfo(APP_TITLE, summary, parent=self.build_window)
                else:
                    self.status_var.set(f"Build completo falhou com codigo {exit_code}.")
                    self.append_build_console(f"Build completo falhou com codigo {exit_code}.")
                    messagebox.showerror(APP_TITLE, summary, parent=self.build_window)
            elif message_type == "error":
                keep_polling = False
                if self.build_button is not None:
                    self.build_button.configure(state=tk.NORMAL)
                if self.build_cancel_button is not None:
                    self.build_cancel_button.configure(state=tk.DISABLED)
                self.status_var.set("Build completo falhou.")
                self.append_build_console(f"Falha ao executar build: {payload}")
                messagebox.showerror(APP_TITLE, f"Falha ao executar build:\n{payload}", parent=self.build_window)

        if keep_polling and self.build_worker is not None and self.build_worker.is_alive():
            self.after(200, self.poll_build_queue)

    def _load_default_catalog_list(self) -> None:
        paths = sorted(CONFIG_DIR.glob(CATALOG_PATTERN)) if CONFIG_DIR.exists() else []
        for path in paths:
            self.register_catalog_path(path)
        if paths:
            self.open_path(paths[0])

    def register_catalog_path(self, path: Path) -> str:
        """Inclui o catalogo na caixa e devolve o rotulo exibido (identificacao, ou nome do arquivo)."""
        resolved = path.resolve()
        for label, known in self.catalog_paths.items():
            if known == resolved:
                return label
        match = re.fullmatch(r"qdisk\.quallit\.(.+)\.json", resolved.name, re.IGNORECASE)
        label = match.group(1) if match else resolved.name
        if label in self.catalog_paths:
            label = f"{label} ({resolved.parent})"
        self.catalog_paths[label] = resolved
        self.catalog_combo["values"] = list(self.catalog_paths)
        return label

    def open_selected_path(self) -> None:
        path = self.catalog_paths.get(self.path_var.get())
        if path is not None:
            self.open_path(path)

    def open_file(self) -> None:
        selected = filedialog.askopenfilename(
            title="Abrir catalogo",
            initialdir=str(CONFIG_DIR if CONFIG_DIR.exists() else REPO_ROOT),
            filetypes=(("Catalogos JSON", "*.json"), ("Todos os arquivos", "*.*")),
        )
        if selected:
            self.open_path(Path(selected))

    def open_path(self, path: Path) -> None:
        self.append_console(f"Abrindo catalogo: {path}")
        try:
            self.document = load_catalog(path)
        except Exception as exc:
            self.append_console(f"Falha ao abrir catalogo {path}: {exc}", "ERROR")
            messagebox.showerror(APP_TITLE, f"Falha ao abrir catalogo:\n{exc}")
            return
        self.selected_index = None
        self.path_var.set(self.register_catalog_path(path))
        self._suppress_form_dirty = True
        self.package_format_var.set(self.document.source_format == "package")
        self._suppress_form_dirty = False
        self.refresh_tree()
        self.clear_form()
        self.mark_clean()
        self.status_var.set(f"Carregado: {path} ({len(self.document.items)} itens).")
        self.append_console(f"Catalogo carregado: {path.name} ({len(self.document.items)} itens).")

    def new_catalog(self) -> None:
        self.document = CatalogDocument(items=[])
        self.selected_index = None
        self.path_var.set("")
        self._suppress_form_dirty = True
        self.package_format_var.set(False)
        self._suppress_form_dirty = False
        self.refresh_tree()
        self.clear_form()
        self.document_dirty = True
        self._refresh_dirty_status()
        self.status_var.set("Novo catalogo em memoria.")
        self.append_console("Novo catalogo em memoria.")

    def save_file(self) -> bool:
        if not self.apply_pending_form_changes("salvar"):
            return False
        if self.document.path is None:
            return self.save_file_as()
        return self._save_to_path(self.document.path)

    def save_file_as(self) -> bool:
        if not self.apply_pending_form_changes("salvar"):
            return False
        selected = filedialog.asksaveasfilename(
            title="Salvar catalogo",
            initialdir=str(CONFIG_DIR if CONFIG_DIR.exists() else REPO_ROOT),
            defaultextension=".json",
            filetypes=(("Catalogos JSON", "*.json"), ("Todos os arquivos", "*.*")),
        )
        if selected:
            return self._save_to_path(Path(selected))
        return False

    def _save_to_path(self, path: Path) -> bool:
        errors = validate_items(self.document.items, allow_empty_devteam=path.name == "qdisk.quallit.devteam.json")
        if errors:
            self.append_console(f"Validacao falhou antes de salvar {path.name}: {len(errors)} erro(s).", "ERROR")
            messagebox.showerror(APP_TITLE, "Corrija antes de salvar:\n\n" + "\n".join(errors[:12]))
            return False
        try:
            write_catalog(path, self.document.items, self.package_format_var.get())
        except Exception as exc:
            self.append_console(f"Falha ao salvar catalogo {path}: {exc}", "ERROR")
            messagebox.showerror(APP_TITLE, f"Falha ao salvar catalogo:\n{exc}")
            return False
        self.document.path = path
        self.path_var.set(self.register_catalog_path(path))
        self.mark_clean()
        self.status_var.set(f"Salvo: {path.name} ({len(self.document.items)} itens).")
        self.append_console(f"Catalogo salvo: {path.name} ({len(self.document.items)} itens).")
        return True

    def refresh_tree(self) -> None:
        self.tree.delete(*self.tree.get_children())
        query = self.filter_var.get().strip().lower() if hasattr(self, "filter_var") else ""
        for index, item in enumerate(self.document.items):
            if query:
                haystack = " ".join(str(item.get(key, "")) for key in ("name", "group", "fileName", "fileUrl")).lower()
                if query not in haystack:
                    continue
            enabled = item.get("enabled", True)
            self.tree.insert(
                "",
                END,
                iid=str(index),
                values=(
                    "Ativo" if enabled else "Inativo",
                    usb_identification_status(item.get("usbIdentification")),
                    write_policy_status(item),
                    item.get("name", ""),
                    item.get("group", ""),
                    item.get("fileName", ""),
                    item.get("fileUrl", ""),
                    item.get("sha256", ""),
                    item.get("imageSizeBytes", ""),
                ),
                tags=() if enabled else ("inactive",),
            )
        if self.selected_index is not None and self.tree.exists(str(self.selected_index)):
            self.select_tree_index(self.selected_index)
        self._refresh_form_header()

    def select_tree_index(self, index: int) -> None:
        if not self.tree.exists(str(index)):
            return
        self._suppress_tree_select = True
        try:
            self.tree.selection_set(str(index))
            self.tree.focus(str(index))
        finally:
            self._suppress_tree_select = False

    def on_tree_select(self, _event: object) -> None:
        if self._suppress_tree_select:
            return
        selection = self.tree.selection()
        if not selection:
            return
        new_index = int(selection[0])
        previous_index = self.selected_index

        if previous_index is not None and new_index != previous_index and self.selected_form_differs_from_item():
            if not self.apply_selected_form_changes(select_index=new_index, status_message=None):
                self.select_tree_index(previous_index)
                return

        self.selected_index = new_index
        item = self.document.items[self.selected_index]
        self.set_form(item)

    def get_form_item(self) -> dict:
        values = {key: value.get() for key, value in self.form_vars.items()}
        values["enabled"] = self.enabled_var.get()
        if self.selected_index is not None and 0 <= self.selected_index < len(self.document.items):
            current = self.document.items[self.selected_index]
            if "usbIdentification" in current:
                values["usbIdentification"] = current.get("usbIdentification")
            if isinstance(current.get("_extra"), dict):
                values["_extra"] = dict(current["_extra"])
        policy = self.get_form_policy(values.get("sha256", ""))
        if policy is not None:
            values["usbWritePolicy"] = policy
        values["description"] = self.description_text.get("1.0", "end").strip()
        return normalize_item(values)

    def _on_description_modified(self, _event: object = None) -> None:
        if self.description_text.edit_modified():
            self.description_text.edit_modified(False)
            self._on_form_changed()

    def _set_description(self, text: str) -> None:
        self.description_text.delete("1.0", "end")
        self.description_text.insert("1.0", text)
        self.description_text.edit_modified(False)

    def get_form_policy(self, item_sha256: str) -> dict | None:
        if not self.policy_enabled_var.get():
            return None
        allow_transform = bool(self.policy_transform_var.get())
        policy = {
            "imageSha256": (self.policy_image_sha256 or str(item_sha256)).strip().lower(),
            "preferredMethod": self.policy_preferred_var.get().strip(),
            "allowedMethods": [method for method in WRITE_METHODS if self.policy_allowed_vars[method].get()],
            "preserveSourceImage": not allow_transform,
            "allowContentTransformations": allow_transform,
        }
        note = self.policy_note_var.get().strip()
        if note:
            policy["note"] = note
        volume_label = self.policy_label_var.get().strip()
        if volume_label:
            policy["volumeLabel"] = volume_label
        # Alvos de boot vem da analise (sugestao); so valem enquanto o metodo preferido for o mesmo.
        loaded_method, loaded_targets = getattr(self, "policy_loaded_targets", ("", []))
        if loaded_targets and policy["preferredMethod"] == loaded_method:
            policy["preferredBootTargets"] = list(loaded_targets)
        return policy

    def set_form_policy(self, policy: object) -> None:
        valid = isinstance(policy, dict)
        policy_dict = policy if isinstance(policy, dict) else {}
        allowed = policy_dict.get("allowedMethods") if isinstance(policy_dict.get("allowedMethods"), list) else []
        self.policy_enabled_var.set(valid)
        self.policy_preferred_var.set(str(policy_dict.get("preferredMethod", "")))
        targets = policy_dict.get("preferredBootTargets") if isinstance(policy_dict.get("preferredBootTargets"), list) else []
        self.policy_loaded_targets = (str(policy_dict.get("preferredMethod", "")), targets)
        for method, value in self.policy_allowed_vars.items():
            value.set(method in allowed)
        self.policy_transform_var.set(policy_dict.get("allowContentTransformations") is True)
        self.policy_note_var.set(str(policy_dict.get("note", "")))
        self.policy_label_var.set(str(policy_dict.get("volumeLabel", "")))
        self.policy_image_sha256 = str(policy_dict.get("imageSha256", "")).strip().lower()
        self._refresh_policy_widgets()

    def _refresh_policy_widgets(self) -> None:
        state = ["!disabled"] if self.policy_enabled_var.get() else ["disabled"]
        self.policy_preferred_combo.state(state + (["readonly"] if self.policy_enabled_var.get() else []))
        for widget in (*self.policy_method_checks, self.policy_transform_check, self.policy_note_entry, self.policy_label_entry):
            widget.state(state)

    def describe_analysis(self, item: dict) -> str:
        analysis = technical_analysis_of(item)
        if not analysis:
            return "Análise: sem manifesto (gere o manifesto USB da ISO)."
        if analysis.get("analysisEngine") != FULL_ANALYSIS_ENGINE:
            return f"Análise parcial (sem helper .NET): recomendado {analysis.get('recommendedMethod') or '-'}. Gere o manifesto novamente."
        methods = [
            f"{entry.get('method')} ({'+'.join(entry.get('bootTargets') or []) or '-'}{', altera a imagem' if entry.get('transformsContent') else ''})"
            for entry in analysis.get("methods") or []
            if isinstance(entry, dict)
        ]
        targets = "+".join(analysis.get("imageBootTargets") or []) or "-"
        return f"Análise: imagem {targets}; recomendado {analysis.get('recommendedMethod') or '-'}; viáveis: {', '.join(methods) or 'nenhum'}."

    def suggest_policy_for_form(self) -> None:
        if self.selected_index is None:
            messagebox.showinfo(APP_TITLE, "Selecione um item para sugerir o método de gravação.")
            return
        item = dict(self.document.items[self.selected_index])
        item["sha256"] = self.form_vars["sha256"].get().strip().lower()
        policy, message = suggest_write_policy(item)
        if policy is None:
            messagebox.showwarning(APP_TITLE, message)
            self.append_console(f"Sugestao de gravacao indisponivel para {item.get('name', '')}: {message}", "WARNING")
            return
        self.set_form_policy(policy)
        self.status_var.set("Método sugerido pela análise. Revise e clique em Atualizar item.")
        self.append_console(f"Gravacao sugerida para {item.get('name', '')}: {message}")

    def set_form(self, item: dict) -> None:
        self._suppress_form_dirty = True
        self.enabled_var.set(bool(item.get("enabled", True)))
        for key, value in self.form_vars.items():
            value.set(str(item.get(key, "")))
        self.usb_identification_var.set(usb_identification_summary(item.get("usbIdentification")))
        self.set_form_policy(item.get("usbWritePolicy"))
        self.policy_analysis_var.set(self.describe_analysis(item))
        self._set_description(str(item.get("description") or ""))
        self.scan_summary_var.set(image_scan_summary(get_image_scan(item), str(item.get("sha256", ""))) if str(item.get("fileName", "")).lower().endswith(".iso") else "")
        self._refresh_readiness(item)
        self._suppress_form_dirty = False
        self.form_dirty = False
        self._refresh_dirty_status()

    def clear_form(self) -> None:
        self.selected_index = None
        self._suppress_form_dirty = True
        self.enabled_var.set(True)
        self.usb_identification_var.set("Sem usbIdentification.")
        for value in self.form_vars.values():
            value.set("")
        self.set_form_policy(None)
        self.policy_analysis_var.set("")
        self._set_description("")
        self.scan_summary_var.set("")
        self.readiness_var.set("")
        self._suppress_form_dirty = False
        self.form_dirty = False
        self.tree.selection_remove(self.tree.selection())
        self._refresh_dirty_status()

    def _refresh_readiness(self, item: dict) -> None:
        if hasattr(self, "readiness_var"):
            self.readiness_var.set("\n".join(f"{'✓' if ok else '✗'}  {text}" for ok, text in publish_readiness(item)))

    def prepare_iso_from_form(self) -> None:
        """Hashes + manifesto (helper) + gravacao sugerida e, em seguida, Defender, com a ISO escolhida uma vez."""
        self._prepare_chain_scan = True
        self.generate_manifest_from_iso()

    def selected_form_differs_from_item(self) -> bool:
        if not self.form_dirty or self.selected_index is None:
            return False

        if self.selected_index < 0 or self.selected_index >= len(self.document.items):
            return False

        return compact_item(self.get_form_item()) != compact_item(self.document.items[self.selected_index])

    def apply_selected_form_changes(self, select_index: int | None = None, status_message: str | None = "Alteracoes do item aplicadas ao catalogo.") -> bool:
        if self.selected_index is None:
            return True

        item = self.get_form_item()
        candidate_items = list(self.document.items)
        candidate_items[self.selected_index] = item
        errors = validate_items(candidate_items, allow_empty_devteam=True)
        if errors:
            messagebox.showerror(APP_TITLE, "Corrija o catalogo:\n\n" + "\n".join(errors[:12]))
            return False

        self.document.items[self.selected_index] = item
        target_index = self.selected_index if select_index is None else select_index
        self.refresh_tree()
        self.select_tree_index(target_index)
        self.form_dirty = False
        self.mark_document_dirty(status_message)
        self.append_console(f"Alteracoes pendentes aplicadas: {item.get('name', '')}")
        return True

    def apply_pending_form_changes(self, action_name: str) -> bool:
        if not self.selected_form_differs_from_item():
            return True
        return self.apply_selected_form_changes(status_message=f"Alteracoes do item aplicadas antes de {action_name}.")

    def on_close(self) -> None:
        if self.hash_worker is not None and self.hash_worker.is_alive():
            messagebox.showinfo(APP_TITLE, "Aguarde o calculo de hashes terminar antes de fechar.")
            return
        if self.manifest_worker is not None and self.manifest_worker.is_alive():
            messagebox.showinfo(APP_TITLE, "Aguarde a geracao do manifesto terminar antes de fechar.")
            return
        if self.upload_worker is not None and self.upload_worker.is_alive():
            messagebox.showinfo(APP_TITLE, "Aguarde o upload terminar antes de fechar.")
            return
        if self.scan_worker is not None and self.scan_worker.is_alive():
            messagebox.showinfo(APP_TITLE, "Aguarde a verificacao do Defender ou a geracao da ISO limpa terminar antes de fechar.")
            return
        if self.build_worker is not None and self.build_worker.is_alive():
            messagebox.showinfo(APP_TITLE, "Aguarde o build terminar (ou cancele-o) antes de fechar.")
            return

        if not self.apply_pending_form_changes("fechar"):
            return

        if self.document_dirty:
            response = messagebox.askyesnocancel(
                APP_TITLE,
                "Existem alteracoes no catalogo que ainda nao foram salvas.\n\n"
                "Deseja salvar antes de fechar?\n\n"
                "Sim: salva e fecha.\n"
                "Nao: descarta as alteracoes e fecha.\n"
                "Cancelar: volta para o editor.",
            )
            if response is None:
                return
            if response:
                if not self.save_file():
                    return
            else:
                self.append_console("Catalogo fechado com alteracoes descartadas.", "WARNING")

        self.destroy()

    def add_item(self) -> None:
        item = self.get_form_item()
        errors = validate_items([item])
        if errors:
            messagebox.showerror(APP_TITLE, "Corrija o item:\n\n" + "\n".join(errors))
            return
        self.document.items.append(item)
        self.refresh_tree()
        self._safe_selection_set(str(len(self.document.items) - 1))
        self.form_dirty = False
        self.mark_document_dirty("Item adicionado.")
        self.status_var.set("Item adicionado.")
        self.append_console(f"Item adicionado: {item.get('name', '')}")

    def update_item(self) -> None:
        if self.selected_index is None:
            messagebox.showinfo(APP_TITLE, "Selecione um item para atualizar.")
            return
        if not self.apply_selected_form_changes():
            return
        self.status_var.set("Item atualizado.")

    def duplicate_item(self) -> None:
        if self.selected_index is None:
            messagebox.showinfo(APP_TITLE, "Selecione um item para duplicar.")
            return
        # Copia profunda: a copia rasa dividia usbIdentification e usbWritePolicy com o original.
        item = copy.deepcopy(self.document.items[self.selected_index])
        names = {str(existing.get("name", "")) for existing in self.document.items}
        base = f"{item.get('name', 'item')}-copy"
        candidate, counter = base, 2
        while candidate in names:
            candidate, counter = f"{base}{counter}", counter + 1
        item["name"] = candidate
        self.document.items.insert(self.selected_index + 1, item)
        self.refresh_tree()
        self._safe_selection_set(str(self.selected_index + 1))
        self.mark_document_dirty("Item duplicado. Ajuste o name antes de salvar.")
        self.status_var.set("Item duplicado. Ajuste o name antes de salvar.")

    def toggle_selected_item(self) -> None:
        if self.selected_index is None:
            messagebox.showinfo(APP_TITLE, "Selecione um item para ativar ou desativar.")
            return
        item = self.document.items[self.selected_index]
        item["enabled"] = not bool(item.get("enabled", True))
        self.set_form(item)
        current = self.selected_index
        self.refresh_tree()
        self._safe_selection_set(str(current))
        state = "ativo" if item["enabled"] else "inativo"
        self.form_dirty = False
        self.mark_document_dirty(f"Item marcado como {state}.")
        self.status_var.set(f"Item marcado como {state}.")
        self.append_console(f"Item {item.get('name', '')} marcado como {state}.")

    def remove_item(self) -> None:
        if self.selected_index is None:
            messagebox.showinfo(APP_TITLE, "Selecione um item para remover.")
            return
        item = self.document.items[self.selected_index]
        if not messagebox.askyesno(APP_TITLE, f"Remover {item.get('name', 'item')}?"):
            return
        del self.document.items[self.selected_index]
        self.refresh_tree()
        self.clear_form()
        self.mark_document_dirty("Item removido.")
        self.status_var.set("Item removido.")
        self.append_console(f"Item removido: {item.get('name', 'item')}")

    def fill_hashes_from_file(self) -> None:
        if self.hash_worker is not None and self.hash_worker.is_alive():
            messagebox.showinfo(APP_TITLE, "Aguarde o calculo de hashes atual terminar.")
            return
        selected = filedialog.askopenfilename(
            title="Selecionar imagem TIBX ou ISO",
            initialdir=str(Path.home() / "Downloads"),
            filetypes=IMAGE_FILETYPES,
        )
        if not selected:
            return
        path = Path(selected)
        self.append_console(f"Arquivo selecionado para hashes: {path}")
        if path.suffix.lower() not in (".tibx", ".iso"):
            proceed = messagebox.askyesno(
                APP_TITLE,
                f"O arquivo selecionado nao e TIBX/ISO:\n{path.name}\n\nDeseja calcular os hashes mesmo assim?",
            )
            if not proceed:
                return
        self.hash_progress_var.set(0)
        self.hash_button.configure(state=tk.DISABLED)
        self.status_var.set(f"Calculando hashes de {path.name}: 0%")
        self.append_console(f"Iniciando calculo de hashes: {path.name}")
        self.hash_worker = threading.Thread(target=self.calculate_hashes_worker, args=(path,), daemon=True)
        self.hash_worker.start()
        self.after(100, lambda: self.poll_hash_queue(path))

    def calculate_hashes_worker(self, path: Path) -> None:
        try:
            md5_hash = hashlib.md5()
            sha256_hash = hashlib.sha256()
            total_size = path.stat().st_size
            processed = 0
            last_percent = -1
            self.hash_queue.put(("debug", f"Tamanho do arquivo: {total_size} bytes"))

            with path.open("rb") as file_handle:
                for chunk in iter(lambda: file_handle.read(HASH_CHUNK_SIZE), b""):
                    md5_hash.update(chunk)
                    sha256_hash.update(chunk)
                    processed += len(chunk)
                    percent = 100 if total_size == 0 else int((processed / total_size) * 100)
                    if percent != last_percent:
                        last_percent = percent
                        self.hash_queue.put(("progress", percent))

            self.hash_queue.put(("done", (md5_hash.hexdigest(), sha256_hash.hexdigest(), total_size)))
        except Exception as exc:
            self.hash_queue.put(("error", exc))

    def poll_hash_queue(self, path: Path) -> None:
        keep_polling = True
        while True:
            try:
                message_type, payload = self.hash_queue.get_nowait()
            except queue.Empty:
                break

            if message_type == "progress":
                percent = float(payload)
                self.hash_progress_var.set(percent)
                self.status_var.set(f"Calculando hashes de {path.name}: {percent:.0f}%")
            elif message_type == "debug":
                self.append_console(str(payload), "DEBUG")
            elif message_type == "done":
                md5, sha256, size = payload  # type: ignore[misc]
                self.apply_file_metadata(path, str(md5), str(sha256), int(size))
                keep_polling = False
            elif message_type == "error":
                self.hash_button.configure(state=tk.NORMAL)
                self.append_console(f"Falha ao calcular hashes de {path.name}: {payload}", "ERROR")
                messagebox.showerror(APP_TITLE, f"Falha ao calcular hashes:\n{payload}")
                self.status_var.set("Falha ao calcular hashes.")
                keep_polling = False

        if keep_polling and self.hash_worker is not None and self.hash_worker.is_alive():
            self.after(100, lambda: self.poll_hash_queue(path))

    def apply_file_metadata(self, path: Path, md5: str, sha256: str, size: int) -> None:
        self.hash_progress_var.set(100)
        self.hash_button.configure(state=tk.NORMAL)
        self.form_vars["md5"].set(md5)
        self.form_vars["sha256"].set(sha256)
        self.form_vars["imageSizeBytes"].set(str(size))
        self.form_vars["fileName"].set(path.name)
        if not self.form_vars["name"].get().strip():
            self.form_vars["name"].set(suggest_name_from_image(path))
        missing = [label for key, label in (("group", "group"), ("fileUrl", "fileUrl")) if not self.form_vars[key].get().strip()]
        if missing:
            self.status_var.set(f"Hashes calculados para {path.name}. Preencha manualmente: {', '.join(missing)}.")
        else:
            self.status_var.set(f"Hashes calculados para {path.name}.")
        self.append_console(f"Hashes calculados: {path.name} md5={md5} sha256={sha256} bytes={size}")

    def _ask_iso_for_selected(self, title: str) -> Path | None:
        if self.selected_index is None:
            messagebox.showinfo(APP_TITLE, "Selecione um item do catalogo.")
            return None
        selected = filedialog.askopenfilename(
            title=title,
            initialdir=str(Path.home() / "Downloads" / "qdisk"),
            filetypes=(("Imagens ISO", "*.iso"), ("Todos os arquivos", "*.*")),
        )
        return Path(selected) if selected else None

    def scan_iso_from_form(self) -> None:
        """Varre a ISO do item com o Defender (sem remediacao) e guarda o resultado no item (imageScan)."""
        if self.scan_worker is not None and self.scan_worker.is_alive():
            messagebox.showinfo(APP_TITLE, "Aguarde a operacao atual terminar.")
            return
        if not self.apply_pending_form_changes("verificar no Defender"):
            return
        iso_path = self._ask_iso_for_selected("Selecionar a ISO deste item para verificar no Defender")
        if iso_path is None:
            return
        self.start_defender_scan(iso_path)

    def start_defender_scan(self, iso_path: Path) -> None:
        target_index = self.selected_index
        self.scan_button.configure(state=tk.DISABLED)
        self.clean_button.configure(state=tk.DISABLED)
        self.status_var.set(f"Verificando {iso_path.name} no Microsoft Defender (pode levar varios minutos)...")
        self.append_console(f"Defender: verificando {iso_path.name} (sem remediacao)")

        def worker() -> None:
            try:
                scan = scan_iso_with_defender(iso_path, calculate_file_sha256(iso_path))
                self.scan_queue.put(("scan", (target_index, scan)))
            except Exception as exc:
                self.scan_queue.put(("error", exc))

        self.scan_worker = threading.Thread(target=worker, daemon=True)
        self.scan_worker.start()
        self.after(500, self.poll_scan_queue)

    def clean_iso_from_form(self) -> None:
        """Gera a ISO sem os arquivos escolhidos (ameacas ja marcadas), a partir da ultima verificacao do item."""
        if self.scan_worker is not None and self.scan_worker.is_alive():
            messagebox.showinfo(APP_TITLE, "Aguarde a operacao atual terminar.")
            return
        if self.selected_index is None:
            messagebox.showinfo(APP_TITLE, "Selecione um item do catalogo.")
            return
        scan = get_image_scan(self.document.items[self.selected_index])
        if scan is None:
            messagebox.showinfo(APP_TITLE, "Use 'Verificar no Defender' antes: a lista de arquivos vem da verificacao.")
            return
        if find_oscdimg() is None:
            messagebox.showerror(APP_TITLE, "oscdimg.exe nao encontrado: instale o Windows ADK (Deployment Tools).")
            return
        chosen = self._choose_files_to_remove(scan)
        if not chosen:
            return
        iso_path = self._ask_iso_for_selected(f"Selecionar a ISO original ({scan.get('imageFileName', '')})")
        if iso_path is None:
            return
        output = filedialog.asksaveasfilename(
            title="Salvar ISO limpa", initialdir=str(iso_path.parent), initialfile=f"{iso_path.stem}_clean.iso",
            defaultextension=".iso", filetypes=(("Imagens ISO", "*.iso"),),
        )
        if not output:
            return
        self.scan_button.configure(state=tk.DISABLED)
        self.clean_button.configure(state=tk.DISABLED)
        self.status_var.set(f"Gerando {Path(output).name}...")
        self.append_console(f"ISO limpa: {iso_path.name} sem {len(chosen)} arquivo(s) -> {output}")

        def worker() -> None:
            try:
                result = build_clean_iso(iso_path, Path(output), chosen, log=lambda message: self.scan_queue.put(("log", message)))
                self.scan_queue.put(("clean", (result, calculate_file_sha256(result))))
            except Exception as exc:
                self.scan_queue.put(("error", exc))

        self.scan_worker = threading.Thread(target=worker, daemon=True)
        self.scan_worker.start()
        self.after(500, self.poll_scan_queue)

    def _choose_files_to_remove(self, scan: dict) -> list[str]:
        """Janela com os arquivos da verificacao: ameacas marcadas; copias de ferramentas do Windows desmarcadas."""
        dialog = tk.Toplevel(self)
        dialog.title("Arquivos a remover da ISO")
        dialog.transient(self)
        dialog.grab_set()
        frame = ttk.Frame(dialog, padding=12)
        frame.pack(fill=BOTH, expand=True)
        ttk.Label(frame, text="Ameacas vem marcadas; copias de ferramentas do Windows (regra ASR) ficam a seu criterio.", style="Muted.TLabel").pack(anchor=tk.W, pady=(0, 8))
        canvas = tk.Canvas(frame, height=320, width=620, highlightthickness=0)
        inner = ttk.Frame(canvas)
        scrollbar = ttk.Scrollbar(frame, orient=VERTICAL, command=canvas.yview)
        canvas.configure(yscrollcommand=scrollbar.set)
        canvas.create_window((0, 0), window=inner, anchor="nw")
        inner.bind("<Configure>", lambda _event: canvas.configure(scrollregion=canvas.bbox("all")))
        canvas.pack(side=LEFT, fill=BOTH, expand=True)
        scrollbar.pack(side=RIGHT, fill=Y)
        choices: list[tuple[str, tk.BooleanVar]] = []
        for threat in scan.get("threats") or []:
            var = tk.BooleanVar(value=True)
            ttk.Checkbutton(inner, text=f"{threat['path']}  —  {threat['name']}", variable=var).pack(anchor=tk.W)
            choices.append((threat["path"], var))
        for path in scan.get("systemToolCopies") or []:
            var = tk.BooleanVar(value=False)
            ttk.Checkbutton(inner, text=f"{path}  —  copia de ferramenta do Windows", variable=var).pack(anchor=tk.W)
            choices.append((path, var))
        result: list[str] = []

        def confirm() -> None:
            result.extend(path for path, var in choices if var.get())
            dialog.destroy()

        buttons = ttk.Frame(dialog, padding=(12, 0, 12, 12))
        buttons.pack(fill=X)
        ttk.Button(buttons, text="Gerar ISO sem os marcados", command=confirm, style="Primary.TButton").pack(side=RIGHT)
        ttk.Button(buttons, text="Cancelar", command=dialog.destroy).pack(side=RIGHT, padx=(0, 6))
        if not choices:
            messagebox.showinfo(APP_TITLE, "A ultima verificacao nao encontrou arquivos para remover.", parent=dialog)
            dialog.destroy()
            return []
        self.wait_window(dialog)
        return result

    def poll_scan_queue(self) -> None:
        keep_polling = True
        while True:
            try:
                kind, payload = self.scan_queue.get_nowait()
            except queue.Empty:
                break
            if kind == "log":
                self.append_console(str(payload))
                continue
            keep_polling = False
            self.scan_button.configure(state=tk.NORMAL)
            self.clean_button.configure(state=tk.NORMAL)
            if kind == "scan":
                target_index, scan = payload  # type: ignore[misc]
                if 0 <= target_index < len(self.document.items):
                    item = dict(self.document.items[target_index])
                    extra = dict(item.get("_extra") or {})
                    extra["imageScan"] = scan
                    item["_extra"] = extra
                    self.document.items[target_index] = item
                    self.mark_document_dirty("Verificacao do Defender guardada no item.")
                    if self.selected_index == target_index:
                        self.scan_summary_var.set(image_scan_summary(scan, str(item.get("sha256", ""))))
                        self._refresh_readiness(item)
                summary = image_scan_summary(scan)
                self.append_console(summary, "WARNING" if scan.get("threats") else "INFO")
                self.status_var.set(summary)
                messagebox.showinfo(APP_TITLE, summary)
            elif kind == "clean":
                output, sha = payload  # type: ignore[misc]
                message = (f"ISO limpa gerada:\n{output}\nSHA-256: {sha}\n\n"
                           "Proximos passos: publicar a ISO (SharePoint, Dropbox ou mateweb), apontar o item para ela "
                           "(File URL) e usar 'Preparar ISO…' com a ISO limpa.")
                self.append_console(f"ISO limpa gerada: {output} (SHA-256 {sha})")
                self.status_var.set(f"ISO limpa gerada: {Path(output).name}")
                messagebox.showinfo(APP_TITLE, message)
            else:
                self.append_console(f"Falha: {payload}", "ERROR")
                self.status_var.set("Falha na verificacao/geracao da ISO.")
                messagebox.showerror(APP_TITLE, f"Falha:\n{payload}")
        if keep_polling and self.scan_worker is not None and self.scan_worker.is_alive():
            self.after(500, self.poll_scan_queue)

    def generate_manifest_from_iso(self) -> None:
        if self.manifest_worker is not None and self.manifest_worker.is_alive():
            messagebox.showinfo(APP_TITLE, "Aguarde a geracao do manifesto atual terminar.")
            return
        if self.selected_index is None:
            messagebox.showinfo(APP_TITLE, "Selecione um item para receber o manifesto.")
            return
        if not self.apply_pending_form_changes("gerar manifesto"):
            return

        selected = filedialog.askopenfilename(
            title="Selecionar ISO para manifesto USB",
            initialdir=str(Path.home() / "Downloads" / "qdisk"),
            filetypes=(("Imagens ISO", "*.iso"), ("Todos os arquivos", "*.*")),
        )
        if not selected:
            return
        iso_path = Path(selected)
        if iso_path.suffix.lower() != ".iso":
            messagebox.showerror(APP_TITLE, "Selecione uma imagem ISO para gerar manifesto USB logico.")
            return

        target_index = self.selected_index
        manifest_url_base = read_endpoint_base_urls()[0].rstrip("/") + "/manifests"
        out_dir = CONFIG_DIR / "manifests"
        self.manifest_button.configure(state=tk.DISABLED)
        self.status_var.set(f"Gerando manifesto de {iso_path.name}...")
        self.append_console(f"Iniciando geracao de manifesto: {iso_path.name}")
        self.manifest_worker = threading.Thread(
            target=self.generate_manifest_worker,
            args=(iso_path, out_dir, manifest_url_base, target_index),
            daemon=True,
        )
        self.manifest_worker.start()
        self.after(250, self.poll_manifest_queue)

    def generate_manifest_worker(self, iso_path: Path, out_dir: Path, manifest_url_base: str, target_index: int) -> None:
        try:
            manifest_path, summary = generate_iso_manifest(iso_path, out_dir, manifest_url_base)
            manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
            image = manifest.get("image") if isinstance(manifest.get("image"), dict) else {}
            self.manifest_queue.put(("done", (target_index, iso_path, manifest_path, summary, image)))
        except Exception as exc:
            self.manifest_queue.put(("error", (iso_path, exc)))

    def poll_manifest_queue(self) -> None:
        keep_polling = True
        while True:
            try:
                message_type, payload = self.manifest_queue.get_nowait()
            except queue.Empty:
                break

            if message_type == "done":
                target_index, iso_path, manifest_path, summary, image = payload  # type: ignore[misc]
                self.apply_generated_manifest(int(target_index), iso_path, manifest_path, summary, image)
                keep_polling = False
            elif message_type == "error":
                iso_path, exc = payload  # type: ignore[misc]
                if self.manifest_button is not None:
                    self.manifest_button.configure(state=tk.NORMAL)
                self._prepare_chain_scan = False
                self.append_console(f"Falha ao gerar manifesto de {iso_path.name}: {exc}", "ERROR")
                messagebox.showerror(APP_TITLE, f"Falha ao gerar manifesto:\n{exc}")
                self.status_var.set("Falha ao gerar manifesto.")
                keep_polling = False

        if keep_polling and self.manifest_worker is not None and self.manifest_worker.is_alive():
            self.after(250, self.poll_manifest_queue)

    def apply_generated_manifest(self, target_index: int, iso_path: Path, manifest_path: Path, summary: object, image: object) -> None:
        if self.manifest_button is not None:
            self.manifest_button.configure(state=tk.NORMAL)
        if target_index < 0 or target_index >= len(self.document.items):
            messagebox.showwarning(APP_TITLE, "O item selecionado para receber o manifesto nao existe mais.")
            self.status_var.set("Manifesto gerado, mas item nao encontrado.")
            return
        if not isinstance(summary, dict):
            messagebox.showerror(APP_TITLE, "Resumo do manifesto invalido.")
            self.status_var.set("Resumo do manifesto invalido.")
            return

        item = dict(self.document.items[target_index])
        item["usbIdentification"] = summary
        if not str(item.get("fileName", "")).strip():
            item["fileName"] = iso_path.name
        elif str(item.get("fileName", "")).strip().lower() != iso_path.name.lower():
            self.append_console(f"Atencao: o item usa o arquivo {item.get('fileName')} e a ISO escolhida e {iso_path.name}.", "WARNING")
        if isinstance(image, dict):
            if image.get("md5"):
                item["md5"] = image.get("md5")
            if image.get("sha256"):
                item["sha256"] = image.get("sha256")
            if image.get("sizeBytes") is not None:
                item["imageSizeBytes"] = image.get("sizeBytes")
        policy_message = ""
        if not item.get("usbWritePolicy"):
            suggested, policy_message = suggest_write_policy(item)
            if suggested is not None:
                item["usbWritePolicy"] = suggested
                policy_message = f"Gravacao sugerida (revise antes de salvar): {policy_message}"
            else:
                policy_message = f"Gravacao nao sugerida: {policy_message}"
        elif str(item["usbWritePolicy"].get("imageSha256", "")).lower() != str(item.get("sha256", "")).lower():
            suggested, policy_message = suggest_write_policy(item)
            if suggested is not None:
                # Imagem nova: refaz a sugestao, mantendo o rotulo do pendrive ja escolhido.
                if item["usbWritePolicy"].get("volumeLabel"):
                    suggested["volumeLabel"] = item["usbWritePolicy"]["volumeLabel"]
                item["usbWritePolicy"] = suggested
                policy_message = f"Gravacao refeita para a imagem nova (revise antes de salvar): {policy_message}"
            else:
                policy_message = f"A politica de gravacao atual e de outra imagem e nao ha sugestao: {policy_message}"
        item = normalize_item(item)

        candidate_items = list(self.document.items)
        candidate_items[target_index] = item
        # Politica de outra imagem nao bloqueia aplicar o manifesto: o ADM refaz a sugestao em seguida.
        errors = [error for error in validate_items(candidate_items, allow_empty_devteam=True) if "se refere a outra imagem" not in error]
        if errors:
            messagebox.showerror(APP_TITLE, "Manifesto gerado, mas o item ficou invalido:\n\n" + "\n".join(errors[:12]))
            self.status_var.set("Manifesto gerado, mas item invalido.")
            return

        self.document.items[target_index] = item
        self.selected_index = target_index
        self.refresh_tree()
        self.select_tree_index(target_index)
        self.set_form(item)
        self.mark_document_dirty("Manifesto USB aplicado ao item selecionado.")
        self.status_var.set(f"Manifesto aplicado: {manifest_path.name}")
        self.append_console(f"Manifesto aplicado ao item {item.get('name', '')}: {iso_path.name} -> {manifest_path.name}")
        if policy_message:
            self.append_console(policy_message, "WARNING" if "nao sugerida" in policy_message or "nao ha sugestao" in policy_message else "INFO")
            self.status_var.set(f"Manifesto aplicado: {manifest_path.name}. {policy_message}")
        self._refresh_readiness(item)
        if getattr(self, "_prepare_chain_scan", False):
            self._prepare_chain_scan = False
            self.start_defender_scan(iso_path)

    def validate_current_catalog(self) -> None:
        if not self.apply_pending_form_changes("validar"):
            return
        path = self.document.path
        allow_empty_devteam = path is not None and path.name == "qdisk.quallit.devteam.json"
        errors = validate_items(self.document.items, allow_empty_devteam=allow_empty_devteam)
        if errors:
            self.append_console(f"Catalogo invalido: {len(errors)} erro(s).", "ERROR")
            messagebox.showerror(APP_TITLE, "Catalogo invalido:\n\n" + "\n".join(errors[:20]))
            self.status_var.set(f"Catalogo invalido: {len(errors)} erro(s).")
            return
        messagebox.showinfo(APP_TITLE, f"Catalogo valido com {len(self.document.items)} item(ns).")
        self.status_var.set("Catalogo valido.")
        self.append_console(f"Catalogo valido com {len(self.document.items)} item(ns).")

    def upload_current_catalog(self) -> None:
        if self.upload_worker is not None and self.upload_worker.is_alive():
            messagebox.showinfo(APP_TITLE, "Aguarde o upload atual terminar.")
            return
        if self.document.path is None:
            messagebox.showinfo(APP_TITLE, "Salve o catalogo antes de fazer upload.")
            return
        if not self.apply_pending_form_changes("enviar o catalogo"):
            return
        if not PUBLISH_CATALOGS_SCRIPT.exists():
            messagebox.showerror(APP_TITLE, f"Script de upload nao encontrado:\n{PUBLISH_CATALOGS_SCRIPT}")
            return
        errors = validate_items(self.document.items, allow_empty_devteam=self.document.path.name == "qdisk.quallit.devteam.json")
        if errors:
            messagebox.showerror(APP_TITLE, "Corrija antes do upload:\n\n" + "\n".join(errors[:12]))
            return
        scan_warnings = scan_publish_warnings(self.document.items)
        if scan_warnings:
            self.append_console("Aviso do Defender antes do upload: " + " | ".join(scan_warnings), "WARNING")
            if not messagebox.askyesno(
                APP_TITLE,
                "A ultima verificacao do Microsoft Defender encontrou ameacas nestas imagens:\n\n"
                + "\n".join(scan_warnings[:10])
                + "\n\nNas maquinas com o Defender, a gravacao do USB pode ser reprovada (arquivo em quarentena). "
                "Considere publicar a ISO limpa.\n\nPublicar o catalogo mesmo assim?",
            ):
                return
        if self.document_dirty:
            save_response = messagebox.askyesnocancel(
                APP_TITLE,
                "Existem alteracoes no catalogo que ainda nao foram salvas em disco.\n\n"
                f"Arquivo: {self.document.path.name}\n\n"
                "Deseja salvar antes de enviar aos repositorios?\n\n"
                "Sim: salva e envia.\n"
                "Nao: envia a ultima versao salva em disco.\n"
                "Cancelar: interrompe o upload.",
            )
            if save_response is None:
                return
            if save_response:
                if not self._save_to_path(self.document.path):
                    return
            else:
                self.append_console("Upload continuou sem salvar alteracoes pendentes em disco.", "WARNING")
        upload_target = self.upload_target_var.get().strip() or "Both"
        target_label = UPLOAD_TARGET_LABELS.get(upload_target, upload_target)
        if not messagebox.askyesno(
            APP_TITLE,
            "Enviar este catalogo para o destino selecionado?\n\n"
            f"Arquivo: {self.document.path.name}\n\n"
            f"Destino: {target_label}.",
        ):
            return
        try:
            copied, skipped = sync_local_jsons_to_project_config()
        except Exception as exc:
            messagebox.showerror(APP_TITLE, f"Falha ao sincronizar JSONs para config antes do upload:\n{exc}")
            return
        if copied or skipped:
            self.append_console(f"JSONs sincronizados para config antes do upload: {copied} copiado(s), {skipped} falha(s).")
        if skipped:
            messagebox.showerror(APP_TITLE, "Nem todos os JSONs foram sincronizados para config antes do upload.")
            return

        self.upload_button.configure(state=tk.DISABLED)
        self.upload_target_combo.configure(state=tk.DISABLED)
        self.status_var.set(f"Enviando {self.document.path.name}...")
        self.append_console(f"Iniciando upload: {self.document.path.name} ({upload_target})")
        self.upload_worker = threading.Thread(target=self.upload_worker_run, args=(self.document.path, upload_target), daemon=True)
        self.upload_worker.start()
        self.after(200, self.poll_upload_queue)

    def upload_worker_run(self, path: Path, upload_target: str) -> None:
        command = [
            "powershell",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            str(PUBLISH_CATALOGS_SCRIPT),
            "-CatalogPath",
            str(path),
            "-Target",
            upload_target,
        ]
        try:
            self.upload_queue.put(("debug", "Comando: " + " ".join(command)))
            process = subprocess.Popen(
                command,
                cwd=str(REPO_ROOT),
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                encoding="utf-8",
                errors="replace",
            )
            output_lines: list[str] = []
            assert process.stdout is not None
            for line in process.stdout:
                clean_line = line.strip()
                if clean_line:
                    output_lines.append(clean_line)
                    self.upload_queue.put(("progress", clean_line))
            exit_code = process.wait()
            debug_output = "\n".join(output_lines)
            self.upload_queue.put(("debug", f"Saida completa do upload:\n{debug_output}"))
            self.upload_queue.put(("done", (exit_code, "\n".join(output_lines[-40:]))))
        except Exception as exc:
            self.upload_queue.put(("error", exc))

    def poll_upload_queue(self) -> None:
        keep_polling = True
        while True:
            try:
                message_type, payload = self.upload_queue.get_nowait()
            except queue.Empty:
                break

            if message_type == "progress":
                self.status_var.set(str(payload))
                self.append_console(str(payload))
            elif message_type == "debug":
                self.append_console(str(payload), "DEBUG")
            elif message_type == "done":
                exit_code, output = payload  # type: ignore[misc]
                self.upload_button.configure(state=tk.NORMAL)
                self.upload_target_combo.configure(state="readonly")
                keep_polling = False
                summary = build_upload_summary(str(output), int(exit_code) == 0, self.debug_log_path)
                if int(exit_code) == 0:
                    self.status_var.set("Upload de catalogo concluido.")
                    self.append_console("Upload de catalogo concluido.")
                    messagebox.showinfo(APP_TITLE, summary)
                else:
                    self.status_var.set(f"Upload falhou com codigo {exit_code}.")
                    self.append_console(f"Upload falhou com codigo {exit_code}. Veja log_debug: {self.debug_log_path}", "ERROR")
                    messagebox.showerror(APP_TITLE, summary)
            elif message_type == "error":
                self.upload_button.configure(state=tk.NORMAL)
                self.upload_target_combo.configure(state="readonly")
                keep_polling = False
                self.status_var.set("Upload falhou.")
                self.append_console(f"Falha ao executar upload: {payload}", "ERROR")
                messagebox.showerror(APP_TITLE, f"Falha ao executar upload:\n{payload}")

        if keep_polling and self.upload_worker is not None and self.upload_worker.is_alive():
            self.after(200, self.poll_upload_queue)


def run_cli(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Editor e validador de catalogos Q-DISK.")
    parser.add_argument("--validate", type=Path, help="Valida um catalogo sem abrir a interface grafica.")
    parser.add_argument("--export-package", nargs=2, metavar=("SOURCE", "DEST"), help="Exporta SOURCE como objeto build/items.")
    parser.add_argument("--generate-manifest", type=Path, help="Gera manifesto logico para uma ISO usando helper .NET com fallback por montagem do Windows.")
    parser.add_argument("--generate-missing-manifests", type=Path, help="Gera manifestos ausentes e atualiza um catalogo.")
    parser.add_argument("--image-dir", type=Path, default=Path.home() / "Downloads" / "qdisk", help="Diretorio local das ISOs para --generate-missing-manifests.")
    parser.add_argument("--out", type=Path, default=CONFIG_DIR / "manifests", help="Diretorio de saida para --generate-manifest.")
    # Mesmo padrao da interface: URL absoluta do servidor principal (antes o CLI gravava "manifests" relativo).
    parser.add_argument("--manifest-url-base", default=None, help="Base do manifestUrl no resumo (padrao: <primary.baseUrl>/manifests, como a interface).")
    parser.add_argument("--refresh", action="store_true", help="Com --generate-missing-manifests: regera tambem os manifestos de ISOs que ja tem usbIdentification.")
    parser.add_argument("--suggest-write-policies", type=Path, help="Mostra a usbWritePolicy sugerida pela analise de cada ISO do catalogo.")
    parser.add_argument("--apply", action="store_true", help="Com --suggest-write-policies: grava as sugestoes no catalogo.")
    parser.add_argument("--overwrite", action="store_true", help="Com --suggest-write-policies: substitui politicas existentes.")
    parser.add_argument("--scan-iso", type=Path, help="Varre a ISO com o Microsoft Defender (sem remediacao) e lista ameacas e copias de ferramentas do Windows.")
    parser.add_argument("--catalog", type=Path, help="Com --scan-iso: catalogo onde gravar o resultado (imageScan) no item --item.")
    parser.add_argument("--item", default="", help="Com --scan-iso --catalog: nome do item (ou varios, separados por virgula).")
    parser.add_argument("--clean-iso", type=Path, help="Gera uma ISO sem os arquivos de --exclude, no mesmo layout de boot (exige oscdimg).")
    parser.add_argument("--clean-out", type=Path, help="Com --clean-iso: caminho da ISO limpa.")
    parser.add_argument("--exclude", nargs="*", default=[], help="Com --clean-iso: arquivos a remover (relativos a raiz da ISO).")
    args = parser.parse_args(argv)
    if args.manifest_url_base is None:
        args.manifest_url_base = read_endpoint_base_urls()[0].rstrip("/") + "/manifests"

    if args.validate:
        document = load_catalog(args.validate)
        errors = validate_items(document.items, allow_empty_devteam=args.validate.name == "qdisk.quallit.devteam.json")
        if errors:
            for error in errors:
                print(error, file=sys.stderr)
            return 1
        print(f"OK: {args.validate} ({len(document.items)} itens)")
        return 0

    if args.export_package:
        source = Path(args.export_package[0])
        dest = Path(args.export_package[1])
        document = load_catalog(source)
        errors = validate_items(document.items, allow_empty_devteam=source.name == "qdisk.quallit.devteam.json")
        if errors:
            for error in errors:
                print(error, file=sys.stderr)
            return 1
        write_catalog(dest, document.items, package_format=True)
        print(f"Exportado: {dest}")
        return 0

    if args.generate_manifest:
        manifest_path, summary = generate_iso_manifest(args.generate_manifest, args.out, args.manifest_url_base)
        print(f"Manifesto gerado: {manifest_path}")
        print("Resumo usbIdentification:")
        print(json.dumps(summary, indent=2, ensure_ascii=False))
        return 0

    if args.generate_missing_manifests:
        changed, messages = generate_missing_manifests_for_catalog(
            args.generate_missing_manifests,
            args.image_dir,
            args.out,
            args.manifest_url_base,
            refresh=args.refresh,
        )
        for message in messages:
            print(message)
        print(f"Catalogo atualizado: {args.generate_missing_manifests} ({changed} item(ns) alterado(s))")
        return 0

    if args.scan_iso:
        image_sha256 = calculate_file_sha256(args.scan_iso)
        scan = scan_iso_with_defender(args.scan_iso, image_sha256)
        print(json.dumps(scan, indent=2, ensure_ascii=False))
        print(image_scan_summary(scan))
        if args.catalog and args.item:
            document = load_catalog(args.catalog)
            names = {name.strip() for name in args.item.split(",") if name.strip()}
            updated = 0
            for item in document.items:
                if item.get("name") in names:
                    extra = dict(item.get("_extra") or {})
                    extra["imageScan"] = scan
                    item["_extra"] = extra
                    updated += 1
            write_catalog(args.catalog, document.items, package_format=document.source_format == "package")
            print(f"Catalogo atualizado: {args.catalog} ({updated} item(ns) com imageScan)")
        return 0

    if args.clean_iso:
        if not args.clean_out:
            print("Informe --clean-out.", file=sys.stderr)
            return 1
        output = build_clean_iso(args.clean_iso, args.clean_out, list(args.exclude))
        print(f"ISO limpa gerada: {output} ({output.stat().st_size} bytes, SHA-256 {calculate_file_sha256(output)})")
        return 0

    if args.suggest_write_policies:
        changed, messages = suggest_write_policies_for_catalog(args.suggest_write_policies, args.apply, args.overwrite)
        for message in messages:
            print(message)
        if args.apply:
            print(f"Catalogo atualizado: {args.suggest_write_policies} ({changed} item(ns) alterado(s))")
        return 0

    app = CatalogAdminApp()
    app.mainloop()
    return 0


if __name__ == "__main__":
    raise SystemExit(run_cli(sys.argv[1:]))