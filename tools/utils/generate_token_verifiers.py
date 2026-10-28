# -----------------------------------------------------------------------------
# qdisk | Tools / Utils | generate_token_verifiers.py
# Gera config/qdisk.tokens.app.json a partir de config/qdisk.tokens.json.
#
# O app embute so o arquivo gerado: perfis (nome, catalogo) e, para cada perfil e dia da janela, o PBKDF2-SHA256 do
# token daquele dia. Os internal_id ficam so em qdisk.tokens.json (repositorio privado e QDisk_Adm). O token
# continua o mesmo do site (docs/DOCUMENTACAO_TECNICA.md, secao 14). A mesma conta do hash esta em
# TokenVerifierSet.ComputeHash (src/qdisk.Core/Services/TokenVerifiers.vb).
#
# Uso: python tools/utils/generate_token_verifiers.py [--days-back 2] [--days-ahead 730] [--iterations 100000]
#
# (c) Quallit - Licenciado sob GPL-3.0-or-later.
# -----------------------------------------------------------------------------
from __future__ import annotations

import argparse
import base64
import datetime as dt
import hashlib
import json
import os
import re
import sys
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[2]
SOURCE = PROJECT_ROOT / "config" / "qdisk.tokens.json"
TARGET = PROJECT_ROOT / "config" / "qdisk.tokens.app.json"
BASE_DATE = dt.date(1970, 10, 1)
HASH_BYTES = 16


def site_token(day: dt.date, internal_id: str) -> str:
    """Regra do site: MD5(dias desde 1970-10-01 + internal_id), 6 primeiros digitos do hex."""
    total = (day - BASE_DATE).days + int(internal_id)
    digits = re.sub(r"\D", "", hashlib.md5(str(total).encode("utf-8")).hexdigest())
    return digits[:6] if len(digits) >= 6 else digits.rjust(6, "0")


def week_start(day: dt.date) -> dt.date:
    """Domingo da semana de day (mesma conta de TokenValidationService.WeekStart)."""
    return day - dt.timedelta(days=(day.weekday() + 1) % 7)


def verifier(args: tuple[str, str, dt.date, bytes, int]) -> tuple[str, str, str]:
    profile, internal_id, day, salt, iterations = args
    day_text = day.isoformat()
    full_salt = salt + f"|{profile}|{day_text}".encode("utf-8")
    digest = hashlib.pbkdf2_hmac("sha256", site_token(day, internal_id).encode("utf-8"), full_salt, iterations, HASH_BYTES)
    return profile, day_text, digest.hex()


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Gera os verificadores do token diario embutidos no app.")
    parser.add_argument("--days-back", type=int, default=2)
    parser.add_argument("--days-ahead", type=int, default=730)
    parser.add_argument("--iterations", type=int, default=100_000)
    parser.add_argument("--today", default="", help="Data base (yyyy-mm-dd); padrao: hoje.")
    args = parser.parse_args(argv)

    source = json.loads(SOURCE.read_text(encoding="utf-8-sig"))
    profiles = [p for p in source.get("profiles", []) if p.get("profile") and re.fullmatch(r"\d{6}", str(p.get("internalId", "")))]
    if not profiles:
        print(f"ERRO: nenhum perfil com internalId valido em {SOURCE}", file=sys.stderr)
        return 1

    today = dt.date.fromisoformat(args.today) if args.today else dt.date.today()
    # O app aceita o token de qualquer dia da semana (domingo a sabado): a janela comeca num domingo e termina num sabado.
    first = week_start(today - dt.timedelta(days=args.days_back))
    last = week_start(today + dt.timedelta(days=args.days_ahead)) + dt.timedelta(days=6)
    salt = os.urandom(16)
    days = [first + dt.timedelta(days=n) for n in range((last - first).days + 1)]
    jobs = [(p["profile"], str(p["internalId"]), day, salt, args.iterations) for p in profiles for day in days]

    verifiers: dict[str, dict[str, str]] = {p["profile"]: {} for p in profiles}
    with ProcessPoolExecutor() as pool:
        for profile, day_text, digest in pool.map(verifier, jobs, chunksize=64):
            verifiers[profile][day_text] = digest

    output = {
        "generatedBy": "tools/utils/generate_token_verifiers.py",
        "profiles": [{"profile": p["profile"], "displayName": p.get("displayName", p["profile"]), "catalogFile": p.get("catalogFile", "")} for p in profiles],
        "verifiers": {
            "algorithm": "PBKDF2-SHA256",
            "iterations": args.iterations,
            "salt": base64.b64encode(salt).decode("ascii"),
            "from": first.isoformat(),
            "to": last.isoformat(),
            "profiles": verifiers,
        },
    }
    TARGET.write_text(json.dumps(output, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"Gerado: {TARGET} | perfis={len(profiles)} | dias={len(days)} ({first} a {last}) | iteracoes={args.iterations}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
