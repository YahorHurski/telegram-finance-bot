# Project Research Summary

**Project:** Finance Bot (multi-user Telegram expense tracker fed by Apple Pay through iOS Shortcuts)
**Domain:** Personal finance / Telegram bot + ASP.NET Core ingestion API (Poland, PLN, Europe/Warsaw, Russian UI)
**Researched:** 2026-11-14
**Confidence:** MEDIUM-HIGH. The stack and architecture confidence is HIGH. iOS Shortcut payload behaviour is LOW-MEDIUM because Apple publishes no documentation for it.

## Executive Summary

The product is a modular-monolith ASP.NET Core service with two inbound adapters: an HTTPS ingestion endpoint called by the iOS "Transaction"/"Wallet" automation, and the Telegram webhook. It has one outbound adapter, Telegram pushes. Practitioners build this kind of system ledger-first. Every number the user sees (remaining weekly limit, rollover, balance) is derived from an immutable expense ledger plus settings history and is never stored as a mutable counter. Server-initiated messages go through a transactional outbox, and ingestion is idempotent in two layers. Telegram and the Shortcut are thin clients over Application use cases, which keeps a v2 web dashboard or Open Banking source cheap to add and makes a strong portfolio story.

Recommended approach: .NET 10 LTS, minimal APIs, Telegram.Bot 22.x, PostgreSQL 18 with EF Core 10 and Npgsql 10. Pin EF to 10.x because Npgsql rejects EF 11. Use NodaTime for calendar logic, ScottPlot with a bundled Cyrillic font for charts, and Docker Compose with Caddy. Research recommended a ~EUR 6/month Hetzner VPS; the user decided on a free Oracle Cloud Always Free VM (Pay As You Go account, encrypted backups outside Oracle), with Hetzner as the fallback. Avoid free tiers that sleep, since a cold start loses Apple Pay events. Use five projects (Domain, Application, Infrastructure, Telegram, Api), plain handler classes with no MediatR or AutoMapper, xUnit v3 + Testcontainers, and an EF migration bundle as a one-shot migrator service.

The dominant risk is the data source, not the code. The Shortcut payload has no transaction ID and no category, and its amount is a locale-formatted string such as `1 234,56 zł` with NBSP separators. The trigger fires on declined taps, times out silently, resets to "Ask Before Running" after iOS updates, and never fires for online or in-app Apple Pay, BLIK or cash. Mitigations:
- An on-device payload spike before the parser is designed.
- Manual entry built as a first-class input.
- The bot confirmation treated as the user's receipt.
- A Shortcut-side "not saved" notification.
- An ingestion-inactivity nudge.
- Visible (never silent) duplicate handling.

The secondary risks are DST and week-boundary math, rollover semantics, callback-data IDOR, and Cyrillic charts in Linux containers.

## Key Findings

### Recommended Stack

One ASP.NET Core 10 process hosts the ingestion API, the Telegram webhook, health checks and background workers. All versions were verified against NuGet on 2026-11-14 (see STACK.md).

**Core technologies:**
- **.NET 10 LTS (10.0.15):** .NET 11 STS has the same end-of-support date and its ecosystem is not ready yet (Npgsql EF 11 is still RC).
- **ASP.NET Core minimal APIs:** route groups, a custom `ApiToken` AuthenticationHandler, built-in rate limiting and validation.
- **Telegram.Bot 22.10.4:** webhook in production, long polling in development, one shared update handler, separate dev and prod bot tokens. Use the v22 API (`SendMessage`), not old tutorials.
- **PostgreSQL 18 + EF Core 10.0.15 + Npgsql 10.0.3 + EFCore.NamingConventions:** manage versions centrally and add a Dependabot ignore rule for `>= 11`.
- **NodaTime 3.3.7 + Npgsql NodaTime plugin:** `Instant` maps to `timestamptz` and `LocalDate` to `date`. It ships its own TZDB, so it does not depend on tzdata in the image.
- **ScottPlot 5.1.59:** brings SkiaSharp 3.119 transitively. Do not reference SkiaSharp 4.x directly. Bundle Noto Sans or Inter for Cyrillic.
- **Image `aspnet:10.0-noble-chiseled-extra`:** plain chiseled has no ICU, which breaks `ru-RU` formatting. Verify ScottPlot native loading with an in-image smoke test and fall back to `aspnet:10.0-noble` if it fails.
- **Tests:** xUnit v3 4.0.3 on MTP v2, Testcontainers.PostgreSql, Respawn, WebApplicationFactory 10.0.15, NSubstitute, WireMock.Net as a fake Bot API, `FakeTimeProvider` and NodaTime `FakeClock`. Never use EF InMemory or SQLite.
- **Deploy and ops:** Compose (app + migrator + postgres + caddy), GHCR, GitHub Actions SSH deploy, Serilog compact JSON, OpenTelemetry (off by default), nightly off-box `pg_dump`, an external uptime pinger, gitleaks from the first commit.

### Resolved Conflicts Between Researchers

**1. Time library: NodaTime (decision).** STACK.md and PITFALLS.md recommend NodaTime. ARCHITECTURE.md accepts BCL `DateOnly`/`TimeZoneInfo` + `TimeProvider` and says "do not mix". The choice is NodaTime for all domain and calendar logic, for these reasons:
- It is independent of image tzdata, which removes a production-only failure mode on chiseled images.
- It has first-class `LocalDate`, `IsoDayOfWeek` and ISO week rules.
- Npgsql's own docs recommend it.
- Week math and DST are the highest-risk domain logic here.

`TimeProvider` stays only as the infrastructure tick source for `PeriodicTimer` in BackgroundServices. The domain sees an injected NodaTime `IClock` (`FakeClock` in tests). The `WeekPeriod` value object from ARCHITECTURE.md is kept but written over NodaTime (`LocalDate Monday`, UTC range via `DateTimeZone.AtStartOfDay`). PITFALLS.md adds a refinement: store a computed `local_date` column (purchase date in the user's zone) on each transaction, so that week membership becomes `local_date BETWEEN monday AND sunday`.

**2. Scheduling and delivery: one design, the outbox is the delivery record.** STACK.md proposes a BackgroundService catch-up dispatcher with a `summary_dispatch` row. ARCHITECTURE.md proposes an `outbox_messages` table with a unique `dedupe_key`. PITFALLS.md proposes a `ReportDeliveries` claim table. These merge into one design:
- **`SummaryScheduler`** (BackgroundService, `PeriodicTimer` about 60 s) decides when a summary is due: Sunday 20:00 or the 1st at 09:00, in the user's local time via NodaTime. It enqueues an outbox row with a period key (`summary:week:{user}:{monday}`, `summary:month:{user}:{yyyy-MM}`) using `ON CONFLICT DO NOTHING`. The unique `dedupe_key` is the "already sent" marker, so no separate dispatch table is needed. Because the rule is "due when local time ≥ 20:00 and no row exists", catch-up after a restart is automatic.
- **`OutboxDispatcher`** (BackgroundService) sends rows with `FOR UPDATE SKIP LOCKED`. A `Channel` signal after commit wakes it immediately. It uses backoff, honours 429 `retry_after`, throttles with a token bucket (about 20 msg/s), and sets `bot_blocked_at` on a 403. Reports are rendered at send time.
- The same outbox carries ingest confirmations (the Shortcut gets a fast 200 regardless of Telegram's state) and threshold alerts (`alert:{user}:{week}:{threshold}`).
- Direct replies to a user's own command or button stay inline in the webhook request and do not use the outbox.
- No Quartz or Hangfire in v1. Quartz 4.x with a Postgres store is the escalation path when Open Banking polling arrives.

**3. Migrations: bundle.** PITFALLS.md accepts runtime `MigrateAsync()` for a single-instance MVP. The decision is to ship an EF migration bundle as a one-shot migrator from the start (STACK.md), with `has-pending-model-changes` checked in CI. It costs little and is the stronger portfolio signal.

### Expected Features

**Must have (table stakes, v1):**
- `/start` registration with a hashed per-user token, `/token` rotation, full tenant isolation (T1, T4, T23)
- An iCloud Shortcut template using Import Questions (no embedded token), a Russian setup guide, and a "test payment" verification (T2, T3)
- Idempotent ingestion with robust locale-aware amount parsing and a raw payload log (T5, T6)
- Manual entry `45 такси` / `такси 45` / `320 куртка !` / `+5000 зп`, going through the same `RecordExpense` operation as ingestion (T7)
- A confirmation message with the remaining weekly limit, plus inline buttons (category, in/out of limit, edit, delete) edited in place, and delete with undo (T8–T10)
- Seed categories and Polish merchant rules with merchant normalization, "Другое" plus a category prompt, learning that offers "remember for this merchant?", and `/other` (T11–T14)
- A Mon–Sun Europe/Warsaw weekly limit (default 200 zł), Reset/Carry rollover computed by derivation, the `!` out-of-limit flag, and 80%/100% alerts that fire once per threshold per week (T15–T18)
- `/week` and `/month` with a server-rendered PNG chart, and scheduled summaries (T19–T21)
- `/balance N` anchor and income entries (T22); `/help` with `setMyCommands` (T24)
- Cheap protections for the core value: ingestion-inactivity nudge (D2), partial-payload rescue when the amount is missing (D3), a foreign-currency PLN prompt (D4), and remaining amount with carry shown in every confirmation (D6)
- Proportionate GDPR measures once other people use the bot: `/privacy`, `/deleteme`, no merchants or amounts in logs, and an invite code or allowlist

**Should have (v1.x):**
- Weekly pace hint (D7), `/history` with filters and date tokens (`вчера`) (T25), CSV `/export` (D10), an NBP exchange-rate suggestion, a Shortcut offline queue, rich-message tables (D11)

**Defer (v2+):**
- Open Banking reconciliation. Enable Banking is free only in restricted personal mode (owner's linked accounts only), and GoCardless has closed new signups. Keep `Source` and `ExternalId` columns from v1.
- Web dashboard or Mini App, LLM fallback categorization, Wallet-category hint automations, shared or family budgets

**Anti-features:**
- Silently dropping duplicates
- Confirm-before-delete dialogs
- Snapshotting weekly balances
- Token in the URL query
- MarkdownV2 (use HTML parse mode)
- Daily limit
- Full multi-currency support

### Architecture Approach

The system is a pragmatic Clean Architecture monolith with feature folders in Application. Domain has zero packages beyond NodaTime, which is an accepted, deliberate exception. Application uses `IAppDbContext` directly, with no generic repositories. ArchitectureTests enforce the layering in CI.

**Major components:**
1. **Ingestion API:** ApiToken auth (SHA-256 lookup), per-token rate limit, 16 KB body limit, two-layer idempotency, `ingest_log`.
2. **Telegram webhook + UpdateRouter:** a secret-header check with `FixedTimeEquals`, `update_id` dedupe (`processed_updates`), private chats only, command/callback/text handlers, `dialog_state` stored in Postgres, a versioned callback codec of 64 bytes or less, and a catch-all that still returns 200.
3. **Domain:** `Money` (decimal / `numeric(12,2)`), `AmountParser`, `ManualEntryParser`, `MerchantNormalizer`, `CategoryResolver` (user rule, then global rule, then "Другое"), `WeekPeriod`, `BudgetCalculator` (a pure fold over weeks), `ThresholdDetector` (before/after comparison), `BalanceCalculator`.
4. **Persistence:** EF Core 10 named query filters (`"Tenant"` + `"SoftDelete"`) that read a context member, a SaveChanges interceptor that stamps UserId, an `xmin` concurrency token, and `UNIQUE (user_id, idempotency_key)`.
5. **Outbox + OutboxDispatcher, and SummaryScheduler:** see the resolved design above.
6. **ChartRenderer:** behind the `IChartRenderer` port, using ScottPlot, rendered to a PNG `byte[]`. If rendering fails, reports degrade to text only.

**Key patterns:**
- **Two-layer idempotency.** Layer 1 is a client nonce (`Idempotency-Key`, built in the Shortcut from date plus a random number, since Shortcuts has no UUID action). A replay returns the original 200, and a mismatched hash returns 422. Layer 2 is a fingerprint window under a per-user advisory lock: the same amount, normalized merchant and card within about 120 s are recorded as `SuppressedDuplicate` with a restore button. Never use a deterministic hash of amount, merchant and minute.
- **Derived rollover.** `effective(w) = limit(w) + carry_in(w)`. Limit and mode are snapshotted per week (`budget_weeks`, or a settings history with effective-from), computed on read, so past-week edits stay consistent.
- **Alerts** come from a before/after comparison inside the same transaction, using integer comparison (`spent*100 >= effective*t`) and outbox dedupe keys.

### Critical Pitfalls

1. **Amount parsing and locale:** send Amount as Text, parse server-side (NBSP and U+202F separators, `zł`/`PLN`/`€`, the last `,` or `.` as decimal separator), store `RawAmount`, return 422 on failure, never store 0. Build the test table from real spike payloads.
2. **Duplicates without a source ID:** client nonce plus a visible soft-duplicate window. Run a concurrency test (10 parallel posts with the same key must produce 1 row). Handle unique violations as 200, not 500.
3. **The automation silently not firing:** manual entry parity, a bot receipt for every ingest, a Shortcut that checks `{ok:true}` and otherwise shows a local "not saved" notification (Shortcuts treats HTTP 4xx/5xx as success), an inactivity nudge, and onboarding text that names known gaps (online Apple Pay, BLIK, iOS updates resetting "Run Immediately").
4. **Time and DST:** NodaTime, a `local_date` column, the user's timezone column, no `DateTime.Now`, never `EnableLegacyTimestampBehavior`. Accept the Shortcut date as ISO 8601 with offset and clamp it to `received_at` if it is in the future or more than 48 h old. Test the DST weekends (2026-03-29 and 2026-10-25), Sunday 23:59 / Monday 00:00, and ISO week 53.
5. **Rollover, history and alert semantics:** treat the ledger as the source of truth, keep a settings history, write the rules spec before coding, and make sure a 75% to 105% jump sends the 100% alert.
6. **Telegram security and robustness:** webhook `secret_token`, callback ownership through tenant filters (IDOR test with user B pressing user A's buttons), the 64-byte callback limit (byte-length unit test), always `answerCallbackQuery`, treat "message is not modified" as success, HTML parse mode with HtmlEncode and a plain-text fallback, `long` Telegram IDs, groups disabled.
7. **Charts in Docker:** bundled font, in-image Cyrillic render smoke test in CI, no `System.Drawing`.
8. **Secrets and tokens:** token only in the `Authorization` header with redaction in logs, a template Shortcut with no secret (verify from a second Apple ID), gitleaks before the first commit.

## Open Product Decisions (the user must decide during requirements)

These are product choices, not technical ones. Research gives a recommendation for each, but the user should confirm.

| Decision | Options | Research recommendation |
|----------|---------|-------------------------|
| **Rollover cap** | No cap / cap carry at ±1 week's limit / cap only surplus | Optional cap at ±1× weekly limit, as a documented setting. This prevents the overspend spiral and the +400 zł after idle weeks. |
| **Mode-switch / limit-change effective week** | Current week / next Monday | Takes effect **next Monday**, recorded as settings history with effective-from. History is never rewritten. (PITFALLS suggests limit changes apply from the current week; confirm which.) |
| **Refunds handling** | Ignore (manual delete) / `+` income / dedicated `Refund` type reducing week spend | The trigger doesn't capture refunds. Minimal v1: delete or edit the original expense, or a manual refund entry that reduces the spend of the current week (or the original purchase's week if linked). Never record it as a positive expense. Verify on device whether refunds fire the trigger at all. |
| **Duplicate handling UX** | Silent drop / record as `SuppressedDuplicate` (excluded) + "это отдельная покупка" restore / record as active + "похоже на дубль, удалить?" | **SuppressedDuplicate by default, with a restore button**, which matches "без дублей" and still loses nothing. Window about 120 s, configurable. Declined-then-approved taps are the main case. |
| First (partial) week after registration | Full limit / pro-rated / limits start next Monday | Pro-rate or start next Monday; explain it in `/start`. |
| Alert re-arm after a deletion drops below threshold | Re-arm / never re-arm in the same week | No re-arm within the week (follows naturally from dedupe keys). |
| Negative effective limit (after carried overspend) | Alert at 100% immediately / silent | Show "неделя начата с −X" in the confirmation and the Monday state. |
| Foreign-currency payments | Reject / store original + ask for PLN amount / NBP estimate | Store original amount and currency, ask for PLN in the confirmation, keep it out of the limit until provided. NBP estimate in v1.x. |
| Alerts as separate messages vs folded into the confirmation | | Fold into the confirmation as a bold line to avoid notification spam. |
| Empty-week summaries | Send / skip / short version | Send a short version. |
| Registration openness | Open / invite code / allowlist | Invite code or allowlist for v1 (GDPR scope, abuse). |

## Implications for Roadmap

Research supports 9 phases. Phases 1–3 are the critical path to the core value. Phases 4–6 deepen the domain and can be reordered. Reports depend on finalized categories and limits, and the scheduler depends on reports.

### Phase 0 (or the start of Phase 1): On-device Shortcut payload spike (REQUIRED)
**Rationale:** Every parser, idempotency and onboarding decision rests on undocumented Apple behaviour.
**Delivers:** A throwaway HTTPS endpoint (a Cloudflare quick tunnel is enough) that logs raw bodies, plus a captured corpus from the user's iPhone (PKO BP, mBank and Millennium cards; PL region and RU language), covering:
- amounts ≥ 1 000 zł and < 1 zł
- the `Current Date` ISO formatting
- empty merchant / 0 amount
- a declined tap
- a foreign-currency payment
- a refund
- Apple Watch
- airplane mode
- "Get Contents of URL" behaviour on 4xx/5xx
- whether Import Questions can carry the token
- iOS 26 "Wallet" naming

**Avoids:** Pitfalls 1, 2, 3, 9.

### Phase 1: Foundation + deployed walking skeleton
**Rationale:** Telegram webhooks and the real Shortcut both need public HTTPS. Early deployment surfaces image, ICU, TLS and secrets problems while they are still cheap.
**Delivers:**
- Solution with 5 projects plus ArchitectureTests, Central Package Management with pins, `global.json`, `.gitignore` and gitleaks before the first commit
- Docker Compose (app, migrator bundle, postgres, caddy) on a VPS with a domain, `/health`, an uptime pinger
- GitHub Actions build/test/publish/deploy, Testcontainers harness, Serilog
- Money and NodaTime primitives with a DST test suite
- A chart-in-Docker smoke test (Cyrillic), which decides between chiseled-extra and noble

**Avoids:** Pitfalls 5, 6, 12, secrets in the repo, migration footguns.

### Phase 2: Identity, Telegram channel, tenant isolation
**Delivers:**
- `users`, `api_tokens` (SHA-256, one active token), `/start`, `/token`, invite gate
- Webhook with secret filter and `update_id` dedupe, UpdateRouter, dev polling with a separate bot
- `ICurrentUser`, named tenant filters, stamping interceptor, isolation test suite
- HTML message builder, `setMyCommands`, private chats only

**Avoids:** Pitfalls 8, 14, 15, 4 (partially).

### Phase 3: Expense ingestion + outbox (core value)
**Delivers:**
- `AmountParser` built from the spike corpus, `MerchantNormalizer`, `WeekPeriod`, Reset-mode `BudgetCalculator`
- Ingestion endpoint with ApiToken auth, rate limit, `ingest_log` and two-layer idempotency
- Partial-payload and foreign-currency handling
- Manual entry grammar through the shared `RecordExpense` operation
- Outbox + dispatcher, confirmation message with the remaining limit
- Seed categories and global rules
- The Shortcut template (Import Questions, response check plus "not saved" notification, `shortcutVersion`) exported to `shortcut/`, the Russian setup guide, the test-ping verification

**Features:** T2, T3, T5–T8, T11 (seed), D3, D4, D6.
**Avoids:** Pitfalls 1, 2, 3, 4, 9.

### Phase 4: Interactive buttons + categorization learning
**Delivers:**
- Versioned callback codec of 64 bytes or less
- Change category, in/out-of-limit toggle, edit amount/date (`dialog_state`), soft delete with undo, restore suppressed duplicates
- Edit-in-place
- "Другое" prompt, "remember for this merchant?" with optional retroactive apply, `/other`

**Features:** T9, T10, T12–T14. **Avoids:** Pitfalls 10, 11.

### Phase 5: Budget rules + alerts (spec first)
**Delivers:**
- `/limit` settings with history, Carry mode fold, `!` exclusion semantics
- ThresholdDetector with 80%/100% outbox alerts
- Concurrency tests

**Prerequisite:** the written rules spec answering the open decisions above.
**Features:** T15–T18. **Avoids:** Pitfalls 6, 7.

### Phase 6: Balance + incomes
**Delivers:** `balance_entries` (Set anchor + Income), `/balance N`, `+5000 зп`, balance in confirmations. Small and independent; can run in parallel with Phase 5.
**Features:** T22.

### Phase 7: Reports + charts
**Delivers:** Week and month report queries, an out-of-limit section, month-over-month comparison, ScottPlot donut or horizontal bar charts with a "Прочее" tail, text message plus photo with a short caption (≤1024 chars), `/week`, `/month`, text-only fallback.
**Features:** T19, T20.

### Phase 8: Scheduler + automatic summaries + ingestion health
**Delivers:** SummaryScheduler (resolved design), Sunday 20:00 and 1st 09:00 in the user's local time, staggered fan-out, 403 handling, `FakeClock`/`FakeTimeProvider` tests across DST and restart-at-send-time, the inactivity nudge (D2), retention jobs (`processed_updates`, `ingest_log`).
**Features:** T21, D2. **Avoids:** Pitfall 13.

### Phase 9: Hardening, GDPR & portfolio launch
**Delivers:** `/privacy`, `/deleteme` (built cheaply alongside the bot core if preferred), log-redaction audit, a backup restore tested once, README with a C4 diagram and ADRs (NodaTime, outbox, no MediatR, two-layer idempotency), a demo GIF with seeded fake data, onboarding of other users.
**Avoids:** Pitfall 16 and token leakage.

### Phase Ordering Rationale
- The spike comes before parser and idempotency design, because guessing the payload format is the most expensive mistake available.
- Identity and isolation come before any user data exists.
- One `RecordExpense` operation shared by the Shortcut and manual entry keeps confirmation, alert and button behaviour identical across sources.
- The limit and rollover semantics must be final before alerts (Phase 5) and before reports (Phase 7), or both need rework.
- The scheduler reuses the report builders and the outbox, so it comes last among the features.

### Research Flags

Phases likely needing `/gsd-plan-phase --research-phase`:
- **Phase 0/3 (Ingestion):** on-device validation is mandatory. All Shortcut behaviour is community-sourced (LOW-MEDIUM).
- **Phase 5 (Budget rules):** needs a discuss/spec step for the product decisions rather than technical research.
- **Phase 1 (image choice):** a short spike on ScottPlot native assets in chiseled-extra and on ARM64 if a Hetzner CAX is used.

Phases with standard patterns (skip research): Phase 2 (Telegram.Bot webhook, EF filters), Phase 4, Phase 6, Phase 8 (the design is settled), Phase 9.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | Versions and nuspec constraints verified on NuGet. Hosting prices MEDIUM. Chiseled-extra plus Skia is unverified first-hand. |
| Features | MEDIUM | Telegram limits come from the official spec. Shortcut fields and behaviour come from third-party guides and forums. Competitor patterns MEDIUM. |
| Architecture | MEDIUM-HIGH | Well-established patterns (outbox, tenant filters, derived ledger). The idempotency design depends on payload facts. |
| Pitfalls | MEDIUM-HIGH | Telegram, EF and Npgsql pitfalls are HIGH. iOS trigger failure modes are MEDIUM/LOW. The GDPR section is engineering guidance, not legal advice. |

**Overall confidence:** MEDIUM-HIGH. The engineering is well understood; the input channel is not.

### Gaps to Address

- **Shortcut payload format and behaviour:** resolve in the Phase 0 spike (see the list above).
- **Refunds and pre-authorizations:** unknown whether they trigger at all. Decide the policy, use the Edit button as the escape hatch.
- **Trigger card scope:** whether newly added Wallet cards are covered. Verify on device and document in `/help`.
- **Rollover and alert rule details:** product decisions, see the table above.
- **Telegram.Bot support for rich messages (Bot API 10.2+):** check before using them in v1.x.
- **Live Hetzner pricing and availability (CX23 vs CAX11):** check in the console.

## Sources

### Primary (HIGH confidence)
- NuGet registry API (versions, nuspec dependency ranges), the .NET support policy, Docker Hub postgres tags, Caddy and GitHub Actions releases
- Telegram Bot API 10.4 spec and Bots FAQ: webhook retries, `secret_token`, callback_data 1–64 bytes, rate limits, caption 1024, deleteMessage 48 h, 52-bit IDs
- Telegram.Bot book and v22 migration notes; ScottPlot docs (Context7); xunit.net v3 4.0 release notes
- Microsoft Learn: EF Core migrations and bundles, named query filters, chiseled/distroless images; Npgsql date/time docs

### Secondary (MEDIUM confidence)
- CashJot, Actuali, Moneyfetch, Matthew Cassinelli guides on the Transaction/Wallet trigger
- Copilot, Monarch and Actual Budget help docs on rollover, exclusion and rules
- Enable Banking docs, ToS and changelog; GoCardless signup closure
- Quartz.NET 4.0 release notes; Hetzner price-change reviews; MediatR/AutoMapper licensing

### Tertiary (LOW confidence)
- Apple Developer Forums 765516, 773745, 758053, 797233 and r/shortcuts threads (timeouts, declined-payment fires, empty payloads, decimal loss)
- Oracle Always Free reclamation reports; Salt Edge, Kontomatik and open-banking.io; inferred Polish merchant descriptor formats

---
*Research completed: 2026-11-14*
*Ready for roadmap: yes*
