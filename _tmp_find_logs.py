import psycopg2
from pathlib import Path

c = psycopg2.connect(
    host="localhost",
    port=5432,
    dbname="smart_monitoring",
    user="postgres",
    password="postgres",
)
cur = c.cursor()
cur.execute(
    """
    SELECT column_name
    FROM information_schema.columns
    WHERE table_schema='public' AND table_name='system_logs'
    ORDER BY ordinal_position
    """
)
print("COLS:", [r[0] for r in cur.fetchall()])

cur.execute(
    """
    SELECT *
    FROM public.system_logs
    WHERE message ILIKE %s OR message ILIKE %s
    ORDER BY 1 DESC
    LIMIT 10
    """,
    ("%applied live%", "%purpose updated%"),
)
rows = cur.fetchall()
print("ROWS:", len(rows))
for r in rows:
    print(r)
c.close()

needles = [
    b"applied live",
    "applied live".encode("utf-16le"),
    b"no service restart",
    "no service restart".encode("utf-16le"),
    b"purpose updated",
    "purpose updated".encode("utf-16le"),
    b"READER_PURPOSE_UPDATED",
]
roots = [
    Path(r"C:\Users\User\Desktop\rfid_service"),
    Path(r"C:\Users\User\Desktop\New folder (6)"),
    Path(r"C:\Users\User\Desktop\New folder (5)"),
]
hits = []
for root in roots:
    if not root.exists():
        print("missing root", root)
        continue
    for p in root.rglob("*"):
        if p.suffix.lower() not in {".cs", ".dll", ".exe", ".py"}:
            continue
        try:
            if p.stat().st_size > 80_000_000:
                continue
            data = p.read_bytes()
        except Exception:
            continue
        for n in needles:
            if n in data:
                hits.append((str(p), n.decode("utf-8", "replace")[:40]))
                break
print("HITS:", len(hits))
for h in hits[:60]:
    print(h)
