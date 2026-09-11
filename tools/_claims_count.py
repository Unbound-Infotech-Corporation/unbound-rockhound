import sqlite3, os
p = os.path.join(os.environ["LOCALAPPDATA"], "GeoMineralTrace", "claims.db")
c = sqlite3.connect(p)
names = {0: "Unknown", 1: "Active", 2: "ExpiringSoon", 3: "LapsedReopenable", 4: "Closed"}
print("total", c.execute("SELECT COUNT(*) FROM mining_claims").fetchone()[0])
for s, n in c.execute("SELECT status, COUNT(*) FROM mining_claims GROUP BY status ORDER BY status"):
    print(f"{names.get(s, s)}: {n}")
print("by_state", list(c.execute("SELECT state_code, COUNT(*) FROM mining_claims GROUP BY state_code ORDER BY COUNT(*) DESC")))
print("sources", list(c.execute("SELECT source_dataset, COUNT(*) FROM mining_claims GROUP BY source_dataset")))
