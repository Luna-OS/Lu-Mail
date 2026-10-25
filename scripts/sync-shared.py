#!/usr/bin/env python3
"""Bettet die Dateien aus shared/ als Swift-Strings in LuMailCore ein.

Aufruf:  python3 scripts/sync-shared.py          → schreibt die Datei neu
         python3 scripts/sync-shared.py --check  → Exit-Code 1, wenn sie veraltet ist (für die CI)

Windows braucht das nicht: LuMail.Core bindet shared/ direkt als EmbeddedResource ein.
"""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
TARGET = ROOT / "macos" / "Sources" / "LuMailCore" / "Generated" / "SharedResources.swift"

FILES = {
    "providersJSON": "shared/providers.json",
    "categoriesJSON": "shared/categories.json",
    "themeJSON": "shared/theme/lu-theme.json",
    "loginHTML": "shared/login/login.html",
    "doneHTML": "shared/login/done.html",
    "oauthExampleJSON": "shared/oauth.example.json",
    "updateJSON": "shared/update.json",
    "trackersJSON": "shared/trackers.json",
    "phishingJSON": "shared/phishing.json",
    "customServersHTML": "shared/login/custom-servers.html",
    "webmailJS": "shared/webmail/lu-webmail.js",
    "webmailCaptureJS": "shared/webmail/lu-capture.js",
}


def render() -> str:
    out = [
        "// AUTOMATISCH ERZEUGT von scripts/sync-shared.py – nicht von Hand bearbeiten.",
        "// Quelle: shared/",
        "",
        "public enum SharedResources {",
    ]
    for name, rel in FILES.items():
        text = (ROOT / rel).read_text(encoding="utf-8")
        # Raw-String mit genug '#', damit kein Inhalt ihn vorzeitig beendet.
        hashes = "#"
        while f'"{hashes}' in text or f"\\{hashes}" in text:
            hashes += "#"
        out.append(f"    /// {rel}")
        out.append(f'    public static let {name} = {hashes}"""')
        out.extend(text.rstrip("\n").split("\n"))
        out.append(f'"""{hashes}')
        out.append("")
    out[-1] = "}"
    return "\n".join(out) + "\n"


def main() -> int:
    content = render()
    if "--check" in sys.argv:
        if not TARGET.exists() or TARGET.read_text(encoding="utf-8") != content:
            print(f"{TARGET.relative_to(ROOT)} ist veraltet – bitte 'python3 scripts/sync-shared.py' ausführen.")
            return 1
        print("SharedResources.swift ist aktuell.")
        return 0
    TARGET.parent.mkdir(parents=True, exist_ok=True)
    TARGET.write_text(content, encoding="utf-8")
    print(f"geschrieben: {TARGET.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
