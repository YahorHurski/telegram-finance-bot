#!/usr/bin/env bash
# Manual deploy on the VM (D-13): ./deploy.sh from the repo checkout.
set -euo pipefail
cd "$(dirname "$0")"

git pull --ff-only
docker compose build app
docker compose run --rm app migrate      # manual migration step until the migrator service (Phase 2)
docker compose up -d

domain=$(grep '^DOMAIN=' .env | cut -d= -f2 | tr -d '\r')
# Caddy may still be fetching the certificate right after the first start
curl -fsS --retry 10 --retry-delay 3 --retry-all-errors "https://${domain}/health" && echo " OK"
