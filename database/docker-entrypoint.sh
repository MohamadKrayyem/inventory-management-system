#!/bin/bash
# Starts SQL Server and, on the first run only, creates and seeds InventoryDb.
# "First run" = the data volume has no .seeded marker yet.
set -euo pipefail

MARKER=/var/opt/mssql/.seeded
SQLCMD=(/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b)

/opt/mssql/bin/sqlservr &
pid=$!

# Pass docker stop on to SQL Server so it shuts down cleanly
trap 'kill -TERM "$pid"; wait "$pid"' TERM INT

if [ ! -f "$MARKER" ]; then
    echo "[seed] Waiting for SQL Server to accept connections..."
    for i in $(seq 1 60); do
        if "${SQLCMD[@]}" -Q "SELECT 1" > /dev/null 2>&1; then break; fi
        if [ "$i" -eq 60 ]; then echo "[seed] SQL Server did not start in time." >&2; exit 1; fi
        sleep 2
    done

    # database.sql drops and recreates the tables, so a half-finished earlier
    # attempt is simply redone. The marker is only written after all scripts succeed.
    for script in database.sql phase2.sql phase3.sql; do
        echo "[seed] Running $script"
        "${SQLCMD[@]}" -i "/seed/$script"
    done
    touch "$MARKER"
    echo "[seed] Database created and seeded."
else
    echo "[seed] Database already seeded, skipping."
fi

wait "$pid"
