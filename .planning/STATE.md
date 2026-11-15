---
gsd_state_version: "1.0"
current_phase: 1
current_phase_name: Фундамент и разведка iOS-Команды
status: planning
stopped_at: Roadmap and STATE initialized; next step is `/gsd-plan-phase 1`
last_updated: "2026-11-15T03:32:17.974Z"
last_activity: 2026-11-15
last_activity_desc: "Completed quick task 261115-68j: правки планировочных документов по итогам ревью (53/53 требований сопоставлены с фазами)"
state_head: 1f78743ad89f2121dd369fe70e908271a4004c50
progress:
  total_phases: 7
  completed_phases: 0
  total_plans: 0
  completed_plans: 0
  percent: 0
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-11-14)

**Core value:** Каждая оплата Apple Pay автоматически и без дублей попадает в бот, и я в любой момент вижу, сколько осталось от недельного лимита (200 zł).
**Current focus:** Phase 1: Фундамент и разведка iOS-Команды

## Current Position

Phase: 1 of 7 (Фундамент и разведка iOS-Команды)
Plan: 0 of 0 in current phase
Status: Ready to plan
Last activity: 2026-11-15 - BAL-04: рядом с балансом показывается давность последней сверки

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

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- [Roadmap]: Спайк payload'ов iOS-Команды (SPIKE-01/02) входит в Phase 1 и завершается до проектирования парсера и идемпотентности (Phase 3)
- [Roadmap]: Хостинг: Oracle Cloud Always Free VM + Docker Compose + DuckDNS (не Hetzner); бэкап (FND-06) делается в Phase 1 из-за риска reclamation
- [Review 261115-68j]: Аккаунт Oracle — на Pay As You Go; бэкап шифрованный и вне Oracle (B2/R2)
- [Review 261115-68j]: Команда повторяет отправку 2–3 раза с одним Idempotency-Key; ING-09 (не-PLN → запрос суммы) и HLTH-01 (напоминание) входят в v1
- [Review 261115-68j]: LIM-07 убран, неделя фиксированно пн–вс; публичного демо-инвайта нет; PRIV-03 перенесён в Phase 1
- [Review 261115-68j]: Поведение дублей по умолчанию выбирается по данным спайка до планирования Phase 3
- [Review 261115-68j]: Баланс (BAL) остаётся в v1 — решение пользователя; MVP = Apple Pay + ручной ввод, затем Open Banking
- [Balance]: Рядом с балансом показывается давность последней сверки `/balance N` (BAL-04)
- [Roadmap]: Баланс (BAL) объединён с лимитами в Phase 5: оба считаются из истории трат
- [Roadmap]: Приватность (PRIV-01, PRIV-02) вынесена в Phase 7, когда известны все хранимые данные

### Pending Todos

- После появления CI (Phase 1): дополнить GitHub ruleset "Protect main" правилами required status checks (+ решить про require pull request). Сейчас активны только deletion и non_fast_forward.

### Blockers/Concerns

- [Phase 1]: Поведение iOS-автоматизации «Транзакция» не документировано Apple; редкие случаи (возврат, валюта, авиарежим) могут не воспроизвестись быстро, поэтому их нужно явно помечать и выбирать для них политику
- [Phase 1]: Oracle Always Free может забрать простаивающую VM (reclamation) или заблокировать аккаунт; перед деплоем проверить по документации Oracle, что на Pay As You Go отбор не действует. Ampere A1 — это ARM64 (нужны arm64-образы и проверка нативных библиотек SkiaSharp)
- [Phase 5]: Open Decisions из REQUIREMENTS.md нужно закрыть через `/gsd-discuss-phase 5` до планирования
- [Phase 3]: Поведение дублей по умолчанию (ING-06) нужно выбрать по данным спайка до планирования фазы

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 261115-68j | Правки планировочных документов по итогам ревью | 2026-11-15 | 1f78743 | [261115-68j-review-fixes-planning-docs](./quick/261115-68j-review-fixes-planning-docs/) |

## Deferred Items

Items acknowledged and deferred at milestone close, most recent first:

| Category | Item | Status | Deferred At | Milestone |
|----------|------|--------|-------------|-----------|
| *(none)* | | | | |

## Session Continuity

Last session: 2026-11-15
Stopped at: Roadmap and STATE initialized; next step is `/gsd-plan-phase 1`
Resume file: None
