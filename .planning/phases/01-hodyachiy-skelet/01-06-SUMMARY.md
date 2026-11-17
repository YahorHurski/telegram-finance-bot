---
phase: 01-hodyachiy-skelet
plan: 06
subsystem: infra
tags: [oracle-cloud, arm64, caddy, lets-encrypt, ios-shortcuts, apple-pay, telegram, parsing]

requires:
  - phase: 01-hodyachiy-skelet
    provides: "VM Oracle A1 с Docker, .env и открытыми 80/443 (план 01-05); compose.yaml, deploy.sh, docs/shortcut.md (план 01-04)"
provides:
  - "Прод https://finance-bot-yh.duckdns.org на Oracle A1.Flex (arm64, Frankfurt): app + postgres + caddy, сертификат Let's Encrypt"
  - "Сквозной путь подтверждён реальной оплатой Apple Pay: iPhone → HTTPS → app → Postgres → Telegram"
  - "Parsing.Time понимает оба русских формата даты, которые отдаёт iOS для переменной «Дата»"
  - "docs/shortcut.md с уроками первой настоящей оплаты"
  - "Доказательство, что SkiaSharp работает нативно на arm64 в aspnet:10.0 (решение по базовому образу для Phase 6)"
affects: [02, 03, 06]

actuals:
  tokens: 2800
  tasks: 2
  commits: 3
plan_head_before: facb75d5b875b1dfbdebc734f6cc9d77726fdb59
plan_head_after: 0a84a5a

tech-stack:
  added: []
  patterns:
    - "Время с телефона: сначала строгие шаблоны NodaTime, потом регулярка для «17 нояб. 2026 г., 12:03»; неразобранное или невозможное время → null, сервер берёт время получения"
    - "Перед разбором строки времени NBSP и узкий NBSP заменяются обычным пробелом (iOS ставит их между частями даты)"

key-files:
  created:
    - ".planning/phases/01-hodyachiy-skelet/01-06-SUMMARY.md"
  modified:
    - "src/FinanceBot/Parsing.cs"
    - "tests/FinanceBot.Tests/ParsingTests.cs"
    - "docs/shortcut.md"

key-decisions:
  - "Базовый образ для Phase 6 остаётся aspnet:10.0 (noble): спайк ScottPlot/SkiaSharp нативно на arm64 VM отрисовал PNG 8260 байт"
  - "В Команде время отправляется сырой переменной «Дата» без «Формата даты»: сервер принимает оба русских формата iOS, а невалидная дата падает на время получения"
  - "Сумма и Карта оборачиваются в действие «Текст», чтобы iOS отправила их строкой"
  - "Migrator как отдельный compose-сервис перенесён в Phase 2 (D-13); в FND-02 это отмечено"
  - "4 пустые записи от ручных запусков Команды (amount null) оставлены: в лимит они не идут"

patterns-established:
  - "Проверка Команды только настоящей оплатой: ручной запуск автоматизации «Транзакция» шлёт пустые поля и даёт ⚠️"

requirements-completed: [PRIV-03]

coverage:
  - id: D1
    description: "Прод по HTTPS: /health Healthy с доверенным сертификатом Let's Encrypt, http → https 308, POST без токена 401, 5432 снаружи закрыт"
    requirement: FND-02
    verification:
      - kind: other
        ref: "curl -fsS https://finance-bot-yh.duckdns.org/health → Healthy; http → 308; POST /api/v1/transactions без токена → 401; curl telnet://79.76.107.24:5432 не соединяется"
        status: pass
    human_judgment: false
  - id: D2
    description: "VM пережила перезагрузку, сервис поднялся сам"
    requirement: FND-02
    verification:
      - kind: other
        ref: "sudo reboot → /health снова Healthy без ручных действий"
        status: pass
    human_judgment: false
  - id: D3
    description: "Реальная оплата Apple Pay сохранена в Postgres и подтверждена в Telegram"
    requirement: ING-03
    verification:
      - kind: other
        ref: "оплата 2026-11-17 11:09 UTC: amount 1.20 из «1,20 zł», merchant и card сохранены, occurred_at из времени телефона (без fallback), 0 ошибок отправки в Telegram"
        status: pass
    human_judgment: true
    rationale: "Приход сообщения в Telegram подтвердил пользователь"
  - id: D4
    description: "Неверный токен → 401 и уведомление «Трата не сохранена» на iPhone"
    requirement: ING-03
    verification:
      - kind: other
        ref: "два 401 в логах app (11:11:35, 11:11:46 UTC); после возврата токена ручной запуск → 201 и ⚠️ «Не разобрал сумму»"
        status: pass
    human_judgment: true
    rationale: "Уведомление на iPhone видит только пользователь; он подтвердил"
  - id: D5
    description: "Логи прода без токенов, сумм и продавцов"
    requirement: PRIV-03
    verification:
      - kind: other
        ref: "на VM: 0 совпадений в логах app и caddy по регулярке токена бота, «bearer», «zł» и имени продавца"
        status: pass
    human_judgment: false
  - id: D6
    description: "SkiaSharp нативно на arm64"
    requirement: FND-02
    verification:
      - kind: other
        ref: "docker run --rm skia-spike на VM → PNG 8260 bytes"
        status: pass
    human_judgment: false
  - id: D7
    description: "Аккаунт Oracle на Pay As You Go"
    requirement: FND-02
    verification: []
    human_judgment: true
    rationale: "PAYG всё ещё не включён (отложен пользователем). Открытый todo в STATE.md"
  - id: D8
    description: "Зелёный CI на PR в main (SC-5)"
    requirement: FND-05
    verification: []
    human_judgment: true
    rationale: "Отложено до /gsd-ship: PR открывается при отгрузке фазы"

duration: ~10h календарных (деплой ночью, оплата и проверки утром 2026-11-17)
completed: 2026-11-17
status: complete
---

# Phase 1 Plan 06: Боевой запуск Summary

**Сервис работает на Oracle A1 по `https://finance-bot-yh.duckdns.org` с сертификатом Let's Encrypt. Настоящая оплата Apple Pay на 1,20 zł прошла iPhone → HTTPS → app → Postgres → Telegram, и подтверждение пришло. По пути пришлось научить сервер двум русским форматам даты, которые отдаёт iOS.**

## Performance

- **Duration:** деплой и внешние проверки ночью, два исправления парсера в 01:39 и 11:06 UTC, реальная оплата в 11:09 UTC, SUMMARY в 11:15 UTC 2026-11-17
- **Tasks:** 2 (чекпоинт пользователя + auto), финальный human-check одобрен пользователем
- **Files modified:** 3 (`Parsing.cs`, `ParsingTests.cs`, `docs/shortcut.md`)

## Accomplishments

- Стек app + postgres + caddy поднят `./deploy.sh` на VM.Standard.A1.Flex (arm64, Frankfurt). Caddy получил сертификат Let's Encrypt с первой попытки.
- Внешние проверки пройдены: /health Healthy без `-k`, http → https 308, POST без токена 401, порт 5432 снаружи закрыт.
- После `sudo reboot` сервис вернулся сам.
- Спайк ScottPlot/SkiaSharp нативно на arm64 отрисовал PNG (8260 байт). Значит, диаграммы Phase 6 пойдут на текущем образе `aspnet:10.0`.
- Реальная оплата Apple Pay сохранена и подтверждена в Telegram (подробности ниже).
- `docs/shortcut.md` дополнен уроками первой оплаты.

## Вывод проверок (проверено оркестратором 2026-11-17, без секретов)

```
https://finance-bot-yh.duckdns.org/health      → 200 Healthy, сертификат Let's Encrypt
http://…/health                                 → 308 на https
POST /api/v1/transactions без токена            → 401
curl telnet://79.76.107.24:5432                 → не соединяется
sudo reboot → /health                           → снова Healthy без ручных действий
docker run --rm skia-spike (на VM, aarch64)     → PNG 8260 bytes

Реальная оплата Apple Pay, 2026-11-17 11:09 UTC:
  amount       → 1.20 (из «1,20 zł»)
  merchant     → сохранён
  card         → сохранена
  occurred_at  → из времени телефона (fallback на время получения не понадобился)
  Telegram     → подтверждение получено пользователем; 0 ошибок «Telegram send failed»

Испорченный токен в Команде:
  логи app     → 401 в 11:11:35 и 11:11:46 UTC
  iPhone       → уведомление «Трата не сохранена» пришло (подтвердил пользователь)
  токен возвращён, ручной запуск → 201 и ⚠️ «Не разобрал сумму» (ожидаемо: у ручного запуска нет данных оплаты)

Логи прода (app + caddy), grep на VM:
  регулярка токена бота / «bearer» / «zł» / имя продавца → 0 совпадений
```

## Task Commits

1. **Задача 1: токен бота в `.env` на VM и сборка Команды**: чекпоинт, выполнено пользователем, без коммита.
2. **Задача 2: деплой и внешние проверки**: работа на VM, в репозиторий не попадает. Исправления, найденные на финальном human-check:
   - `5ebd368` fix(01-06): accept iOS ru date+time with comma in transaction time
   - `2eec932` fix(01-06): accept iOS ru medium date ("17 нояб. 2026 г., 12:03") in transaction time
   - `0a84a5a` docs(01-06): shortcut guide lessons from the first real payment

## Files Created/Modified

- `src/FinanceBot/Parsing.cs`: `Time` принимает `dd.MM.yyyy HH:mm:ss`, `dd.MM.yyyy, HH:mm[:ss]` и `17 нояб. 2026 г., 12:03`
- `tests/FinanceBot.Tests/ParsingTests.cs`: кейсы для новых форматов и невалидной даты
- `docs/shortcut.md`: «Текст» для Суммы и Карты, сырая «Дата», ручной запуск даёт ⚠️, параллельная отладочная автоматизация

## Decisions Made

- **Почему агентская проверка шла через неразобранную сумму.** Такая трата сохраняется и вызывает сообщение в Telegram, но в недельный лимит не идёт. Так проверяется весь путь, включая токен бота и chat id, и реальные цифры пользователя не портятся.
- **Почему токен бота вписывал пользователь.** Значение набрано прямо на VM через `nano` и не прошло ни через чат, ни через логи агента. Агент проверял только формат через `grep -c`. Чем меньше мест видели секрет, тем меньше мест, откуда он может утечь.
- **Почему `--resolve` вместо своего публичного IP.** Изнутри облака запрос на собственный публичный IP часто не возвращается обратно (нет hairpin NAT). `--resolve домен:443:127.0.0.1` отправляет запрос на локальный Caddy, но с правильным именем, поэтому TLS и маршрутизация проверяются так же, как снаружи.
- **Почему время с телефона, а не время сервера.** Трата должна попасть в ту неделю, когда была покупка. Если запрос задержался, время сервера может перескочить через полночь воскресенья. Поэтому сервер старается разобрать время телефона и только при неудаче берёт своё.
- **Почему SkiaSharp проверялся нативно на VM.** Под эмуляцией QEMU сам .NET падал (вывод плана 01-04), поэтому это не доказательство. Нативный прогон на arm64 — единственная честная проверка перед Phase 6.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Время с запятой из iOS не разбиралось**
- **Found during:** задача 2, human-check реальной оплаты
- **Issue:** сырая переменная «Дата» в русской локали приходит как `17.11.2026, 12:03` (с запятой, иногда без секунд). Сервер ждал только `dd.MM.yyyy HH:mm:ss`, поэтому брал время получения.
- **Fix:** `Parsing.Time` перебирает три шаблона NodaTime, включая варианты с запятой.
- **Files modified:** `src/FinanceBot/Parsing.cs`, `tests/FinanceBot.Tests/ParsingTests.cs`
- **Commit:** `5ebd368`

**2. [Rule 1 - Bug] Средний русский формат даты из iOS не разбирался**
- **Found during:** задача 2, повтор human-check
- **Issue:** при другом стиле даты iOS шлёт `17 нояб. 2026 г., 12:03` с неразрывными пробелами между частями.
- **Fix:** NBSP и узкий NBSP заменяются пробелом; регулярка разбирает день, месяц по первым трём буквам (май по «ма»), год и время. Невозможная дата («31 февр.») → `null`, сервер берёт время получения.
- **Files modified:** `src/FinanceBot/Parsing.cs`, `tests/FinanceBot.Tests/ParsingTests.cs`
- **Commit:** `2eec932`
- **Tests:** 63/63 проходят.

### Прочие отклонения

- Отдельный вывод агентской проверки DEPLOY-CHECK (201 + ⚠️ + удаление строки) в этот SUMMARY не перенесён. Сквозной путь вместо неё подтверждён реальной оплатой Apple Pay, а логи проверены на отсутствие имени продавца.
- Зелёный CI на PR в main (SC-5) не проверен: PR откроется на `/gsd-ship`.

**Total deviations:** 2 автоисправления (Rule 1), оба покрыты тестами.
**Impact on plan:** без них трата попадала бы в неделю по времени получения, а не покупки. Для оплат около полуночи воскресенья это давало бы неверный остаток.

## Issues Encountered

- Ручной запуск автоматизации «Транзакция» шлёт пустые поля. Сервер честно сохраняет такой запрос и присылает ⚠️. В БД осталось 4 такие записи (amount null). Их не удаляли: в лимит они не идут.

## User Setup Required

**PAYG всё ещё не включён.** Без него Oracle может остановить простаивающую VM по правилу 7 дней. Шаги — в 01-05-SUMMARY, раздел «User Setup Required». После включения напиши агенту «PAYG включён».

## Known Stubs

None.

## Next Phase Readiness

- Phase 1 технически готова к верификации: цель фазы (реальная оплата → Telegram с остатком) подтверждена на проде.
- Открыто: PAYG (FND-02 полностью не закрыт), зелёный CI на PR (на `/gsd-ship`).
- Phase 2: migrator как отдельный compose-сервис (D-13), бэкапы вне Oracle, CI/CD.
- Phase 3: корпус оплат из отладочной автоматизации «Заметки» продолжает копиться.

---
*Phase: 01-hodyachiy-skelet*
*Completed: 2026-11-17*

## Self-Check: PASSED

Files: 01-06-SUMMARY.md, docs/shortcut.md, src/FinanceBot/Parsing.cs, tests/FinanceBot.Tests/ParsingTests.cs exist; commits 5ebd368, 2eec932, 0a84a5a are ancestors of HEAD.
