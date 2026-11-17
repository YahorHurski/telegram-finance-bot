---
phase: 01-hodyachiy-skelet
verified: 2026-11-17T11:39:50Z
status: passed
score: 57/59 must-haves verified (3 overrides applied; roadmap level 8/9, plan level 49/50)
covered_files:
  - ".dockerignore"
  - ".env.example"
  - ".github/workflows/ci.yml"
  - ".gitignore"
  - ".planning/phases/01-hodyachiy-skelet/01-01-PLAN.md"
  - ".planning/phases/01-hodyachiy-skelet/01-01-SUMMARY.md"
  - ".planning/phases/01-hodyachiy-skelet/01-02-PLAN.md"
  - ".planning/phases/01-hodyachiy-skelet/01-02-SUMMARY.md"
  - ".planning/phases/01-hodyachiy-skelet/01-03-PLAN.md"
  - ".planning/phases/01-hodyachiy-skelet/01-03-SUMMARY.md"
  - ".planning/phases/01-hodyachiy-skelet/01-04-PLAN.md"
  - ".planning/phases/01-hodyachiy-skelet/01-04-SUMMARY.md"
  - ".planning/phases/01-hodyachiy-skelet/01-05-PLAN.md"
  - ".planning/phases/01-hodyachiy-skelet/01-05-SUMMARY.md"
  - ".planning/phases/01-hodyachiy-skelet/01-06-PLAN.md"
  - ".planning/phases/01-hodyachiy-skelet/01-06-SUMMARY.md"
  - "Caddyfile"
  - "Directory.Packages.props"
  - "Dockerfile"
  - "README.md"
  - "compose.yaml"
  - "deploy.sh"
  - "docs/deploy.md"
  - "docs/shortcut.md"
  - "global.json"
  - "spikes/skia-arm64/Program.cs"
  - "src/FinanceBot/AppDb.cs"
  - "src/FinanceBot/FinanceBot.csproj"
  - "src/FinanceBot/Ingest.cs"
  - "src/FinanceBot/Messages.cs"
  - "src/FinanceBot/Migrations/20261116205018_Initial.cs"
  - "src/FinanceBot/Parsing.cs"
  - "src/FinanceBot/Program.cs"
  - "src/FinanceBot/Week.cs"
  - "tests/FinanceBot.Tests/ApiFactory.cs"
  - "tests/FinanceBot.Tests/CapturingSink.cs"
  - "tests/FinanceBot.Tests/IngestTests.cs"
  - "tests/FinanceBot.Tests/LogPrivacyTests.cs"
  - "tests/FinanceBot.Tests/MessagesTests.cs"
  - "tests/FinanceBot.Tests/ParsingTests.cs"
  - "tests/FinanceBot.Tests/SchemaTests.cs"
  - "tests/FinanceBot.Tests/WeekTests.cs"
covered_digest: "v4:sha256:fb83b03dd71a82bf57db61e73f1721b95d01fe08fbbaa8a6b413d79fe630bab4"
behavior_unverified: 0
overrides_applied: 3
overrides:
  - must_have: "Аккаунт Oracle переведён на Pay As You Go (ROADMAP SC-1, FND-02)"
    reason: "Перевод на PAYG отложен владельцем (нужна авторизация ~400 zł на карте). Риск принят как AR-02 в 01-SECURITY.md; ROADMAP SC-1 и REQUIREMENTS FND-02 переписаны с явной пометкой «перевод на PAYG отложен и ещё не выполнен»; открытый todo в STATE.md. Смягчение: всё в Docker (переезд через deploy.sh), бэкапы вне Oracle в Phase 2 (SC-3). FND-02 в REQUIREMENTS не отмечен выполненным."
    accepted_by: "owner (AR-02, 01-SECURITY.md)"
    accepted_at: "2026-11-17T00:00:00Z"
  - must_have: "Существует VM.Standard.A1.Flex arm64 с Ubuntu 24.04 в пределах Always Free; аккаунт Oracle в статусе Pay As You Go; бюджет с alert rule на email создан (01-05 truth 2)"
    reason: "VM и бюджет есть (01-05-SUMMARY, живой прод); не выполнена только PAYG-часть, принятая как AR-02 (см. первую запись)."
    accepted_by: "owner (AR-02, 01-SECURITY.md)"
    accepted_at: "2026-11-17T00:00:00Z"
  - must_have: "Pay As You Go защищает VM от отбора простаивающих инстансов (01-05 backstop truth)"
    reason: "Backstop-truth нельзя проверить автоматически, а PAYG ещё не включён. Пользователь выбрал option-a (письмо Oracle 2023 как доказательство) и отложил сам перевод; риск — AR-02."
    accepted_by: "owner (option-a в 01-05, AR-02 в 01-SECURITY.md)"
    accepted_at: "2026-11-17T00:00:00Z"
deferred:
  - truth: "FND-02: migrator как отдельный compose-сервис"
    addressed_in: "Phase 2"
    evidence: "Phase 2 SC-2: «Миграции применяются отдельным одноразовым сервисом migrator (EF migration bundle) до старта приложения»; D-13 в 01-CONTEXT.md"
  - truth: "Полный CI/CD на push в main (сборка, тесты, gitleaks, автодеплой)"
    addressed_in: "Phase 2"
    evidence: "Phase 2 SC-1: «Пуш в main запускает GitHub Actions: сборка, unit-, интеграционные ... тесты, проверка gitleaks, а при зелёном прогоне автоматический деплой на VM»"
  - truth: "Смягчение риска AR-02 (отбор VM без PAYG): бэкапы вне Oracle"
    addressed_in: "Phase 2"
    evidence: "Phase 2 SC-3: «Ежедневный шифрованный бэкап БД уходит во внешнее хранилище вне Oracle»"
human_verified:
  - item: "CI на GitHub (ROADMAP SC-5)"
    result: "PR #1 (draft) → run 44076472547 conclusion success: gitleaks, .env must stay ignored, dotnet test — all green (2026-11-17)"
  - item: "Строка DEPLOY-CHECK в прод-БД"
    result: "count = 0 (psql на VM, 2026-11-17); 4 пустые строки от ручных запусков, amount NULL, в лимит не входят"
  - item: "Judgment-запреты 01-05 (только Always Free, без искусственной нагрузки)"
    result: "владелец подтвердил форму A1.Flex 1 OCPU / 6 GB «Always Free-eligible» и бюджет с алертом (01-05); нагрузочных скриптов на VM нет"
  - item: "Judgment-запреты 01-01/01-02"
    result: "приняты по вердикту верификатора; владелец подтвердил реальной оплатой и тестом «Трата не сохранена» (2026-11-17)"
---

# Phase 1: Ходячий скелет — отчёт верификации

**Цель фазы:** Реальная оплата Apple Pay проходит весь путь iPhone → HTTPS на Oracle Cloud → сервер → Postgres → Telegram, и пользователь сразу видит остаток недельного лимита
**Проверено:** 2026-11-17T11:39:50Z
**Статус:** passed (ручные пункты закрыты 2026-11-17, см. human_verified)
**Повторная верификация:** нет, первичная

## Итог в двух словах

Цель фазы достигнута: код пути «токен → сырое тело → Postgres → остаток недели → Telegram → 201» на месте, связан и покрыт тестами (полный прогон в этой сессии: 70/70 зелёные), а живой прод и реальная оплата Apple Pay подтверждены оркестратором и пользователем 2026-11-17. Статус `human_needed`, а не `passed`, по двум причинам: CI ни разу не запускался на GitHub (SC-5), и judgment-запреты требуют подтверждения владельца. PAYG не включён: это принято владельцем как риск AR-02 и проведено через override, а не скрыто.

## Достижение цели

### Наблюдаемые истины: контракт ROADMAP

| # | Истина | Статус | Доказательство |
|---|--------|--------|----------------|
| G | Реальная оплата Apple Pay: iPhone → HTTPS на Oracle → сервер → Postgres → Telegram с остатком | ✓ VERIFIED | Проверено человеком 2026-11-17 11:09 UTC: amount 1.20 из «1,20 zł», merchant, card, время телефона разобрано, подтверждение в Telegram получено пользователем. Кодовый путь: `Program.cs` MapPost → `Ingest.Handle` → SaveChangesAsync → `Week.Containing(tx.OccurredAt)` → `Messages.Confirmation` → SendMessage(OwnerChatId) |
| 1a | `https://<поддомен>.duckdns.org/health` = 200 с Oracle Always Free VM; стек app + postgres + caddy через Compose; бюджетный алерт | ✓ VERIFIED | Живой прод: /health Healthy с сертификатом Let's Encrypt, http → https 308, после reboot поднимается сам (проверено оркестратором). `compose.yaml`: три сервиса, `restart: unless-stopped`, порты только у caddy. Бюджет создан (01-05-SUMMARY, задача 2) |
| 1b | Аккаунт Oracle переведён на Pay As You Go | PASSED (override) | Override: перевод отложен владельцем, риск AR-02 (01-SECURITY.md), формулировки ROADMAP/REQUIREMENTS это прямо говорят; принято owner 2026-11-17 |
| 2 | POST (D-05: `/api/v1/transactions` вместо `/tx`) с токеном сохраняет трату вместе с сырым телом; без токена или с неверным отклоняется | ✓ VERIFIED | `Ingest.TokenMatches` (SHA-256 + FixedTimeEquals) до чтения тела; `RawBody = raw`. Тесты ValidToken_SavesRowAndSendsConfirmation (raw_body побайтно), MissingToken/WrongToken/NonBearerScheme (401, нет строки, нет SendMessage). Живой прод: 401 без токена; 401 ×2 с испорченным токеном в Команде |
| 3 | После сохранения бот присылает сумму, продавца и остаток; лимит 200 zł и неделя пн–вс Europe/Warsaw — константы | ✓ VERIFIED | `Week.Limit = 200m`, `Week.Warsaw`, `Containing` через PreviousOrSame(Monday). Тест проверяет точный текст `− 45,99 zł · Żabka\n🟢 Осталось 154,01 из 200 zł`, chat id 42, ParseMode.Html; WeekTests (167/169 ч); реальная оплата подтверждена |
| 4 | Все таблицы с пользовательскими данными содержат `user_id` с первой миграции | ✓ VERIFIED | `20261116205018_Initial.cs` содержит `user_id` и FK на users; SchemaTests.AllUserDataTablesHaveUserId (не пустой: сначала проверяет наличие transactions) зелёный |
| 5a | Пуш в main запускает GitHub Actions со сборкой и тестами | ⚠️ PRESENT_BEHAVIOR_UNVERIFIED | `ci.yml`: push в main + pull_request, checkout fetch-depth 0, gitleaks-action@v3, `git check-ignore`, setup-dotnet по global.json, `dotnet test`, timeout 20 мин. Ни одного запуска на GitHub не было (отложено на /gsd-ship) |
| 5b | Секреты только в `.env`, который в `.gitignore` с первого коммита | ✓ VERIFIED | Первый коммит кода `f9dfbed` содержит `.gitignore` со строкой `.env` (строки 7 и 485); `.env` не отслеживается (`git ls-files`); локальный `gitleaks git --no-banner --redact .` в этой сессии: 57 коммитов, «no leaks found», exit 0 |
| 6 | Логи без сумм, продавцов и токенов с первого деплоя; проверяется автотестом | ✓ VERIFIED | `Program.cs`: Serilog с уровнями в коде (Microsoft.AspNetCore, EF, System.Net.Http.HttpClient → Warning), RenderedCompactJsonFormatter, без HTTP-логирования тел; TelegramBotClient вне IHttpClientFactory. LogPrivacyTests (NoSensitiveDataInLogs + LogsAreCaptured, не пустой лог) на окружении Production. Живой прод: 0 совпадений по секретам и суммам |

**Счёт уровня ROADMAP:** 8/9 (1 override, 1 present-behavior-unverified).

### Наблюдаемые истины: must_haves из планов

| План | Истин | Итог | Замечания |
|------|-------|------|-----------|
| 01-01 трассер приёма | 10 | 10 ✓ | Неделя считается по `tx.OccurredAt`, а не по времени приёма; fallback времени (UnparsedTime_FallsBackToReceivedAt); сбой Telegram → 201 (TelegramFailure_StillReturns201AndKeepsRow); кривой JSON и сумма числом → amount NULL + сырое тело. Сверх плана: тесты 429 и 64 KB на реальном Kestrel |
| 01-02 честные цифры | 9 | 9 ✓ | 41 InlineData в ParsingTests (U+00A0, U+202F, EUR, DST 29.03 и 25.10), WeekTests 167/169 ч, 9 MessagesTests (перерасход 🔴 жирным, ровно 200 → 0,00, обрезка 100 символов до экранирования). Разделители в коде проверены побайтно: U+00A0 в NumberGroupSeparator, U+00A0/U+202F в Parsing.Time |
| 01-03 PRIV-03 и CI | 5 | 5 ✓ | Истина про ci.yml касается содержимого файла, оно проверено; сам запуск на GitHub вынесен в SC-5a |
| 01-04 Compose, deploy, docs, спайк | 11 | 11 ✓ | Порты только у caddy, том PG18 в `/var/lib/postgresql`, `USER $APP_UID` (id -u = 1654 по аудиту безопасности), `.env` в .dockerignore, deploy.sh 100755 + `eol=lf`. Backstop-истина ScottPlot на arm64 закрыта прямым наблюдением: нативный прогон на VM → PNG 8260 bytes. docs/shortcut.md эволюционировал (сырая «Дата» вместо «Формата даты»), но формат `dd.MM.yyyy HH:mm:ss` упомянут как рабочий, а сервер понимает оба формата iOS |
| 01-05 подготовка Oracle VM | 7 | 5 ✓ + 2 override | PAYG-часть truth 2 и backstop-истина «PAYG защищает от отбора» → override (AR-02). Решение option-a записано, aarch64, `.env` 600, iptables 80/443, порт 80 проверен снаружи |
| 01-06 боевой запуск | 8 | 7 ✓ + 1 ? | UNCERTAIN: «строка DEPLOY-CHECK удалена после проверки»: вывод агентской проверки в SUMMARY не перенесён. Сквозной путь доказан более сильным свидетельством (реальная оплата). Вынесено в human verification |

**Счёт уровня планов:** 49/50 (2 override, 1 uncertain).

### Отложенные пункты

| # | Пункт | Где закрывается | Доказательство |
|---|-------|-----------------|----------------|
| 1 | migrator как отдельный compose-сервис (часть текста FND-02) | Phase 2 | Phase 2 SC-2; D-13 |
| 2 | Полный CI/CD на push в main с автодеплоем | Phase 2 | Phase 2 SC-1 |
| 3 | Бэкапы вне Oracle (смягчение AR-02) | Phase 2 | Phase 2 SC-3 |

### Обязательные артефакты

| Артефакт | Ожидание | Статус | Детали |
|----------|----------|--------|--------|
| `.gitignore` | игнор `.env` | ✓ VERIFIED | есть с `f9dfbed` |
| `global.json` | SDK 10.0.x без preview, MTP | ✓ VERIFIED | `allowPrerelease: false`, `test.runner` |
| `Directory.Packages.props` | CPM, только 10.x | ✓ VERIFIED | ни одной версии `11.` |
| `src/FinanceBot/Program.cs` | DI, Serilog, rate limit, 64 KB, migrate, маршрут | ✓ VERIFIED | |
| `src/FinanceBot/Ingest.cs` | токен → тело → сохранение → остаток → SendMessage → 201 | ✓ VERIFIED | FixedTimeEquals, JsonException, SaveChanges до отправки |
| `src/FinanceBot/AppDb.cs` | User, Transaction, user_id | ✓ VERIFIED | |
| `src/FinanceBot/Migrations/` | Initial (users, transactions) | ✓ VERIFIED | `verify.artifacts` дал ложный отказ «path is a directory»; вручную: `20261116205018_Initial.cs` содержит `user_id` и `raw_body` |
| `src/FinanceBot/Parsing.cs`, `Week.cs`, `Messages.cs` | разбор, неделя, тексты D-01..D-04 | ✓ VERIFIED | |
| `tests/FinanceBot.Tests/*` | ApiFactory (postgres:18-alpine), Ingest/Schema/LogPrivacy/Parsing/Week/Messages | ✓ VERIFIED | 70/70 |
| `.github/workflows/ci.yml` | gitleaks + check-ignore + dotnet test | ✓ VERIFIED (файл) | запуск на GitHub не проверен (SC-5a) |
| `Dockerfile`, `.dockerignore`, `compose.yaml`, `Caddyfile`, `.env.example` | стек | ✓ VERIFIED | `.env.example` без значений |
| `deploy.sh`, `.gitattributes` | ручной деплой | ✓ VERIFIED | 100755, LF |
| `README.md`, `docs/deploy.md`, `docs/shortcut.md` | инструкции | ✓ VERIFIED | Security List, netfilter-persistent, A1.Flex, chmod 600, «Трата не сохранена», `/api/v1/transactions` |
| `spikes/skia-arm64/*` | спайк SkiaSharp | ✓ VERIFIED | вне slnx, ScottPlot 5.1.59 |

`verify.artifacts`: 01-01 9/10 (ложный отказ на каталоге, см. выше), 01-02 4/4, 01-03 4/4, 01-04 9/9, 01-05 2/2, 01-06 артефактов не объявляет.

### Проверка ключевых связей

| От | К | Через | Статус |
|----|---|-------|--------|
| Program.cs | Ingest.cs | `MapPost("/api/v1/transactions", Ingest.Handle).RequireRateLimiting("ingest")` | ✓ WIRED |
| Ingest.cs | AppDb.cs | `db.Transactions.Add` + `SaveChangesAsync` до отправки | ✓ WIRED |
| Ingest.cs | Messages.cs | `Messages.Confirmation(tx, spent)` → `SendMessage(OwnerChatId, …, ParseMode.Html)` | ✓ WIRED |
| ApiFactory.cs | Program.cs | `WebApplicationFactory<Program>`, `ILogEventSink` → `ReadFrom.Services` | ✓ WIRED |
| MessagesTests / ParsingTests | Messages.cs / Parsing.cs | прямые вызовы | ✓ WIRED |
| ci.yml | global.json | `global-json-file` | ✓ WIRED |
| compose.yaml → Caddyfile → app:8080 | Kestrel | том `./Caddyfile`, `reverse_proxy app:8080` | ✓ WIRED |
| deploy.sh | compose.yaml | build → `run --rm app migrate` → up -d → curl /health | ✓ WIRED |
| docs/shortcut.md (iOS-Команда) | Ingest.cs | POST `/api/v1/transactions`, Bearer, 4 строковых поля | ✓ WIRED (подтверждено реальной оплатой) |

`verify.key-links`: все 15 связей по шести планам verified.

### Трассировка данных (уровень 4)

| Артефакт | Значение | Источник | Реальные данные | Статус |
|----------|----------|----------|-----------------|--------|
| Messages.Confirmation, строка остатка | `spent` | `SUM(amount)` из `transactions` по user_id и неделе траты (EF LINQ) | да | ✓ FLOWING |
| Messages.Confirmation, первая строка | `tx.Amount`, `tx.Merchant` | `Parsing.Amount(p.Amount)`, payload из сырого тела | да | ✓ FLOWING |
| Transaction.OccurredAt | время траты | `Parsing.Time(p.Time) ?? now` | да (на проде время телефона разобрано) | ✓ FLOWING |

### Поведенческие проверки

| Поведение | Команда | Результат | Статус |
|-----------|---------|-----------|--------|
| Полный набор тестов (один прогон) | `dotnet test` | итог 70, сбой 0, 9.2 с | ✓ PASS |
| Секретов нет во всей истории | `gitleaks git --no-banner --redact .` | 57 commits, no leaks found, exit 0 | ✓ PASS |
| `.gitignore` с первого коммита кода | `git show --stat f9dfbed`; строки `.gitignore` из этого коммита | `.gitignore` в коммите, `.env` в нём есть | ✓ PASS |
| deploy.sh исполняемый | `git ls-files -s deploy.sh` | `100755` | ✓ PASS |
| Прод: health, 308, 401, 5432, reboot, логи, SkiaSharp arm64, реальная оплата | живые проверки | подтверждено оркестратором и пользователем 2026-11-17 | ✓ PASS (human-verified) |
| CI на GitHub | `gh run list --workflow ci.yml` | не запускался | ? SKIP → human |

### Выполнение проб

Проб `scripts/*/tests/probe-*.sh` в репозитории нет, планы их не объявляют. Шаг пропущен.

### Покрытие требований

| Требование | Планы | Описание | Статус | Доказательство |
|------------|-------|----------|--------|----------------|
| FND-02 | 01-04, 01-05, 01-06 | Oracle Always Free + Compose + HTTPS DuckDNS + /health; PAYG; бюджетный алерт | ⚠️ ЧАСТИЧНО (принято) | Прод работает, бюджет есть. PAYG не включён (override AR-02); migrator перенесён в Phase 2 (D-13, отмечено в REQUIREMENTS). В REQUIREMENTS.md FND-02 честно оставлен `[ ]` Pending |
| FND-05 | 01-01, 01-03, 01-04 | секреты не в репозитории, gitleaks в CI, .gitignore с первого коммита | ✓ SATISFIED | `f9dfbed`, локальный gitleaks 0, gitleaks-шаг в ci.yml; запуск в CI ждёт PR |
| PRIV-03 | 01-03, 01-06 | логи без сумм, продавцов, токенов, автотест | ✓ SATISFIED | LogPrivacyTests + проверка логов прода |
| ING-03 (упрощ.) | 01-01, 01-02, 01-06 | Apple Pay → HTTPS с токеном → сохранение | ✓ SATISFIED в объёме Phase 1 | Полностью закрывается в Phase 3 (REQUIREMENTS: Phase 3, Pending) |
| MAN-03 (упрощ.) | 01-01, 01-02, 01-06 | подтверждение с суммой, продавцом, остатком | ✓ SATISFIED в объёме Phase 1 | Категории нет (Phase 3+), это ожидаемо |

Осиротевших требований нет: REQUIREMENTS.md относит к Phase 1 только FND-02, FND-05, PRIV-03, и все они заявлены в планах.

### Найденные антипаттерны

| Файл | Строка | Шаблон | Серьёзность | Влияние |
|------|--------|--------|-------------|---------|
| — | — | TBD / FIXME / XXX / TODO / HACK | — | Не найдено ни в src, ни в tests, ни в инфраструктурных файлах |
| `src/FinanceBot/AppDb.cs` | 6 | `shortcut:` один владелец до Phase 7 | ℹ️ Info | Учтённый долг с привязкой к фазе |
| `src/FinanceBot/Parsing.cs` | 13 | `shortcut:` временный парсер до корпуса SPIKE-01 | ℹ️ Info | Учтённый долг, Phase 3 |
| `src/FinanceBot/Week.cs` | 6 | `shortcut:` лимит-константа до Phase 5 | ℹ️ Info | По требованию SC-3 |
| ROADMAP SC-2 | — | путь `/tx` в тексте критерия | ℹ️ Info | D-05 явно велит читать как `/api/v1/transactions` |
| прод-БД | — | 4 строки с amount NULL от ручных запусков Команды | ℹ️ Info | В лимит не входят; можно вычистить при случае |
| 01-REVIEW-DISPOSITION | — | IN-01, IN-02, IN-03, IN-05, IN-07 отложены | ℹ️ Info | Отслеживаются, привязаны к Phase 3 и 6 |

Блокеров нет.

### Запреты (prohibitions)

| План | Запрет | Тир | Вердикт верификатора (не авторитетный) |
|------|---------|-----|---------------------------------------|
| 01-01 | Сумма, продавец, карта уходят только в свою БД и в Telegram владельца | judgment | Соблюдён: в `src` нет других исходящих вызовов, только `TelegramBotClient`; тест проверяет chat id 42. Нужно подтверждение |
| 01-01 | После сохранения не отвечать Команде не-2xx | judgment | Соблюдён: SaveChangesAsync → весь пост-путь в `try/catch (Exception)` → 201; тест TelegramFailure зелёный. Нужно подтверждение |
| 01-02 | Не показывать картину лимита лучше настоящей | judgment | Соблюдён: allow-list `zł`/`PLN`, отказ для ≤0, типографский минус → отказ, ветка `spent > Limit` → 🔴; ParsingTests и MessagesTests зелёные. Нужно подтверждение |
| 01-05 | Нет ресурсов Oracle сверх Always Free | judgment | Из кода не проверяется; по 01-SECURITY A1.Flex 1/6. Нужно подтверждение |
| 01-05 | Нет искусственной нагрузки против отбора | judgment | В репозитории нет ничего похожего (cron, нагрузочных скриптов). Нужно подтверждение по VM |

### Требуется проверка человеком

### 1. CI зелёный на GitHub (SC-5)
**Тест:** открыть PR ветки фазы в main (/gsd-ship), затем `gh run list --workflow ci.yml --limit 1`.
**Ожидание:** success; gitleaks «no leaks found»; `dotnet test` зелёный на ubuntu-latest.
**Почему человек:** workflow ни разу не запускался, а пуш и PR делаются только с разрешения пользователя.

### 2. Строка DEPLOY-CHECK удалена из прод-БД
**Тест:** на VM `docker compose exec -T postgres psql -U financebot -d financebot -tAc "select count(*) from transactions where merchant='DEPLOY-CHECK'"`.
**Ожидание:** 0 (или осознанно оставить: amount NULL, в лимит не входит).
**Почему человек:** вывод этой проверки в 01-06-SUMMARY не попал, а прод-БД из репозитория не видна.

### 3. Judgment-запреты 01-05 (Oracle)
**Тест:** в консоли Oracle убедиться, что ресурсы только Always Free и на VM нет искусственной нагрузки.
**Ожидание:** оба соблюдены; перед включением PAYG повторить проверку ресурсов и бюджетного алерта.
**Почему человек:** консоль и биллинг агенту недоступны.

### 4. Подтвердить вердикт по judgment-запретам 01-01 и 01-02
**Тест:** просмотреть таблицу «Запреты» выше.
**Ожидание:** владелец согласен с вердиктом «соблюдены».
**Почему человек:** вердикт LLM-верификатора по judgment-запретам не авторитетен.

### Сводка по разрывам

Блокирующих разрывов нет. Принятые и записанные отклонения:

- **PAYG не включён.** Это часть FND-02 и SC-1. Владелец отложил перевод (AR-02), формулировки ROADMAP и REQUIREMENTS это прямо говорят, FND-02 не отмечен выполненным. Override применён на основании AR-02. Риск остаётся настоящим: без PAYG Oracle может остановить простаивающую VM по правилу 7 дней, и тогда траты будут теряться («Трата не сохранена» на iPhone). Открытый todo в STATE.md.
- **CI ни разу не запускался на GitHub.** Файл готов и проверен по содержимому, запуск ждёт /gsd-ship (SC-5a, human verification).
- **migrator перенесён в Phase 2** (D-13, Phase 2 SC-2): deferred.

Что сознательно не проверялось: прод-БД, логи прода, консоль Oracle и телефон. Эти доказательства приняты как проверенные человеком (оркестратор и пользователь, 2026-11-17).

---

_Проверено: 2026-11-17T11:39:50Z_
_Верификатор: Claude (gsd-verifier)_
