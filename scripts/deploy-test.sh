#!/usr/bin/env bash
# Checks the decisions deploy.sh makes (D-03 rollback, D-12 stop on a failed migrator) without a VM.
# Real git, docker and curl must not be touched on a dev machine or in CI, so stubs come first in PATH
# and write their arguments to logs. This tests the logic only; it does not replace a live rollback on the VM.
set -euo pipefail
cd "$(dirname "$0")/.."

ORIG=$(printf 'a%.0s' {1..40}); PREV=$(printf 'b%.0s' {1..40}); NEW=$(printf 'c%.0s' {1..40})
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
failed=0

# run_case <name> [VAR=value ...]: fresh dir with a copy of deploy.sh and stubs; sets $d and $code
run_case() {
  d="$work/$1"; shift
  mkdir -p "$d/bin" "$d/scripts"
  cp deploy.sh "$d/"
  printf 'DOMAIN=deploy-test.invalid\nTAG=%s\n' "$PREV" > "$d/.env"
  printf '#!/usr/bin/env bash\nexit 0\n' > "$d/scripts/backup.sh"
  cat > "$d/bin/git" <<EOF
#!/usr/bin/env bash
echo "\$*" >> "$d/git.log"
[ "\$*" = "rev-parse HEAD" ] && echo $ORIG
exit 0
EOF
  cat > "$d/bin/docker" <<EOF
#!/usr/bin/env bash
echo "TAG=\${TAG-} \$*" >> "$d/docker.log"
[[ -n "\${FAIL_MIGRATOR-}" && "\$*" == *"run --rm migrator"* ]] && exit 1
exit 0
EOF
  printf '#!/usr/bin/env bash\nexit "${CURL_EXIT:-0}"\n' > "$d/bin/curl"
  touch "$d/git.log" "$d/docker.log"
  chmod +x "$d/bin/"* "$d/scripts/backup.sh"
  code=0
  env PATH="$d/bin:$PATH" "$@" bash "$d/deploy.sh" "$NEW" >/dev/null 2>&1 || code=$?
}

# check <case> <description> <command...>: one FAIL line per broken expectation
check() { local c="$1" what="$2"; shift 2; "$@" || { echo "FAIL: $c: $what"; bad=1; }; }
last_checkout() { grep '^checkout' "$d/git.log" | tail -n 1 | grep -q "$1"; }
report() { if [ "$bad" = 0 ]; then echo "ok: $1"; else failed=1; fi; }

bad=0; run_case rollback CURL_EXIT=22
check rollback "exit $code, want 1" test "$code" = 1
check rollback "TAG in .env is not the previous SHA" grep -qx "TAG=$PREV" "$d/.env"
check rollback "last checkout is not the previous SHA" last_checkout "$PREV"
check rollback "last up -d is not on the previous TAG" sh -c "grep 'up -d' '$d/docker.log' | tail -n 1 | grep -q '^TAG=$PREV '"
report rollback

bad=0; run_case deploy
check deploy "exit $code, want 0" test "$code" = 0
check deploy "TAG in .env is not the new SHA" grep -qx "TAG=$NEW" "$d/.env"
report deploy

bad=0; run_case "migrator failed" FAIL_MIGRATOR=1
check "migrator failed" "exit $code, want 1" test "$code" = 1
check "migrator failed" "TAG in .env changed" grep -qx "TAG=$PREV" "$d/.env"
check "migrator failed" "up -d was called" sh -c "! grep -q 'up -d' '$d/docker.log'"
check "migrator failed" "last checkout is not the original commit" last_checkout "$ORIG"
report "migrator failed"

exit "$failed"
