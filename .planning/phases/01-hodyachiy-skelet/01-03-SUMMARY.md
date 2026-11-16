---
phase: 01-hodyachiy-skelet
plan: 03
subsystem: observability
status: complete
tags: [serilog, logging, privacy, github-actions, gitleaks, ci]
requires:
  - phase: 01-01
    provides: "ApiFactory fixture (Production env, NSubstitute bot), Ingest.Handle with 'Telegram send failed' warning"
provides:
  - "Serilog compact JSON to stdout, levels fixed in code (Information; AspNetCore/EF/HttpClient = Warning)"
  - "UseSerilogRequestLogging: one line per request without body or headers"
  - "CapturingSink + ApiFactory.Logs: tests see the exact prod log output"
  - "LogPrivacyTests: PRIV-03 proven on the prod log configuration"
  - ".github/workflows/ci.yml: gitleaks over full history, .env ignore check, dotnet test on push(main)/pull_request"
affects: [01-04, 01-05, 01-06]
tech-stack:
  added: ["Serilog.AspNetCore 10.0.0 (already pinned in Directory.Packages.props)", "gitleaks/gitleaks-action@v3", "actions/checkout@v7", "actions/setup-dotnet@v6"]
  patterns: ["log levels in code, never in config", "ReadFrom.Services as the test hook into the prod log pipeline", "ASCII sentinels for log-leak tests"]
key-files:
  created:
    - tests/FinanceBot.Tests/CapturingSink.cs
    - tests/FinanceBot.Tests/LogPrivacyTests.cs
    - .github/workflows/ci.yml
  modified:
    - src/FinanceBot/FinanceBot.csproj
    - src/FinanceBot/Program.cs
    - src/FinanceBot/appsettings.json
    - src/FinanceBot/appsettings.Development.json
    - tests/FinanceBot.Tests/ApiFactory.cs
key-decisions:
  - "Serilog levels set in code (Information; Microsoft.AspNetCore, Microsoft.EntityFrameworkCore, System.Net.Http.HttpClient = Warning); no Serilog config section is read, so env vars cannot raise verbosity in prod"
  - "Logging sections removed from appsettings*.json: Serilog replaces the logger factory and ignores them, keeping them would suggest levels are tunable there"
  - "LogPrivacyTests send their four requests once per run (static Task) to stay under the shared 30/min ingest limit"
requirements-completed: [PRIV-03, FND-05]
duration: 4 min
completed: 2026-11-16
estimate:
  tokens: 40000
  tasks: 2
actuals:
  tokens: 2400
  tasks: 2
  commits: 3
plan_head_before: 9f22d9f650357084ad0e5c02d5f00901020d76b9
plan_head_after: e3eb448e2150a56edc988d70c41462b75419c962
coverage:
  - id: D1
    description: "Logs contain no merchant, card, raw body, amount (987,65 / 987.65), ingest token, bot token or wrong token across success, Telegram failure, 401 and malformed JSON"
    requirement: PRIV-03
    verification:
      - kind: integration
        ref: "tests/FinanceBot.Tests/LogPrivacyTests.cs#NoSensitiveDataInLogs"
        status: pass
    human_judgment: false
  - id: D2
    description: "Log capture is not empty: request-logging line with /api/v1/transactions and the 'Telegram send failed' warning are present"
    requirement: PRIV-03
    verification:
      - kind: integration
        ref: "tests/FinanceBot.Tests/LogPrivacyTests.cs#LogsAreCaptured"
        status: pass
    human_judgment: false
  - id: D3
    description: "Full history has no secrets and .env is ignored (local run of the same checks CI runs)"
    requirement: FND-05
    verification:
      - kind: other
        ref: "gitleaks git --no-banner --redact . (37 commits, no leaks found, exit 0); git check-ignore -q .env (exit 0)"
        status: pass
    human_judgment: false
  - id: D4
    description: "Workflow ci is green on GitHub for a PR into main"
    requirement: FND-05
    verification:
      - kind: other
        ref: "actionlint ci.yml (exit 0) — syntax only"
        status: pass
    human_judgment: true
    rationale: "Not pushed by instruction; the real run (gitleaks-action, Testcontainers on ubuntu-latest) is checked when the phase branch is pushed and a PR is opened (plan 01-06 or /gsd-ship): gh run list --workflow ci.yml --limit 1 must show success."
---

# Phase 1 Plan 03: Логи без денег и токенов + CI Summary

**Serilog пишет compact JSON в stdout с уровнями, зашитыми в код (HttpClient/EF/AspNetCore на Warning, чтобы не утёк URL Telegram с токеном бота), а LogPrivacyTests на прод-конфигурации доказывает, что ни сумма, ни продавец, ни карта, ни тело, ни токены в лог не попадают; GitHub Actions на push в main и PR гоняет gitleaks по всей истории, проверку `.env` в .gitignore и все 56 тестов.**

## Performance

- **Duration:** 4 min (2026-11-16T21:03:54Z → 2026-11-16T21:07:25Z)
- **Tasks:** 2/2
- **Files:** 3 созданных, 5 изменённых
- **Tests:** `dotnet test` → итог: 56, сбой: 0, успешно: 56, exit 0 (≈10 с)

## Accomplishments

- `AddSerilog((sp, lc) => …)`: Information по умолчанию, Warning для `Microsoft.AspNetCore`, `Microsoft.EntityFrameworkCore`, `System.Net.Http.HttpClient`; `ReadFrom.Services(sp)`; `WriteTo.Console(new RenderedCompactJsonFormatter())`. Bootstrap-логгера нет.
- `app.UseSerilogRequestLogging()` первым middleware: одна строка на запрос (метод, путь, статус, время).
- `CapturingSink` форматирует события тем же форматтером, что прод; `ApiFactory.Logs` регистрируется как `ILogEventSink`.
- `LogPrivacyTests`: успех с сентинелами, сбой Telegram, неверный токен, кривой JSON → 9 запрещённых подстрок не найдены; лог не пуст.
- `.github/workflows/ci.yml`: checkout `fetch-depth: 0` → gitleaks-action@v3 → `git check-ignore -q .env` → setup-dotnet по global.json → `dotnet test`. Шагов деплоя нет.

## Task Commits

1. **Task 1 (TDD): PRIV-03**
   - `7d705e7` test(01-03): RED — failing log privacy test with ASCII sentinels
   - `7de0142` feat(01-03): GREEN — Serilog with levels in code and request logging (REFACTOR не понадобился)
2. **Task 2: CI** — `e3eb448` chore(01-03): CI workflow with gitleaks, .env ignore check and tests

## TDD Gate Compliance

RED (`7d705e7`, Program.cs ещё без Serilog): `dotnet test --filter-class "*LogPrivacyTests"` → итог 2, сбой 1, успешно 1, exit 2.

| Тест | RED | Причина |
|------|-----|---------|
| LogsAreCaptured | сбой | `no log events captured` (LogPrivacyTests.cs:61): Serilog не подключён, sink через `ReadFrom.Services` не получает событий. Это запланированное утверждение, не сборка и не фикстура. |
| NoSensitiveDataInLogs | зелёный (пусто) | На пустом логе запретов не нарушить. Поэтому отдельный LogsAreCaptured и требует непустой лог. |

GREEN (`7de0142`): 2/2, полный набор 56/56. Мутационная проверка после GREEN: временно дописал `tx.Merchant` в предупреждение о сбое Telegram → `NoSensitiveDataInLogs` упал с `Found: "SENTINEL-MERCHANT"`; правка откатана. Классификатор `tdd-red-evidence` не понимает консоль xUnit v3/MTP (как и в 01-01), поэтому RED записан здесь вручную по реальному выводу.

## Почему так (для собеседования)

- **Уровни логов в коде, а не в appsettings.** Если уровни читаются из конфигурации, любая переменная окружения вроде `Serilog__MinimumLevel=Debug` на сервере включит подробные логи EF и HttpClient, и в `docker logs` поедут данные. В коде их можно поменять только новым коммитом, который пройдёт ревью и тест. Секции `Logging` из appsettings я удалил: Serilog их игнорирует, и они только вводили бы в заблуждение.
- **Токен бота и HttpClient.** Telegram.Bot кладёт токен прямо в путь URL (`/bot<TOKEN>/sendMessage`), а .NET скрывает в логах только query-строку, не путь. Поэтому `System.Net.Http.HttpClient` поднят до Warning, а `TelegramBotClient` создаётся со своим HttpClient вне фабрики (это было в 01-01). Тест ищет фейковый токен бота в логе и не находит.
- **Сентинелы только ASCII.** JSON-форматтер может экранировать не-ASCII символы (`ż` → `ż`), и поиск «Żabka» по логу ничего не нашёл бы даже при утечке: тест был бы ложно зелёным. Строки вроде `SENTINEL-MERCHANT-7F3A` в JSON не меняются.
- **`fetch-depth: 0` для gitleaks.** По умолчанию checkout берёт только последний коммит. Секрет, закоммиченный и «удалённый» следующим коммитом, остаётся в истории публичного репо, и без полной истории gitleaks его не увидит.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical] Удалены секции `Logging` из appsettings.json и appsettings.Development.json**
- **Found during:** Task 1 (GREEN)
- **Issue:** после `AddSerilog` эти секции не действуют, но выглядят как место, где настраиваются уровни (план разрешал удалить, если мешают).
- **Fix:** секции удалены, `AllowedHosts` и строка подключения для разработки оставлены.
- **Files modified:** src/FinanceBot/appsettings.json, src/FinanceBot/appsettings.Development.json
- **Commit:** 7de0142

**2. [Rule 3 - Blocking] PackageReference Serilog.AspNetCore добавлен в RED-коммит**
- **Issue:** тестовый проект не скомпилировался бы без типов `ILogEventSink` и `RenderedCompactJsonFormatter`, а RED должен падать на утверждении, не на сборке.
- **Fix:** ссылка на пакет в RED-коммите, код Serilog в Program.cs только в GREEN.
- **Commit:** 7d705e7

**Total deviations:** 2 auto-fixed (1 Rule 2, 1 Rule 3). **Impact:** поведение по плану, лишнего кода нет.

## Issues Encountered

- PyYAML не установлен, поэтому ci.yml проверен через `rhysd/actionlint` в Docker: exit 0. Это проверка синтаксиса и известных actions, а не реальный прогон на GitHub.
- Rate limit 30/мин не помешал: LogPrivacyTests шлёт 4 запроса один раз на прогон (вместе с IngestTests 15 запросов в минуту).

## User Setup Required

None. Пушить и открывать PR в этом плане не нужно.

## Next Phase Readiness

Готово к 01-04/01-06. Открытый пункт: зелёный прогон workflow `ci` на GitHub проверяется при первом PR ветки фазы в main (`gh run list --workflow ci.yml --limit 1` → success, в логе gitleaks «no leaks found»). Остаточный риск A11: детали ошибок Postgres в тесте не моделируются; закрыто тем, что в строке подключения нет `Include Error Detail`.

## Self-Check: PASSED

- Файлы существуют: CapturingSink.cs, LogPrivacyTests.cs, .github/workflows/ci.yml.
- Коммиты 7d705e7, 7de0142, e3eb448 есть в HEAD; `git rev-list --count 9f22d9f..HEAD` = 3.
- Acceptance criteria обеих задач перепроверены grep/командами: все PASS (AddSerilog 1, HttpClient 1, ReadFrom.Services 1, запрещённые флаги 0, SENTINEL-CARD 3; ci.yml: 6 обязательных строк по 1, шагов деплоя 0; gitleaks exit 0; check-ignore exit 0).
