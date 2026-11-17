---
phase: 01
review: 01-REVIEW.md
---

# Phase 01 — review disposition

| ID | Severity | Disposition | Note |
|----|----------|-------------|------|
| CR-01 | critical | fixed | Rate limiter partitioned by token validity (owner / anon); test `JunkTraffic_DoesNotStarveOwnerRateLimit` |
| WR-01 | warning | fixed | Weekly sum moved into the guarded block after SaveChanges: a failure there still returns 201 |
| WR-02 | warning | fixed | U+2212 / U+2013 normalised to `-`, so refunds are rejected; tests added |
| WR-03 | warning | fixed | Currency allow-list (`zł`, `PLN`, none) instead of block-list; `Kč`, `CHF` tests added |
| WR-04 | warning | fixed | `timeout-minutes: 20` on the CI job |
| IN-01 | info | deferred | Telegram 4096 limit on merchant: Phase 3 (parser/messages hardening) |
| IN-02 | info | deferred | Plausibility of occurred_at: Phase 3 (ING-09 parser corpus) |
| IN-03 | info | deferred | DB readiness check: Phase 6 (observability) |
| IN-04 | info | accepted | Test budget sharing: owner partition 30/min is enough for the suite |
| IN-05 | info | deferred | Docker build in CI: Phase 6 (GHCR pipeline) |
| IN-06 | info | accepted | pg_isready on first init: app restarts until DB is up |
| IN-07 | info | deferred | Log privacy on the 500 path: Phase 6 |
