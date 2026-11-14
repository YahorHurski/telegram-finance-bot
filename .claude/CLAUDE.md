<!-- GSD:project-start source:PROJECT.md -->

## Project

**Finance Bot**

Мультипользовательский Telegram-бот для контроля личных расходов. Траты попадают в него автоматически — iOS-Команда (автоматизация «Транзакция») при каждой оплате Apple Pay отправляет сумму и продавца на сервер; остальное (наличные, BLIK) быстро вносится вручную сообщением. Бот раскладывает траты по категориям, следит за недельным лимитом, шлёт алерты и сводки с диаграммами. Делается для себя (живу в Польше, iPhone) и как портфолио-проект на C# для позиции Backend Developer.

**Core Value:** Каждая оплата Apple Pay автоматически и без дублей попадает в бот, и я в любой момент вижу, сколько осталось от недельного лимита (200 zł).

### Constraints

- **Платформа**: iOS без Apple Developer Program — только встроенные Команды; всё законно
- **Tech stack**: C# / .NET (актуальная LTS), ASP.NET Core, PostgreSQL + EF Core, Telegram.Bot (webhook) — предварительно, уточняется исследованием
- **Хостинг**: строго бесплатно — Oracle Cloud Always Free VM + Docker Compose, поддомен DuckDNS с HTTPS; сервер доступен 24/7. Всё в Docker, чтобы при необходимости переехать (например, на Hetzner ~6 €/мес) без переделок. Vercel/Netlify/GitHub Pages/Cloudflare Pages/Workers не подходят (нет .NET-бэкенда 24/7); Render free засыпает и теряет транзакции
- **Валюта**: только PLN
- **Язык интерфейса**: русский
- **Безопасность**: токены хранятся хэшированными; финансовые данные пользователя изолированы
- **Сроки**: не жмут — качество важнее скорости

<!-- GSD:project-end -->

<!-- GSD:stack-start source:research/STACK.md -->

## Technology Stack

## TL;DR (prescriptive)

- **Runtime:** **.NET 10 LTS** (SDK 10.0.x, runtime 10.0.15), **not** .NET 11. .NET 11 (STS) shipped on 2026-11-10, but the Npgsql EF Core provider for 11 is still at RC3, and .NET 10 and 11 both reach end of support on 2028-11-14. You get nothing from 11 and lose a stable provider.
- **Web:** One ASP.NET Core process with **minimal APIs** (route groups plus endpoint filters, a custom token `AuthenticationHandler`, built-in .NET 10 minimal-API validation). It hosts the Shortcut ingestion endpoint, the Telegram webhook, health checks and the background workers.
- **Bot:** **Telegram.Bot 22.10.4** on webhook in production and long polling in development, with both transports feeding one `IUpdateHandler`.
- **Data:** **PostgreSQL 18** + **EF Core 10.0.15** + **Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3** + **NodaTime** (via the Npgsql NodaTime plugin) + **EFCore.NamingConventions** (snake_case). Migrations ship as an **EF migration bundle** run by a one-shot compose service before the app starts.
- **Scheduling:** A plain **`BackgroundService` + `PeriodicTimer` + `TimeProvider`** "catch-up dispatcher" with DB-tracked "summary sent" markers. **No Quartz or Hangfire in v1.**
- **Charts:** **ScottPlot 5.1.59** (MIT, SkiaSharp 3.119 underneath), with a **bundled Cyrillic TTF (Noto Sans / Inter)** registered through `Fonts.AddFontFile`.
- **Tests:** **xUnit v3 4.0.3** on Microsoft Testing Platform v2, **Testcontainers.PostgreSql 4.16.0**, **WebApplicationFactory (Microsoft.AspNetCore.Mvc.Testing 10.0.15)**, **Respawn 7**, **NSubstitute**, **AwesomeAssertions** (or plain `Assert`), **NodaTime.Testing / FakeTimeProvider**, **WireMock.Net** as a fake Telegram Bot API.
- **Deploy:** Docker Compose (app + migrator + postgres + caddy) on a **~€5–6/month Hetzner Cloud VPS**. **Caddy 2.11** does automatic HTTPS. GitHub Actions builds, tests, pushes to GHCR and deploys over SSH.
- **Observability:** **Serilog.AspNetCore 10.0.0** (compact JSON to stdout), **OpenTelemetry 1.21** traces and metrics (OTLP, off by default in production), the standalone Aspire Dashboard for local viewing, health checks plus an external uptime pinger.

## Recommended Stack

### Core Technologies

| Technology | Version | Purpose | Why Recommended | Confidence |
|------------|---------|---------|-----------------|------------|
| .NET SDK / runtime | **10.0** (runtime 10.0.15, Nov 2026 patch) | Platform | Current **LTS**, released 2025-11-11, supported to 2028-11-14. .NET 11 (STS, 2026-11-10) has the **same** end-of-support date, and its ecosystem is not ready yet (Npgsql EF 11 = 11.0.0-rc.3, Serilog.AspNetCore 11 = dev builds only). Upgrade straight to .NET 12 LTS (Nov 2027) later. C# 14. | HIGH (official support-policy page + NuGet) |
| ASP.NET Core (minimal APIs) | 10.0.x (shared framework) | HTTP ingestion API, Telegram webhook, health endpoints, hosting for background services | One deployable keeps a €5 VPS simple. In .NET 10, minimal APIs are the first-class default: route groups, endpoint filters, typed results, **built-in validation** (`builder.Services.AddValidation()`), native OpenAPI 3.1. Controllers add ceremony and buy nothing for ~10 endpoints. | HIGH |
| Telegram.Bot | **22.10.4** (published 2026-11-09, Bot API 10.4) | Telegram Bot API client | The de facto .NET Telegram client, actively maintained (several releases per month), now on nuget.org. Since v22.5 it needs **no** `ConfigureTelegramBot*` JSON setup for webhooks, because its types are compatible with ASP.NET Core's default camelCase System.Text.Json. Methods have no `Async` suffix (`SendMessage`, `AnswerCallbackQuery`). | HIGH (NuGet + official book via Context7) |
| PostgreSQL | **18** (image `postgres:18-alpine`, currently 18.7) | Primary store | Current stable major. **PostgreSQL 19 is still RC2**, so don't use it. Needed features (unique constraints for idempotency, `numeric` money, `date`/`timestamptz`, partial indexes) are all mature. | HIGH (Docker Hub tags) |
| Entity Framework Core | **10.0.15** (pin; do **not** take 11.0.0) | ORM and migrations | LTS-aligned. EF Core 10 adds **named query filters** (several filters per entity, separately toggleable), which suit `UserId` tenant isolation plus soft-delete. `ExecuteUpdate`/`ExecuteDelete` cover bulk edits. | HIGH |
| Npgsql.EntityFrameworkCore.PostgreSQL | **10.0.3** | EF provider | **Hard constraint:** its nuspec requires `Microsoft.EntityFrameworkCore [10.0.4, 11.0.0)`. NuGet will offer EF Core 11.0.0 as "latest", and taking it breaks restore or runtime. Pin EF to 10.0.x. | HIGH (nuspec inspected) |
| NodaTime | **3.3.7** | Time zones, ISO weeks, local dates | Week boundaries (Mon–Sun in Europe/Warsaw), "Sunday evening" and "1st of the month" are calendar logic, and `DateTime` makes them easy to get wrong. NodaTime ships its **own TZDB**, so it does not depend on the container having `tzdata` (chiseled images don't). `Instant` for storage, `LocalDate`/`ZonedDateTime` for business rules, `WeekYearRules.Iso` for weeks. | HIGH |
| Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime | **10.0.3** | Maps NodaTime types to PG columns | `Instant` to `timestamptz`, `LocalDate` to `date` with no converters. Version-locked to the provider. | HIGH |
| ScottPlot | **5.1.59** (2026-06-22) | Server-side PNG pie/bar charts | MIT-licensed, headless, works on Linux/Docker (uses `SkiaSharp.NativeAssets.Linux.NoDependencies`, so no `libfontconfig` needed), and has `plot.GetImageBytes(w, h, ImageFormat.Png)` for sending a `byte[]` straight to Telegram. Pie, donut and bar plots are built in. Custom fonts via `Fonts.AddFontFile(name, path)` + `plot.Font.Set(name)` handle Cyrillic. | HIGH (NuGet + Context7 docs) |
| Docker + Docker Compose | Compose v2 | Packaging and runtime | Reproducible prod and dev setup, portfolio-friendly ("`docker compose up` and it runs"). | HIGH |
| Caddy | **2.11.x** (2.11.9, 2026-11-05) | TLS-terminating reverse proxy | Automatic Let's Encrypt certificates and renewal from a 3-line Caddyfile. Telegram requires a valid HTTPS webhook on port 443/80/88/8443, and Caddy makes that trivial. | HIGH |

### Supporting Libraries

| Library | Version | Purpose | When to Use | Confidence |
|---------|---------|---------|-------------|------------|
| EFCore.NamingConventions | 10.0.1 | `snake_case` tables and columns | Always. Idiomatic Postgres, nicer raw SQL. Requires EF `[10.0.1, 11.0.0)`. | HIGH |
| Microsoft.EntityFrameworkCore.Design | 10.0.15 | Design-time migrations | Infrastructure/startup project, `PrivateAssets=all`. | HIGH |
| dotnet-ef (local tool) | **10.0.15** (pin; latest is 11.0.0) | `migrations add`, `migrations bundle` | Pin in `.config/dotnet-tools.json` so CI and local generate identical bundles. | HIGH |
| Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | 10.0.15 | `/health/ready` DB check | Always. Use the Microsoft package instead of AspNetCore.HealthChecks.NpgSql (still 9.0.0). | HIGH |
| Microsoft.AspNetCore.OpenApi | 10.0.15 | OpenAPI 3.1 document for the ingestion API | Portfolio value: documents the Shortcut contract. | HIGH |
| Scalar.AspNetCore | 2.21.0 | API reference UI over the OpenAPI doc | Dev/staging only. Replaces the Swagger UI that is no longer in the templates. | MEDIUM |
| Serilog.AspNetCore | **10.0.0** | Structured logging, request logging | Always. Write `RenderedCompactJsonFormatter` (Serilog.Formatting.Compact 3.0.0) to stdout and let Docker collect it. | HIGH |
| Serilog.Sinks.Seq | 9.1.0 | Ship logs to Seq | Optional. Local dev or an extra container if the VPS has RAM to spare. | MEDIUM |
| OpenTelemetry.Extensions.Hosting | 1.21.0 | Traces and metrics pipeline | Wire up from day one; export only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. | HIGH |
| OpenTelemetry.Instrumentation.AspNetCore / .Http / .Runtime | 1.21.0 | Auto-instrumentation | With the above. HttpClient instrumentation also covers outgoing Telegram API calls. | HIGH |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.21.0 | OTLP export | To the Aspire Dashboard locally, or Grafana Cloud free tier / any OTLP backend later. | HIGH |
| Npgsql.OpenTelemetry | 10.0.3 | DB spans | With the above (`.AddNpgsql()` on the tracer). | HIGH |
| Microsoft.Extensions.Http.Resilience | 10.x / 11.0.0 (multi-targets net10) | Retry/timeout for the Telegram `HttpClient` | Register `TelegramBotClient` via `AddHttpClient(...).AddStandardResilienceHandler()` so transient Bot API errors and 429s are retried. **Don't retry non-idempotent sends blindly**: keep retries for 5xx/timeouts and honour `retry_after`. | MEDIUM |
| Built-in `Microsoft.AspNetCore.RateLimiting` | in framework | Throttle the ingestion endpoint per token | Always. The ingestion endpoint is public. | HIGH |
| BCL crypto (`RandomNumberGenerator`, `SHA256.HashData`, `CryptographicOperations.FixedTimeEquals`) | in framework | Per-user API tokens | Generate 32 random bytes, show once, store **SHA-256** hash, look up by hash. A slow KDF (bcrypt/Argon2) is unnecessary for high-entropy random tokens and would add a dependency. | HIGH |

### Testing Stack

| Library | Version | Purpose | Notes | Confidence |
|---------|---------|---------|-------|------------|
| xunit.v3 | **4.0.3** (2026-11-09) | Test framework | 4.0 (Aug 2026) **dropped MTP v1; MTP v2 is the default**. On the .NET 10 SDK, put `"test": { "runner": "Microsoft.Testing.Platform" }` in `global.json` so `dotnet test` uses MTP. Test projects are executables (`OutputType=Exe`). | HIGH (NuGet + xunit.net release notes) |
| xunit.runner.visualstudio | 4.0.1 | VS/Rider Test Explorer (VSTest bridge) | Only if IDE Test Explorer needs it. CI uses MTP. | HIGH |
| Microsoft.AspNetCore.Mvc.Testing | **10.0.15** (not 11.0.0) | `WebApplicationFactory<Program>` in-memory host | End-to-end tests of the ingestion endpoint and the webhook endpoint (post a fake `Update` JSON). Must match the ASP.NET Core 10 runtime. | HIGH |
| Testcontainers.PostgreSql | **4.16.0** | Real PostgreSQL 18 in tests | The project requires integration tests on real Postgres. Use the same image tag as production (`postgres:18-alpine`). Works out of the box on GitHub `ubuntu-latest` runners (Docker preinstalled). | HIGH |
| Testcontainers.XunitV3 | 4.16.0 | xUnit v3 fixture integration | Optional convenience (`ContainerFixture`). A hand-written `IAsyncLifetime` collection fixture is fine too. | HIGH |
| Respawn | 7.0.0 | Fast DB reset between tests | One container per test run, `Respawner.ResetAsync` between tests. Much faster than a container per test. | HIGH |
| NSubstitute | 6.2.0 | Mocks | `ITelegramBotClient` is an interface, so substitute it in unit tests. | HIGH |
| WireMock.Net | 2.31.0 | Fake Telegram Bot API server | Integration tests: point `TelegramBotClientOptions(token, baseUrl: wiremock.Url)` at it and assert on the outgoing `sendMessage` / `sendPhoto` payloads, with no real Telegram calls. | MEDIUM |
| AwesomeAssertions | 9.6.0 | Fluent assertions | Apache-2.0 community fork of FluentAssertions. Or plain xUnit `Assert` / Shouldly 4.3.0. | HIGH |
| NodaTime.Testing | 3.3.7 | `FakeClock` | Test week rollover, Sunday-evening summaries and DST transitions deterministically. | HIGH |
| Microsoft.Extensions.TimeProvider.Testing | 10.x / 11.0.0 (targets net10) | `FakeTimeProvider` | Drive `PeriodicTimer`-based background services in tests. | HIGH |
| Verify.XunitV3 | 33.4.0 | Snapshot tests | Optional: snapshot the rendered Russian report text (`/week`, `/month`). Good at catching formatting regressions. | MEDIUM |

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| `global.json` | Pin SDK 10.0.x + MTP runner | `{"sdk":{"version":"10.0.100","rollForward":"latestFeature"},"test":{"runner":"Microsoft.Testing.Platform"}}` |
| Central Package Management (`Directory.Packages.props`) | One place for versions | Lets you pin EF Core 10.0.15 / Npgsql 10.0.3 consistently and stops accidental 11.0.0 bumps. |
| `Directory.Build.props` | `Nullable=enable`, `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended` | Shows code quality in a portfolio repo. |
| CSharpier 1.4.0 (dotnet tool) | Opinionated formatter | `dotnet csharpier check .` in CI. |
| Dependabot or Renovate | Dependency PRs | **Configure an ignore rule for EF Core / ASP.NET packages `>= 11`** until Npgsql 11 is GA and you decide to move to .NET 11/12. |
| Telegram dev bot + long polling | Local development without a public URL | Separate bot token from @BotFather for dev. In `Development`, run a polling `BackgroundService` (`bot.ReceiveAsync`) that calls the same `IUpdateHandler` as the webhook endpoint. |
| Cloudflare quick tunnel (`cloudflared tunnel --url`) or Microsoft dev tunnels | Expose localhost to the iPhone Shortcut during development | Free. Needed only to test the real Shortcut against a dev machine. |
| Aspire Dashboard (standalone container `mcr.microsoft.com/dotnet/aspire-dashboard`) | Local OTLP viewer for logs, traces, metrics | Zero-config, in-memory. Optional for local dev; skip in production on a small VPS. Full Aspire AppHost (13.6.5) is optional polish, and Compose stays the source of truth. |

## Installation

# global.json + central package management assumed (versions in Directory.Packages.props)

# --- Web/API host (FinanceBot.Api) ---

# --- Persistence (FinanceBot.Infrastructure) ---

# --- Domain / reporting ---

# --- Tests ---

# --- Tools ---

## Key Decisions in Detail

### 1. Minimal APIs, one process, two inbound channels

- `POST /api/v1/transactions`: Shortcut ingestion. Authenticated by a custom `AuthenticationHandler` that reads `Authorization: Bearer <token>`, hashes it, and resolves the user. iOS "Get Contents of URL" supports custom headers and JSON bodies. Prefer a **real ASP.NET Core auth scheme** over an ad-hoc endpoint filter: it gives `HttpContext.User`, `[Authorize]`/`RequireAuthorization()`, and per-user rate-limit partitioning.
- `POST /telegram/webhook`: Telegram updates. Verify the `X-Telegram-Bot-Api-Secret-Token` header (set with `SetWebhook(url, secretToken: ..., allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery])`). Accept `Update` as the body. **Always return 200 quickly.** Telegram redelivers on non-2xx, which would duplicate side effects. Dedupe on `update_id` if processing is not naturally idempotent.
- Register the webhook at startup through an `IHostedService` (idempotent `SetWebhook`), or through a deploy step.
- **Dev/prod transport split:** webhook in production, long polling in development, one shared `IUpdateHandler`. Telegram allows only one mode per bot at a time, so use separate dev and prod bot tokens.

### 2. Scheduling: BackgroundService dispatcher, not Quartz/Hangfire

### 3. Charts: ScottPlot + bundled font

- Bundle an OFL-licensed font with Cyrillic coverage (**Noto Sans** or **Inter**, regular + bold) under `assets/fonts/` and copy it to output. At startup: `Fonts.AddFontFile("Noto Sans", path)` then `plot.Font.Set("Noto Sans")` per plot. **Never rely on system fonts.** Slim and chiseled .NET images have none, and DejaVu fallback differs between machines, so snapshot tests become flaky.
- Render to `byte[]` (`plot.GetImageBytes(800, 600, ImageFormat.Png)`) and send with `SendPhoto(chatId, InputFile.FromStream(new MemoryStream(bytes), "week.png"), caption: ...)`. Nothing touches disk.
- **Do not add SkiaSharp 4.x directly.** SkiaSharp 4.156.0 GA'd on 2026-11-11, but ScottPlot 5.1.59 is built against **SkiaSharp 3.119.0**. A direct 4.x reference lifts the transitive version and risks native/managed mismatches.
- Keep emoji out of chart labels: the bundled font has no emoji glyphs. Emoji in Telegram message text is fine, because Telegram renders it.
- ScottPlot calls are not guaranteed thread-safe across a shared `Plot`. Create a new `Plot` per render. Rendering is CPU-cheap at this scale.

### 4. Database and migrations

- Money: `decimal` mapped to `numeric(12,2)`. PLN only, but store a `currency char(3)` column defaulted to `PLN` so multi-currency later is not a migration on a hot table.
- Idempotent ingestion: a unique index on `(user_id, dedup_key)`, where `dedup_key` is a hash of the normalized Shortcut payload (amount, merchant, card, timestamp truncated to a sensible granularity). EF Core 10 has no native upsert, so use `INSERT ... ON CONFLICT DO NOTHING RETURNING id` via `Database.SqlQuery`/`ExecuteSql`, or catch `PostgresException { SqlState: "23505" }`.
- Tenant isolation: EF Core 10 **named query filter** `"Tenant"` on every user-owned entity (`HasQueryFilter("Tenant", e => e.UserId == _currentUser.Id)`), plus `UserId` in every composite index. Integration tests should prove that user A cannot read user B's data.
- **Migrations:** generate in dev with `dotnet ef migrations add`. In CI, produce a self-contained **migration bundle** (`dotnet ef migrations bundle --self-contained -r linux-x64` or `linux-arm64`). Ship it in a `migrator` image or stage. In Compose, the `app` service has `depends_on: migrator: condition: service_completed_successfully`. Do **not** call `Database.Migrate()` on app startup in production: it races with multiple replicas, hides failures in app logs, and needs DDL rights for the runtime user. For tests, `Database.MigrateAsync()` against the Testcontainers DB is fine, because it also verifies the migrations.

### 5. Container image

- Build: multi-stage Dockerfile, `mcr.microsoft.com/dotnet/sdk:10.0` to `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`.
- ARM64 VPS (Hetzner CAX): .NET, Npgsql, SkiaSharp NoDependencies native assets and postgres images all support `linux/arm64`. Build on GitHub's free `ubuntu-24.04-arm` runners (public repos) rather than QEMU emulation.

### 6. Deployment topology

- Caddyfile: `bot.example.com { reverse_proxy app:8080 }`. That's the whole thing.
- **Get a domain** (cheap `.pl`/`.dev`, or a free DDNS subdomain such as DuckDNS). The Shortcut URL and the webhook must stay stable across VPS rebuilds. Let's Encrypt IP-address certificates are GA since 2026-01-15 (6-day "shortlived" profile, which Caddy supports), but they tie the iPhone Shortcut to an IP and Caddy issue reports exist. Use them only as a fallback.
- **GitHub Actions pipeline:** `build-test` (restore, CSharpier check, build, `dotnet test` with Testcontainers) → `publish` (docker buildx, push app and migrator images to **GHCR** tagged with the commit SHA) → `deploy` (SSH to the VPS with a deploy key, `docker compose pull && docker compose up -d`, gated on `main` and a GitHub Environment). Current action majors: `actions/checkout@v7`, `actions/setup-dotnet@v6`, `docker/build-push-action@v7`.
- Secrets (bot token, webhook secret, DB password) live in GitHub Environment secrets and a `.env` on the VPS (chmod 600). Never commit them.
- **Backups:** nightly `pg_dump` (cron on the host or a sidecar) to off-box storage (Hetzner Storage Box, Backblaze B2 via rclone). A finance app without backups fails a portfolio review.
- **Uptime:** a free external pinger (UptimeRobot / Better Stack free tier) on `/health`, alerting by Telegram or email.

## Hosting Options Compared (must keep a webhook up 24/7)

| Option | Cost (approx.) | 24/7? | Verdict |
|--------|----------------|-------|---------|
| **Hetzner Cloud CX23** (x86, 2 vCPU / 4 GB) | ~€5.49/mo + €0.50 IPv4, excl. VAT, after the **15 June 2026 price increase**. Some reports say availability is constrained. | Yes | **Recommended.** EU (DE/FI) data locality next to Poland, enough RAM for app + Postgres + Caddy, mature tooling. Check the live price in the console. (MEDIUM: prices from secondary sources) |
| Hetzner Cloud CAX11 (ARM64, 2 vCPU / 4 GB) | Same price band | Yes | Equally good if x86 is out of stock. Build arm64 images. |
| Other EU VPS (Netcup, OVHcloud VPS, Contabo) | ~€4–7/mo | Yes | Fine alternatives. Contabo is cheap but has a weaker performance and support reputation. |
| Oracle Cloud Always Free (Ampere A1) | €0 | Nominally | **Not as primary.** Oracle reclaims idle Always Free instances (95th-percentile CPU < 20% over 7 days, and this bot is idle most of the time). The A1 allowance was reportedly cut from 4 OCPU/24 GB to 2 OCPU/12 GB in Aug 2026, and "out of capacity" errors are common. Fine as a free staging box. (LOW–MEDIUM) |
| Render free web service | €0 | **No.** Spins down after 15 min idle, ~1 min cold start | **Reject.** An Apple Pay Shortcut firing into a cold service times out and the transaction is lost. Telegram webhooks also lag. |
| Fly.io | No free allowance for new accounts since late 2024 | Paid only | Possible paid option, but no cheaper than Hetzner and more moving parts. |
| Koyeb free | Restricted / card-hold, may sleep | Uncertain | Reject (conflicting reports). |
| Azure App Service F1 | €0 | No "Always On" on free tier, so it idles | Reject for webhook ingestion. |

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| .NET 10 LTS | .NET 11 (STS, 2026-11-10) | After Npgsql EF Core 11 and Serilog.AspNetCore 11 go GA, only if you want a specific 11 feature. Otherwise wait for .NET 12 LTS (Nov 2027). |
| Minimal APIs | MVC controllers | Large API surfaces with heavy filter/convention reuse. Not this project. |
| Telegram.Bot | Telegram.BotAPI, Funogram (F#) | Telegram.Bot has the largest community, docs (telegrambots.github.io/book) and release cadence. No reason to switch. |
| Telegram.Bot raw handlers + own router | Bot frameworks on top (e.g. state-machine / "scenes" libraries) | Only if conversations become deeply multi-step. v1 flows (inline buttons, a few commands) fit a hand-written command router plus callback-data parsing, which also shows the engineering better. |
| BackgroundService dispatcher | Quartz.NET 4.7.3 | Many heterogeneous jobs, clustered nodes, cron editing at runtime. |
| BackgroundService dispatcher | Hangfire 1.8.25 + Hangfire.PostgreSql 1.21.1 | You want a job dashboard and fire-and-forget retries with persistence. Heavier (its own schema, dashboard security); overkill for 2 periodic jobs. |
| BackgroundService dispatcher | TickerQ 11.x | Newer EF-Core-based scheduler. Less battle-tested; revisit later. |
| NodaTime | `DateTimeOffset` + `TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")` | Acceptable only if the image contains tzdata (`-extra` / full). Loses `LocalDate`/ISO-week types, so week math becomes ad-hoc. |
| ScottPlot | Raw SkiaSharp drawing | Bespoke visuals ScottPlot can't do. Much more code for pie labels and legends. |
| ScottPlot | OxyPlot (+ OxyPlot.SkiaSharp), LiveCharts2 | OxyPlot is ageing. LiveCharts2 is UI-control-oriented and its headless rendering is clunkier. |
| ScottPlot | QuickChart.io / external chart API | Zero native deps, but adds a third-party runtime dependency and sends financial data to an external service. Reject for privacy. |
| Serilog + OTel | OTel logging only (`builder.Logging.AddOpenTelemetry`) | If you go all-in on an OTLP backend. Serilog's console formatting and enrichers are still nicer for `docker logs`. |
| Docker Compose + SSH deploy | Kamal, Coolify, Dokploy | Coolify/Dokploy give a PaaS-like UI on your VPS but use 0.5–1 GB RAM themselves. Plain Compose is more transparent for a portfolio. |
| Caddy | Nginx + certbot, Traefik | Traefik if you want label-based routing for many services. Nginx needs certbot glue. Caddy is simplest. |
| Caddy on VPS | Cloudflare Tunnel (`cloudflared`) | No open inbound ports, free TLS. Requires the domain's DNS on Cloudflare. A good hardening option later. |
| xUnit v3 | NUnit 4, MSTest 4 | Personal preference. xUnit v3 + MTP is the most common choice in modern .NET OSS. |
| Testcontainers | Shared docker-compose test DB, EF InMemory/SQLite | Never use EF InMemory/SQLite for "integration" tests: they don't enforce Postgres unique-constraint/`ON CONFLICT`/`timestamptz` semantics, which are exactly what idempotency relies on. |

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| EF Core **11.0.0** / Microsoft.AspNetCore.Mvc.Testing 11.0.0 / dotnet-ef 11.0.0 with .NET 10 | NuGet shows them as "latest stable" (2026-11-10), but **Npgsql EF 10.0.3 requires EF `< 11.0.0`** and Npgsql 11 is RC-only. Mixing causes restore or runtime failures. | Pin all `Microsoft.*` 10.0.15 packages via Central Package Management. |
| .NET 8 / 9 | .NET 9 STS and .NET 8 LTS both end support 2026-11-10 (passed or passing now). | .NET 10. |
| `SendTextMessageAsync` / old Telegram.Bot tutorials (v19–v21) | `*Async` methods were removed in v22.5. Old samples also add Newtonsoft and `ConfigureTelegramBot`, neither needed now. | v22 API (`SendMessage`, `EditMessageText`, `AnswerCallbackQuery`), official book at telegrambots.github.io/book. |
| `Telegram.Bot.AspNetCore` package | Only needed for edge cases (Native AOT/trimming). | Plain `Telegram.Bot` + minimal API `Update` binding. |
| `System.Drawing.Common` for charts | Windows-only since .NET 6, throws on Linux. | ScottPlot. |
| Direct SkiaSharp 4.x reference | ScottPlot 5.1.59 targets SkiaSharp 3.119.0. | Let ScottPlot bring SkiaSharp transitively. |
| Plain `aspnet:10.0-*-chiseled` image | Invariant globalization, no ICU/tzdata: broken `ru-RU` formatting. | `aspnet:10.0-noble-chiseled-extra` (or `aspnet:10.0-noble`). |
| `Database.Migrate()` at app startup in production | Races, hides failures, needs DDL rights at runtime. | EF migration bundle as a one-shot `migrator` service. |
| EF Core InMemory / SQLite for integration tests | Different constraint and time semantics from Postgres. | Testcontainers.PostgreSql + Respawn. |
| `DateTime.Now` / `DateTime.UtcNow` in domain code | Untestable; local-vs-UTC bugs at week boundaries and DST. | Inject NodaTime `IClock` (and/or `TimeProvider`). Store `Instant`, compute `LocalDate` in the user's zone. |
| `float`/`double` for money | Rounding errors. | `decimal` to `numeric(12,2)`. |
| MediatR (v13+), AutoMapper (v15+), FluentAssertions (v8+) | Now commercially licensed (Lucky Penny / Xceed). Licensing friction in a public repo, and MediatR adds indirection with little benefit at this size. | Plain handler classes + DI; manual mapping; AwesomeAssertions or `Assert`. |
| Moq | 2023 SponsorLink controversy; reputationally risky in a portfolio. | NSubstitute. |
| Hashing API tokens with bcrypt/Argon2, or storing them plaintext | Plaintext violates the constraint. A slow KDF is pointless for 256-bit random tokens and slows every ingestion request. | SHA-256 of a 32-byte random token, constant-time compare. |
| Free tiers that sleep (Render, Koyeb, Azure F1) | Cold start loses Apple Pay ingestion events. | ~€6/month VPS. |
| PostgreSQL 19 | Still RC2 as of 2026-11-14. | PostgreSQL 18. |
| Watchtower-style auto-updating containers | Uncontrolled deploys, no migration ordering. | Explicit CI deploy step (`compose pull && up -d`). |

## Stack Patterns by Variant

- Build `linux/arm64` images on `ubuntu-24.04-arm` runners, or multi-arch with buildx.
- Migration bundle with `-r linux-arm64`.
- Replace Caddy with a `cloudflared` container (Cloudflare Tunnel), with the domain on Cloudflare DNS.
- Same ASP.NET Core host. Add Telegram Login Widget / Mini App `initData` validation as another auth scheme. Data access already has tenant filters.
- Introduce Quartz.NET 4.x with the Postgres job store (`Quartz` package only; hosting is folded in since 4.0).

## Version Compatibility

| Package | Compatible With | Notes |
|---------|-----------------|-------|
| Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 | Microsoft.EntityFrameworkCore **[10.0.4, 11.0.0)** | Verified from nuspec. EF 11 is **incompatible**. |
| Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime 10.0.3 | Npgsql EF 10.0.3, Npgsql.NodaTime 10.0.3 | Keep all Npgsql packages on the same version. |
| EFCore.NamingConventions 10.0.1 | EF Core [10.0.1, 11.0.0) | Verified from nuspec. |
| ScottPlot 5.1.59 | SkiaSharp 3.119.0, SkiaSharp.HarfBuzz 3.119.0, HarfBuzzSharp.NativeAssets.Linux 8.3.1.1 | Targets net8/9/10. Do not force SkiaSharp 4.x. |
| xunit.v3 4.0.3 | xunit.v3.mtp-v2 4.0.3 (MTP v2) | MTP v1 dropped. On .NET 10 SDK set `test.runner` in global.json. |
| Testcontainers.PostgreSql 4.16.0 | Docker on the CI runner; `postgres:18-alpine` | Match the prod image tag. |
| Serilog.AspNetCore 10.0.0 | net10.0 | 11.x exists only as dev builds. |
| Quartz 4.7.3 (if adopted later) | net10.0 | `Quartz.Extensions.Hosting` contents merged into `Quartz` since 4.0. |
| Microsoft.Extensions.* 11.0.0 (TimeProvider.Testing, Http.Resilience) | Multi-target net8/9/10/11 | Fine on .NET 10. These are not tied to EF/ASP.NET 11. |

## Sources

- **NuGet registry API** (`api.nuget.org/v3-flatcontainer` + registration index, queried 2026-11-14): exact latest versions and publish dates for every package listed, plus nuspec dependency ranges (Npgsql EF → EF `[10.0.4, 11.0.0)`; ScottPlot → SkiaSharp 3.119.0; EFCore.NamingConventions → EF `[10.0.1, 11.0.0)`; xunit.v3 4.0.3 → mtp-v2). **HIGH**
- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core: .NET 10 LTS (10.0.15) and .NET 11 STS (11.0.0) both end support 2028-11-14; STS = 24 months. **HIGH**
- Context7 `/websites/telegrambots_github_io_book`: Telegram.Bot v22.5 migration notes (no `ConfigureTelegramBot` needed, `*Async` methods removed, Telegram.Bot.AspNetCore split). **HIGH**
- Telegram.Bot 22.10.4 nuspec release notes: Bot API 10.4 (2026-11-09 changelog). **HIGH**
- Context7 `/scottplot/scottplot`: `Fonts.AddFontFile`, `plot.Font.Set`, `Add.Pie`, `GetImageBytes`. **HIGH**
- https://xunit.net/releases/v3/4.0.0: MTP v1 dropped, MTP v2 default, global.json runner setting for .NET 10 SDK. **HIGH**
- https://www.quartz-scheduler.net/posts/2026-09-03-quartznet-4.0-released.html and the 4.x migration guide: 4.0 breaking major, hosting folded into `Quartz`, schema migration required. **MEDIUM** (via search summary)
- Docker Hub `library/postgres` tags: 18.7 current, 19rc2 pre-release. **HIGH**
- GitHub releases API: Caddy v2.11.9 (2026-11-05); actions/checkout v7.0.1, setup-dotnet v6.1.0, docker/build-push-action v7.4.0. **HIGH**
- https://github.com/dotnet/dotnet-docker/blob/main/documentation/distroless.md and Microsoft Learn "container images": chiseled images omit ICU/tzdata (invariant mode); `-extra` variants include them. **MEDIUM** (doc content partly .NET 8-era)
- https://letsencrypt.org/2026/01/15/6day-and-ip-general-availability.html: IP and 6-day certificates GA. **HIGH**
- Hetzner price adjustment (docs.hetzner.com price-adjustment page; bitdoze.com, findstack.com, betterstack.com reviews): 15 June 2026 increase, CX23 ≈ €5.49 + €0.50 IPv4. **MEDIUM** (exact figures from secondary sources; verify in console)
- Oracle Always Free docs (docs.oracle.com FreeTier resourceref) + 2026 reports (InfoQ, community forum, blogs): idle reclamation rule; reported A1 quota cut Aug 2026. **LOW–MEDIUM**
- render.com free-tier article, flaviocopes.com hosting free tiers (Sept 2026), agentdeals.dev: Render 15-min spin-down, Fly.io no free tier for new users, Koyeb restricted. **MEDIUM**
- Licensing changes for MediatR/AutoMapper (Lucky Penny, 2025) and FluentAssertions v8 (Xceed, 2025): from training knowledge, not re-verified this session. **MEDIUM**

<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->

## Conventions

Conventions not yet established. Will populate as patterns emerge during development.
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->

## Architecture

Architecture not yet mapped. Follow existing patterns found in the codebase.
<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->

## Project Skills

No project skills found. Add skills to any of: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, `.github/skills/`, or `.codex/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->

## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd-fast` for a trivial task inline, with no subagents and no PLAN.md
- `/gsd-quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd-debug` for investigation and bug fixing
- `/gsd-execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->

<!-- GSD:profile-start -->

## Developer Profile

> Profile not yet configured. Run `/gsd-profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
