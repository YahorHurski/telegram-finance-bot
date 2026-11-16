# Phase 1: Ходячий скелет - Research

**Researched:** 2026-11-16
**Domain:** ASP.NET Core 10 minimal API + EF Core 10/Npgsql/NodaTime + Telegram.Bot (только отправка) + Serilog + Docker Compose/Caddy на Oracle Cloud A1 (ARM64) + GitHub Actions
**Confidence:** HIGH для стека и кода (версии и API проверены по NuGet, nupkg XML-docs, Context7, локальным прогоном); MEDIUM для Oracle/iOS-части (документация Oracle молчит про PAYG, поведение Команды не документировано Apple)

## Summary

Фаза создаёт с нуля один проект ASP.NET Core 10 (minimal API) и один тестовый проект. Весь путь укладывается примерно в 300 строк кода. Эндпоинт `POST /api/v1/transactions` сначала проверяет Bearer-токен: SHA-256 от обеих сторон, затем `CryptographicOperations.FixedTimeEquals`. Потом читает **сырое тело строкой** (не привязывая его к параметру), сохраняет строку в `transactions` с `user_id = 1`, считает сумму за неделю пн–вс по Europe/Warsaw (NodaTime) и отправляет владельцу `SendMessage` с `ParseMode.Html`. Ошибка Telegram не мешает ответу `201 {id}`: трата уже сохранена. Логи: Serilog с уровнями, заданными **в коде**, а не в конфиге, плюс `ReadFrom.Services`, через который тест подключает перехватывающий sink и проверяет, что в выводе нет сумм, продавцов и токена.

Деплой: на VM `git pull` → `docker compose build` → `docker compose run --rm app migrate` (тот же образ, `Database.MigrateAsync()` по аргументу командной строки) → `docker compose up -d`. Сервисы: `app` (образ `aspnet:10.0` = Ubuntu noble, multi-arch), `postgres:18-alpine` (**том монтируется в `/var/lib/postgresql`, а не в `/data`**, это изменение PG18), `caddy:2.11-alpine` с Caddyfile из одной строки. Образ собирается прямо на A1 VM. Поэтому VM должна быть Ampere A1: x86 E2.1.Micro с 1 GB RAM не потянет SDK-сборку вместе с Postgres.

Главный нетехнический риск: **в официальной документации Oracle НЕТ утверждения, что отбор простаивающих инстансов не действует на Pay As You Go.** Об этом говорит только письмо Oracle 2023 года, перепечатанное в блоге, а отчёты сообщества за 2026 год расходятся. Success criterion 1 («по документации проверено») буквально выполнить нельзя. Нужен checkpoint с пользователем и переформулировка: «проверено по доступным источникам: документация молчит, письмо Oracle прямо называет PAYG защитой».

**Primary recommendation:** Один проект `src/FinanceBot` (Program.cs + 5–6 маленьких файлов) и `tests/FinanceBot.Tests` (xUnit v3 + WebApplicationFactory + Testcontainers + NSubstitute). Уровни логирования зашиты в коде, Telegram-клиент мокается через `ITelegramBotClient.SendRequest` (а не через `SendMessage`, это extension-метод). Миграции применяются командой `docker compose run --rm app migrate`.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Сообщение в Telegram
- **D-01:** Подтверждение короткое, две строки: `− 45,99 zł · Żabka` и `🟢 Осталось 154,01 из 200 zł`. Без карты, времени, прогресс-бара.
- **D-02:** При перерасходе вторая строка заменяется на `🔴 **Потрачено 237,50/200 · Перерасход 37,50 zł**` (жирный через parse mode). Цвет текста в Telegram невозможен — подсветка только эмодзи 🟢/🔴 + жирный. Светофор 🟡 (80%) не делаем — алерты это Phase 5.
- **D-03:** Если сумма не разобрана: трата сохраняется (сырое тело + статус «сумма не разобрана»), в лимит не идёт, бот пишет `⚠️ Не разобрал сумму: «<исходная строка>» · <продавец>`. Запрос «введи сумму» — в Phase 3 (см. Deferred).
- **D-04:** Формат чисел — русская локаль: запятая как десятичный разделитель, `zł` после суммы.

#### Контракт приёма трат
- **D-05:** Эндпоинт `POST /api/v1/transactions` с первого дня (не `/tx`), чтобы Команду на iPhone не перенастраивать в Phase 3. Success criteria ROADMAP с `POST /tx` читать как этот путь. — **Reversibility:** costly — URL прописан в iOS-Команде на телефоне; смена = ручная правка Команды.
- **D-06:** Аутентификация: `Authorization: Bearer <token>`, токен из `.env`; без токена / неверный → 401. (Хэширование токенов в БД — Phase 7; сравнение в постоянном времени уже сейчас.)
- **D-07:** Тело — JSON, все поля строками как отдаёт автоматизация «Транзакция», без обработки на iPhone: `amount`, `merchant`, `card`, `time`. Сырое тело запроса сохраняется всегда целиком (корпус для SPIKE-01).
- **D-08:** `time` приходит в формате `дд.мм.гггг чч:мм:сс` (локальное время телефона) и интерпретируется как Europe/Warsaw. Это время траты (определяет неделю). Если не разобралось — берётся время приёма на сервере.
- **D-09:** Ответ: `201` + JSON `{ "id": ... }`. Команда при успехе ничего не показывает (подтверждение придёт в Telegram); при ошибке (401/5xx/нет сети) показывает уведомление «Трата не сохранена». Инструкция по сборке Команды — часть фазы.
- **D-10:** Неразобранная сумма — не ошибка запроса: ответ тоже 201 (трата сохранена со статусом), см. D-03.

#### CI и тесты
- **D-11:** GitHub Actions на push в main: сборка + тесты + gitleaks. Автодеплоя нет.
- **D-12:** Тесты в скелете: сквозной интеграционный тест на реальном Postgres (Testcontainers + WebApplicationFactory): POST с токеном → строка в БД + вызов Telegram (подменённый клиент); без токена → 401; логи не содержат сумм, продавцов и токенов (PRIV-03). Плюс unit-тесты разбора суммы, разбора времени и границ недели пн–вс по Europe/Warsaw.
- **D-13:** Деплой в фазе 1 руками: SSH на VM → `git pull` → `docker compose up -d --build` (образ собирается на самой VM, без GHCR и кросс-сборки). Команды записаны в README или скрипте. Миграции — ручной шаг (migrator в Phase 2).

### Claude's Discretion
- Инфраструктура (пользователь не выбрал для обсуждения): создание VM Oracle (ARM Ampere A1 или x86), перевод на PAYG, бюджетный алерт, поддомен DuckDNS, бот в @BotFather — план включает их как ручные шаги (checkpoint:human-action) с пошаговыми инструкциями. Что уже сделано, уточнить у пользователя на чекпоинте.
- Схема таблиц (с `user_id` с первой миграции), формат статуса неразобранной суммы, структура проекта, базовый образ (учесть ARM64 и ICU для ru-RU).
- Тексты ошибок, кроме зафиксированных выше.

### Deferred Ideas (OUT OF SCOPE)
- **Phase 3 (расширение ING-09):** если сумма не разобрана, бот просит ввести сумму и дописывает её в ту же сохранённую трату. Требует приёма сообщений (вебхук), поэтому не в скелете.
- **Phase 5:** предупреждение 🟡 при 80% лимита (уже покрыто алертами LIM).
</user_constraints>

Источник: `[VERIFIED: .planning/phases/01-hodyachiy-skelet/01-CONTEXT.md:19-41, 86-90]` (скопировано дословно).

**Точные символы из D-01..D-03** `[VERIFIED: 01-CONTEXT.md:19-21, code points прочитаны python'ом]`: минус в `− 45,99` это **U+2212**, разделитель `·` это **U+00B7**, `⚠️` = U+26A0 U+FE0F, 🟢 = U+1F7E2, 🔴 = U+1F534, кавычки « » = U+00AB/U+00BB. Между числом и `zł` стоит **обычный пробел** (U+00A0 в строках нет). `**...**` в D-02 означает «жирный». Реализуется как `<b>...</b>` в `ParseMode.Html` (см. Pitfall 5).

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| FND-02 | Сервис развёрнут бесплатно на Oracle Always Free VM через Docker Compose и доступен по HTTPS на DuckDNS; `/health` отвечает; аккаунт на PAYG, бюджетный алерт | §Deploy topology (compose/Caddyfile/Dockerfile), §Oracle (A1 обязателен, два слоя фаервола, PAYG: доказательства и пробел, Budgets: шаги), Pitfalls 1–3, 8 |
| FND-05 | Секреты не попадают в репозиторий (gitleaks в CI, `.gitignore` с первого коммита) | §CI (gitleaks-action@v3 без лицензии для личного аккаунта, `fetch-depth: 0`, `git check-ignore .env`), Pitfall 9. Сейчас в репо **нет `.gitignore`**, это первая задача |
| PRIV-03 | Логи не содержат сумм, продавцов и токенов; проверяется автотестом | §Logging (уровни в коде, HttpClient-логи содержат bot token в пути URL, `ReadFrom.Services` + capturing sink), Pitfall 4 |
| ING-03 (упрощ.) | Трата сохраняется через HTTPS-эндпоинт с аутентификацией по токену | §Ingest endpoint (raw body → JSON → parse → save), §Auth |
| MAN-03 (упрощ.) | После траты бот присылает сумму, продавца, остаток лимита | §Week math (NodaTime, проверено прогоном), §Message builder (HTML escaping, формат чисел) |
| SC-4 | `user_id` во всех таблицах с первой миграции | §Schema (users с id=1 без identity + transactions.user_id FK) + schema-тест через information_schema |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

Из `D:\finance-bot\.claude\CLAUDE.md` (проект) и `~/.claude/CLAUDE.md` + fable-cheatsheet (глобально):

- **Стек зафиксирован:** .NET 10 LTS (не 11); EF Core **10.0.15** (не 11.0.0); Npgsql EF **10.0.3** (требует EF `[10.0.4, 11.0.0)`); NodaTime через Npgsql-плагин; EFCore.NamingConventions (snake_case); Telegram.Bot **22.10.4**, методы без `Async` (`SendMessage`); Serilog.AspNetCore **10.0.0**, compact JSON в stdout; xUnit v3 **4.0.3** на MTP v2; Testcontainers.PostgreSql **4.16.0**; NSubstitute (не Moq); без MediatR/AutoMapper/FluentAssertions v8.
- **Central Package Management** (`Directory.Packages.props`), чтобы EF 11 не подтянулся случайно.
- **Запрещено:** `DateTime.Now/UtcNow` в доменном коде (NodaTime `IClock`); `float/double` для денег (`decimal` → `numeric(12,2)`); EF InMemory/SQLite в интеграционных тестах; `System.Drawing`; plain `-chiseled` образ без ICU; PostgreSQL 19; `Database.Migrate()` при старте приложения в проде (в Phase 1 вместо него явная одноразовая команда, см. ниже); Watchtower.
- **Postgres-образ = тот же тег в тестах и в проде** (`postgres:18-alpine`).
- **Секреты** только в `.env` (в `.gitignore`), на VM `chmod 600`.
- **Rate limiting** на ingestion-эндпоинте: CLAUDE.md помечает это «Always».
- **Глобально:** никаких `Co-Authored-By`/атрибуции Claude в коммитах; самый маленький шаг, который работает целиком; никакой «архитектуры на вырост»; на каждое нетривиальное решение 2–3 предложения «почему» простыми словами (пользователь junior, будет объяснять на собеседовании); финансовые данные не логировать.
- **GSD:** правки только через GSD-команды.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Сбор данных об оплате | iOS Shortcut (клиент) | — | Только автоматизация «Транзакция» видит оплату; отправляет строки как есть (D-07) |
| TLS, сертификаты | Caddy (edge) | — | Автоматический Let's Encrypt; приложение слушает только HTTP 8080 внутри сети compose |
| Аутентификация токеном | API (ASP.NET Core) | — | Проверка до чтения тела; constant-time |
| Разбор суммы/времени | API (чистые функции) | — | Граница доверия; unit-тестируется без БД |
| Хранение траты + сырого тела | Database (Postgres) | — | Источник истины; сырое тело это корпус для SPIKE-01 |
| Подсчёт недели и остатка | API (NodaTime) + Database (SUM) | — | Границы недели считаются в коде (DST), сумма считается SQL |
| Уведомление | API → Telegram Bot API | — | Только исходящий `SendMessage`; без вебхука |
| Миграции схемы | Одноразовый запуск того же образа | — | `app migrate`; в Phase 2 заменяется отдельным migrator |
| CI/секрет-скан | GitHub Actions | — | build + test + gitleaks; без деплоя (D-11) |

## Standard Stack

### Core (версии проверены 2026-11-16 через `api.nuget.org/v3-flatcontainer`)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET SDK / ASP.NET Core | 10.0 (локально SDK 10.0.301) | Runtime, minimal API | LTS; закреплено в CLAUDE.md `[VERIFIED: dotnet --list-sdks]` |
| Telegram.Bot | 22.10.4 | `SendMessage` владельцу | `[VERIFIED: NuGet index + nupkg XML docs]` |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | EF-провайдер | последняя 10.x; 11.x только RC `[VERIFIED: NuGet]` |
| Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime | 10.0.3 | `Instant` → `timestamptz` | зависит от Npgsql EF 10.0.3 + Npgsql.NodaTime 10.0.3 `[VERIFIED: nuspec]` |
| NodaTime | 3.3.7 | неделя пн–вс Europe/Warsaw, разбор времени | своя TZDB, не зависит от tzdata образа `[VERIFIED: NuGet + локальный прогон]` |
| EFCore.NamingConventions | 10.0.1 | snake_case с **первой** миграции | переименовать таблицы позже = миграция на всё `[VERIFIED: NuGet]` |
| Microsoft.EntityFrameworkCore.Design | 10.0.15 (`PrivateAssets=all`) | `dotnet ef migrations add` | latest 10.x; NuGet «latest» = 11.0.0, его **не брать** `[VERIFIED: NuGet]` |
| Serilog.AspNetCore | 10.0.0 | логирование | уже тянет `Serilog.Formatting.Compact 3.0.0`, `Serilog.Sinks.Console 6.1.1`, `Serilog.Settings.Configuration 10.0.0`, отдельно их ставить не нужно `[VERIFIED: nuspec]` |

### Tests

| Library | Version | Purpose |
|---------|---------|---------|
| xunit.v3 | 4.0.3 | фреймворк, MTP v2 по умолчанию `[VERIFIED: NuGet + xunit.net MTP docs]` |
| Microsoft.AspNetCore.Mvc.Testing | **10.0.15** (не 11.0.0) | `WebApplicationFactory<Program>` `[VERIFIED: NuGet]` |
| Testcontainers.PostgreSql | 4.16.0 | реальный `postgres:18-alpine` `[VERIFIED: NuGet]` |
| NSubstitute | 6.2.0 | подмена `ITelegramBotClient` `[VERIFIED: NuGet]` |

### Tools
| Tool | Version | Purpose |
|------|---------|---------|
| dotnet-ef (local tool, `.config/dotnet-tools.json`) | 10.0.15 | генерация миграции `[VERIFIED: NuGet]` |
| postgres image | `postgres:18-alpine` | БД `[VERIFIED: Docker Hub tags]` |
| caddy image | `caddy:2.11-alpine` (2.11.9) | TLS-прокси `[VERIFIED: Docker Hub tags + GitHub release 2026-11-05]` |
| actions/checkout | v7 (7.0.1) | CI `[VERIFIED: gh api releases/latest]` |
| actions/setup-dotnet | v6 (6.1.0) | CI `[VERIFIED: gh api]` |
| gitleaks/gitleaks-action | **v3** (3.0.0) | секрет-скан `[VERIFIED: gh api + README]` |

### Сознательно НЕ берём в Phase 1 (из списка CLAUDE.md)
NodaTime.Testing (в коде нет зависящих от «сейчас» правил, кроме фолбэка `received_at`; хватает `IClock` = `SystemClock.Instance`), Respawn (тесты разделяют БД через уникальные значения), WireMock.Net, AwesomeAssertions (хватает `Assert`), OpenTelemetry, Scalar/OpenAPI, health-check-пакет EF, Microsoft.Extensions.Http.Resilience, ScottPlot (только опциональный спайк, см. ниже). Всё это приходит в фазах 2–6.

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `app migrate` (тот же образ) | EF migration bundle в отдельном stage | Bundle это Phase 2 (FND-04). Сейчас он добавил бы `dotnet tool restore` в Dockerfile и второй target |
| Inline-проверка токена | Custom `AuthenticationHandler` | Нужен при многих пользователях (Phase 7); для одного токена это лишняя церемония |
| Своя `NumberFormatInfo` | `CultureInfo("ru-RU")` | ru-RU зависит от ICU в образе и от NLS на Windows. Своя NFI даёт одинаковый результат везде |

**Installation (через CPM, версии в `Directory.Packages.props`):**
```bash
dotnet new sln -n FinanceBot
dotnet new web -o src/FinanceBot
dotnet new console -o tests/FinanceBot.Tests   # затем руками csproj под xunit.v3 (OutputType Exe)
dotnet add src/FinanceBot package Telegram.Bot
dotnet add src/FinanceBot package Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime
dotnet add src/FinanceBot package EFCore.NamingConventions
dotnet add src/FinanceBot package Microsoft.EntityFrameworkCore.Design
dotnet add src/FinanceBot package Serilog.AspNetCore
dotnet add src/FinanceBot package NodaTime
dotnet add tests/FinanceBot.Tests package xunit.v3
dotnet add tests/FinanceBot.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/FinanceBot.Tests package Testcontainers.PostgreSql
dotnet add tests/FinanceBot.Tests package NSubstitute
dotnet new tool-manifest && dotnet tool install dotnet-ef --version 10.0.15
```
⚠️ `dotnet add package` без версии возьмёт **11.0.0** для `Microsoft.*`. Версии ставить явно в `Directory.Packages.props`.

## Package Legitimacy Audit

Seam `package-legitimacy check` не поддерживает NuGet (`Usage: --ecosystem <npm|pypi|crates>`). Аудит выполнен вручную через NuGet search API (`azuresearch-usnc.nuget.org/query`, 2026-11-16). Все пакеты взяты из CLAUDE.md и подтверждены официальными источниками (Context7 / официальные доки / nupkg).

| Package | Registry | Total downloads | Verified prefix | Owners / Source | Verdict | Disposition |
|---------|----------|-----------------|-----------------|-----------------|---------|-------------|
| Telegram.Bot 22.10.4 | NuGet | 11.1M | no | TelegramBots, Wiz0u / github.com/TelegramBots/Telegram.Bot | OK | Approved |
| NodaTime 3.3.7 | NuGet | 355M | yes | jon skeet, NodaTime | OK | Approved |
| Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 | NuGet | 557M | yes | Npgsql, roji | OK | Approved |
| Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime 10.0.3 | NuGet | 16M | yes | Npgsql, roji | OK | Approved |
| EFCore.NamingConventions 10.0.1 | NuGet | 91.6M | no | EFCore.Extensions, roji (мейнтейнер Npgsql) | OK | Approved |
| Serilog.AspNetCore 10.0.0 | NuGet | 869M | yes | serilog | OK | Approved |
| xunit.v3 4.0.3 | NuGet | 67.9M | yes | xunit | OK | Approved |
| Testcontainers.PostgreSql 4.16.0 | NuGet | 59.1M | yes | HofmeisterAn, Testcontainers | OK | Approved |
| NSubstitute 6.2.0 | NuGet | 338M | yes | NSubstitute | OK | Approved |
| Microsoft.AspNetCore.Mvc.Testing 10.0.15 | NuGet | 450M | yes | Microsoft | OK | Approved |
| Microsoft.EntityFrameworkCore.Design 10.0.15 | NuGet | 1.15B | yes | Microsoft | OK | Approved |
| dotnet-ef 10.0.15 | NuGet | 139M | yes | Microsoft | OK | Approved |

**Packages removed due to [SLOP] verdict:** none
**Packages flagged as suspicious [SUS]:** none. (У Telegram.Bot и EFCore.NamingConventions нет verified prefix, но им много лет, у них десятки миллионов загрузок и известные мейнтейнеры; оба явно указаны в CLAUDE.md.) postinstall-скриптов у NuGet нет. Аналог, MSBuild `.targets` в пакете, у этих пакетов не проверялся `[ASSUMED]`.

## Architecture Patterns

### System Architecture Diagram

```
iPhone: оплата Apple Pay
   │  автоматизация «Транзакция» → Команда
   │  POST https://<sub>.duckdns.org/api/v1/transactions
   │  Authorization: Bearer <token>   body: {"amount":"45,99 zł","merchant":"Żabka","card":"…","time":"16.11.2026 14:03:05"}
   ▼
Caddy :443 (Let's Encrypt, авто) ──reverse_proxy──► app:8080 (сеть compose; наружу не опубликован)
   ▼
ASP.NET Core pipeline: SerilogRequestLogging ─► RateLimiter("ingest") ─► handler
   │
   ├─ токен неверен/нет ──────────────────────────────► 401 (тело пустое)
   │
   ├─ прочитать тело строкой (≤64 KB) ─► JSON → Payload? (ошибка JSON → все поля null)
   ├─ AmountParser(amount) → decimal? (null = «сумма не разобрана»)
   ├─ TimeParser(time) → Instant? ── null ─► IClock.Now
   ├─ INSERT transactions (user_id=1, amount, amount_raw, merchant, card, occurred_at, received_at, raw_body)
   │        └──► PostgreSQL 18 (том pgdata)
   ├─ amount != null? ─► Week(occurred_at) → SUM(amount) за [пн 00:00, след. пн 00:00) Warsaw
   ├─ Messages.Confirmation(...) → HTML-текст (🟢 / 🔴<b> / ⚠️)
   ├─ try SendMessage(ownerChatId, text, Html) с таймаутом 10 c ─► api.telegram.org
   │     └─ ошибка → LogWarning(тип ошибки, tx id) — без текста
   └─► 201 {"id":"<uuid v7>"}

Отдельно: GET /health → 200 (Caddy → app)
Деплой: ssh → deploy.sh: git pull → compose build → compose run --rm app migrate → compose up -d
CI: push main / PR → gitleaks → setup-dotnet → dotnet test (Testcontainers на ubuntu-latest)
```

### Recommended Project Structure
```
/
├── .gitignore                 # dotnet new gitignore + .env  ← ПЕРВЫЙ коммит фазы
├── .dockerignore              # **/bin **/obj .git .env
├── .env.example               # имена переменных без значений
├── global.json                # SDK 10.0.100 latestFeature, allowPrerelease:false, test.runner MTP
├── Directory.Packages.props   # CPM, все версии
├── FinanceBot.slnx (или .sln)
├── .config/dotnet-tools.json  # dotnet-ef 10.0.15
├── src/FinanceBot/
│   ├── Program.cs             # DI, Serilog, endpoints, ветка "migrate"
│   ├── Ingest.cs              # обработчик POST + проверка токена
│   ├── AppDb.cs               # DbContext + сущности User/Transaction
│   ├── Parsing.cs             # AmountParser, TimeParser (чистые функции)
│   ├── Week.cs                # границы недели (NodaTime)
│   ├── Messages.cs            # тексты D-01..D-03, формат чисел, HTML-escape
│   ├── Migrations/            # dotnet ef migrations add Initial
│   ├── appsettings.json
│   └── appsettings.Development.json  # только несекретная dev-строка подключения
├── tests/FinanceBot.Tests/
│   ├── ParsingTests.cs  WeekTests.cs  MessagesTests.cs     # unit
│   ├── ApiFactory.cs  CapturingSink.cs                      # фикстура
│   └── IngestTests.cs  LogPrivacyTests.cs  SchemaTests.cs   # интеграционные
├── Dockerfile  compose.yaml  Caddyfile  deploy.sh
├── docs/shortcut.md           # пошаговая сборка iOS-Команды (часть фазы, D-09)
├── README.md                  # как запустить/задеплоить
└── .github/workflows/ci.yml
```
Почему так: один проект и плоские файлы, потому что слои появятся рефакторингом в Phase 2 (ROADMAP Notes). Чистые функции (`Parsing`, `Week`, `Messages`) вынесены отдельно, потому что их проще всего тестировать и при разбивке они без изменений уедут в Domain.

### Pattern 1: Читать сырое тело, а не привязывать его к параметру
**Что:** обработчик принимает `HttpRequest` и сам читает `req.Body` в строку. Потом пытается десериализовать `record Payload(string? Amount, string? Merchant, string? Card, string? Time)`.
**Почему:** (1) D-07 требует сохранять тело всегда. При привязке `[FromBody] Payload` невалидный JSON вернёт 400 ещё до кода, и тело пропадёт. (2) Привязка параметров в minimal API выполняется **до** endpoint-фильтров, то есть тело разбиралось бы до проверки токена. (3) Если Команда пришлёт `amount` числом, а не строкой, `JsonException` даст «не разобрано» вместо потери траты.
**Объяснение для собеседования:** «Тело читаю сам, потому что сначала проверяю токен, а потом в любом случае сохраняю то, что пришло. Даже кривой запрос не теряется».

### Pattern 2: Сравнение токена в постоянном времени через хэши
`SHA256(присланный)` против `SHA256(ожидаемый)` и `CryptographicOperations.FixedTimeEquals`. `FixedTimeEquals` при разной длине сразу возвращает false и тем самым раскрывает длину. Хэширование выравнивает длину до 32 байт. Хватает обычного SHA-256 без соли: токен случайный, 256 бит (`openssl rand -hex 32`) `[CITED: .planning/research/ARCHITECTURE.md Flow 4; BCL API — ASSUMED стандарт]`.

### Pattern 3: Конфиг читать внутри DI-фабрик, а не наверху Program.cs
`AddDbContext((sp, o) => o.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default"), …))`, `AddSingleton<ITelegramBotClient>(sp => new TelegramBotClient(...))`, `AddOptions<BotOptions>().BindConfiguration("Bot").Validate(...).ValidateOnStart()`.
**Почему:** (a) `dotnet ef migrations add` запускает Program до `Build()` без секретов. Если `throw` стоит на верхнем уровне, генерация миграции падает. (b) Ветка `migrate` не запускает хост, поэтому `ValidateOnStart` не требует токена Telegram только ради миграции. (c) Настройки из `WebApplicationFactory.UseSetting` гарантированно видны фабрикам `[ASSUMED: при чтении на уровне builder это не гарантировано, см. aspnetcore issues про minimal hosting]`.

### Pattern 4: Неделя через NodaTime, а не `start + 7 дней`
Проверено локальным прогоном (`dotnet run probe.cs`, NodaTime 3.3.7) `[VERIFIED: local run]`:
```
15.11.2026 23:59:59 -> [2026-11-08T23:00:00Z, 2026-11-15T23:00:00Z) hours=168
16.11.2026 00:00:00 -> [2026-11-15T23:00:00Z, 2026-11-22T23:00:00Z) hours=168
25.03.2026 12:00:00 -> [2026-03-22T23:00:00Z, 2026-03-29T22:00:00Z) hours=167   ← переход на летнее
21.10.2026 12:00:00 -> [2026-10-18T22:00:00Z, 2026-10-25T23:00:00Z) hours=169   ← переход на зимнее
```
Ровно эти четыре случая и надо положить в `WeekTests`.

### Pattern 5: Ветка `migrate` в том же образе
`if (args is ["migrate"]) { … Database.MigrateAsync(); return; }` после `builder.Build()`. Запуск: `docker compose run --rm app migrate`. Это не «Migrate при старте». Это явная одноразовая команда, ровно «простой ручной шаг» из ROADMAP. В Phase 2 её заменит bundle. Плюс: EF 9+ бросает исключение при pending model changes `[CITED: .planning/research/PITFALLS.md «EF Core migrations in production»]`, и тестовая фикстура, которая вызывает `MigrateAsync()`, ловит забытую миграцию.

### Anti-Patterns to Avoid
- **`UseHttpsRedirection()` в app:** TLS снимает Caddy, приложение видит http. Не добавлять.
- **`UseHttpLogging()` / `EnableSensitiveDataLogging()` / `Include Error Detail=true` в строке подключения:** любое из них выводит тела, параметры SQL или значения строк в логи. PRIV-03 нарушен.
- **`AddHttpClient` для Telegram на Information:** логирует URL `https://api.telegram.org/bot<TOKEN>/sendMessage` (см. Pitfall 4).
- **Отправка в Telegram до `SaveChanges`** или ответ 5xx при сбое Telegram: Команда покажет «не сохранено», хотя трата сохранена, и при ручном повторе появится дубль.
- **`CreateBootstrapLogger()`** вместе с WebApplicationFactory: известная ошибка «logger already frozen» при нескольких хостах `[ASSUMED]`. В Phase 1 bootstrap-логгер не нужен.
- **`jsonb` для raw_body:** невалидный JSON не вставится, и трата пропадёт. Хранить `text`.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Часовые пояса / DST / границы недели | арифметику с `TimeZoneInfo` и `AddDays(7)` | NodaTime `DateTimeZoneProviders.Tzdb["Europe/Warsaw"]`, `DateAdjusters.PreviousOrSame`, `AtStartOfDay`, `AtLeniently` | недели по 167/169 ч; своя TZDB не зависит от образа |
| TLS / продление сертификатов | certbot + cron | Caddy | 1 строка конфига, автопродление |
| Constant-time сравнение | цикл по байтам | `CryptographicOperations.FixedTimeEquals` | JIT может оптимизировать самописное сравнение |
| Rate limit | счётчики в памяти | `Microsoft.AspNetCore.RateLimiting` (в фреймворке) | готово, 6 строк |
| Схема БД | ручной SQL | EF migrations (`dotnet ef migrations add Initial`) | история схемы, тестируется `MigrateAsync` |
| Скан секретов | grep по репо | gitleaks-action@v3 | сотни правил, вся история |
| Escape для Telegram HTML | `WebUtility.HtmlEncode` | ручная замена 3 символов `& < >` | `HtmlEncode` кодирует `ó` в `&#243;` (проверено), тесты на `Contains("Mokotów")` ломаются; Telegram требует экранировать только `&<>` `[VERIFIED: local run]` |

**Key insight:** в этой фазе почти нет «сложного» кода. Риски сидят в интеграции: время, логи, Docker, сеть Oracle. Поэтому каждое место интеграции закрыто либо готовым инструментом, либо тестом.

## Common Pitfalls

### Pitfall 1: Документация Oracle НЕ подтверждает, что PAYG защищает от отбора
**What goes wrong:** success criterion 1 требует «по документации проверено», но в документации этого нет.
**Факты:**
- Официальная страница Always Free `[CITED: docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm]` дословно: «Idle Always Free compute instances may be reclaimed by Oracle. Oracle will deem virtual machine and bare metal compute instances as idle if, during a 7-day period, the following are true: CPU utilization for the 95th percentile is less than 20% · Network utilization is less than 20% · Memory utilization is less than 20% (applies to A1 shapes only)». **Про Pay As You Go на странице нет ни слова.**
- Страница Free Tier `[CITED: docs.oracle.com/iaas/Content/FreeTier/freetier.htm]`: «You will continue to have access to all of your cloud resources after upgrading your account.» Про отбор ничего.
- Единственное прямое свидетельство: письмо Oracle (январь 2023), перепечатанное в блоге `[CITED: 51sec.org/2023/02/24/oracle-cloud-cleaning-up-idle-compute-instances-how-to-keep-it]`: «Oracle Cloud Infrastructure (OCI) will be reclaiming idle Always Free compute resources from Always Free customers.» … «You can keep idle compute instances from being stopped by converting your account to Pay As You Go (PAYG).» … «With PAYG, you will not be charged as long as your usage for all OCI resources remains within the Always Free limits.»
- Отчёты 2026 года противоречат друг другу; есть жалобы на апгрейд, застрявший в статусе «Upgrading» на недели `[CITED: websearch, community.oracle.com/terminalbytes.com — LOW]`.
- Бот почти всё время простаивает, а память на A1 12 GB при занятых ~1–2 GB заведомо < 20%. **Без PAYG VM попадает под критерий простоя.**
**How to avoid:** checkpoint:human-action: (1) пользователь переводит аккаунт на PAYG и подтверждает в консоли статус «Pay As You Go»; (2) в SUMMARY записывается честная формулировка «документация молчит; письмо Oracle 2023 называет PAYG способом избежать остановки». Обходных «нагрузок» против простоя не делать. Защита от худшего случая: всё в Docker (переезд на Hetzner без переделок) и бэкапы вне Oracle (Phase 2, FND-06).
**Warning signs:** письмо от Oracle про idle instances; VM в статусе Stopped.

### Pitfall 2: Порты 80/443 закрыты на двух уровнях
**What goes wrong:** Caddy не получает сертификат (HTTP-01 challenge не проходит), Команда и `/health` не открываются.
**Why:** у OCI два фаервола: Security List / NSG в VCN и iptables внутри образа Ubuntu от Oracle (ufw там не используется; в INPUT стоит REJECT) `[CITED: dev.to/armiedema/opening-up-port-80-and-443-for-oracle-cloud-servers-j35; techoverflow.net — MEDIUM]`.
**How to avoid:** (1) VCN → Security List → Ingress: TCP 80 и 443 с 0.0.0.0/0. (2) На VM: `sudo iptables -I INPUT 6 -p tcp --dport 80 -m state --state NEW -j ACCEPT` (и 443) **выше** строки REJECT (`iptables -L INPUT -n --line-numbers`), затем `sudo netfilter-persistent save`. Не делать `iptables -F`: удалятся и служебные правила OCI. Проверять снаружи (`curl http://<домен>` с телефона по LTE). Docker публикует порты через FORWARD/DNAT, поэтому часть гайдов пишет, что INPUT ему не мешает. Это не проверено `[ASSUMED]`, так что надёжнее открыть оба уровня и проверить.
**Warning signs:** в логах Caddy `challenge failed` / `connection refused`/timeout.

### Pitfall 3: Rate limit Let's Encrypt, если поднять Caddy раньше DNS и портов
**What goes wrong:** Caddy начинает выпуск сертификата сразу при старте. Если DuckDNS ещё не указывает на VM или порты закрыты, валидации падают, и после нескольких неудач Let's Encrypt временно блокирует имя `[ASSUMED: лимит failed validations ≈5/час/имя]`.
**How to avoid:** порядок шагов: DuckDNS → IP VM (`curl "https://www.duckdns.org/update?domains=<sub>&token=<t>&ip="`; пустой `ip` означает автоопределение IPv4 `[CITED: duckdns.org/spec.jsp]`), открыть порты и проверить `nc`/curl снаружи, только потом `docker compose up`. Том `caddy_data:/data` обязателен, иначе сертификат перевыпускается на каждом пересоздании контейнера. Ephemeral public IP OCI переживает stop/start («When you stop an instance, its ephemeral public IPs remain assigned», `[CITED: docs.oracle.com/.../managingpublicIPs.htm]`), поэтому DuckDNS достаточно обновить один раз. Cron не нужен, пока VM не пересоздана.

### Pitfall 4: Токен бота утекает через логи HttpClient
**What goes wrong:** Telegram.Bot кладёт токен **в путь** URL (`/bot<TOKEN>/sendMessage`). Логирование `IHttpClientFactory` на Information пишет «Start processing HTTP request POST https://api.telegram.org/bot123:ABC.../sendMessage». Редакция в .NET 9+ закрывает только **query и fragment**, а путь остаётся `[CITED: learn.microsoft.com/dotnet/core/compatibility/networking/9.0/query-redaction-logs]`.
**How to avoid:** (1) регистрировать `new TelegramBotClient(token)` синглтоном. Он создаёт свой `HttpClient` вне фабрики, и фабричных логов нет `[VERIFIED: nupkg XML — ctor (string token, HttpClient httpClient = null, CancellationToken)]`. (2) Всё равно ставить `MinimumLevel.Override("System.Net.Http.HttpClient", Warning)`. (3) Тест PRIV-03 проверяет, что токен бота (фейковый, но уникальный) не встречается в логах.
**Другие источники утечек, закрытые настройками:** `Microsoft.AspNetCore` и `Microsoft.EntityFrameworkCore` на Warning (SQL-параметры и так не логируются без `EnableSensitiveDataLogging`); у Npgsql `PostgresException.Detail` скрыт по умолчанию без `Include Error Detail` `[ASSUMED: поведение Npgsql 6+]`; свой лог ошибки Telegram пишет только тип исключения и id траты.
**Warning signs:** в `docker compose logs app` встречается `api.telegram.org/bot`.

### Pitfall 5: Telegram отклоняет сообщение из-за разметки
**What goes wrong:** в MarkdownV2 надо экранировать `. - ( ) !` и ещё десяток символов. Продавец `JMP S.A. BIEDRONKA` или любая сумма с точкой дают 400 `can't parse entities`, и подтверждение не приходит `[CITED: .planning/research/PITFALLS.md Pitfall 15]`. Книга Telegram.Bot тоже советует HTML `[CITED: Context7 /websites/telegrambots_github_io_book FAQ #14]`.
**How to avoid:** `ParseMode.Html` (значения enum: `None, Markdown, Html, MarkdownV2` `[VERIFIED: nupkg XML]`). Во всех строках от пользователя или продавца заменять `&`→`&amp;`, `<`→`&lt;`, `>`→`&gt;`. `WebUtility.HtmlEncode` не использовать: он превращает `ó` в `&#243;` `[VERIFIED: local run: "Żabka Mokot&#243;w &lt;b&gt;&amp;"]`.

### Pitfall 6: Мок `SendMessage` в NSubstitute не сработает
**What goes wrong:** `SendMessage` это **extension-метод** (`TelegramBotClientExtensions.SendMessage(ITelegramBotClient, ChatId, string, ParseMode, …)`). NSubstitute не перехватывает extension-методы, поэтому `bot.Received().SendMessage(...)` не скомпилируется или ничего не проверит.
**How to avoid:** проверять реальный метод интерфейса `ITelegramBotClient.SendRequest<TResponse>(IRequest<TResponse>, CancellationToken)` с `Arg.Is<SendMessageRequest>(r => …)`. У `SendMessageRequest` есть свойства `ChatId`, `Text`, `ParseMode` `[VERIFIED: Telegram.Bot 22.10.4 nupkg XML docs: M:Telegram.Bot.ITelegramBotClient.SendRequest``1(IRequest{``0},CancellationToken); P:SendMessageRequest.Text/ChatId/ParseMode]`.

### Pitfall 7: Postgres 18 и старый путь тома
**What goes wrong:** привычный `pgdata:/var/lib/postgresql/data` в образах 18+ не работает как раньше.
**Fact:** «the PGDATA environment variable of the image was changed to be version specific in PostgreSQL 18 and above» и «The defined VOLUME was changed in 18 and above to /var/lib/postgresql» `[CITED: github.com/docker-library/docs/blob/master/postgres/content.md]`.
**How to avoid:** `volumes: [pgdata:/var/lib/postgresql]`. `POSTGRES_USER/PASSWORD/DB` применяются только к пустому каталогу данных (там же). Смена пароля в `.env` после первого запуска **не меняет** пароль в БД.

### Pitfall 8: x86 Micro VM не соберёт образ
**What goes wrong:** D-13 требует собирать образ на VM. У E2.1.Micro 1/8 OCPU и 1 GB RAM: SDK-сборка .NET и Postgres рядом не поместятся `[ASSUMED: по опыту; объём RAM микро-шейпа — CITED страница Always Free]`.
**How to avoid:** VM.Standard.A1.Flex (ARM64). Лимит Always Free: «equivalent to 2 OCPUs and 12 GB of memory» `[CITED: freetier_topic-Always_Free_Resources.htm]`. Рекомендация: 2 OCPU / 12 GB (или 1/6, чтобы был запас под второй инстанс), Ubuntu 24.04 aarch64. **На PAYG всё, что сверх 1 500 OCPU-часов и 9 000 GB-часов в месяц, платное.** Бюджетный алерт ловит именно эту ошибку. Ошибки «Out of capacity» для A1 случаются часто; помогает другой availability domain или повтор позже `[CITED: community — LOW]`.

### Pitfall 9: gitleaks и тестовые «секреты»
**What goes wrong:** константа вроде `"a8f3...e91"` с высокой энтропией в тестах срабатывает на правило `generic-api-key`, и CI краснеет.
**How to avoid:** в тестах явно фейковые значения с низкой энтропией (`"test-ingest-token-that-is-at-least-32-chars"`, бот-токен `"123:fake"`), либо комментарий `// gitleaks:allow`. Настоящие значения есть только в `.env` на VM. `.env.example` содержит только имена. Отдельный шаг CI: `git check-ignore -q .env`, который упадёт, если `.env` исчезнет из `.gitignore`.

### Pitfall 10: `global.json` и preview SDK на машине разработчика
**What goes wrong:** локально установлен `10.0.400-preview`. Без `allowPrerelease` CLI по умолчанию **рассматривает prerelease** («If you're not in Visual Studio, the default value is true») и сборка пойдёт на preview SDK `[CITED: learn.microsoft.com/dotnet/core/tools/global-json]`.
**How to avoid:** `{"sdk":{"version":"10.0.100","rollForward":"latestFeature","allowPrerelease":false},"test":{"runner":"Microsoft.Testing.Platform"}}`. Секция `test.runner` нужна, чтобы `dotnet test` на SDK 10 работал через MTP `[CITED: xunit.net/docs/getting-started/v3/microsoft-testing-platform]`.

### Pitfall 11: Сумма в чужой валюте посчитается как злотые
**What goes wrong:** временный парсер прочитает `€5,00` как 5.00 PLN (проверено прогоном). ING-09 говорит «оплата не в PLN не записывается как сумма в злотых».
**How to avoid:** в Phase 1 одна проверка: если в сырой строке есть `€ $ £` или `EUR/USD/GBP`, сумма считается «не разобранной» (D-03 с ⚠️). Полная обработка будет в Phase 3.

### Pitfall 12: Отрицательная сумма (возврат) и CHECK-ограничение
**What goes wrong:** если добавить `CHECK (amount > 0)` и пропустить `-23,50`, INSERT упадёт и трата пропадёт. Вдобавок Postgres пишет в **свой** лог `DETAIL: Failing row contains (...)` со всеми значениями.
**How to avoid:** парсер возвращает `null` для ≤ 0 (проверено: `-23,50 zł → null`, `0,00 zł → null`). Значит строка сохраняется как «не разобрано», и CHECK не нужен.

### Pitfall 13: Бот не может написать первым
**What goes wrong:** `SendMessage` получает 403 «bot can't initiate conversation», если владелец ещё не нажал Start у бота `[ASSUMED: стандартное поведение Bot API]`.
**How to avoid:** в чекпоинте настройки бота: написать боту `/start`, затем узнать chat id (для личного чата он равен user id), например через @userinfobot или `curl https://api.telegram.org/bot<TOKEN>/getUpdates` **на VM** (не в браузере, чтобы токен не осел в истории).

### Pitfall 14: Долгая отправка в Telegram задерживает ответ Команде
**How to avoid:** отдельный `CancellationTokenSource(TimeSpan.FromSeconds(10))` для `SendMessage`, **не** `RequestAborted`: отправка должна завершиться, даже если клиент отключился. Таймаут по умолчанию у клиента больше `[ASSUMED]`.

## Code Examples

Ниже код собран из проверенных API. Фрагменты с пометкой `VERIFIED: local run` реально выполнены 2026-11-16 на SDK 10 / NodaTime 3.3.7.

### global.json
```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestFeature", "allowPrerelease": false },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

### Directory.Packages.props
```xml
<Project>
  <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
  <ItemGroup>
    <!-- EF/ASP.NET строго 10.x: Npgsql 10.0.3 требует EF [10.0.4, 11.0.0) -->
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.15" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.15" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime" Version="10.0.3" />
    <PackageVersion Include="EFCore.NamingConventions" Version="10.0.1" />
    <PackageVersion Include="NodaTime" Version="3.3.7" />
    <PackageVersion Include="Telegram.Bot" Version="22.10.4" />
    <PackageVersion Include="Serilog.AspNetCore" Version="10.0.0" />
    <PackageVersion Include="xunit.v3" Version="4.0.3" />
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.16.0" />
    <PackageVersion Include="NSubstitute" Version="6.2.0" />
  </ItemGroup>
</Project>
```

### Program.cs (каркас)
```csharp
// Source: Serilog AddSerilog/ReadFrom.Services — Context7 /serilog/serilog-aspnetcore;
//         UseNodaTime — Context7 /npgsql/efcore.pg/v10.0.3; остальное — стандартные API ASP.NET Core 10
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((sp, lc) => lc
    // Уровни заданы в коде, а не в appsettings: в проде их нельзя случайно понизить через env (PRIV-03)
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning) // URL Telegram содержит токен
    .ReadFrom.Services(sp)                        // тест регистрирует ILogEventSink и видит все события
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services.AddOptions<BotOptions>().BindConfiguration("Bot")
    .Validate(o => o.IngestToken.Length >= 32 && o.TelegramToken.Length > 0 && o.OwnerChatId != 0,
              "Bot__IngestToken (>=32), Bot__TelegramToken, Bot__OwnerChatId обязательны")
    .ValidateOnStart();
builder.Services.AddDbContext<AppDb>((sp, o) => o
    .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default"), x => x.UseNodaTime())
    .UseSnakeCaseNamingConvention());
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
    new TelegramBotClient(sp.GetRequiredService<IOptions<BotOptions>>().Value.TelegramToken));
builder.Services.AddHealthChecks();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddFixedWindowLimiter("ingest", w => { w.PermitLimit = 30; w.Window = TimeSpan.FromMinutes(1); });
});
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 64 * 1024);

var app = builder.Build();

if (args is ["migrate"])  // docker compose run --rm app migrate
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDb>().Database.MigrateAsync();
    return;
}

app.UseSerilogRequestLogging();   // одна строка на запрос: метод, путь, статус, время, без тела
app.UseRateLimiter();
app.MapHealthChecks("/health");
app.MapPost("/api/v1/transactions", Ingest.Handle).RequireRateLimiting("ingest");
app.Run();

public sealed class BotOptions
{
    public string IngestToken { get; set; } = "";
    public string TelegramToken { get; set; } = "";
    public long OwnerChatId { get; set; }
}
```
`public partial class Program {}` в .NET 10 не нужен: source generator сам делает top-level `Program` публичным `[CITED: ASP.NET Core 10 release notes via websearch — MEDIUM]`. Если `WebApplicationFactory<Program>` не увидит класс, добавить эту строку.

### Ingest.cs
```csharp
static class Ingest
{
    public static async Task<IResult> Handle(HttpRequest req, AppDb db, IClock clock,
        ITelegramBotClient bot, IOptions<BotOptions> opt, ILogger<Program> log)
    {
        if (!TokenMatches(req.Headers.Authorization.ToString(), opt.Value.IngestToken))
            return Results.Unauthorized();

        using var reader = new StreamReader(req.Body);
        var raw = await reader.ReadToEndAsync();
        Payload? p = null;
        try { p = JsonSerializer.Deserialize<Payload>(raw, JsonSerializerOptions.Web); }
        catch (JsonException) { }   // кривой JSON: сохраняем как «не разобрано», тело не теряем

        var now = clock.GetCurrentInstant();
        var tx = new Transaction
        {
            Id = Guid.CreateVersion7(), UserId = User.OwnerId,
            Amount = Parsing.Amount(p?.Amount), AmountRaw = p?.Amount,
            Merchant = p?.Merchant, Card = p?.Card,
            OccurredAt = Parsing.Time(p?.Time) ?? now, ReceivedAt = now, RawBody = raw,
        };
        db.Transactions.Add(tx);
        await db.SaveChangesAsync();

        decimal spent = 0;
        if (tx.Amount is not null)
        {
            var (start, end) = Week.Containing(tx.OccurredAt);
            spent = await db.Transactions
                .Where(t => t.UserId == tx.UserId && t.Amount != null && t.OccurredAt >= start && t.OccurredAt < end)
                .SumAsync(t => t.Amount) ?? 0m;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await bot.SendMessage(opt.Value.OwnerChatId, Messages.Confirmation(tx, spent),
                                  ParseMode.Html, cancellationToken: cts.Token);
        }
        catch (Exception ex)  // трата уже сохранена, Команде всё равно отвечаем 201
        {
            log.LogWarning("Telegram send failed for transaction {TransactionId}: {ErrorType}", tx.Id, ex.GetType().Name);
        }
        return Results.Json(new { id = tx.Id }, statusCode: StatusCodes.Status201Created);
    }

    static bool TokenMatches(string header, string expected)
    {
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal)) return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(header[prefix.Length..])),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }

    record Payload(string? Amount, string? Merchant, string? Card, string? Time);
}
```
Решение по «статусу неразобранной суммы» (Claude's Discretion): **`amount IS NULL` и есть статус.** Отдельная колонка не нужна. Полноценный `status` (Active/Duplicate/Deleted) появится в Phase 3 вместе с идемпотентностью. Если для `TransactionId` логируется только id (uuid), это не нарушает PRIV-03.

### Parsing.cs: разбор суммы (временный) и времени
```csharp
// VERIFIED: local run — "45,99 zł"→45.99, "1 234,56 zł"(U+00A0/U+202F)→1234.56, "PLN 23.50"→23.50,
// "23"→23, "0,99 zł"→0.99, "-23,50 zł"→null, ""→null, "abc"→null, "1.234,56"→1234.56,
// "1,234.56"→1234.56, "0,00 zł"→null, "12,5"→12.5  (и "€5,00"→5.00 — поэтому нужна проверка валюты ниже)
public static decimal? Amount(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return null;
    // shortcut: временный парсер до корпуса SPIKE-01; полная версия (ING-04, ING-09) — Phase 3
    if (raw.IndexOfAny(['€', '$', '£']) >= 0 || raw.Contains("EUR") || raw.Contains("USD") || raw.Contains("GBP"))
        return null;
    var s = new string(raw.Where(c => char.IsDigit(c) || c is ',' or '.' or '-').ToArray());
    int sep = s.LastIndexOfAny([',', '.']);
    s = sep >= 0 && s.Length - sep - 1 is 1 or 2
        ? s[..sep].Replace(",", "").Replace(".", "") + "." + s[(sep + 1)..]
        : s.Replace(",", "").Replace(".", "");
    return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                            CultureInfo.InvariantCulture, out var v) && v > 0 && v < 1_000_000 ? v : null;
}

// VERIFIED: local run — "16.11.2026 14:03:05"→2026-11-16T13:03:05Z; "29.03.2026 02:30:00" (дыра DST)→01:30Z;
// "25.10.2026 02:30:00" (двойной час)→00:30Z (более раннее смещение); "garbage"/"16.11.2026, 14:03"→fail
static readonly LocalDateTimePattern TimePattern =
    LocalDateTimePattern.CreateWithInvariantCulture("dd'.'MM'.'uuuu HH':'mm':'ss");
public static Instant? Time(string? raw)
{
    if (raw is null) return null;
    var r = TimePattern.Parse(raw.Trim());
    return r.Success ? Week.Warsaw.AtLeniently(r.Value).ToInstant() : null;
}
```
`.` и `:` в шаблоне заключены в кавычки. Страница NodaTime с шаблонами не говорит прямо, литералы ли они без кавычек, а в кавычках вопрос снимается `[CITED: nodatime.org/3.3.x/userguide/localdatetime-patterns — стандартный шаблон 'o' сам квотирует их]`.

### Week.cs
```csharp
// VERIFIED: local run (см. Pattern 4)
public static class Week
{
    public static readonly DateTimeZone Warsaw = DateTimeZoneProviders.Tzdb["Europe/Warsaw"];
    public const decimal Limit = 200m;   // shortcut: константы до Phase 5 (LIM-01)

    public static (Instant Start, Instant End) Containing(Instant t)
    {
        var monday = t.InZone(Warsaw).Date.With(DateAdjusters.PreviousOrSame(IsoDayOfWeek.Monday));
        return (Warsaw.AtStartOfDay(monday).ToInstant(), Warsaw.AtStartOfDay(monday.PlusWeeks(1)).ToInstant());
    }
}
```

### Messages.cs
```csharp
// Форматы D-01..D-04. NFI собрана вручную, чтобы не зависеть от ICU/NLS: на Windows, в CI и в контейнере
// результат одинаковый. VERIFIED: local run — 45.99→"45,99", 1234.5→"1 234,50" (U+00A0), 200→"200"
static readonly NumberFormatInfo Nfi = CreateNfi();
static NumberFormatInfo CreateNfi()
{
    var n = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
    n.NumberDecimalSeparator = ","; n.NumberGroupSeparator = "\u00A0";
    return n;
}
static string Money(decimal v) => v.ToString("#,0.00", Nfi);
static string Esc(string? s) => (s ?? "?").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

public static string Confirmation(Transaction tx, decimal spent)
{
    if (tx.Amount is null)
        return $"⚠️ Не разобрал сумму: «{Esc(tx.AmountRaw ?? "")}» · {Esc(tx.Merchant)}";
    var limit = Week.Limit.ToString("0", Nfi);
    var line2 = spent <= Week.Limit
        ? $"🟢 Осталось {Money(Week.Limit - spent)} из {limit} zł"
        : $"🔴 <b>Потрачено {Money(spent)}/{limit} · Перерасход {Money(spent - Week.Limit)} zł</b>";
    return $"− {Money(tx.Amount.Value)} zł · {Esc(tx.Merchant)}\n{line2}";   // "−" = U+2212
}
```
Для неразобранной суммы стоит обрезать `AmountRaw` примерно до 100 символов (лимит сообщения Telegram 4096).

### AppDb.cs: схема (Claude's Discretion)
```csharp
public sealed class User { public long Id { get; set; } public const long OwnerId = 1; }
public sealed class Transaction
{
    public Guid Id { get; set; }                 // UUIDv7: упорядочен по времени, пригоден для callback-данных в Phase 4
    public long UserId { get; set; }             // SC-4: с первой миграции
    public decimal? Amount { get; set; }         // null = «сумма не разобрана» (D-03)
    public string? AmountRaw { get; set; }
    public string? Merchant { get; set; }
    public string? Card { get; set; }
    public Instant OccurredAt { get; set; }      // timestamptz
    public Instant ReceivedAt { get; set; }
    public string RawBody { get; set; } = "";    // text, НЕ jsonb (кривой JSON тоже сохраняется)
}
// OnModelCreating:
b.Entity<User>(e => { e.Property(u => u.Id).ValueGeneratedNever(); e.HasData(new User { Id = User.OwnerId }); });
b.Entity<Transaction>(e =>
{
    e.Property(t => t.Amount).HasPrecision(12, 2);
    e.HasOne<User>().WithMany().HasForeignKey(t => t.UserId);
    e.HasIndex(t => new { t.UserId, t.OccurredAt });
});
```
**Почему `ValueGeneratedNever` у `users.id`:** если засеять `HasData(Id = 1)` в identity-колонку, последовательность Postgres не сдвинется, и в Phase 7 первая регистрация упадёт на duplicate key `[ASSUMED: известное поведение HasData + identity в Npgsql]`. Когда в Phase 7 появится регистрация, identity можно добавить с `START WITH 2`.

### Тестовая фикстура (xUnit v3 + WAF + Testcontainers + NSubstitute)
```csharp
// Source: Testcontainers PostgreSqlBuilder(string image), GetConnectionString, UseSetting — Context7 /testcontainers/testcontainers-dotnet
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string IngestToken = "test-ingest-token-that-is-at-least-32-chars";
    public const string BotToken = "123:fake-bot-token-sentinel";
    readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:18-alpine").Build();
    public ITelegramBotClient Bot { get; } = Substitute.For<ITelegramBotClient>();
    public CapturingSink Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        b.UseEnvironment("Production");            // проверяем ровно прод-конфиг логов
        b.UseSetting("ConnectionStrings:Default", _pg.GetConnectionString());
        b.UseSetting("Bot:IngestToken", IngestToken);
        b.UseSetting("Bot:TelegramToken", BotToken);
        b.UseSetting("Bot:OwnerChatId", "42");
        b.ConfigureTestServices(s => { s.AddSingleton(Bot); s.AddSingleton<ILogEventSink>(Logs); });
    }
    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDb>().Database.MigrateAsync(); // заодно ловит забытую миграцию
    }
    public override async ValueTask DisposeAsync() { await base.DisposeAsync(); await _pg.DisposeAsync(); }
}

public sealed class CapturingSink : ILogEventSink
{
    readonly ConcurrentQueue<string> _lines = new();
    readonly RenderedCompactJsonFormatter _fmt = new();   // тот же форматтер, что в проде
    public void Emit(LogEvent e) { var w = new StringWriter(); _fmt.Format(e, w); _lines.Enqueue(w.ToString()); }
    public string All => string.Join("\n", _lines);
    public int Count => _lines.Count;
}

// Проверка вызова Telegram (Pitfall 6): SendRequest, а не SendMessage
await f.Bot.Received(1).SendRequest(
    Arg.Is<SendMessageRequest>(r => r.Text.Contains("SENTINEL-MERCHANT") && r.ParseMode == ParseMode.Html),
    Arg.Any<CancellationToken>());
```
`[ASSUMED]`: точная сигнатура `override DisposeAsync` у `WebApplicationFactory` и вывод типа `TResponse` из `Arg.Is<SendMessageRequest>`. Проверить компиляцией в первой задаче.

**Тест PRIV-03 (`LogPrivacyTests`):** POST с ASCII-сентинелами (`merchant = "SENTINEL-MERCHANT-7F3A"`, `card = "SENTINEL-CARD-9C1B"`, `amount = "987,65 zł"`), затем второй прогон, где `Bot.SendRequest(...)` бросает `ApiRequestException`, затем запрос с неверным токеном `"SENTINEL-WRONG-TOKEN-…"`. Проверки: `Logs.Count > 0` (тест не пустой: строка request logging есть) и `Logs.All` **не содержит** `SENTINEL-MERCHANT`, `SENTINEL-CARD`, `987,65`, `987.65`, `IngestToken`, `BotToken`, `SENTINEL-WRONG`. Сентинелы ASCII, потому что JSON-форматтер может экранировать не-ASCII, и тогда поиск по «Żabka» даст ложный «чисто» `[ASSUMED]`.

**Schema-тест (SC-4):** `db.Database.SqlQueryRaw<string>("SELECT t.table_name AS \"Value\" FROM information_schema.tables t WHERE t.table_schema='public' AND t.table_type='BASE TABLE' AND t.table_name NOT IN ('users') AND t.table_name NOT LIKE '\\_\\_%' AND NOT EXISTS (SELECT 1 FROM information_schema.columns c WHERE c.table_schema='public' AND c.table_name=t.table_name AND c.column_name='user_id')")` должен вернуть пусто. Этот тест защищает правило и в следующих фазах.

### Dockerfile
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Packages.props ./
COPY src/FinanceBot/FinanceBot.csproj src/FinanceBot/
RUN dotnet restore src/FinanceBot/FinanceBot.csproj
COPY src/ src/
RUN dotnet publish src/FinanceBot/FinanceBot.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "FinanceBot.dll"]
```
`aspnet:10.0` = **Ubuntu 24.04 noble** на amd64 и arm64v8 `[CITED: github.com/dotnet/dotnet-docker/blob/main/README.aspnet.md]`. Тег один и тот же для обеих архитектур, поэтому на A1 подтянется arm64 без флагов (multi-arch manifest `[ASSUMED: стандарт MCR]`). Полный noble выбран, а не chiseled-extra: есть shell для отладки (`docker compose exec app bash`) и меньше сюрпризов с нативными библиотеками SkiaSharp в Phase 6. ICU и tzdata нам не нужны (своя NFI и TZDB NodaTime), так что выбор образа на корректность не влияет. Порт 8080 и `$APP_UID` (non-root) по умолчанию в образах .NET 8+ `[ASSUMED]`.

### compose.yaml
```yaml
services:
  app:
    build: .
    restart: unless-stopped
    env_file: .env                     # Bot__IngestToken, Bot__TelegramToken, Bot__OwnerChatId
    environment:
      ConnectionStrings__Default: Host=postgres;Database=financebot;Username=financebot;Password=${POSTGRES_PASSWORD}
    depends_on:
      postgres: { condition: service_healthy }
  postgres:
    image: postgres:18-alpine
    restart: unless-stopped
    environment:
      POSTGRES_USER: financebot
      POSTGRES_DB: financebot
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes: [ "pgdata:/var/lib/postgresql" ]          # PG18+: не /data (Pitfall 7)
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U financebot -d financebot"]
      interval: 5s
      retries: 12
    # ports: НЕ публиковать — БД доступна только внутри сети compose
  caddy:
    image: caddy:2.11-alpine
    restart: unless-stopped
    ports: [ "80:80", "443:443" ]
    environment: { DOMAIN: "${DOMAIN}" }
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_data:/data                                 # сертификаты переживают пересоздание
      - caddy_config:/config
volumes: { pgdata: {}, caddy_data: {}, caddy_config: {} }
```
Пароль и токены генерировать как `openssl rand -hex 32`: в hex нет символов `;`, `=` и пробелов, которые ломают строку подключения или заголовок.

### Caddyfile
```
{$DOMAIN} {
	reverse_proxy app:8080
}
```
`[ASSUMED: синтаксис {$ENV} в Caddyfile — стандартный]`. Access-логов в Caddy по умолчанию нет, пока не добавлена директива `log` `[ASSUMED]`. Не добавлять её.

### deploy.sh (ручной деплой D-13)
```bash
#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
git pull --ff-only
docker compose build app
docker compose run --rm app migrate      # ручной шаг миграций (Phase 2 → migrator)
docker compose up -d
curl -fsS "https://$(grep '^DOMAIN=' .env | cut -d= -f2)/health" && echo " OK"
```

### .github/workflows/ci.yml
```yaml
name: ci
on:
  push: { branches: [main] }
  pull_request:
jobs:
  build-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with: { fetch-depth: 0 }                      # gitleaks сканирует всю историю
      - uses: gitleaks/gitleaks-action@v3
        env: { GITHUB_TOKEN: "${{ secrets.GITHUB_TOKEN }}" }   # GITLEAKS_LICENSE не нужен для личного аккаунта
      - run: git check-ignore -q .env                 # .env обязан быть в .gitignore
      - uses: actions/setup-dotnet@v6
        with: { global-json-file: global.json }
      - run: dotnet test
```
gitleaks-action: «GITLEAKS_LICENSE … required for organizations, not required for user accounts». Репо `YahorHurski/telegram-finance-bot` принадлежит личному аккаунту и публичное `[VERIFIED: gh repo view → owner YahorHurski, PUBLIC; CITED: github.com/gitleaks/gitleaks-action README]`. Docker на `ubuntu-latest` предустановлен, Testcontainers работает без настройки `[CITED: CLAUDE.md, ASSUMED стандарт]`. Точный синтаксис `dotnet test` в MTP-режиме с `.slnx` в корне `[ASSUMED]`; при неоднозначности указать `dotnet test --solution FinanceBot.slnx`.

### iOS-Команда (docs/shortcut.md, кратко)
Автоматизация «Транзакция» (Wallet): все карты, «Выполнять сразу», уведомление о запуске выключено. Действия: «Формат даты» (Текущая дата, свой формат `dd.MM.yyyy HH:mm:ss`) → «Получить содержимое URL»: POST `https://<sub>.duckdns.org/api/v1/transactions`; заголовки `Authorization: Bearer <token>`, `Content-Type: application/json`; тело JSON, все 4 поля типа **Текст**: amount = Сумма, merchant = Продавец, card = Карта, time = отформатированная дата → «Если» (содержимое URL **не содержит** `"id"`) → «Показать уведомление: Трата не сохранена». `[ASSUMED]`: по сообществу, «Получить содержимое URL» при 4xx/5xx не падает и возвращает тело, а без сети действие падает и iOS показывает свою ошибку. Это пункт SPIKE-01, проверить на устройстве в UAT. Ответ 401 у нас без тела, поэтому `"id"` в нём точно нет.

### Опциональный спайк SkiaSharp на arm64 (ROADMAP Notes)
15 минут, не в солюшене: каталог `spikes/skia-arm64/` с консолью на ScottPlot 5.1.59, которая рендерит PNG и печатает число байт. Собрать и запустить на VM в том же `aspnet:10.0`. Результат записать в SUMMARY. Риск низкий: полный noble, NoDependencies-ассеты `[CITED: CLAUDE.md]`. Если план перегружен, перенести в начало Phase 6.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `SendTextMessageAsync` | `SendMessage` (extension), `SendRequest` в интерфейсе | Telegram.Bot v22 | старые туториалы не компилируются; мокать `SendRequest` `[CITED: Context7 telegrambots book migrate v22]` |
| `public partial class Program {}` | генерируется автоматически | .NET 10 | строка не нужна `[CITED: websearch → ASP.NET Core 10 release notes]` |
| VSTest + Microsoft.NET.Test.Sdk | MTP v2 + `global.json test.runner` | xunit.v3 4.0 / .NET 10 SDK | `Microsoft.NET.Test.Sdk` не нужен `[CITED: xunit.net MTP docs]` |
| Том `/var/lib/postgresql/data` | `/var/lib/postgresql` | postgres 18 image | старые compose-файлы ломаются `[CITED: docker-library docs]` |
| gitleaks-action@v2 | @v3 | 2026-05-30 | `[VERIFIED: gh api]` |
| Логи HttpClientFactory с полным URL | query/fragment редактируются, путь нет | .NET 9 | токен бота в пути всё ещё виден `[CITED: MS compat docs]` |
| A1 Always Free 4 OCPU/24 GB | 2 OCPU/12 GB | 2026 | `[CITED: Oracle Always Free page]` |

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | PAYG защищает от отбора простаивающих инстансов (только письмо Oracle 2023, в документации нет) | Pitfall 1 | VM остановят, сервис недоступен. Смягчение: Docker + бэкапы вне Oracle |
| A2 | Docker-порты на OCI Ubuntu требуют правил iptables в INPUT (или не требуют) | Pitfall 2 | Лишний шаг безвреден; без шага возможен недоступный 443. Проверить curl снаружи |
| A3 | Лимит LE ≈5 неудачных валидаций/час/имя | Pitfall 3 | Только задержка, не потеря |
| A4 | E2.1.Micro не потянет сборку | Pitfall 8 | Если выбрать x86, сборка может падать по OOM |
| A5 | `override DisposeAsync` у WAF и вывод типа `Arg.Is<SendMessageRequest>` компилируются как в примере | Code Examples | Мелкая правка в первой задаче тестов |
| A6 | Порт 8080 и `$APP_UID` по умолчанию в `aspnet:10.0`; multi-arch тег | Dockerfile | Неверный порт → 502 в Caddy; легко увидеть |
| A7 | `{$DOMAIN}` в Caddyfile; access-логи выключены по умолчанию | Caddyfile | Если access-логи есть, в них путь и IP, но не тело и не Authorization |
| A8 | «Получить содержимое URL» при 4xx не падает; проверка `"id"` в ответе работает | iOS-Команда | Уведомление «не сохранено» может не показаться. Проверяется на устройстве (часть SPIKE-01) |
| A9 | Формат поля «Сумма» в JSON-тексте Команды выглядит как `45,99 zł` | Parsing | Парсер покрывает варианты; неизвестный формат станет ⚠️ «не разобрано», трата не теряется |
| A10 | `HasData` в identity-колонку не сдвигает sequence | Schema | Если сдвигает, `ValueGeneratedNever` всё равно безвреден |
| A11 | Npgsql скрывает `PostgresException.Detail` без `Include Error Detail` | Pitfall 4 | Закрывается тестом PRIV-03 только частично (ошибки БД в тесте не моделируются) |
| A12 | Бот не может написать пользователю до `/start` | Pitfall 13 | Только порядок шагов в чекпоинте |
| A13 | Синтаксис `dotnet test` (MTP) без аргументов находит solution в корне | CI | Добавить `--solution` |
| A14 | При апгрейде на PAYG возможна временная авторизация на карте | Oracle | Информировать пользователя; нужна карта |

## Open Questions

1. **Как читать success criterion 1 про «по документации проверено» (PAYG)?**
   - Что известно: документация молчит, письмо Oracle 2023 прямо обещает защиту, отчёты 2026 года расходятся.
   - Рекомендация: checkpoint:decision с пользователем. Принять письмо как доказательство и записать формулировку в SUMMARY/VERIFICATION. Опционально пользователь задаёт вопрос в поддержку Oracle и прикладывает ответ.
2. **Остаток какой недели показывать, если оплата пришла с опозданием через границу недели (вс 23:58 → пришло пн 00:01)?**
   - Рекомендация: неделю **траты** (`occurred_at`). Она согласуется с D-08 («время траты определяет неделю») и делается без лишнего кода. Сообщить пользователю одной строкой.
3. **Что из инфраструктуры уже сделано (VM, PAYG, DuckDNS, бот, chat id)?** CONTEXT говорит уточнить на чекпоинте. План начинает с checkpoint:human-action со списком и пропускает готовое.
4. **CI на ветке фазы.** Работа идёт в `gsd/phase-01-hodyachiy-skelet`, а D-11 говорит «на push в main». Рекомендация: триггер `push: main` + `pull_request`. Тогда CI проверит PR до мержа, и ruleset «Protect main» (STATE Pending Todos) можно будет дополнить required check уже в Phase 2.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK 10 | сборка, тесты, `dotnet ef` | ✓ | 10.0.301 (+ 10.0.400-preview, см. Pitfall 10) | — |
| Docker Desktop (Windows) | Testcontainers локально | ✓ установлен, **демон не запущен** (`failed to connect to … dockerDesktopLinuxEngine`) | client 29.1.3 | Запустить Docker Desktop перед `dotnet test`; иначе интеграционные тесты только в CI |
| git | всё | ✓ | 2.53.0 | — |
| gh CLI | проверка CI/ruleset | ✓ | 2.96.0 | — |
| ssh | деплой на VM | ✓ | OpenSSH 10.2p1 | — |
| GitHub repo | CI | ✓ `YahorHurski/telegram-finance-bot`, PUBLIC, личный аккаунт | — | — |
| Oracle Cloud VM (A1, Ubuntu 24.04 arm64) + Docker Engine + compose plugin | FND-02 | ✗ не проверено (вне машины) | — | checkpoint:human-action |
| DuckDNS поддомен + токен | FND-02 | ✗ не проверено | — | checkpoint:human-action |
| Telegram-бот от @BotFather + chat id владельца | MAN-03 | ✗ не проверено | — | checkpoint:human-action |
| iPhone с Командами | ING-03 E2E | у пользователя | — | curl с ноутбука как замена до настройки Команды |

**Missing dependencies with no fallback:** VM / DuckDNS / бот. Это ручные шаги пользователя, план ставит их чекпоинтами.
**Missing dependencies with fallback:** демон Docker локально (fallback: CI на ubuntu-latest).

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit v3 4.0.3 на Microsoft Testing Platform v2 (`global.json` `test.runner`) |
| Config file | нет; Wave 0 создаёт `global.json`, `tests/FinanceBot.Tests/FinanceBot.Tests.csproj` (`OutputType=Exe`) |
| Quick run command | `dotnet test` (весь набор ≈ 20–40 c, из них большая часть уходит на старт контейнера) `[ASSUMED время]`. Только unit: `dotnet test -- --filter-class "*ParsingTests"` и т.п. `[ASSUMED синтаксис фильтра MTP/xUnit v3]` |
| Full suite command | `dotnet test` (требует запущенный Docker) |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| ING-03 (упр.) | POST с верным токеном → 201 `{id}`, строка в БД с `user_id=1`, `raw_body` = тело как есть | integration | `dotnet test` → `IngestTests.ValidToken_SavesRowWithRawBody` | ❌ Wave 0 |
| ING-03 (упр.) | Нет заголовка / неверный токен → 401, строки нет, Telegram не вызван | integration | `IngestTests.MissingOrWrongToken_Returns401` | ❌ |
| D-03/D-10 | Неразбираемая сумма или невалидный JSON → 201, `amount IS NULL`, сообщение ⚠️ | integration | `IngestTests.UnparsedAmount_Saved_And_WarningSent` | ❌ |
| MAN-03 (упр.) | Telegram получает `SendMessageRequest` (chat 42, Html, текст с продавцом и остатком) | integration | `IngestTests.ValidToken_SendsConfirmation` | ❌ |
| MAN-03 (упр.) | Тексты D-01/D-02/D-03, NBSP-группы, U+2212, `<b>` при перерасходе, экранирование `& < >`, `Mokotów` не портится | unit | `MessagesTests` | ❌ |
| D-08 | Разбор `dd.MM.yyyy HH:mm:ss` как Warsaw, DST-дыра/двойной час, мусор → null (фолбэк на received_at) | unit | `ParsingTests.Time_*` | ❌ |
| ING-04 (зачаток) | Таблица сумм (см. VERIFIED-набор + валюта → null) | unit | `ParsingTests.Amount_*` (Theory) | ❌ |
| LIM неделя | вс 23:59:59 / пн 00:00, недели 167 ч и 169 ч | unit | `WeekTests` | ❌ |
| PRIV-03 | В логах нет сентинелов продавца/карты/суммы/токенов (успех, ошибка Telegram, 401); логи не пустые | integration | `LogPrivacyTests.NoSensitiveDataInLogs` | ❌ |
| SC-4 | Каждая таблица, кроме `users` и `__*`, имеет `user_id` | integration | `SchemaTests.AllUserDataTablesHaveUserId` | ❌ |
| FND-05 | `.env` игнорируется; gitleaks чистый на всей истории | CI | шаги `git check-ignore -q .env`, `gitleaks-action@v3` | ❌ |
| FND-02 | `https://<sub>.duckdns.org/health` → 200 с VM | smoke (ручной) | `curl -fsS https://<sub>.duckdns.org/health` (в конце `deploy.sh`) | ❌ |
| FND-02 | PAYG + бюджетный алерт | manual-only | скриншот/запись в SUMMARY: консоль Oracle недоступна автотестам | — |
| E2E | Реальная оплата Apple Pay → сообщение в Telegram | manual (UAT) | — устройство пользователя | — |

### Sampling Rate
- **Per task commit:** `dotnet test` (или только unit-классы, если Docker не запущен)
- **Per wave merge:** `dotnet test` полностью, с Docker
- **Phase gate:** CI зелёный на PR/main + `curl /health` с VM + одна реальная оплата (UAT) до `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `.gitignore` (`dotnet new gitignore` + `.env`), `.env.example`, `.dockerignore`. **Первый коммит фазы**, до любого `.env`
- [ ] `global.json`, `Directory.Packages.props`, solution, `.config/dotnet-tools.json`
- [ ] `tests/FinanceBot.Tests/FinanceBot.Tests.csproj` (xunit.v3, Mvc.Testing 10.0.15, Testcontainers.PostgreSql, NSubstitute, `OutputType=Exe`, ссылка на `src/FinanceBot`)
- [ ] `ApiFactory.cs`, `CapturingSink.cs`: общая фикстура
- [ ] `.github/workflows/ci.yml`

## Security Domain

`security_enforcement: true`, `security_asvs_level: 1`, `security_block_on: "high"` `[VERIFIED: .planning/config.json:49-51]`.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | yes | Bearer-токен 256 бит (`openssl rand -hex 32`), SHA-256 + `CryptographicOperations.FixedTimeEquals`, проверка до чтения тела, fail-closed при пустом или коротком токене (`ValidateOnStart`) |
| V3 Session Management | no | сессий нет |
| V4 Access Control | частично | один владелец; `user_id` в схеме с первой миграции (изоляция в Phase 7) |
| V5 Input Validation | yes | лимит тела 64 KB (Kestrel), JSON → `string?` поля, парсер с белым списком символов, отказ при ≤0/валюте; экранирование `& < >` для Telegram HTML; EF параметризует SQL |
| V6 Cryptography | yes | только BCL (`RandomNumberGenerator`/openssl, `SHA256`), своей криптографии нет |
| V7 Error Handling & Logging | yes | PRIV-03: уровни в коде, без тел, параметров и деталей ошибок; тест с сентинелами; ответы 401/500 без подробностей |
| V9 Communications | yes | TLS только через Caddy (Let's Encrypt); Postgres и app не публикуют порты; Telegram по HTTPS |
| V14 Configuration | yes | секреты в `.env` (chmod 600 на VM, `.gitignore`, gitleaks); контейнер app non-root (`$APP_UID`); образы с фиксированными мажорными тегами |

### Known Threat Patterns for this stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Подбор токена / флуд эндпоинта украденным токеном | Spoofing / DoS | 256-битный токен; fixed-window rate limiter 30/мин → 429 |
| Утечка токена бота через логи HttpClient | Information Disclosure | синглтон `new TelegramBotClient(token)`, override `System.Net.Http.HttpClient` = Warning, тест PRIV-03 |
| Финансовые данные в логах (тело, SQL-параметры, детали ошибок Postgres) | Information Disclosure | без HttpLogging и SensitiveDataLogging, без `Include Error Detail`; свой лог ошибки содержит только тип и id |
| HTML-инъекция через имя продавца в сообщение бота | Tampering | ручное экранирование `& < >` |
| Секреты в публичном репо | Information Disclosure | `.gitignore` первым коммитом, gitleaks-action@v3 на всей истории, `git check-ignore` в CI |
| Postgres доступен из интернета | Elevation / Disclosure | порт 5432 не публикуется; Security List открывает только 22/80/443 |
| Большое тело запроса (память) | DoS | `MaxRequestBodySize = 64 KB` |
| Подмена ответа Команде (MITM) | Tampering | только HTTPS (Shortcuts не принимает самоподписанные сертификаты) |

## Sources

### Primary (HIGH confidence)
- NuGet registry API (`api.nuget.org/v3-flatcontainer/*/index.json`, nuspec), 2026-11-16: версии всех пакетов, зависимости Serilog.AspNetCore 10.0.0 и Npgsql NodaTime 10.0.3
- Telegram.Bot 22.10.4 nupkg, `lib/net6.0/Telegram.Bot.xml`: члены `ITelegramBotClient`, сигнатура `SendMessage`, `SendMessageRequest`, `ParseMode`, конструкторы
- Локальные прогоны `dotnet run probe.cs` / `probe2.cs` (SDK 10, NodaTime 3.3.7): форматирование чисел, разбор времени, границы недели (DST), `WebUtility.HtmlEncode`, парсер сумм
- Context7 `/serilog/serilog-aspnetcore`: `AddSerilog((services, lc) => …)`, `ReadFrom.Services` подключает `ILogEventSink` из DI
- Context7 `/npgsql/efcore.pg/v10.0.3`: `UseNpgsql(cs, o => o.UseNodaTime())`
- Context7 `/testcontainers/testcontainers-dotnet`: `PostgreSqlBuilder(string image)`, `GetConnectionString`, `UseSetting` с WAF
- Context7 `/websites/telegrambots_github_io_book`: миграция на v22 (`SendMessage`, `SendRequest`), рекомендация HTML
- `gh api repos/*/releases/latest`: checkout v7.0.1, setup-dotnet v6.1.0, gitleaks-action v3.0.0, caddy v2.11.9; Docker Hub tags: `postgres:18-alpine`, `caddy:2.11-alpine`

### Secondary (MEDIUM confidence)
- https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm: правило idle reclamation, лимит A1
- https://docs.oracle.com/iaas/Content/FreeTier/freetier.htm: апгрейд аккаунта
- https://docs.oracle.com/en-us/iaas/Content/Billing/Tasks/create-budget.htm: шаги Budget + Alert Rule (Actual Spend, Absolute Amount, Email Recipients)
- https://docs.oracle.com/en-us/iaas/Content/Network/Tasks/managingpublicIPs.htm: ephemeral IP переживает stop
- https://github.com/docker-library/docs/blob/master/postgres/content.md: PGDATA/VOLUME в PG18
- https://github.com/dotnet/dotnet-docker/blob/main/README.aspnet.md: `10.0` = noble, arm64v8
- https://xunit.net/docs/getting-started/v3/microsoft-testing-platform: MTP v2, `global.json` runner
- https://learn.microsoft.com/en-us/dotnet/core/tools/global-json: `allowPrerelease` по умолчанию true вне VS
- https://learn.microsoft.com/dotnet/core/compatibility/networking/9.0/query-redaction-logs: редакция только query/fragment
- https://github.com/gitleaks/gitleaks-action: v3, лицензия не нужна для user accounts
- https://www.duckdns.org/spec.jsp: формат update URL
- https://nodatime.org/3.3.x/userguide/localdatetime-patterns: шаблоны

### Tertiary (LOW confidence)
- https://51sec.org/2023/02/24/oracle-cloud-cleaning-up-idle-compute-instances-how-to-keep-it: перепечатка письма Oracle про PAYG
- Отчёты сообщества 2026 о PAYG и отборе (community.oracle.com, terminalbytes.com, infoq.com/news/2026/07/oracle-cloud-free-tier-limits)
- https://dev.to/armiedema/opening-up-port-80-and-443-for-oracle-cloud-servers-j35, https://techoverflow.net/2021/08/19/how-to-disable-instance-firewall-on-ubuntu-on-oracle-cloud/: iptables на OCI Ubuntu
- ASP.NET Core 10 release notes (через websearch): автогенерация `public partial class Program`

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH. Каждая версия сверена с NuGet в день исследования, ключевые API проверены по XML-докам пакета или прогоном
- Architecture / код: HIGH для чистых функций (выполнены локально), MEDIUM для связки WAF+Serilog+NSubstitute (API подтверждены, компиляция примера не проверялась, A5)
- Pitfalls: HIGH для логов, Postgres 18, Telegram и global.json (официальные источники); MEDIUM/LOW для Oracle (фаервол, PAYG) и поведения iOS-Команды

**Research date:** 2026-11-16
**Valid until:** ~2026-12-16 для стека (выходят патчи .NET и пакетов, но мажоры зафиксированы); Oracle-часть перепроверить в день создания VM
