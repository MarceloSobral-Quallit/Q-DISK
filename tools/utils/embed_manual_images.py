# ─────────────────────────────────────────────────────────────────────────────
# qdisk  |  Tools  |  embed_manual_images.py
# Converte imagens do manual HTML para Base64 inline (embutimento para distribuição).
#
# © Quallit — Licenciado sob GPL-3.0-or-later.
# ─────────────────────────────────────────────────────────────────────────────

import base64
import re
from pathlib import Path


MANUAL_IMAGE_NAMES = [
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
]


def to_data_uri(png_path: Path) -> str:
    data = png_path.read_bytes()
    b64 = base64.b64encode(data).decode("ascii")
    return f"data:image/png;base64,{b64}"


def replace_img_sources_in_order(html: str, image_paths: list[Path]) -> str:
    image_iter = iter(image_paths)

    def replace_src(match: re.Match[str]) -> str:
        try:
            image_path = next(image_iter)
        except StopIteration:
            return match.group(0)
        return f'{match.group(1)}{to_data_uri(image_path)}{match.group(3)}'

    return re.sub(r'(<img\b[^>]*\bsrc=")([^"]+)("[^>]*>)', replace_src, html, count=len(image_paths))


def main() -> None:
    repo_root = Path(__file__).resolve().parents[2]
    images_root = repo_root / "assets" / "manual" / "images"
    manual_path = repo_root / "assets" / "manual" / "manual.html"
    version_path = repo_root / "version_info.txt"

    html = manual_path.read_text(encoding="utf-8")
    original_html = html

    manual_images = [images_root / name for name in MANUAL_IMAGE_NAMES]
    missing_images = [path.name for path in manual_images if not path.exists()]
    if missing_images:
        missing_text = ", ".join(missing_images)
        raise FileNotFoundError(f"Imagens do manual nao encontradas em {images_root}: {missing_text}")

    html = replace_img_sources_in_order(html, manual_images)

    if version_path.exists():
        version_text = ""
        updated_text = ""
        for line in version_path.read_text(encoding="utf-8").splitlines():
            if line.startswith("Versao:"):
                version_text = line.replace("Versao:", "").strip()
            elif line.startswith("Atualizado em:"):
                updated_text = line.replace("Atualizado em:", "").strip()

        if version_text:
            html = re.sub(
                r'<div class="meta">Vers(?:ao|ão):.*?</div>',
                f'<div class="meta">Versão: {version_text}</div>',
                html,
            )
        if updated_text:
            html = re.sub(
                r'<div class="meta">Atualizado em:.*?</div>',
                f'<div class="meta">Atualizado em: {updated_text}</div>',
                html,
            )

    if html != original_html:
        manual_path.write_text(html, encoding="utf-8")


if __name__ == "__main__":
    main()
