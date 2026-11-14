# Architecture Research

**Domain:** Multi-user personal expense tracker. ASP.NET Core API + PostgreSQL. Two inbound channels: an iOS Shortcut that POSTs Apple Pay transactions, and the Telegram Bot webhook. One outbound channel: Telegram Bot API pushes.
**Researched:** 2026-11-14
**Confidence:** MEDIUM-HIGH overall. Architecture patterns are well-established. The Telegram and EF Core facts were checked against official docs. The iOS Shortcut payload behaviour is LOW-MEDIUM: the evidence comes from community and forum reports and has to be validated against real payloads in the ingestion phase.

---

## Standard Architecture

### System Overview

```
           ┌──────────────────────┐                 ┌──────────────────────┐
           │ iOS Shortcut         │                 │ Telegram servers     │
           │ (Wallet/Transaction  │                 │ (webhook push +      │
           │  automation)         │                 │  Bot API for sends)  │
           └──────────┬───────────┘                 └───────┬──────▲───────┘
   HTTPS POST         │ Authorization: Bearer <token>       │      │ sendMessage /
   /api/v1/transactions │ Idempotency-Key: <nonce>          │      │ editMessageText /
                      │          POST /telegram/webhook     │      │ sendPhoto
                      │          X-Telegram-Bot-Api-Secret-Token   │
┌─────────────────────┼─────────────────────────────────────┼──────┼───────────────┐
│ FinanceBot.Api (host / composition root)                  │      │               │
│  ┌──────────────────▼──────────┐   ┌──────────────────────▼──┐   │               │
│  │ Ingestion endpoint          │   │ Webhook endpoint        │   │               │
│  │ ApiToken auth scheme,       │   │ secret-header filter,   │   │               │
│  │ rate limiter, payload parse │   │ update_id dedupe        │   │               │
│  └──────────────┬──────────────┘   └───────────┬─────────────┘   │               │
│                 │                 ┌────────────▼──────────────┐  │               │
│                 │                 │ FinanceBot.Telegram       │  │               │
│                 │                 │  UpdateRouter → Command / │  │               │
│                 │                 │  Callback / Text handlers │──┘ direct replies│
│                 │                 │  Formatters (RU), Keyboards│                 │
│                 │                 └────────────┬──────────────┘                  │
├─────────────────┴──────────────────────────────┴─────────────────────────────────┤
│ FinanceBot.Application  (use cases as feature slices, ports)                     │
│  Expenses: Ingest, AddManual, ChangeCategory, ToggleLimit, Edit, Delete          │
│  Budget: GetWeekStatus, UpdateSettings │ Balance: Set, AddIncome                 │
│  Reports: WeekReport, MonthReport      │ Users: Register, RotateToken            │
│  Ports: IAppDbContext, ICurrentUser, IUserNotifier(outbox), IChartRenderer       │
├──────────────────────────────────────────────────────────────────────────────────┤
│ FinanceBot.Domain  (pure C#, no packages)                                        │
│  Money, WeekPeriod, BudgetCalculator (Reset/Carry), ThresholdDetector,           │
│  MerchantNormalizer, CategoryResolver, AmountParser, entities + invariants       │
├──────────────────────────────────────────────────────────────────────────────────┤
│ FinanceBot.Infrastructure                                                        │
│  EF Core DbContext + tenant query filters + migrations │ TokenHasher             │
│  OutboxDispatcher (BackgroundService) ─────────────────────────► Telegram sender │
│  SummaryScheduler (BackgroundService, PeriodicTimer)   │ ChartRenderer (PNG)     │
└───────────────────────────────┬──────────────────────────────────────────────────┘
                                │ Npgsql
                       ┌────────▼────────┐
                       │ PostgreSQL      │
                       └─────────────────┘
```

**Shape:** a modular monolith in one deployable process, organised as pragmatic Clean Architecture with feature folders inside Application. Telegram and the Shortcut are clients. The domain does not know they exist. A later web dashboard or Open Banking source becomes another adapter calling the same Application use cases, which is the extensibility goal in PROJECT.md.

### Component Responsibilities

| Component | Owns | Does NOT own | Implementation |
|-----------|------|--------------|----------------|
| **Ingestion API** | HTTP contract for the Shortcut: auth, payload validation, idempotency header, mapping to the `IngestTransaction` command, HTTP status codes | Categorization, limit math, sending Telegram messages | Minimal API endpoint group `/api/v1`. Custom `AuthenticationHandler` ("ApiToken" scheme). `RateLimiter` partitioned per token. |
| **Telegram webhook endpoint** | Secret-header check, `update_id` dedupe, a fast 200 OK | Business decisions | Minimal API `POST /telegram/webhook` with an endpoint filter |
| **Telegram presentation (UpdateRouter)** | Mapping updates to use cases: `/start`, `/token`, `/week`, `/month`, `/other`, `/balance`, `/limit`, the manual-text grammar (`45 такси`, `320 куртка !`, `+5000 зп`), callback buttons, dialog state ("enter new amount"), Russian formatting, inline keyboards, callback-data encoding | Persistence and math | `FinanceBot.Telegram` project: `IUpdateHandler` implementations selected by a router |
| **Notifier adapter** | Implements `IUserNotifier`: turns domain notifications (`ExpenseRecorded`, `ThresholdCrossed`, `WeeklySummaryDue`) into Telegram messages | Deciding *when* to notify | Lives in `FinanceBot.Telegram`. Invoked by the OutboxDispatcher. |
| **Domain: Categorization** | Normalizing merchant strings and resolving a category: per-user rule, then global rule, then "Другое" | Asking the user (presentation does that) | Pure functions plus `MerchantRule` entity |
| **Domain: Budget** | Week boundaries (Mon–Sun, user TZ), effective limit with Reset/Carry rollover, spent-in-limit vs out-of-limit, threshold crossing (80/100%) | Querying the DB | `WeekPeriod`, `BudgetCalculator`, `ThresholdDetector` as pure, table-testable code |
| **Domain: Balance** | Balance = last `Set` anchor + incomes after it − all expenses after it | Bank balance (out of scope) | `BalanceCalculator` |
| **Reporting** | Aggregation queries into report models (`WeekReport`, `MonthReport`) | Text layout and chart pixels | Application query handlers (LINQ over `IAppDbContext`) |
| **Chart rendering** | Report model to PNG bytes | Data access | `IChartRenderer` port. Infrastructure implementation on ScottPlot/SkiaSharp (library choice belongs in STACK.md). |
| **Outbox + dispatcher** | Reliable, deduplicated delivery of server-initiated messages (ingest confirmations, alerts, summaries) after the DB commit | Request/response replies to a user's own command (those go direct) | `outbox_messages` table plus a `BackgroundService` polling with `FOR UPDATE SKIP LOCKED`, woken early by an in-process `Channel` signal |
| **Scheduler** | Deciding when weekly (Sun evening) and monthly (1st) summaries are due per user in the user's time zone. Enqueues outbox messages with a period dedupe key. | Sending | `BackgroundService` + `PeriodicTimer` (1 min tick) + `TimeProvider` |
| **Persistence** | Schema, migrations, tenant isolation filters, concurrency tokens, UTC conversion | Business rules | EF Core + Npgsql, `IEntityTypeConfiguration<T>` per entity |

---

## Recommended Project Structure

**Recommendation:** five `src` projects and four test projects. This is Clean Architecture with feature-sliced Application code. Do not use MediatR or AutoMapper; use plain handler classes registered in DI.

Reasons:
- Project references enforce the dependency rule at compile time, which reviewers notice. ArchitectureTests make the rule visible in CI.
- Feature folders (vertical slices) inside Application keep each use case in one place, so the codebase does not turn into `Services/` and `Repositories/` sprawl.
- MediatR v13+ and AutoMapper moved to a dual RPL/commercial license (Lucky Penny Software, July 2025). Plain handlers avoid the licensing question and the indirection. Explaining that in the README is a deliberate-decision signal.

```
finance-bot/
├── src/
│   ├── FinanceBot.Domain/                 # no NuGet deps
│   │   ├── Common/          Money.cs, Entity.cs, DomainException.cs
│   │   ├── Users/           User.cs, ApiToken.cs, UserSettings.cs, RolloverMode.cs
│   │   ├── Expenses/        Transaction.cs, TransactionSource.cs, TransactionStatus.cs
│   │   ├── Categorization/  Category.cs, MerchantRule.cs, MerchantNormalizer.cs, CategoryResolver.cs
│   │   ├── Budget/          WeekPeriod.cs, BudgetWeek.cs, BudgetCalculator.cs, ThresholdDetector.cs
│   │   ├── Balance/         BalanceEntry.cs, BalanceCalculator.cs
│   │   └── Parsing/         AmountParser.cs, ManualEntryParser.cs   # "45 такси", "+5000 зп", "!"
│   ├── FinanceBot.Application/            # refs Domain; EF Core abstractions only
│   │   ├── Abstractions/    IAppDbContext.cs, ICurrentUser.cs, IUserNotifier.cs, IChartRenderer.cs, ITokenGenerator.cs
│   │   ├── Users/           RegisterUser/, RotateToken/
│   │   ├── Expenses/        IngestTransaction/ (Command, Handler, Validator, Result), AddManualExpense/,
│   │   │                    ChangeCategory/, ToggleLimitExclusion/, EditExpense/, DeleteExpense/, ListUncategorized/
│   │   ├── Budget/          GetWeekStatus/, UpdateBudgetSettings/
│   │   ├── Balance/         SetBalance/, AddIncome/
│   │   ├── Reports/         WeekReport/, MonthReport/
│   │   └── Notifications/   Notification records (ExpenseRecorded, ThresholdCrossed, WeeklySummary...)
│   ├── FinanceBot.Infrastructure/         # refs Application
│   │   ├── Persistence/     AppDbContext.cs, Configurations/, Migrations/, Interceptors/ (UserId stamping, audit)
│   │   ├── Outbox/          OutboxMessage.cs, OutboxDispatcher.cs (BackgroundService)
│   │   ├── Scheduling/      SummaryScheduler.cs (BackgroundService)
│   │   ├── Security/        Sha256TokenHasher.cs, TokenGenerator.cs
│   │   └── Charts/          ScottPlotChartRenderer.cs
│   ├── FinanceBot.Telegram/               # refs Application (presentation + notifier adapter)
│   │   ├── Routing/         UpdateRouter.cs, IUpdateHandler.cs, CallbackData.cs (≤64-byte codec)
│   │   ├── Commands/        StartCommand.cs, TokenCommand.cs, WeekCommand.cs, MonthCommand.cs, OtherCommand.cs, BalanceCommand.cs, LimitCommand.cs
│   │   ├── Callbacks/       CategoryCallback.cs, LimitToggleCallback.cs, EditCallback.cs, DeleteCallback.cs, NotDuplicateCallback.cs
│   │   ├── Text/            ManualEntryHandler.cs, DialogStateHandler.cs
│   │   ├── Formatting/      MessageFormatter.cs (RU, HTML parse mode), Keyboards.cs
│   │   ├── Notifier/        TelegramUserNotifier.cs (implements IUserNotifier)
│   │   └── Hosting/         WebhookRegistrar.cs (IHostedService), PollingReceiver.cs (dev only)
│   └── FinanceBot.Api/                    # host; refs everything; composition root
│       ├── Program.cs
│       ├── Endpoints/       TransactionsEndpoints.cs, TelegramWebhookEndpoint.cs, HealthEndpoints.cs
│       ├── Auth/            ApiTokenAuthenticationHandler.cs, TelegramSecretFilter.cs
│       └── appsettings*.json
├── tests/
│   ├── FinanceBot.Domain.Tests/           # xUnit, pure: week math, rollover, parsing, categorization
│   ├── FinanceBot.Application.Tests/      # handlers against real Postgres (Testcontainers) or SQLite-free
│   ├── FinanceBot.IntegrationTests/       # WebApplicationFactory + Testcontainers.PostgreSql + fake ITelegramBotClient
│   └── FinanceBot.ArchitectureTests/      # NetArchTest/ArchUnitNET: Domain has no deps, Telegram does not touch Infrastructure, etc.
├── deploy/                                # docker-compose.yml, Caddyfile, .env.example
├── shortcut/                              # exported .shortcut + docs/screenshots of the iOS setup
└── docs/                                  # architecture diagram (C4 context + container), ADRs
```

### Structure Rationale

- **Domain has zero package references.** All money, week, rollover, parsing and categorization logic is pure, so it can be unit-tested exhaustively with table-driven cases. This logic holds the real business risk (DST weeks, rollover chains, `320 куртка !` parsing).
- **Application uses `IAppDbContext` (exposing `DbSet<T>`) instead of per-entity repositories.** This is a deliberate trade-off: Application takes a dependency on `Microsoft.EntityFrameworkCore` abstractions, and in return reports and queries stay as plain LINQ without a repository per query shape. A generic repository over EF is an anti-pattern at this size.
- **`FinanceBot.Telegram` is a separate project** so "Telegram is just a client" is visible in the solution structure. A future `FinanceBot.Web` sits beside it. Formatting and Russian strings live only here.
- **Api is a thin composition root.** Endpoints translate HTTP to commands and results to status codes.
- **ArchitectureTests** cost about 30 lines and turn the layering into an enforced, CI-visible rule.

---

## Architectural Patterns

### Pattern 1: Tenant-scoped DbContext with named global query filters (EF Core 10)

**What:** Every user-owned entity implements `IUserOwned { long UserId }`. The DbContext reads the current user id from a scoped `ICurrentUser`, and a named filter `"Tenant"` is applied to all `IUserOwned` types. A `SaveChanges` interceptor stamps `UserId` on inserts and throws if a modified entity's `UserId` differs from the current user.
**When:** Always, for `transactions`, `merchant_rules` (per-user part), `balance_entries`, `budget_weeks`, `user_settings`, `dialog_state`, `outbox_messages`.
**Trade-offs:** Cheap and automatic. The filter is bypassed by raw SQL, `IgnoreQueryFilters()`, and inserts, so it needs integration tests ("user B gets 404 on user A's transaction via callback button"). EF Core 10 named filters allow combining `"Tenant"` with `"SoftDelete"` and disabling only one of them. Postgres Row-Level Security is the defense-in-depth upgrade; it is not worth it for v1.

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> o, ICurrentUser user)
    : DbContext(o), IAppDbContext
{
    // MUST be a context member, not a captured local; otherwise the value is baked into the cached model.
    private long? CurrentUserId => user.UserId;

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Transaction>()
         .HasQueryFilter("Tenant", t => t.UserId == CurrentUserId)
         .HasQueryFilter("SoftDelete", t => t.Status != TransactionStatus.Deleted);
        // ...apply "Tenant" to every IUserOwned via a loop over b.Model.GetEntityTypes()
    }
}
```

`ICurrentUser` is set by three entry points:
1. **ApiToken auth handler:** claims to `UserId`.
2. **Telegram UpdateRouter:** `update.From.Id` to user lookup.
3. **Background jobs:** iterate users; for each, create a scope and set `ICurrentUser` explicitly, or use an `IgnoreQueryFilters(["Tenant"])` query with an explicit `Where(UserId == x)`.

When `UserId` is null (unauthenticated), filtered queries return nothing (fail closed). Do not use `DbContext` pooling with tenant state unless the state is reset per lease. Use a plain scoped `AddDbContext`.

### Pattern 2: Transactional outbox for server-initiated Telegram messages

**What:** Use cases never call the Telegram API. They call `IUserNotifier.Enqueue(notification, dedupeKey)`, which adds an `OutboxMessage` row to the same `SaveChanges` as the business change. `OutboxDispatcher` picks up unsent rows (`FOR UPDATE SKIP LOCKED`), sends them via `TelegramUserNotifier`, and marks them sent, retrying with backoff.
**When:** Ingest confirmations (the Shortcut must get a fast 200 whether or not Telegram is reachable), threshold alerts, weekly and monthly summaries.
**Not for:** direct replies to a user's command or button. Those are sent inline in the webhook request, because the user is waiting and the request is already the retry unit.
**Trade-offs:** About 150 lines of code, plus 0–1 s latency (removed by signalling a `Channel<bool>` after commit). In return there are no "ghost" messages for rolled-back transactions, nothing is lost when Telegram has a blip, and the unique `dedupe_key` makes the 80%/100% alert and the "weekly summary for 2026-W46" fire exactly once even across restarts. This is a strong portfolio talking point.

```csharp
// dedupe keys — unique index on outbox_messages(dedupe_key)
$"alert:{userId}:{week.Start:yyyy-MM-dd}:{threshold}"
$"summary:week:{userId}:{week.Start:yyyy-MM-dd}"
$"summary:month:{userId}:{month:yyyy-MM}"
$"expense:{transactionId}"
```

The insert uses `ON CONFLICT (dedupe_key) DO NOTHING`, done through a small raw-SQL helper or by catching `PostgresException` with SqlState 23505.

### Pattern 3: Two-layer idempotency for Shortcut ingestion

**Facts that shape the design** (community/forum sources, LOW-MEDIUM confidence; must be validated against real payloads):
- The Transaction automation (shown as the "Wallet" trigger on iOS 26) exposes **Amount, Merchant, Card or Pass, Name**. It exposes **no transaction ID and no transaction timestamp**, so the Shortcut has to use `Current Date`.
- Shortcuts has **no built-in UUID action**. It does have `Random Number`, `Current Date` + `Format Date`, and `Generate Hash`.
- Reported flakiness: the trigger sometimes delivers an empty merchant and amount 0.0, times out waiting for the Wallet record (iOS 18+), fires on **declined** payments, and duplicates appear when two automations or old shortcuts coexist.

Duplicates therefore come from two different sources, and each layer handles one:

| Source of duplicate | Same nonce? | Layer that catches it |
|--------------------|-------------|-----------------------|
| Network retry or a retry loop inside the Shortcut (`Repeat` + `If` on error) | Yes, the nonce is generated once per run | **Layer 1: Idempotency-Key** |
| Automation fired twice, two automations on one card, user re-ran it | No, each run makes a new nonce | **Layer 2: content fingerprint window** |

**Layer 1: client nonce (Stripe-style).** The Shortcut builds the `Idempotency-Key` header as `Format Date(Current Date, "yyyyMMddHHmmss") + "-" + Random Number(100000…999999)` **once, before** the HTTP action. The server stores `(user_id, idempotency_key)` under a unique partial index plus a `request_hash`.
- Same key and same hash: return the original result with `200` (flag `replayed: true`). Do not create a row and do not send another Telegram message.
- Same key and different hash: return `422` (key misuse).

**Layer 2: server-side fingerprint window.** Inside the ingest transaction, take a per-user advisory lock (`SELECT pg_advisory_xact_lock(@userId)`) so two concurrent requests cannot both pass the check. Then look for an `Active` Shortcut transaction with the same `amount`, same `merchant_normalized`, same `card_name`, and `occurred_at` within ±120 s (configurable). If one matches, insert the new row with `status = SuppressedDuplicate`, which excludes it from all totals, and enqueue a notice: "Похоже на повтор: 12,50 zł Żabka. [Это была отдельная покупка]". The button flips it to `Active`. Nothing is lost, and the default behaviour matches the core value ("без дублей").

**Why not hash `amount + merchant + timestamp` into the key:** the timestamp is the run time, not the purchase time, so two runs produce different seconds. Bucketing to the minute breaks at bucket edges (12:00:59 vs 12:01:01) and silently swallows genuine repeat purchases. A time *window* with an undo button is the correct model.

**Telegram side:** dedupe on `update_id` (sequential per bot, per Telegram docs). Insert into `processed_updates(update_id PK)` with `ON CONFLICT DO NOTHING` at the start of processing, skip if no row was inserted, and prune after 7 days.

### Pattern 4: Week as a value object, time as an injected dependency

**What:**
- Store every instant as UTC `timestamptz` (Npgsql 6+ only writes `DateTime` with `Kind=Utc`, or `DateTimeOffset` with offset 0, to `timestamptz`; anything else throws).
- Store `users.time_zone` (IANA id, default `Europe/Warsaw`).
- `WeekPeriod` is identified by its Monday `DateOnly` in the user's zone. Its UTC range is `[ToUtc(Monday 00:00 local), ToUtc(next Monday 00:00 local))`. Never compute it as `startUtc.AddDays(7)`: weeks containing a DST switch are 167 or 169 hours long.
- Inject `TimeProvider` everywhere (`FakeTimeProvider` in tests). Never use `DateTime.Now`.

```csharp
public readonly record struct WeekPeriod(DateOnly Monday)
{
    public static WeekPeriod Containing(DateTime utc, TimeZoneInfo tz)
    {
        var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, tz));
        int offset = ((int)local.DayOfWeek + 6) % 7;          // Mon=0 … Sun=6
        return new(local.AddDays(-offset));
    }
    public (DateTime FromUtc, DateTime ToUtc) UtcRange(TimeZoneInfo tz) =>
        (TimeZoneInfo.ConvertTimeToUtc(Monday.ToDateTime(TimeOnly.MinValue), tz),
         TimeZoneInfo.ConvertTimeToUtc(Monday.AddDays(7).ToDateTime(TimeOnly.MinValue), tz));
    public WeekPeriod Previous => new(Monday.AddDays(-7));
}
```

BCL vs NodaTime: Npgsql's docs recommend the NodaTime plugin as the better-designed option. For this project, BCL (`TimeProvider`, `DateOnly`, `TimeZoneInfo` with IANA ids, which .NET 6+ supports on Linux and on Windows via ICU) wrapped in `WeekPeriod` and one `IUserClock` is enough and has fewer moving parts. Choose NodaTime only if the stack researcher prefers it. Do not mix the two. **Docker note:** the runtime image must contain tzdata. Debian-based `aspnet` images include it; chiseled and Alpine images may need the `-extra` variant or the `tzdata` package (MEDIUM confidence, verify in the deploy phase).

### Pattern 5: Rollover as a pure fold over a gap-free week ledger

**What:**
- `budget_weeks(user_id, week_start, limit_amount, rollover_mode)` snapshots the limit and mode in effect for each week, so changing the limit today does not rewrite history.
- A row is created the first time a week is touched, and the scheduler's Monday 00:05 tick guarantees one row per active user per week, so the chain has no gaps.
- `BudgetCalculator` is a pure function. It folds weeks from the carry anchor, which is the most recent week where the mode switched to Carry:

```
carry_in(w)        = (mode(w-1) == Carry) ? effective(w-1) − spent_in_limit(w-1) : 0
effective(w)       = limit(w) + carry_in(w)          // may be ≤ 0 after overspend
remaining(w)       = effective(w) − spent_in_limit(w)
spent_in_limit(w)  = Σ amount where status=Active ∧ ¬excluded_from_limit ∧ occurred_at ∈ week(w)
```

Compute on read. Data volume is tiny (≈ 50 weeks/year × a few hundred rows), and on-read computation stays correct when the user edits or deletes a past-week transaction. A materialized `carry_in` column would go stale in that case. Add caching only if profiling ever asks for it.

**Open product questions to flag:**
- Should carry be capped? Two idle weeks in Carry mode add +400 zł.
- When effective is ≤ 0, alert immediately at "100%"?
- Does a mode switch take effect in the current week or the next one? Recommendation: next week.

### Pattern 6: Threshold alerts as before/after state comparison

Every command that changes week spending (ingest, manual add, delete, edit amount, toggle limit; a category change does not affect totals) computes `ratioBefore` and `ratioAfter` inside the same transaction. `ThresholdDetector.Crossed(before, after, [80, 100])` returns newly crossed thresholds. Each one enqueues an outbox alert with key `alert:{user}:{week}:{t}`. The dedupe key prevents repeat alerts when spending goes down and back up within the week. Compare in integers to avoid fractional issues: `spent * 100 >= effective * t`.

### Pattern 7: Command router + dialog state for the bot

- Commands (`/week`), callbacks (`cat:{txId}:{catId}`), and free text are dispatched by `UpdateRouter` to small `IUpdateHandler`s.
- Callback data is limited to 64 bytes, so encode GUIDs as 22-char base64url, or use bigint ids, plus a short prefix.
- Multi-step flows ("Изменить" asks "введите новую сумму") store `dialog_state(user_id PK, kind, payload jsonb, expires_at)` in Postgres rather than in memory, so they survive restarts and redeploys.
- Always `answerCallbackQuery`, or the button spinner hangs.
- Edit the original confirmation message in place (`editMessageText` using `transactions.confirmation_message_id`) after category or limit changes, so the chat stays clean.
- Support two update receivers feeding the same router: webhook (prod) and long polling (local dev, no public HTTPS needed). Select via config.

---

## Data Model

PostgreSQL, snake_case (`EFCore.NamingConventions`). Money is `numeric(12,2)`. Times are `timestamptz` (UTC). Week keys are `date`.

```
users
  id                 bigint PK (identity)
  telegram_user_id   bigint UNIQUE NOT NULL
  telegram_chat_id   bigint NOT NULL             -- private chat == user id, store anyway
  display_name       text
  time_zone          text NOT NULL DEFAULT 'Europe/Warsaw'
  bot_blocked_at     timestamptz NULL            -- set on 403 "bot was blocked"; dispatcher skips
  created_at         timestamptz NOT NULL

api_tokens                                         -- not tenant-filtered (auth lookup happens pre-tenant)
  id                 bigint PK
  user_id            bigint FK → users
  token_hash         bytea UNIQUE NOT NULL         -- SHA-256(token); token is 256-bit random
  token_prefix       text NOT NULL                 -- first 8 chars, for display ("fb_3kd9…")
  created_at, last_used_at, revoked_at timestamptz
  UNIQUE (user_id) WHERE revoked_at IS NULL        -- exactly one active token per user

user_settings                                      -- 1:1 users
  user_id            bigint PK FK
  weekly_limit       numeric(12,2) NOT NULL DEFAULT 200.00
  rollover_mode      smallint NOT NULL DEFAULT 0   -- 0 Reset, 1 Carry
  alert_thresholds   int[] NOT NULL DEFAULT '{80,100}'
  weekly_summary_enabled, monthly_summary_enabled boolean
  version            xid (xmin concurrency token)

budget_weeks                                       -- limit snapshot per week (Pattern 5)
  user_id            bigint, week_start date, PK (user_id, week_start)
  limit_amount       numeric(12,2) NOT NULL
  rollover_mode      smallint NOT NULL

categories
  id                 int PK
  user_id            bigint NULL                   -- NULL = system category (v1: system only)
  code               text NOT NULL                 -- 'groceries', 'transport', 'other'
  name               text NOT NULL                 -- 'Продукты'
  emoji              text, sort_order int, is_fallback boolean   -- exactly one fallback ('Другое')

merchant_rules
  id                 bigint PK
  user_id            bigint NULL                   -- NULL = global seed rule
  pattern            text NOT NULL                 -- normalized merchant, e.g. 'zabka'
  match_type         smallint NOT NULL             -- Exact | Prefix | Contains
  category_id        int FK
  source             smallint                      -- Seed | UserCorrection
  created_at, updated_at
  UNIQUE (user_id, pattern, match_type)  -- NULLS NOT DISTINCT (PG15+) for the global set

transactions
  id                 uuid PK (UUIDv7: time-ordered, safe to put in callback data)
  user_id            bigint NOT NULL FK
  amount             numeric(12,2) NOT NULL CHECK (amount > 0)
  currency           char(3) NOT NULL DEFAULT 'PLN'
  occurred_at        timestamptz NOT NULL          -- from payload (Shortcut "Current Date") or message date
  received_at        timestamptz NOT NULL
  source             smallint NOT NULL             -- Shortcut | Manual | (future) OpenBanking
  status             smallint NOT NULL             -- Active | SuppressedDuplicate | Deleted
  merchant_raw       text NULL                     -- exactly what arrived
  merchant_normalized text NULL                    -- lowercased, diacritics folded, noise stripped
  card_name          text NULL
  description        text NULL                     -- manual entry text ("такси")
  category_id        int NOT NULL FK
  category_source    smallint NOT NULL             -- GlobalRule | UserRule | Fallback | UserChoice
  excluded_from_limit boolean NOT NULL DEFAULT false
  idempotency_key    text NULL
  request_hash       bytea NULL
  duplicate_of_id    uuid NULL FK → transactions
  confirmation_message_id int NULL                 -- Telegram message to edit in place
  created_at, updated_at; version xid
  UNIQUE (user_id, idempotency_key) WHERE idempotency_key IS NOT NULL
  INDEX (user_id, occurred_at) INCLUDE (amount, category_id, excluded_from_limit, status)
  INDEX (user_id, merchant_normalized, occurred_at)          -- duplicate window + rule learning

balance_entries
  id                 bigint PK
  user_id            bigint FK
  kind               smallint NOT NULL             -- Set | Income
  amount             numeric(12,2) NOT NULL
  occurred_at        timestamptz NOT NULL
  note               text NULL                     -- 'зп'
  -- balance(now) = last Set.amount + Σ Income after it − Σ Active transactions after it
  -- (out-of-limit spending DOES reduce balance; only the weekly limit ignores it)

ingest_log (optional but recommended for the flaky Shortcut)
  id, user_id, received_at, raw_body jsonb, outcome smallint, error text   -- 30-day retention

outbox_messages
  id                 bigint PK
  user_id            bigint FK
  type               text NOT NULL                 -- 'ExpenseRecorded', 'ThresholdCrossed', ...
  payload            jsonb NOT NULL
  dedupe_key         text UNIQUE NOT NULL
  created_at, next_attempt_at, sent_at timestamptz; attempts int; last_error text
  INDEX (next_attempt_at) WHERE sent_at IS NULL

processed_updates
  update_id          bigint PK, received_at timestamptz     -- prune > 7 days

dialog_state
  user_id            bigint PK, kind text, payload jsonb, expires_at timestamptz
```

**Design notes:**
- **Expenses and incomes live in separate tables.** Incomes affect only balance, never limits or category reports. One signed-amount table would put `WHERE amount < 0` into every report query.
- **Soft delete via `status`**, so "Удалить" can offer "Отменить" and suppressed duplicates can be restored. A named `"SoftDelete"` query filter hides them by default.
- **Seed data** (system categories, global merchant rules for Żabka, Biedronka, Lidl, Orlen, Uber, Bolt, Rossmann, etc.) is applied via `HasData` or an idempotent seeder.
- **Rule learning:** when the user picks a category for a transaction, upsert `merchant_rules(user_id, merchant_normalized, Exact)`. Optionally also re-categorize that user's other `Fallback` transactions from the same merchant and tell them "обновлено ещё 3 траты".
- **Money:** `Money` is a `readonly record struct` over `decimal` (PLN only, scale ≤ 2 validated at the parse boundary, no floating point anywhere), mapped to `numeric(12,2)`. Minor units (`bigint` grosze) are an equally valid choice. `decimal` with `numeric` is exact in both C# and Postgres and keeps SQL readable, so it is the recommendation here.
- **Amount parsing is a trust boundary.** The Shortcut's Amount is a currency-typed value. Its text form is locale-dependent (e.g. `12,50 zł`, `PLN 12.50`, NBSP thousands separators). `AmountParser` must accept these forms. It must reject ≤ 0, empty, and non-PLN amounts: either reply "валюта не поддерживается" or store with a flag (decide in the ingestion phase). The raw body is logged to `ingest_log` so parser gaps can be fixed from real data.

---

## Data Flow

### Flow 1: Apple Pay → Shortcut → server → Telegram (core value)

```
iPhone pays ─► Wallet automation runs Shortcut
  Shortcut: key = date+random (once) ─► POST /api/v1/transactions
            { amount:"12,50 zł", merchant:"ZABKA Z1234", card:"mBank", occurredAt:"2026-11-14T12:03:11+01:00" }
            Authorization: Bearer fb_…   Idempotency-Key: 20261114120311-482913
                │
Api: RateLimiter ─► ApiToken auth (SHA-256 lookup, revoked? → 401) ─► ICurrentUser=U
                │
Application.IngestTransaction (one DB transaction):
  1. log raw body (ingest_log)
  2. Layer 1: idempotency_key exists? ─yes─► return original (200, replayed) ─► END (no message)
  3. AmountParser / MerchantNormalizer (domain)          invalid ─► 422 + log
  4. pg_advisory_xact_lock(U)
  5. Layer 2: same amount+merchant+card within ±120s? ─yes─► status=SuppressedDuplicate
  6. CategoryResolver: user rule ▸ global rule ▸ 'Другое'
  7. INSERT transaction; ensure budget_weeks row for week(occurred_at)
  8. BudgetCalculator before/after ─► ThresholdDetector ─► outbox alerts (dedupe keys)
  9. outbox: ExpenseRecorded / PossibleDuplicate
  COMMIT ─► signal dispatcher ─► 201 {id, category, remainingWeek}   (Shortcut may ignore body)
                │
OutboxDispatcher ─► TelegramUserNotifier ─► sendMessage:
  "−12,50 zł · Żabka · 🛒 Продукты\nОсталось на неделю: 87,50 из 200 zł"
  [Категория] [Не в лимит] [Изменить] [Удалить]   (+ category buttons if 'Другое')
  ─► store confirmation_message_id on transaction
```

Direction: Shortcut → Api → Application → Domain/DB. The DB then feeds the Outbox, which feeds Telegram. The Shortcut **never** waits on Telegram.

### Flow 2: Telegram message / button → bot reply

```
Telegram ─► POST /telegram/webhook
  TelegramSecretFilter: FixedTimeEquals(header, secret)?  no ─► 401
  processed_updates INSERT … ON CONFLICT DO NOTHING        dup ─► 200 (skip)
  UpdateRouter: resolve user by from.id (unknown & not /start ─► "нажмите /start")
     ├─ "/week"            ─► Reports.WeekReport ─► Formatter + ChartRenderer ─► sendPhoto (direct)
     ├─ "45 такси" / "!"   ─► ManualEntryParser ─► AddManualExpense ─► reply (direct) + outbox alerts
     ├─ "+5000 зп"         ─► AddIncome ─► reply
     ├─ callback cat:…     ─► ChangeCategory (tenant filter guarantees ownership) ─► learn rule
     │                        ─► editMessageText + answerCallbackQuery
     └─ text while dialog_state=EditAmount ─► EditExpense ─► reply, clear state
  catch-all: log, reply "Что-то пошло не так", return 200 (avoid Telegram retry storm)
```

Return 200 within a few seconds; Telegram retries non-2xx responses and slow handlers. Every handler here is a few DB queries. The slowest is chart rendering for `/week` and `/month`, which should be well under a second for one PNG. If it is ever slow, return 200 and hand off to the outbox. Do **not** use `Task.Run` with the request-scoped DbContext.

### Flow 3: Scheduled summaries and week rollover

```
SummaryScheduler (every 60 s, TimeProvider):
  for each user with summaries enabled (batched):
     nowLocal = now in user.time_zone
     if nowLocal is Sunday ≥ 20:00   ─► enqueue "summary:week:{U}:{monday}"   (ON CONFLICT skip)
     if nowLocal is day 1 ≥ 09:00    ─► enqueue "summary:month:{U}:{yyyy-MM}" (prev month)
     if Monday and no budget_weeks row ─► create it (snapshot current settings)
OutboxDispatcher ─► build report at send time ─► sendPhoto(chart) + caption
```

The dedupe key turns "is it due?" into an idempotent check, so restarts, double ticks, or a missed tick (it catches up on the next tick, any time after 20:00) never double-send or skip. Without a job library there is nothing extra to persist. Quartz.NET or Hangfire would be overkill for two cron-like rules.

### Flow 4: Onboarding and token rotation

```
/start ─► RegisterUser (idempotent on telegram_user_id) ─► create settings + token
       ─► reply: token (shown once) + iCloud link to Shortcut + setup steps
/token ─► RotateToken: revoke active (revoked_at=now) + insert new, same transaction ─► reply new token
```

Token = `"fb_" + base64url(32 random bytes)` from `RandomNumberGenerator`. Store SHA-256 only. A fast hash is correct for 256-bit random secrets, and a deterministic hash allows an indexed lookup. bcrypt/argon2 are salted per row, so they cannot be indexed, and they are only needed for low-entropy human passwords. The Shortcut can ask for the token at install time via its import questions (MEDIUM confidence; verify while building the Shortcut). Suggest that the user delete the bot message containing the token after setup.

---

## Security Boundaries

| Boundary | Control |
|----------|---------|
| Telegram → webhook | `setWebhook(secret_token=<32+ random chars [A-Za-z0-9_-]>)`. Telegram sends it as the `X-Telegram-Bot-Api-Secret-Token` header on every request. Compare with `CryptographicOperations.FixedTimeEquals`. `allowed_updates = [message, callback_query]`. Accept private chats only. Register idempotently from a hosted service on startup. |
| Shortcut → API | Bearer token via a custom auth scheme. HTTPS only (reverse proxy with automatic TLS). `RateLimiter`: per-token fixed window plus per-IP limit on failed auth. 16 KB request size limit. Strict JSON contract, with unknown fields ignored. |
| User ↔ user data | Tenant query filter, UserId-stamping interceptor, and integration tests per use case: user B acting on user A's transaction id gets "not found". |
| Secrets | Bot token, webhook secret, and DB password come from environment or Docker secrets, never from the repo. Log scrubbing: never log the `Authorization` header or bot token (Telegram.Bot URLs contain the token, so watch `HttpClient` logging). |
| Callback buttons | Callback data is untrusted input. Parse strictly, then load via the tenant-filtered context. Never trust `userId` from callback data. |

---

## Scaling Considerations

| Scale | Architecture Adjustments |
|-------|--------------------------|
| 1–100 users (realistic) | Single container + Postgres on one 4–5 €/month VPS. Everything above works unchanged. |
| 100–10k users | Scheduler loads users in batches with a single query for "due" users. Respect Telegram broadcast limits (≈30 msg/s overall, ~1 msg/s per chat) with a send rate limiter in the dispatcher. Add the `(user_id, occurred_at)` covering index (already listed). |
| 10k+ users | Multiple instances: the outbox already uses `SKIP LOCKED`; the scheduler needs leader election (Postgres advisory lock). Consider materialized weekly totals. Not a goal. |

### Scaling Priorities

1. **First bottleneck:** Telegram rate limits during Sunday-evening summary fan-out, not the DB. Fix: dispatcher token bucket, and spread summaries across 20:00–20:30.
2. **Second bottleneck:** chart rendering CPU during fan-out. Fix: render sequentially in the dispatcher (already the case) and cap concurrency.

---

## Anti-Patterns

### Anti-Pattern 1: Business logic in Telegram handlers
**What people do:** Compute remaining limit, categorize, and format inside `HandleUpdateAsync` switch statements, which is the default shape of most bot tutorials.
**Why it's wrong:** It cannot be tested without Telegram, the Shortcut path duplicates the logic, and the later web dashboard has to rewrite it.
**Do this instead:** Handlers only parse input, call one Application use case, and format the result.

### Anti-Pattern 2: Sending Telegram messages inside (or before) the DB transaction
**What people do:** `SaveChanges(); await bot.SendMessage(...)` in the ingest endpoint, or send first and save afterwards.
**Why it's wrong:** A rollback produces a ghost message. A Telegram outage makes the Shortcut fail, so the purchase is lost. Alerts double-fire on retries.
**Do this instead:** Use the transactional outbox with dedupe keys (Pattern 2).

### Anti-Pattern 3: Server-local or `DateTime.Now` week math
**What people do:** `DateTime.Now.DayOfWeek`, `startOfWeek.AddDays(7)` on UTC, or storing local times.
**Why it's wrong:** The VPS runs in UTC, so Sunday 23:30 Warsaw becomes Monday. DST weeks are off by an hour. Tests are non-deterministic.
**Do this instead:** Store UTC, use the user's zone, use `WeekPeriod`, and inject `TimeProvider`.

### Anti-Pattern 4: Deterministic idempotency key from amount+merchant+time
**What people do:** `hash(amount|merchant|yyyyMMddHHmm)` as a unique key.
**Why it's wrong:** It breaks at minute boundaries and silently drops two genuine coffees bought in the same minute. It also cannot be undone.
**Do this instead:** Nonce key for retries, plus a fingerprint *window* that soft-suppresses with an undo button (Pattern 3).

### Anti-Pattern 5: Fire-and-forget update processing with a captured scope
**What people do:** Return 200 to Telegram, then run `_ = Task.Run(() => handler.Handle(update))` using the request's DbContext.
**Why it's wrong:** `ObjectDisposedException`, lost updates on shutdown, and swallowed exceptions.
**Do this instead:** Process inline (fast enough here). If offloading is ever needed, write to a durable queue (outbox-like table) and process in a `BackgroundService` that creates its own scope.

### Anti-Pattern 6: Ceremony stack (MediatR + AutoMapper + generic repository + CQRS buses + event sourcing + microservices)
**What people do:** Copy an enterprise template onto a 10-entity bot.
**Why it's wrong:** Reviewers read it as cargo-culting, and it hides the interesting decisions (idempotency, isolation, outbox, time math).
**Do this instead:** Four or five projects, plain handlers, EF directly, and ADRs in `docs/` explaining why.

### Anti-Pattern 7: Trusting global query filters blindly
**What people do:** Assume the filter covers everything.
**Why it's wrong:**
- Raw SQL and `IgnoreQueryFilters()` bypass it.
- Inserts are not filtered.
- A filter that captures a local variable instead of a context member bakes in the first user's id.
**Do this instead:** Reference context members in filters, stamp UserId via an interceptor, and add isolation integration tests.

### Anti-Pattern 8: In-memory conversation state
**What people do:** A `ConcurrentDictionary<long, State>` for "waiting for new amount".
**Why it's wrong:** It is lost on every deploy or restart, and it breaks with more than one instance.
**Do this instead:** Use a `dialog_state` table with expiry.

---

## Integration Points

### External Services

| Service | Integration Pattern | Notes / Gotchas |
|---------|---------------------|-----------------|
| Telegram Bot API (inbound) | Webhook, secret header, `update_id` dedupe | Must answer 2xx within seconds or the update is retried. Updates may arrive out of order (parallel connections). Order does not matter for this bot's use cases. |
| Telegram Bot API (outbound) | `ITelegramBotClient` (Telegram.Bot 22.x; System.Text.Json; non-`Async` method names since 22.5) | Wrap behind `TelegramUserNotifier`. On 403 "bot was blocked", set `users.bot_blocked_at` and stop sending. On 429, honour `retry_after`. Callback data is limited to 64 bytes. Captions are limited to 1024 chars. |
| iOS Shortcut | JSON POST with Bearer token and Idempotency-Key | No transaction id or timestamp from Wallet. Possible empty merchant or 0 amount. Fires on declined payments (the user deletes those via button). Trigger timeouts mean some payments never arrive, so manual entry is the safety net. Amount text is locale-formatted. Keep the endpoint contract stable and versioned (`/api/v1`), because users install the Shortcut once. |
| PostgreSQL | EF Core + Npgsql, migrations applied at startup or via a CI step | `timestamptz` needs UTC values. Use `NULLS NOT DISTINCT` (PG15+) for global-rule uniqueness. Use advisory locks for the ingest critical section. |
| Reverse proxy | Caddy/Traefik in front of Kestrel, automatic Let's Encrypt | Telegram requires a valid cert on port 443/80/88/8443. Forward headers configured (`UseForwardedHeaders`) for rate-limit-by-IP. |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| Api / Telegram → Application | Direct method call on handler classes (`IngestTransactionHandler.Handle(cmd, ct)`) returning `Result<T>` | No exceptions for expected failures (validation, not found). |
| Application → Domain | Direct calls to pure functions and entity methods | Domain throws only for invariant violations (bugs). |
| Application → Telegram (server push) | Via `IUserNotifier` port, which writes outbox rows | Application never references Telegram.Bot. |
| Application → Infrastructure | Ports: `IAppDbContext`, `IChartRenderer`, `ITokenGenerator`, `TimeProvider` | Wired in the Api composition root. |
| Background services → Application | New DI scope per unit of work, explicit `ICurrentUser` per user | Never reuse scoped services across ticks. |

---

## Suggested Build Order

Dependencies drive the order. The core-value loop (Shortcut → server → Telegram confirmation with remaining limit) should come as early as possible so the user can dogfood real payloads. Real payloads are the biggest unknown.

| # | Phase | Builds | Depends on | Why here |
|---|-------|--------|------------|----------|
| 1 | **Foundation + deployed walking skeleton** | Solution and 5 projects, ArchitectureTests, Postgres via docker-compose, EF baseline migration, `/health`, Testcontainers integration harness, GitHub Actions build+test, Docker image, **VPS + reverse proxy + HTTPS live** | — | Telegram webhooks and the real iOS Shortcut both need public HTTPS. Deploying early surfaces tzdata, TLS, and secrets issues while they are still cheap to fix. |
| 2 | **Identity, Telegram channel, tenant isolation** | `users`, `api_tokens`, `/start`, `/token`, webhook + secret filter + `update_id` dedupe, UpdateRouter skeleton, polling receiver for dev, `ICurrentUser` + named tenant filters + stamping interceptor + isolation tests | 1 | Every later feature needs "who is the user". The isolation pattern must exist before any user data does. |
| 3 | **Expense ingestion + outbox (core value)** | Domain: `Money`, `AmountParser`, `MerchantNormalizer`, `WeekPeriod`, `BudgetCalculator` (Reset mode). Ingest endpoint + ApiToken auth + rate limit + `ingest_log`. Two-layer idempotency. Manual text entry (`45 такси`, `!`). Outbox + dispatcher. Confirmation message with remaining limit. Seed categories + global rules (simple resolver). The Shortcut itself (export to `shortcut/`). | 2 | Delivers the core value. Real Shortcut payloads arrive from here on, so parser and dedupe assumptions get validated early. |
| 4 | **Categorization learning + interactive buttons** | "Другое" prompt with category keyboard, per-user rule learning, change category / out-of-limit toggle / edit (dialog_state) / delete (soft) / "not a duplicate" restore, edit-in-place, `/other` | 3 | Needs transactions and confirmation messages to attach buttons to. |
| 5 | **Budget rules + alerts** | `user_settings` commands (`/limit`), `budget_weeks` snapshots, Carry mode fold, ThresholdDetector, 80%/100% outbox alerts with dedupe keys | 3 (4 for toggle-recompute) | Rollover is the most intricate domain logic. It is isolated here with heavy table-driven tests. |
| 6 | **Balance + incomes** | `balance_entries`, `/balance N`, `+5000 зп`, balance shown in confirmations | 3 | Small and independent. Could run in parallel with 5. |
| 7 | **Reports + charts** | Week and month report queries, comparison with the previous month, out-of-limit section, `IChartRenderer` PNG (verify native SkiaSharp assets in the Docker image), `/week`, `/month` | 4, 5 | Reports are only meaningful once categories and limits are final. |
| 8 | **Scheduler + automatic summaries** | SummaryScheduler with `TimeProvider`, Sunday 20:00 / 1st 09:00 per user TZ, week-row creation, `FakeTimeProvider` tests across DST | 7 (renders the reports), 2 (outbox) | Reuses the report builders and the outbox. Mostly wiring plus time tests. |
| 9 | **Portfolio hardening** | README with C4 diagram and ADRs, demo GIF, CD on tag, DB backups (pg_dump cron), structured logging (Serilog/OpenTelemetry), retention jobs (`processed_updates`, `ingest_log`) | all | Polish and operational maturity. Some items (CI, logging) start in phase 1 and get finished here. |

**Ordering rationale:**
- Phases 1–3 form the critical path to the core value.
- Phases 4–6 deepen the domain and can be reordered.
- Phase 7 depends on finalized categories and limits.
- Phase 8 depends on 7.

**Research flags for phases:**
- **Phase 3:** needs hands-on validation of the actual Shortcut payload: amount text format under the Polish locale, empty merchant, declined payments, `Current Date` formatting, whether import questions can carry the token, and iOS 26 "Wallet" trigger naming. Plan a spike.
- **Phase 5:** product decisions are needed (carry cap, mode-switch timing, negative effective limit).
- **Phase 7:** chart library choice and its Linux/Docker native-dependency story (SkiaSharp).
- **Phases 2, 4, 6, 8:** standard patterns; deeper research is unlikely to be needed.

---

## Sources

- Telegram Bot API, `setWebhook` (`secret_token` charset/length and the `X-Telegram-Bot-Api-Secret-Token` header, non-2xx retry, `update_id` sequential and usable for dedupe, `max_connections`, `allowed_updates`): https://core.telegram.org/bots/api#setwebhook (official doc; seam tier LOW for webfetch, cross-checked against the Telegram.Bot book, so treated as reliable)
- Telegram.Bot book, webhook page (return 200 within seconds, offload slow work, skip `update_id ≤ last`, updates may arrive out of order): https://telegrambots.github.io/book/3/updates/webhook.html
- Telegram.Bot 22.x migration notes (System.Text.Json, no `ConfigureTelegramBot` needed since 22.5, `Async` suffixes removed), via Context7 `/websites/telegrambots_github_io_book` (MEDIUM)
- EF Core 10 named query filters and multi-tenancy via a context member, via Context7 `/dotnet/entityframework.docs` (filters.md) (MEDIUM; official docs)
- Npgsql date/time mapping (UTC-only `timestamptz`, `DateOnly` → `date`, NodaTime recommended): https://www.npgsql.org/doc/types/datetime.html
- iOS Transaction/Wallet automation fields and flakiness (LOW, community):
  - https://developer.apple.com/forums/thread/765516 (trigger timeouts, fires on declined payments, iOS 18+)
  - https://developer.apple.com/forums/thread/797233 (empty merchant / 0 amount)
  - https://help.travel-spend.com/shortcuts--automation/ignQHsp85RQDsig2QwVcdX/set-up-apple-pay-automation/7tL8XfjBceg4D7mQeiSK2V (field wiring)
  - https://help.finerd.ai/apple-pay (duplicate recording from overlapping automations)
  - https://getfinny.app/blog/apple-pay-two-channels-one-tap (Wallet events can be late or missing; vendor blog)
- No built-in UUID action in Shortcuts (only third-party, e.g. Sindre Sorhus' Actions app): https://sindresorhus.com/actions (LOW, absence-of-evidence)
- MediatR/AutoMapper licensing change: https://www.jimmybogard.com/automapper-and-mediatr-commercial-editions-launch-today/ (MEDIUM)

---
*Architecture research for: multi-user expense-tracking Telegram bot (ASP.NET Core + PostgreSQL + iOS Shortcut ingestion)*
*Researched: 2026-11-14*
