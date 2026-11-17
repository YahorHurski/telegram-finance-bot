---
phase: 01-hodyachiy-skelet
reviewed: 2026-11-17T11:23:01Z
depth: standard
files_reviewed: 23
files_reviewed_list:
  - src/FinanceBot/Program.cs
  - src/FinanceBot/Ingest.cs
  - src/FinanceBot/Parsing.cs
  - src/FinanceBot/Messages.cs
  - src/FinanceBot/Week.cs
  - src/FinanceBot/AppDb.cs
  - src/FinanceBot/Migrations/20261116205018_Initial.cs
  - src/FinanceBot/appsettings.json
  - src/FinanceBot/FinanceBot.csproj
  - Dockerfile
  - compose.yaml
  - Caddyfile
  - deploy.sh
  - .env.example
  - .github/workflows/ci.yml
  - Directory.Packages.props
  - tests/FinanceBot.Tests/ApiFactory.cs
  - tests/FinanceBot.Tests/IngestTests.cs
  - tests/FinanceBot.Tests/ParsingTests.cs
  - tests/FinanceBot.Tests/LogPrivacyTests.cs
  - tests/FinanceBot.Tests/WeekTests.cs
  - tests/FinanceBot.Tests/MessagesTests.cs
  - tests/FinanceBot.Tests/SchemaTests.cs
findings:
  critical: 1
  warning: 4
  info: 7
  total: 12
  necessity: 0
status: issues_found
---

# Phase 1: Code Review Report

**Reviewed:** 2026-11-17T11:23:01Z
**Depth:** standard
**Files Reviewed:** 23
**Status:** issues_found

## Summary

Проверены все 23 файла скелета: эндпоинт приёма, парсеры суммы и времени, тексты сообщений, неделя, модель и миграция, Docker/Compose/Caddy, deploy.sh, CI и тесты. Документированные отсрочки (идемпотентность и повторы в Phase 3, migrator в Phase 2, хэширование токенов в Phase 7, константный лимит, один владелец) багами не считались.

Код компактный, и решения в нём в основном верные: токен проверяется до чтения тела, сравнение идёт в постоянном времени, уровни логов зашиты в код, есть тест приватности логов, который проверяет именно продовый форматтер, неделя считается в Europe/Warsaw с учётом DST, сумма ограничена сверху под `numeric(12,2)`.

Главная проблема в одной строке `AddFixedWindowLimiter`. Лимит запросов один на всех и расходуется ещё до проверки токена, поэтому любой, кто знает URL (а поддомен виден в логах Certificate Transparency), может держать приём трат закрытым всего двумя запросами в секунду. Остальные находки касаются парсера суммы (проверено запуском: `−23,50 zł` с U+2212 превращается в +23,50, а `125,00 Kč` засчитывается как 125 zł) и кода после `SaveChanges`. Этот код не защищён, хотя комментарий на строке 54 обещает, что сохранённая трата всегда получает 201.

Необходимость (necessity) проверялась по `necessity-review.md`: находок нет. Двойная защита токена в логах (`Override("System.Net.Http.HttpClient")` вместе с собственным `HttpClient`) задумана как defense in depth, поэтому находкой не считается.

Проверено запуском: поведение `Parsing.Amount` на U+2212, en dash, `Kč` и `CHF`; `OverflowException` из `Week.Containing` для `31.12.9999`; замена одиночного суррогата при сериализации (безопасна, в отчёт не вошла). Не проверено: тесты (`dotnet test`) и Docker-сборка в этом ревью не запускались.

## Narrative Findings (AI reviewer)

## Critical Issues

### CR-01: Общий лимит запросов расходуют неаутентифицированные запросы, и любой может заблокировать приём трат

**File:** `src/FinanceBot/Program.cs:35-39`, `src/FinanceBot/Program.cs:52-54`
**Issue:** `AddFixedWindowLimiter("ingest", ...)` создаёт политику с **одной** общей партицией на всех клиентов. `UseRateLimiter` срабатывает до обработчика, то есть раньше `TokenMatches`, поэтому разрешения тратят и запросы без токена (они получают 401). Тридцати POST в минуту с любым мусором достаточно, чтобы настоящая оплата Apple Pay получила 429 и Команда показала «Трата не сохранена». Это прямой удар по Core Value («каждая оплата попадает в бот»). URL найти несложно: поддомен DuckDNS попадает в публичные логи CT, как только Caddy выпускает сертификат. Разбить лимит по IP тоже не выйдет: `UseForwardedHeaders` нет, так что `RemoteIpAddress` у всех запросов один, это адрес контейнера Caddy.
**Fix:** Разбить лимит на партиции по тому, прошёл ли запрос проверку токена: у владельца свой бюджет, который чужие запросы не трогают.
```csharp
// Ingest.cs: сделать TokenMatches internal static
o.AddPolicy("ingest", ctx =>
{
    var owner = Ingest.TokenMatches(ctx.Request.Headers.Authorization.ToString(),
        ctx.RequestServices.GetRequiredService<IOptions<BotOptions>>().Value.IngestToken);
    return RateLimitPartition.GetFixedWindowLimiter(owner ? "owner" : "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) });
});
```
И добавить тест: 31 запрос с неверным токеном, после чего запрос с верным токеном возвращает 201.

## Warnings

### WR-01: Ошибка после `SaveChanges` возвращает 500 для уже сохранённой траты

**File:** `src/FinanceBot/Ingest.cs:37-45` (и `src/FinanceBot/Week.cs:12`)
**Issue:** Комментарий на строке 54 формулирует правило: «already saved: the Shortcut still gets 201, otherwise the user re-enters it and gets a duplicate». Но защищена только отправка в Telegram. Подсчёт `spent` (строки 41-44) выполняется после сохранения без try/catch. Обрыв соединения с Postgres на `SumAsync` даёт 500, хотя строка уже в БД, и Команда сообщает «не сохранено». Есть и детерминированный триггер (проверен запуском): `time = "31.12.9999 12:00:00"` разбирается успешно, а затем `Week.Containing` падает с `OverflowException` на `monday.PlusWeeks(1)`.
**Fix:** Обернуть подсчёт тем же правилом, а при ошибке отправить подтверждение без остатка:
```csharp
decimal? spent = null;
if (tx.Amount is not null)
{
    try { /* Week.Containing + SumAsync */ }
    catch (Exception ex) { log.LogWarning("Weekly sum failed for {TransactionId}: {ErrorType}", tx.Id, ex.GetType().Name); }
}
```
Год во времени тоже стоит ограничить (см. IN-02), тогда переполнение станет невозможным.

### WR-02: Минус не в ASCII отбрасывается, и возврат считается тратой

**File:** `src/FinanceBot/Parsing.cs:16`
**Issue:** Фильтр оставляет только `char.IsDigit`, `,`, `.` и ASCII `-`. Типографский минус U+2212 и en dash U+2013 тихо выбрасываются, поэтому `"−23,50 zł"` превращается в `23.50` (проверено запуском). Контракт метода (комментарий на строке 9 и тест `"-23,50 zł"` → null) выполняется только для ASCII-дефиса. iOS и банковские строки нередко используют U+2212 (в этом же проекте он стоит в `Messages.cs:34`). Если автоматизация сработает на возврат, он ляжет в лимит как расход.
**Fix:** Считать любой знак минуса отрицательным числом:
```csharp
if (raw.IndexOfAny(['-', '−', '‒', '–', '—']) >= 0) return null;
```
Плюс строка в `Amount_ReturnsNullForInvalidOrForeign`: `[InlineData("−23,50 zł")]`.

### WR-03: Чужие валюты отсекаются по чёрному списку, и `Kč`, `CHF` и прочие засчитываются как злотые

**File:** `src/FinanceBot/Parsing.cs:14-15`
**Issue:** Полный ING-09 отложен на Phase 3, но код уже пытается не считать чужую валюту злотыми и делает это через deny-list из трёх валют. Запуск показал: `"125,00 Kč"` → 125, `"12,00 CHF"` → 12. Так же пройдут `Ft`, `kr`, `₴`, `HUF`, `SEK` и другие. Чехия соседняя страна, и с момента запуска (01-06) такая оплата молча уменьшит остаток лимита на неверную сумму. Список разрешённых обозначений закрывает всю эту дыру одной строкой и в Phase 3 ничего не ломает.
**Fix:** Разрешать только злотые или строку без обозначения валюты:
```csharp
var letters = new string(raw.Where(c => !char.IsDigit(c) && c is not (',' or '.' or '-') && !char.IsWhiteSpace(c)).ToArray());
if (letters is not ("" or "zł" or "PLN" or "zl")) return null;
```
К этому тесты `"125,00 Kč"` и `"12,00 CHF"` → null.

### WR-04: У CI-джоба нет `timeout-minutes`

**File:** `.github/workflows/ci.yml:14`
**Issue:** У `build-test` нет таймаута на уровне джоба. Если Testcontainers зависнет на pull или старте Postgres, раннер будет занят до шестичасового лимита по умолчанию.
**Fix:** `timeout-minutes: 15` под `runs-on`.

## Info

### IN-01: Продавец не обрезается, сырая сумма обрезается

**File:** `src/FinanceBot/Messages.cs:27`, `src/FinanceBot/Messages.cs:34`
**Issue:** `AmountRaw` обрезается до 100 символов, а `Merchant` нет, хотя оба берутся из тела запроса размером до 64 КБ. Длинный продавец (с `&`, который после экранирования растёт в 5 раз) выводит сообщение за лимит Telegram в 4096 символов. Отправка падает, ошибка проглатывается, подтверждения нет.
**Fix:** Обрезать `Merchant` так же, до экранирования; можно вынести общий хелпер `Clip(string?, 100)`.

### IN-02: Время траты не проверяется на правдоподобие, а подтверждение не говорит, о какой неделе остаток

**File:** `src/FinanceBot/Ingest.cs:32`, `src/FinanceBot/Ingest.cs:41`
**Issue:** Любая разобранная дата (год 0001, 9999, дата на год вперёд) принимается как `OccurredAt`. Трата уходит в чужую неделю, а сообщение «Осталось X из 200 zł» показывает остаток той недели и не говорит об этом. Пользователь примет его за текущий остаток. Вывод основан на суждении: обычно iOS шлёт реальную дату, но формат текстовый и зависит от настроек региона.
**Fix:** Если `OccurredAt` отличается от `now` больше чем на 7 дней (или уходит в будущее больше чем на час), брать `now`, как при неразобранном времени. Либо в подтверждении указывать неделю, когда она не текущая.

### IN-03: `/health` проверяет только живость процесса, без базы

**File:** `src/FinanceBot/Program.cs:33`, `deploy.sh:13`
**Issue:** `AddHealthChecks()` без проверок отвечает 200, даже если Postgres недоступен. Проверка в deploy.sh и будущий внешний пингер не заметят, что приём трат сломан.
**Fix:** В Phase 2 добавить `.AddDbContextCheck<AppDb>()` (пакет `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` уже есть в стеке).

### IN-04: Тесты делят один бюджет лимита 30 в минуту

**File:** `tests/FinanceBot.Tests/LogPrivacyTests.cs:21-23`, `tests/FinanceBot.Tests/ApiFactory.cs:11`
**Issue:** Все классы коллекции "Api" работают с одним хостом и одним глобальным лимитером. Сейчас набирается около 14 запросов (10 в IngestTests и 4 в LogPrivacyTests). Каждый новый интеграционный тест приближает момент, когда запросы начнут получать 429 и тесты станут падать случайно. Обходной `static Task? _sent` появился как раз из-за этого.
**Fix:** В `ApiFactory` переопределить лимит через конфигурацию (например, `Ingest:PermitLimit`, в тестах большое значение) или, после CR-01, держать отдельный тест на 429, а для остальных поднять лимит.

### IN-05: CI не собирает Dockerfile

**File:** `.github/workflows/ci.yml:33`
**Issue:** CI запускает только `dotnet test`. Если Dockerfile сломается (например, после добавления `Directory.Build.props`, который не попадает в `COPY` на строке 5), это обнаружится только на VM во время `deploy.sh`. Старый контейнер при этом продолжит работать благодаря `set -e`, так что вред ограничен.
**Fix:** Шаг `docker build .` в CI (без push).

### IN-06: Healthcheck Postgres через unix-сокет может отчитаться о готовности во время первичной инициализации

**File:** `compose.yaml:22`
**Issue:** При первом создании тома entrypoint образа `postgres` поднимает временный сервер, который слушает только сокет. `pg_isready` без `-h` отвечает «ready», а потом сервер перезапускается. Первый `docker compose run --rm app migrate` из `deploy.sh:8` может попасть в окно перезапуска и упасть. Это касается только первого деплоя, повторный запуск помогает.
**Fix:** `test: ["CMD-SHELL", "pg_isready -h 127.0.0.1 -U financebot -d financebot"]`: TCP-проверка проходит только на финальном сервере.

### IN-07: Тест приватности логов не покрывает путь необработанного исключения

**File:** `tests/FinanceBot.Tests/LogPrivacyTests.cs:25-36`
**Issue:** Тест проверяет 201, 401 и сбой Telegram. Путь 500 не проверяется, а там `UseSerilogRequestLogging` пишет полный `exception.ToString()` (поле `@x`). Самая вероятная будущая утечка (например, сообщение исключения с данными тела) пройдёт незамеченной. Сейчас утечки нет: `PostgresException` без `Include Error Detail` значения не печатает.
**Fix:** Добавить пятый запрос, который вызывает 500 (например, `time = "31.12.9999 12:00:00"` до исправления WR-01, или NUL-байт `\u0000` в продавце, который Postgres не принимает в `text`), и проверить, что сентинелы не попали в логи.

---

_Reviewed: 2026-11-17T11:23:01Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
