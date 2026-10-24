#!/usr/bin/env python3
"""Schreibt die eingebauten OAuth-Client-IDs (docs/FUNKTIONEN.md §18) aus Umgebungsvariablen.

In der CI aus GitHub-Secrets aufgerufen. Fehlende Variablen → Anbieter wird weggelassen (Passwort-Anmeldung).
"""
import json
import os
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent

MAPPING = {
    "gmail": ("LUMAIL_GOOGLE_CLIENT_ID", "LUMAIL_GOOGLE_CLIENT_SECRET"),
    "outlook": ("LUMAIL_MICROSOFT_CLIENT_ID", None),
    "mailru": ("LUMAIL_MAILRU_CLIENT_ID", "LUMAIL_MAILRU_CLIENT_SECRET"),
}


def build() -> dict:
    result = {}
    for provider, (id_var, secret_var) in MAPPING.items():
        client_id = os.environ.get(id_var, "").strip()
        if not client_id:
            continue
        entry = {"clientId": client_id}
        secret = os.environ.get(secret_var, "").strip() if secret_var else ""
        if secret:
            entry["clientSecret"] = secret
        result[provider] = entry
    return result


def main() -> None:
    config = build()
    text = json.dumps(config, indent=2, sort_keys=True)
    (ROOT / "windows" / "src" / "LuMail.Core" / "BuiltInOAuth.json").write_text(text + "\n", encoding="utf-8")
    hashes = "#"
    while f'"{hashes}' in text:
        hashes += "#"
    swift = (
        "// Von scripts/write-builtin-oauth.py erzeugt (in der CI aus GitHub-Secrets). Im Repository leer.\n\n"
        "public enum BuiltInOAuth {\n"
        f'    public static let json = {hashes}"""\n{text}\n"""{hashes}\n'
        "}\n"
    )
    (ROOT / "macos" / "Sources" / "LuMailCore" / "Generated" / "BuiltInOAuth.swift").write_text(swift, encoding="utf-8")
    print("Eingebaute OAuth-Anbieter:", ", ".join(sorted(config)) or "keine")


if __name__ == "__main__":
    main()
