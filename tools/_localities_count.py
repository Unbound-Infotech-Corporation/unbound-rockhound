import sqlite3
import os
from pathlib import Path

base = Path(os.environ["LOCALAPPDATA"]) / "GeoMineralTrace"
print("base", base, "exists", base.exists())
if base.exists():
    print("files", sorted(p.name for p in base.iterdir()))

loc = base / "localities.db"
print("localities exists", loc.exists(), "size", loc.stat().st_size if loc.exists() else 0)
if not loc.exists():
    raise SystemExit(0)

c = sqlite3.connect(str(loc))
print("total", c.execute("SELECT COUNT(*) FROM localities").fetchone()[0])
print(
    "by_source",
    list(
        c.execute(
            "SELECT COALESCE(source_dataset, '(null)'), COUNT(*) "
            "FROM localities GROUP BY 1 ORDER BY 2 DESC"
        )
    ),
)
print("by_land", list(c.execute("SELECT land_type, COUNT(*) FROM localities GROUP BY land_type")))
print(
    "by_access",
    list(c.execute("SELECT access_status, COUNT(*) FROM localities GROUP BY access_status")),
)
print(
    "top_states",
    list(
        c.execute(
            "SELECT state_code, COUNT(*) FROM localities "
            "GROUP BY state_code ORDER BY COUNT(*) DESC LIMIT 12"
        )
    ),
)
print(
    "ext",
    list(
        c.execute(
            """
            SELECT CASE
              WHEN external_id LIKE 'usgs-mrds:%' THEN 'usgs-mrds'
              WHEN external_id LIKE 'usgs-critmin:%' THEN 'usgs-critmin'
              WHEN external_id IS NULL THEN 'null'
              ELSE 'other'
            END AS p, COUNT(*)
            FROM localities GROUP BY p
            """
        )
    ),
)
