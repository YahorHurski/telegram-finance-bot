#!/usr/bin/env bash
# Nightly (cron, 02:17 UTC) and pre-deploy backup: pg_dump -> age (public key only) -> Backblaze B2.
# The VM holds only the age public key and a Write Only B2 key: it can add backups, not read or delete them (D-07).
# Old backups are removed by the bucket lifecycle rule, never by this script (D-08).
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; . ./.env; set +a   # BACKUP_AGE_RECIPIENT, B2_BUCKET, RCLONE_CONFIG_B2_ACCOUNT/KEY, Bot__TelegramToken, Bot__OwnerChatId
# The rclone remote "b2:" is defined purely by env vars, there is no rclone.conf
export RCLONE_CONFIG_B2_TYPE=b2

# D-09: tell the owner that the backup failed. Only the fact and the line number, never data from the database.
# shortcut: the bot token is visible in `ps` while curl runs; the VM is single-user. If other users appear, pass the URL via `curl -K -` on stdin.
notify_failure() {
  curl -fsS -m 10 -o /dev/null "https://api.telegram.org/bot${Bot__TelegramToken:-}/sendMessage" \
    -d chat_id="${Bot__OwnerChatId:-}" --data-urlencode text="⚠️ Бэкап не удался (строка $1)" || true
}
failed_line='?'
trap 'failed_line=$LINENO' ERR
umask 077
tmp=$(mktemp)
# Alert from EXIT, not only ERR: an unset variable under `set -u` exits without running the ERR trap
trap 'rc=$?; rm -f "$tmp"; if [ "$rc" -ne 0 ]; then notify_failure "$failed_line"; fi' EXIT

# Dump to a file first: piped straight into rclone, a pg_dump that dies halfway would still
# upload a truncated (and decryptable) file that becomes the "newest backup".
docker compose exec -T postgres pg_dump -U financebot -d financebot -Fc > "$tmp"
name="financebot-$(date -u +%Y%m%dT%H%M%SZ).dump.age"
age -r "$BACKUP_AGE_RECIPIENT" "$tmp" | rclone rcat "b2:${B2_BUCKET}/${name}"
