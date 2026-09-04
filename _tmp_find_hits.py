from pathlib import Path
import re

needles = [
    b"purpose updated",
    "purpose updated".encode("utf-16le"),
    b"READER_PURPOSE_UPDATED",
    "READER_PURPOSE_UPDATED".encode("utf-16le"),
    b"applied live",
    "applied live".encode("utf-16le"),
]
roots = [
    Path(r"C:\Program Files"),
    Path(r"C:\Program Files (x86)"),
    Path(r"C:\Users\User\Desktop"),
    Path(r"C:\Users\User\AppData\Local"),
    Path(r"D:\\"),
]
out = Path(r"C:\Users\User\Desktop\New folder (6)\dashboard_service\dashboard_service\_tmp_find_hits.txt")
hits = []
for root in roots:
    if not root.exists():
        hits.append(f"MISSING {root}")
        continue
    hits.append(f"SCAN {root}")
    try:
        for p in root.rglob("*"):
            suf = p.suffix.lower()
            if suf not in {".dll", ".exe", ".cs"}:
                continue
            name = p.name.lower()
            # speed: prefer rfid-related when under huge trees
            if root.name.startswith("Program") or str(root).startswith(r"C:\Users\User\AppData"):
                if "rfid" not in name and "rfid" not in str(p).lower() and "smart" not in str(p).lower() and "monitor" not in str(p).lower():
                    # still check SystemLog-ish and service binaries under SRP
                    if "srp" not in str(p).lower() and "sensor" not in str(p).lower():
                        continue
            try:
                size = p.stat().st_size
                if size > 100_000_000 or size < 100:
                    continue
                data = p.read_bytes()
            except Exception:
                continue
            for n in needles:
                if n in data:
                    hits.append(f"{p} :: {n[:40]!r}")
                    break
    except Exception as e:
        hits.append(f"ERR {root}: {e}")

out.write_text("\n".join(hits), encoding="utf-8")
print("done", len(hits))
for h in hits[:80]:
    print(h)
