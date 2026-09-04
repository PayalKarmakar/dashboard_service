from pathlib import Path
import re

paths = [
    Path(r"C:\Users\User\Desktop\rfid_service\RfidManagementSystem\bin\Release\net8.0-windows\RfidManagementSystem.dll"),
    Path(r"C:\Users\User\Desktop\rfid_service\RfidManagementSystem\bin\Release\net8.0-windows\win-x64\RfidService.dll"),
    Path(r"C:\Users\User\Desktop\rfid_service\RfidManagementSystem\bin\Release\net8.0-windows\win-x64\RfidService.exe"),
]
out = Path(r"C:\Users\User\Desktop\New folder (6)\dashboard_service\dashboard_service\_tmp_dll_strings.txt")
lines = []
for p in paths:
    lines.append(f"=== {p} exists={p.exists()} ===")
    if not p.exists():
        continue
    data = p.read_bytes()
    # UTF-16LE readable chunks
    u16 = data.decode("utf-16le", errors="ignore")
    for needle in ["purpose updated", "applied live", "READER_PURPOSE", "no service restart", "ENTRY_EXIT"]:
        for m in re.finditer(re.escape(needle), u16, re.I):
            start = max(0, m.start() - 60)
            end = min(len(u16), m.end() + 80)
            chunk = u16[start:end]
            printable = "".join(ch if 32 <= ord(ch) < 127 else "." for ch in chunk)
            lines.append(f"U16[{needle}]: {printable}")
    # ASCII
    for needle in [b"purpose updated", b"applied live", b"READER_PURPOSE", b"no service restart"]:
        idx = 0
        while True:
            i = data.find(needle, idx)
            if i < 0:
                break
            chunk = data[max(0, i - 40) : i + 100]
            printable = "".join(chr(b) if 32 <= b < 127 else "." for b in chunk)
            lines.append(f"ASCII[{needle.decode()}]: {printable}")
            idx = i + 1

out.write_text("\n".join(lines), encoding="utf-8")
print("wrote", out, "lines", len(lines))
