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
    SELECT column_name, data_type
    FROM information_schema.columns
    WHERE table_schema='public' AND table_name='system_logs'
    ORDER BY ordinal_position
    """
)
cols = cur.fetchall()
print("COLS:")
for col in cols:
    print(col)

text_cols = [c[0] for c in cols if c[1] in ("text", "character varying", "json", "jsonb")]
print("TEXT:", text_cols)

if text_cols:
    where = " OR ".join([f"{c}::text ILIKE %s" for c in text_cols])
    sql = f"SELECT * FROM public.system_logs WHERE {where} ORDER BY 1 DESC LIMIT 5"
    params = ["%applied live%"] * len(text_cols)
    cur.execute(sql, params)
    rows = cur.fetchall()
    print("MATCH applied live:", len(rows))
    for row in rows:
        print(row)

    params2 = ["%purpose updated%"] * len(text_cols)
    cur.execute(sql.replace("%applied live%", "%purpose%"), ["%purpose%"] * len(text_cols))
    # rewrite cleanly
    where2 = " OR ".join([f"{c}::text ILIKE %s" for c in text_cols])
    sql2 = f"SELECT * FROM public.system_logs WHERE {where2} ORDER BY 1 DESC LIMIT 5"
    cur.execute(sql2, ["%purpose updated%"] * len(text_cols))
    for row in cur.fetchall():
        print("PURPOSE ROW:", row)

c.close()
