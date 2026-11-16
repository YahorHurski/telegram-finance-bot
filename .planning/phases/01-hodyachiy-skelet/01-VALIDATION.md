---
phase: "1"
slug: "hodyachiy-skelet"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: draft
nyquist_compliant: false
wave_0_complete: false
created: "2026-11-16"
---

# Phase 1 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 4.0.3 на Microsoft Testing Platform v2 (`global.json` → `test.runner`), WebApplicationFactory + Testcontainers.PostgreSql (postgres:18-alpine) + NSubstitute |
| **Config file** | нет — Wave 0 (трассер 01-01) создаёт `global.json`, `tests/FinanceBot.Tests/FinanceBot.Tests.csproj` (OutputType Exe), коллекцию `Api` |
| **Quick run command** | `dotnet test --filter-class "*ParsingTests" --filter-class "*WeekTests" --filter-class "*MessagesTests"` (unit, без Docker) |
| **Full suite command** | `dotnet test` (нужен запущенный Docker) |
| **Estimated runtime** | ~40 секунд на полный набор, большую часть занимает старт контейнера [ASSUMED]; unit-набор ~5 секунд |

Проверено при планировании (скретч-проект, SDK 10.0.301): `dotnet test --filter-class "*X"` работает без `--`; несколько `--filter-class` объединяются через ИЛИ; фильтр без совпадений завершает процесс с кодом 8, поэтому пустой прогон не проходит молча.

---

## Sampling Rate

- **After every task commit:** команда `<automated>` этой задачи (unit-фильтр или класс интеграционных тестов)
- **After every plan wave:** `dotnet test` целиком (Docker запущен)
- **Before `/gsd-verify-work`:** полный набор зелёный, CI зелёный на PR, `curl -fsS https://<поддомен>.duckdns.org/health` = Healthy, одна реальная оплата (UAT)
- **Max feedback latency:** ~60 секунд (интеграционные тесты), ~5 секунд (unit)

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 1-01-01 | 01 | 1 | ING-03, MAN-03, FND-05 | T-01-01, T-01-06, T-01-SC | токен проверяется до чтения тела (SHA-256 + FixedTimeEquals); `.env` игнорируется с первого коммита | integration (tracer) | `dotnet test --filter-class "*IngestTests"` | ❌ W0 (создаётся задачей) | ⬜ pending |
| 1-01-02 | 01 | 1 | ING-03, MAN-03 | T-01-01, T-01-02, T-01-04, T-01-05 | 401 без следов в БД и Telegram; кривой запрос сохраняется; 201 после сохранения даже при сбое Telegram; 30/мин и 64 KB | integration | `dotnet test --filter-class "*IngestTests" --filter-class "*SchemaTests"` | ❌ W0 | ⬜ pending |
| 1-02-01 | 02 | 2 | ING-03, MAN-03 | T-01-07 | чужая валюта, ≤ 0, ≥ 1 000 000 → null и не идут в лимит | unit | `dotnet test --filter-class "*ParsingTests" --filter-class "*WeekTests"` | ❌ | ⬜ pending |
| 1-02-02 | 02 | 2 | MAN-03 | T-01-08 | экранирование `&<>`, обрезка исходной строки до 100 символов | unit (tdd) | `dotnet test --filter-class "*MessagesTests"` | ❌ | ⬜ pending |
| 1-03-01 | 03 | 2 | PRIV-03 | T-01-09 | в логах нет сумм, продавцов, карт, тел и токенов; лог не пуст | integration (tdd) | `dotnet test --filter-class "*LogPrivacyTests"` | ❌ | ⬜ pending |
| 1-03-02 | 03 | 2 | FND-05 | T-01-10, T-01-11 | gitleaks по всей истории, `.env` в .gitignore | static / CI | `gitleaks git --no-banner --redact .` и `git check-ignore -q .env` | ✅ gitleaks 8.30.1 установлен | ⬜ pending |
| 1-04-01 | 04 | 3 | FND-02, FND-05 | T-01-12, T-01-13, T-01-14, T-01-15 | порты только у caddy; не root; `.env` вне образа; логи контейнера чистые | smoke (docker) | `docker compose config --quiet`, проверка портов через `docker compose config --format json`, `curl -kfsS https://localhost/health` | ❌ | ⬜ pending |
| 1-04-02 | 04 | 3 | FND-02 | T-01-16 | в документации нет секретов | static | `bash -n deploy.sh`, `git ls-files -s deploy.sh`, `gitleaks dir --no-banner --redact docs` | ❌ | ⬜ pending |
| 1-04-03 | 04 | 3 | FND-02 (ROADMAP Notes, arm64) | T-01-SC | ScottPlot проверен на nuget.org до restore | smoke (docker) | `docker run --rm skia-spike`, `docker run --rm --platform linux/arm64 skia-spike-arm64` | ❌ | ⬜ pending |
| 1-05-01 | 05 | 4 | FND-02 | — | N/A (решение по формулировке PAYG) | manual (checkpoint:decision) | — | — | ⬜ pending |
| 1-05-02 | 05 | 4 | FND-02 | T-01-17, T-01-20 | SSH только по ключу; ресурсы только Always Free; бюджетный алерт | manual (checkpoint:human-action) | проверка агентом: `ssh -o BatchMode=yes ubuntu@VM_IP uname -m` | — | ⬜ pending |
| 1-05-03 | 05 | 4 | FND-02 | T-01-17, T-01-18, T-01-21 | `.env` 600, секреты сгенерированы на VM; порт 80 проверен до Caddy | remote smoke | `ssh ... "stat -c %a ~/finance-bot/.env"`, `ssh ... "sudo iptables -C INPUT ..."`, `curl ... http://SUBDOMAIN.duckdns.org/` | — | ⬜ pending |
| 1-06-01 | 06 | 5 | FND-02, MAN-03 | T-01-22 | токен бота не покидает VM | manual (checkpoint:human-action) | проверка агентом: `ssh ... "grep -cE '^Bot__TelegramToken=...' ~/finance-bot/.env"` | — | ⬜ pending |
| 1-06-02 | 06 | 5 | FND-02, ING-03, MAN-03, PRIV-03 | T-01-23, T-01-24, T-01-25, T-01-26 | HTTPS с доверенным сертификатом, 401, 5432 закрыт, логи прода чистые, возврат после reboot | remote smoke + UAT | `curl -fsS https://SUBDOMAIN.duckdns.org/health` и проверки из плана; human-check: реальная оплата | — | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `.gitignore` со строкой `.env` — шаг 0 трассера 01-01 (до любого другого файла)
- [ ] `global.json`, `Directory.Packages.props`, `FinanceBot.slnx`, `.config/dotnet-tools.json` — трассер 01-01
- [ ] `tests/FinanceBot.Tests/FinanceBot.Tests.csproj` (xunit.v3, Mvc.Testing 10.0.15, Testcontainers.PostgreSql, NSubstitute, OutputType Exe) — трассер 01-01
- [ ] `tests/FinanceBot.Tests/ApiFactory.cs` + коллекция `Api` — трассер 01-01
- [ ] `tests/FinanceBot.Tests/CapturingSink.cs` — 01-03 задача 1
- [ ] `.github/workflows/ci.yml` — 01-03 задача 2
- [ ] Локально: запущенный Docker Desktop для интеграционных тестов и smoke (предусловие задач 1-01-01, 1-01-02, 1-03-01, 1-04-01, 1-04-03)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Аккаунт Oracle на PAYG, бюджет с alert rule | FND-02 | у агента нет доступа к консоли Oracle | 01-05 задача 2: пользователь делает по docs/deploy.md шаг 3, результат записывается в SUMMARY |
| PAYG защищает от отбора простаивающих VM | FND-02 (SC-1) | официальной документации нет, только письмо Oracle 2023 (backstop-truth) | 01-05 задача 1: решение пользователя по формулировке |
| Реальная оплата Apple Pay → сообщение в Telegram с остатком | ING-03, MAN-03 (цель фазы) | нужен iPhone и настоящая оплата | 01-06 задача 2, human-check: оплатить, проверить две строки D-01 и строку в БД |
| «Трата не сохранена» на iPhone при ошибке | D-09 | поведение Команд известно только на устройстве (RESEARCH A8) | 01-06 задача 2, human-check: временно испортить токен в Команде |
| CI зелёный на GitHub | FND-05, SC-5 | нужен пуш и PR (только с разрешения пользователя) | 01-03 задача 2 / 01-06 задача 2, human-check: `gh run list --workflow ci.yml --limit 1` = success |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies (кроме чекпоинтов 1-05-01, 1-05-02, 1-06-01: у них проверка агентом в `<verification>`)
- [x] Sampling continuity: no 3 consecutive tasks without automated verify (максимум две подряд: 1-05-01, 1-05-02)
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [ ] Feedback latency < 60s (проверяется при исполнении)
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** {pending / approved YYYY-MM-DD}
