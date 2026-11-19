#!/usr/bin/env bash
# Deploy one commit to the VM by its SHA (D-01). The images were built and tested by CI; nothing is built here.
#   CI:     ssh ubuntu@VM <sha>  → authorized_keys `restrict,command=` forces this script, the SHA is in SSH_ORIGINAL_COMMAND
#   Manual: ./deploy.sh <sha>    (on the VM; also the way to roll back by hand to an older SHA)
# Steps: check SHA → git checkout SHA → pull images → backup (D-12: no backup, no migration)
#        → migrator (failed: the running app is not touched) → TAG in .env → up -d → /health (not 200: back to the previous SHA, D-03)
# D-03: migrations must be backward compatible (the old app keeps working on the new schema) and one migration per deploy:
#       rollback restores the image, not the schema, and EF Core 10 runs each migration in its own transaction.
# Output goes to the public Actions log: only step statuses here, never database data or app logs.
set -euo pipefail

main() {
  cd "$(dirname "$0")"
  # Untrusted string from SSH: validate before doing anything else
  local sha="${1:-${SSH_ORIGINAL_COMMAND:-}}"
  [[ "$sha" =~ ^[0-9a-f]{40}$ ]] || { echo "usage: deploy.sh <40-char sha>" >&2; exit 2; }

  local orig prev
  orig=$(git rev-parse HEAD)
  prev=$(sed -n 's/^TAG=//p' .env | tr -d '\r')
  [[ "$prev" =~ ^[0-9a-f]{40}$ ]] || prev=""   # first deploy: nothing to roll back to

  git fetch --quiet origin
  git checkout --quiet --detach "$sha"
  export TAG="$sha"   # TAG = SHA = commit: one value selects both the images and compose.yaml

  docker compose pull --quiet migrator app || abort "$orig" pull
  ./scripts/backup.sh || abort "$orig" backup
  # Explicit step instead of relying on depends_on: compose does not document what `up` does when a dependency fails
  docker compose run --rm migrator || abort "$orig" migrator

  set_tag "$sha"
  if ! docker compose up -d || ! healthy; then
    rollback "$prev"
  fi
  echo "deployed $sha"
}

abort() {
  git checkout --quiet --detach "$1"
  echo "deploy failed at $2: running version unchanged" >&2
  exit 1
}

rollback() {
  local prev="$1"
  if [[ -z "$prev" ]]; then
    echo "first deploy: no automatic rollback, see docs/deploy.md" >&2
    exit 1
  fi
  git checkout --quiet --detach "$prev"
  set_tag "$prev"
  TAG="$prev" docker compose up -d
  echo "rolled back to $prev" >&2
  exit 1   # red job: GitHub sends an email
}

set_tag() {
  if grep -q '^TAG=' .env; then sed -i "s/^TAG=.*/TAG=$1/" .env; else echo "TAG=$1" >> .env; fi
}

healthy() {
  local domain
  domain=$(sed -n 's/^DOMAIN=//p' .env | tr -d '\r')
  # Caddy may still be fetching a certificate, the app may still be starting
  curl -fsS -o /dev/null --retry 10 --retry-delay 3 --retry-all-errors "https://$domain/health"
}

# git checkout rewrites this file mid-run; bash has already parsed the whole function, so the old version finishes cleanly
main "$@"; exit
