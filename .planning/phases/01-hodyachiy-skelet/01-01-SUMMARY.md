---
phase: 01-hodyachiy-skelet
plan: 01
subsystem: ingest
status: complete
tags: [aspnetcore, minimal-api, efcore, postgres, nodatime, telegram, testcontainers, xunit-v3]
requires: []
provides:
  - "POST /api/v1/transactions: Bearer token -> raw body saved -> week remainder -> SendMessage to owner -> 201 {id}"
  - "AppDb with users/transactions (user_id from the first migration), migration Initial"
  - "ApiFactory test fixture: WebApplicationFactory<Program> + Testcontainers postgres:18-alpine + NSubstitute ITelegramBotClient, collection Api"
  - "Pure functions Parsing.Amount/Time, Week.Containing, Messages.Confirmation/Money/Esc"
affects: [01-02, 01-03, 01-04, 01-05, 01-06]
tech-stack:
  added: [".NET 10 SDK 10.0.301", "Telegram.Bot 22.10.4", "EF Core 10.0.15", "Npgsql EF NodaTime 10.0.3", "EFCore.NamingConventions 10.0.1", "NodaTime 3.3.7", "xunit.v3 4.0.3", "Testcontainers.PostgreSql 4.16.0", "NSubstitute 6.2.0", "Microsoft.AspNetCore.Mvc.Testing 10.0.15", "dotnet-ef 10.0.15 (local tool)"]
  patterns: ["Central Package Management with transitive pinning", "raw body read by hand, token checked first", "config read inside DI factories", "send to Telegram strictly after SaveChanges, always 201"]
key-files:
  created:
    - .gitignore
    - global.json
    - Directory.Packages.props
    - FinanceBot.slnx
    - .config/dotnet-tools.json
    - src/FinanceBot/FinanceBot.csproj
    - src/FinanceBot/Program.cs
    - src/FinanceBot/Ingest.cs
    - src/FinanceBot/AppDb.cs
    - src/FinanceBot/Parsing.cs
    - src/FinanceBot/Week.cs
    - src/FinanceBot/Messages.cs
    - src/FinanceBot/appsettings.json
    - src/FinanceBot/appsettings.Development.json
    - src/FinanceBot/Properties/launchSettings.json
    - src/FinanceBot/Migrations/20261116205018_Initial.cs
    - tests/FinanceBot.Tests/FinanceBot.Tests.csproj
    - tests/FinanceBot.Tests/ApiFactory.cs
    - tests/FinanceBot.Tests/IngestTests.cs
    - tests/FinanceBot.Tests/SchemaTests.cs
  modified: []
key-decisions:
  - "amount IS NULL is the 'amount not parsed' status; no separate status column until Phase 3"
  - "Week of the purchase (time field, Europe/Warsaw), not of server arrival, decides the remainder"
  - "CentralPackageTransitivePinningEnabled + pinned Microsoft.EntityFrameworkCore/Relational 10.0.15 so app and tests load one EF version"
  - "Rate limit is one global fixed window 'ingest' 30/min (single user in Phase 1), body limit 64 KB in Kestrel"
requirements-completed: [ING-03, MAN-03, FND-05]
duration: 7 min
completed: 2026-11-16
estimate:
  tokens: 75000
  tasks: 2
actuals:
  tokens: 12300
  tasks: 2
  commits: 6
plan_head_before: b577cf2b7d4c2c74bed60e1206128a6dcf98ea8c
plan_head_after: bae4b54ba7672edae3ce6d19e8cb46057d928a2f
coverage:
  - deliverable: "Happy path: token -> Postgres row -> SendMessage to owner with D-01 text -> 201 {id}"
    human_judgment: false
    verification:
      - kind: test
        ref: "tests/FinanceBot.Tests/IngestTests.cs#ValidToken_SavesRowAndSendsConfirmation"
        status: pass
  - deliverable: "401 without/with wrong/non-Bearer token, no row, no Telegram call"
    human_judgment: false
    verification:
      - kind: test
        ref: "tests/FinanceBot.Tests/IngestTests.cs#MissingToken_Returns401WithoutSideEffects"
        status: pass
      - kind: test
        ref: "tests/FinanceBot.Tests/IngestTests.cs#WrongToken_Returns401WithoutSideEffects"
        status: pass
      - kind: test
        ref: "tests/FinanceBot.Tests/IngestTests.cs#NonBearerScheme_Returns401"
        status: pass
  - deliverable: "Malformed/unparsed requests saved with raw body, amount NULL, ⚠️ message, excluded from limit"
    human_judgment: false
    verification:
      - kind: test
        ref: "tests/FinanceBot.Tests/IngestTests.cs#InvalidJson_SavedAsUnparsed"
        status: pass
      - kind: test
        ref: "tests/FinanceBot.Tests/IngestTests.cs#AmountAsNumber_SavedAsUnparsed"
        status: pass
      - kind: test
        ref: "tests/FinanceBot.Tests/IngestTests.cs#UnparsedAmount_WarnsAndStaysOutOfTheLimit"
        status: pass
      - kind: test
        ref: "tests/FinanceBot.Tests/IngestTests.cs#UnparsedTime_FallsBackToReceivedAt"
        status: pass
  - deliverable: "Telegram failure still returns 201 and keeps the row"
    human_judgment: false
    verification:
      - kind: test
        ref: "tests/FinanceBot.Tests/IngestTests.cs#TelegramFailure_StillReturns201AndKeepsRow"
        status: pass
  - deliverable: "user_id in every user data table (SC-4)"
    human_judgment: false
    verification:
      - kind: test
        ref: "tests/FinanceBot.Tests/SchemaTests.cs#AllUserDataTablesHaveUserId"
        status: pass
  - deliverable: "Rate limit 30/min -> 429 and 64 KB body limit"
    human_judgment: true
    rationale: "No automated test by plan design (TestServer ignores Kestrel limits; 31 requests would break the shared window). Present in code (grep); manual check planned in 01-04."
  - deliverable: ".env ignored from the first code commit (FND-05)"
    human_judgment: false
    verification:
      - kind: command
        ref: "git check-ignore -q .env (exit 0); .gitignore in commit f9dfbed"
        status: pass
---

# Phase 1 Plan 01: Трассер приёма траты Summary

**Одна трата проходит весь путь в коде: POST /api/v1/transactions с Bearer-токеном → строка в настоящем Postgres 18 → SendMessage владельцу с остатком недели (Europe/Warsaw) → 201 {id}; кривые запросы не теряются, сбой Telegram не ломает ответ Команде. Доказано 10 интеграционными тестами на Testcontainers.**

## Performance

- **Duration:** 7 min (2026-11-16T20:47:30Z → 2026-11-16T20:54:56Z)
- **Tasks:** 2/2 (трассер в 4 этапах A–D + расширение TDD)
- **Files:** 20 созданных файлов кода и конфигурации (+ 2 сгенерированных файла миграции)
- **Tests:** `dotnet test --filter-class "*IngestTests" --filter-class "*SchemaTests"` → итог: 10, сбой: 0, успешно: 10, exit 0 (≈35 с, большая часть — старт контейнера)

## Accomplishments

- Решение `FinanceBot.slnx`: `src/FinanceBot` (minimal API) + `tests/FinanceBot.Tests` (xUnit v3, MTP). SDK закреплён на 10.0.x без preview, все пакеты 10.x через Central Package Management.
- `.gitignore` с `.env` в самом первом коммите кода фазы (f9dfbed), `git check-ignore -q .env` = 0.
- Схема `users` / `transactions` с `user_id` с первой миграции, `amount numeric(12,2) NULL`, `raw_body text`, индекс `(user_id, occurred_at)`.
- Обработчик приёма: токен сравнивается в постоянном времени до чтения тела, тело хранится побайтно, отправка в Telegram после сохранения с собственным таймаутом 10 с, всегда 201 после сохранения.
- Лимиты: фиксированное окно 30 запросов/мин → 429, тело ≤ 64 KB.
- `SchemaTests` охраняет правило «user_id в каждой пользовательской таблице» для будущих миграций.

## Task Commits

1. **Task 1 (tracer): POST → Postgres → SendMessage → 201**
   - `f9dfbed` feat(01-01): tracer A — каркас решения
   - `08242ce` feat(01-01): tracer B — AppDb, Parsing, Week, Messages
   - `e10cad0` feat(01-01): tracer C — Program, Ingest, миграция Initial
   - `9a55bcf` test(01-01): tracer D — ValidToken_SavesRowAndSendsConfirmation
   - Tracer gate: verify перезапущен целиком (check-ignore, 4 коммита трассера, build, IngestTests 1/1) → расширение. Дополнительно мутационная проверка: ожидаемый текст `154,02` вместо `154,01` даёт `ReceivedCallsException`, то есть проверка вызова Telegram реально ловит ошибки.
2. **Task 2 (TDD): отказ без токена, кривые запросы, сбой Telegram, лимиты, user_id**
   - `aee515b` test(01-01): RED
   - `bae4b54` feat(01-01): GREEN (REFACTOR не понадобился)

## TDD Gate Compliance

RED (`aee515b`, до правки кода), 10 тестов, exit 2: сбой 3, успешно 7.

| Тест | RED | Причина |
|------|-----|---------|
| InvalidJson_SavedAsUnparsed | сбой | `JsonException: 'n' is an invalid start of a property name` вылетает из обработчика вместо 201 |
| AmountAsNumber_SavedAsUnparsed | сбой | `JsonException: The JSON value could not be converted to Ingest+Payload. Path: $.amount` |
| UnparsedAmount_WarnsAndStaysOutOfTheLimit | сбой | `ReceivedCallsException`: сообщение «⚠️ Не разобрал сумму» не отправлено (в Messages не было ветки) |
| MissingToken / WrongToken / NonBearerScheme | зелёный сразу | 401 по токену реализован трассером (шаг 5, D-06) |
| UnparsedTime_FallsBackToReceivedAt | зелёный сразу | `Parsing.Time(...) ?? now` реализован трассером |
| TelegramFailure_StillReturns201AndKeepsRow | зелёный сразу | try/catch вокруг SendMessage реализован трассером |
| SchemaTests.AllUserDataTablesHaveUserId | зелёный сразу | инвариант-охранник: схема уже корректна, тест защищает будущие миграции |

Все три RED-сбоя относятся к целевым тестам и падают на запланированном поведении, а не на сборке или фикстуре. GREEN (`bae4b54`): 10/10. Классификатор `gsd check tdd-red-evidence` формат консоли xUnit v3/MTP не поддерживает, поэтому доказательство RED записано здесь вручную (вывод выше дословный).

## Почему так (для собеседования)

- **Сырое тело вместо привязки к параметру.** Обработчик сам читает тело строкой, а потом пробует разобрать JSON. Если бы тело привязывалось к параметру, кривой JSON дал бы 400 ещё до моего кода, и трата пропала бы. Так я сначала проверяю токен, а потом сохраняю всё, что пришло, даже мусор.
- **Сравнение хэшей в постоянном времени.** Обычное `==` останавливается на первом несовпавшем символе, и по времени ответа можно подбирать токен посимвольно. `FixedTimeEquals` сравнивает всё целиком, а SHA-256 выравнивает обе стороны до 32 байт, поэтому не раскрывается даже длина токена.
- **Отправка в Telegram после сохранения.** Сначала `SaveChanges`, потом сообщение в `try/catch`, и Команде всегда 201. Если бы Telegram упал и я вернул 500, iPhone показал бы «Трата не сохранена», я бы внёс её руками и получил дубль.
- **`amount IS NULL` как статус.** «Сумма не разобрана» — это просто отсутствие суммы. Отдельная колонка статуса сейчас ничего не даёт, а `SUM` в Postgres сама пропускает NULL. Полноценный статус появится в Phase 3 вместе с дублями.
- **Неделя траты, а не приёма.** Остаток считается за неделю пн–вс по Варшаве, в которую попадает время оплаты из поля `time`. Если оплата в воскресенье 23:59 дошла в понедельник, она всё равно уходит в свою неделю. Границы считает NodaTime, потому что на переходе на летнее время неделя длится 167 часов, а не 168.
- **Program под WebApplicationFactory.** В .NET 10 класс `Program` из top-level кода уже публичный, поэтому `WebApplicationFactory<Program>` поднимает настоящий хост в памяти без `public partial class Program`. Настройки читаются внутри DI-фабрик, поэтому тест может подменить строку подключения и токены через `UseSetting`, а `dotnet ef` работает без секретов.
- **Транзитивный пин EF.** Npgsql тянет EF 10.0.4, а Design — 10.0.15. Без пина приложение и тесты загрузили бы разные версии EF (предупреждение MSB3277). Поэтому EF закреплён на 10.0.15 для всех проектов сразу.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Конфликт версий EF Core между проектами (MSB3277)**
- **Found during:** Task 1, этап A
- **Issue:** тестовый проект получал Microsoft.EntityFrameworkCore 10.0.4 (через Npgsql), приложение 10.0.15 (через Design, PrivateAssets=all).
- **Fix:** `CentralPackageTransitivePinningEnabled=true` + PackageVersion для Microsoft.EntityFrameworkCore и Microsoft.EntityFrameworkCore.Relational 10.0.15 (те же пакеты стека, не новые зависимости, ни одной 11.x).
- **Files modified:** Directory.Packages.props
- **Commit:** f9dfbed

**2. [Rule 3 - Blocking] `dotnet new tool-manifest` в SDK 10 создаёт `dotnet-tools.json` в корне**
- **Fix:** файл перенесён в `.config/dotnet-tools.json`, как в плане; `dotnet tool restore` его находит.
- **Commit:** f9dfbed

**3. [Rule 3 - Blocking] xUnit v3 не добавляет global using `Xunit`**
- **Fix:** `<Using Include="Xunit" />` в тестовом csproj.
- **Commit:** 9a55bcf

**4. [Rule 3 - Blocking] `AddFixedWindowLimiter` требует `using Microsoft.AspNetCore.RateLimiting`** (во фрагменте RESEARCH его не было). **Commit:** bae4b54

Дополнительно, не отклонение: `Properties/launchSettings.json` из шаблона `dotnet new web` оставлен (локальный `dotnet run`), хотя в files_modified плана его нет.

**Total deviations:** 4 auto-fixed (все Rule 3, сборка/инструменты). **Impact:** поведение и контракт не изменились.

## Known Stubs

| File | Line | Reason |
|------|------|--------|
| src/FinanceBot/Messages.cs | 26 | Нет ветки перерасхода (D-02): при тратах > 200 zł за неделю вторая строка покажет «🟢 Осталось −X». Ветку 🔴 добавляет план 01-02 (по плану 01-01 так и задумано). |

## Issues Encountered

None. Docker был запущен, Testcontainers поднял postgres:18-alpine с первого раза.

## Next Phase Readiness

Готово к 01-02 (честные цифры: перерасход 🔴, unit-тесты Parsing/Week/Messages, экранирование). Учесть: окно rate limit 30/мин общее для всего тестового хоста; сейчас набор делает 10 запросов к `/api/v1/transactions`. Если в коллекции `Api` их станет больше 30 за минуту, тесты начнут получать 429.

## Self-Check: PASSED

- Все 20 файлов из key-files.created существуют; файл миграции `*_Initial.cs` содержит `user_id` и `raw_body`.
- Коммиты f9dfbed, 08242ce, e10cad0, 9a55bcf, aee515b, bae4b54 есть в HEAD; `git rev-list --count b577cf2..HEAD` = 6.
- Acceptance criteria задач 1 и 2 перепроверены grep/командами: все PASS.
