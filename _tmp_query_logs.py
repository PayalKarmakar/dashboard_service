import psycopg2

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
    SELECT id, service_name, event_type, message, created_at
    FROM public.system_logs
    WHERE event_type ILIKE '%PURPOSE%'
       OR message ILIKE '%purpose updated%'
       OR message ILIKE '%applied live%'
    ORDER BY id DESC
    LIMIT 30
    """
)
rows = cur.fetchall()
print("count", len(rows))
for r in rows:
    print("---")
    print("id=", r[0], "svc=", r[1], "evt=", r[2], "at=", r[4])
    print("msg=", r[3])
c.close()
