"""Small, offline source hygiene gate. Reports locations, never matching credential values."""
from pathlib import Path
import re
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
paths = set(subprocess.check_output(["git", "ls-files", "-co", "--exclude-standard", "-z"], cwd=root).decode().split("\0"))
patterns = {
    "GitHub credential": re.compile(r"\bgh[pousr]_[A-Za-z0-9]{30,}\b|github_pat_[A-Za-z0-9_]{40,}"),
    "private key": re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----"),
    "AWS access key": re.compile(r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b"),
    "API secret": re.compile(r"\bsk-(?:proj-)?[A-Za-z0-9_-]{30,}"),
}
forbidden = {".mono", "bin", "obj", "TestResults", ".vs", ".git", "artifacts"}
findings = []
checked = 0
for name in sorted(paths):
    if not name:
        continue
    path = root / name
    if not path.is_file():
        continue
    if forbidden.intersection(Path(name).parts) or path.suffix.lower() in {".pfx", ".p12", ".db", ".dmp"}:
        findings.append(f"{name}: generated data or credential container must not be tracked")
        continue
    if path.suffix.lower() in {".ico", ".png", ".jpg", ".zip"}:
        continue
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    checked += 1
    for label, pattern in patterns.items():
        for match in pattern.finditer(text):
            line = text.count("\n", 0, match.start()) + 1
            findings.append(f"{name}:{line}: possible {label} (redacted)")
if findings:
    print("Source hygiene failed:")
    print("\n".join(findings))
    sys.exit(1)
print(f"Source hygiene passed: {checked} text files; no matching credential patterns or tracked build/data files.")
print("This lightweight check complements review; it is not an exhaustive security scanner.")
