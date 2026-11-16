---
gsd_state_version: "1.0"
current_phase: 01
current_phase_name: Ходячий скелет
status: executing
stopped_at: Completed 01-03-PLAN.md
last_updated: "2026-11-16T21:08:27.949Z"
last_activity: 2026-11-16
last_activity_desc: Phase 01 execution started
state_head: f2d815b52cb77cef4683e4461792b151a3d698b6
progress:
  total_phases: 7
  completed_phases: 0
  total_plans: 6
  completed_plans: 3
  percent: 0
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-11-14)

**Core value:** Каждая оплата Apple Pay автоматически и без дублей попадает в бот, и я в любой момент вижу, сколько осталось от недельного лимита (200 zł).
**Current focus:** Phase 01 — Ходячий скелет

## Current Position

Phase: 01 (Ходячий скелет) — EXECUTING
Plan: 4 of 6
Status: Ready to execute
Last activity: 2026-11-16 — Phase 01 execution started

Progress: [░░░░░░░░░░] 0%

## Performance Metrics

**Velocity:**
- Total plans completed: 0
- Average duration: -
- Total execution time: 0 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| - | - | - | - |

**Recent Trend:**
- Last 5 plans: -
- Trend: -

*Updated after each plan completion*
**Per-Plan Metrics:**

| Plan | Duration | Tasks | Files |
|------|----------|-------|-------|
| Phase 01 P01 | 7 min | 2 tasks | 20 files |
| Phase 01 P02 | 3 min | 2 tasks | 4 files |
| Phase 01 P03 | 4 min | 2 tasks | 8 files |

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- [Roadmap 2026-11-16]: Порядок фаз перестроен по схеме walking skeleton: Phase 1 — сквозной путь оплата → Oracle → Postgres → Telegram с остатком лимита (один проект, один пользователь, токен из .env); Phase 2 — CI/CD, migrator, бэкапы, слои; мультипользовательность (ACC-01..06) перенесена в Phase 7, ACC-07 — в Phase 3. Причина: интеграционные риски проверяются первыми, ботом можно пользоваться сразу
- [Roadmap 2026-11-16]: `user_id` во всех таблицах с первой миграции, чтобы мультипользовательность в Phase 7 не требовала переделки схемы
- [Spike 2026-11-16]: Спайк (SPIKE-01/02) вынесен из Phase 1 в предусловие Phase 3. Формат payload'ов не определён; данные пассивно собирает автоматизация «Транзакция» → «Заметки», пока идут фазы 1–2
- [Roadmap]: Хостинг: Oracle Cloud Always Free VM + Docker Compose + DuckDNS (не Hetzner); бэкап (FND-06) делается в Phase 2, сразу после появления реальных данных
- [Review 261115-68j]: Аккаунт Oracle — на Pay As You Go; бэкап шифрованный и вне Oracle (B2/R2)
- [Review 261115-68j]: Команда повторяет отправку 2–3 раза с одним Idempotency-Key; ING-09 (не-PLN → запрос суммы) и HLTH-01 (напоминание) входят в v1
- [Review 261115-68j]: LIM-07 убран, неделя фиксированно пн–вс; публичного демо-инвайта нет; PRIV-03 перенесён в Phase 1
- [Review 261115-68j]: Поведение дублей по умолчанию выбирается по данным спайка до планирования Phase 3
- [Review 261115-68j]: Баланс (BAL) остаётся в v1 — решение пользователя; MVP = Apple Pay + ручной ввод, затем Open Banking
- [Balance]: Рядом с балансом показывается давность последней сверки `/balance N` (BAL-04)
- [Roadmap]: Баланс (BAL) объединён с лимитами в Phase 5: оба считаются из истории трат
- [Roadmap]: Приватность (PRIV-01, PRIV-02) вынесена в Phase 7, когда известны все хранимые данные
- [Phase 01]: amount IS NULL is the 'amount not parsed' status; no status column until Phase 3
- [Phase 01]: Remainder uses the week of the purchase time (Europe/Warsaw), not server arrival
- [Phase 01]: EF Core pinned to 10.0.15 transitively (CentralPackageTransitivePinningEnabled) so app and tests share one EF version
- [Phase 01]: Rate limit is one global fixed window 'ingest' 30/min; body limit 64 KB in Kestrel
- [Phase 01]: Overspend starts strictly above the limit: spent == 200 stays green with 0,00
- [Phase 01]: Unparsed raw amount is truncated to 100 chars + ellipsis before HTML escaping
- [Phase 01]: Serilog levels fixed in code (Information; AspNetCore/EF/HttpClient=Warning), no Serilog config section; Logging sections removed from appsettings
- [Phase 01]: CI ci.yml: push(main)+pull_request, gitleaks-action@v3 over full history, git check-ignore .env, dotnet test; no deploy job until Phase 2

### Pending Todos

- После появления полного CI (Phase 2): дополнить GitHub ruleset "Protect main" правилами required status checks (+ решить про require pull request). Сейчас активны только deletion и non_fast_forward.

### Blockers/Concerns

- [Phase 3]: Поведение iOS-автоматизации «Транзакция» не документировано Apple; редкие случаи (возврат, валюта, авиарежим) могут не воспроизвестись быстро, поэтому их нужно явно помечать и выбирать для них политику
- [Phase 1]: Oracle Always Free может забрать простаивающую VM (reclamation) или заблокировать аккаунт; перед деплоем проверить по документации Oracle, что на Pay As You Go отбор не действует. Ampere A1 — это ARM64 (нужны arm64-образы и проверка нативных библиотек SkiaSharp)
- [Phase 5]: Open Decisions из REQUIREMENTS.md нужно закрыть через `/gsd-discuss-phase 5` до планирования
- [Phase 3]: Поведение дублей по умолчанию (ING-06) нужно выбрать по данным спайка до планирования фазы

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 261115-68j | Правки планировочных документов по итогам ревью | 2026-11-15 | 1f78743 | [261115-68j-review-fixes-planning-docs](./quick/261115-68j-review-fixes-planning-docs/) |
| 2 | Спайк payload'ов перенесён из Phase 1 в предусловие Phase 3 | 2026-11-16 | 1abb7cd | — |
| 3 | ROADMAP перестроен по схеме walking skeleton | 2026-11-16 | 95933b9 | — |

## Deferred Items

Items acknowledged and deferred at milestone close, most recent first:

| Category | Item | Status | Deferred At | Milestone |
|----------|------|--------|-------------|-----------|
| *(none)* | | | | |

## Session Continuity

Last session: 2026-11-16T21:08:27.901Z
Stopped at: Completed 01-03-PLAN.md
Resume file: None
