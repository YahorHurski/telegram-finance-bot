#!/usr/bin/env bash
# Restore the newest (or the given) backup into a running Postgres container.
# usage: restore.sh <postgres-container> [object-name]
# env:   RCLONE_CONFIG_B2_ACCOUNT, RCLONE_CONFIG_B2_KEY (the B2 READ key), B2_BUCKET,
#        AGE_KEY_FILE (path to the private age key)
# Deliberately does NOT read .env: the VM holds neither a read key nor the private key (D-07).
# CI (restore-check.yml) passes them as secrets; in an emergency export them into the shell session by hand.
# Disaster recovery on the VM (docs/deploy.md, "Бэкапы"):
#   docker compose stop app
#   ./scripts/restore.sh "$(docker compose ps -q postgres)"
#   docker compose start app
set -euo pipefail
# The rclone remote "b2:" is defined purely by env vars, there is no rclone.conf
export RCLONE_CONFIG_B2_TYPE=b2

container="${1:-}"
[[ -n "$container" ]] || { echo "usage: restore.sh <postgres-container> [object-name]" >&2; exit 2; }
# The mask keeps a stray object in the bucket from becoming the "newest backup"; names sort by timestamp
obj="${2:-$(rclone lsf --files-only --include 'financebot-*.dump.age' "b2:${B2_BUCKET}" | sort | tail -n 1)}"
[[ -n "$obj" ]] || { echo "no backups found" >&2; exit 1; }
echo "restoring $obj" >&2

rclone cat "b2:${B2_BUCKET}/${obj}" | age -d -i "$AGE_KEY_FILE" \
  | docker exec -i "$container" pg_restore -U financebot -d financebot \
      --no-owner --clean --if-exists --single-transaction --exit-on-error
