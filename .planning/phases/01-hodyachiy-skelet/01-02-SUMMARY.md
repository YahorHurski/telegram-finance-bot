---
phase: 01-hodyachiy-skelet
plan: 02
subsystem: ingest
status: complete
tags: [unit-tests, nodatime, parsing, telegram-html, xunit-v3]
requires: [01-01]
provides:
  - "Table tests for Parsing.Amount/Time and Week.Containing (D-07, D-08, DST weeks 167/169 h)"
  - "Messages.Confirmation overspend branch D-02 (🔴 bold) and 100-char truncation of unparsed raw amount"
  - "MessagesTests pinning exact D-01..D-04 texts with escaped invisible characters"
affects: [01-03, 01-04, 01-05, 01-06]
tech-stack:
  added: []
  patterns: ["pure unit tests without DB/host for domain functions (D-12)", "invisible characters in expected strings written as \\u escapes"]
key-files:
  created:
    - tests/FinanceBot.Tests/ParsingTests.cs
    - tests/FinanceBot.Tests/WeekTests.cs
    - tests/FinanceBot.Tests/MessagesTests.cs
  modified:
    - src/FinanceBot/Messages.cs
key-decisions:
  - "Overspend starts strictly above the limit: spent == 200 stays green with 0,00"
  - "Unparsed raw amount is truncated to 100 chars + … before HTML escaping"
requirements-completed: [MAN-03, ING-03]
duration: 3 min
completed: 2026-11-16
estimate:
  tokens: 35000
  tasks: 2
actuals:
  tokens: 2250
  tasks: 2
  commits: 3
plan_head_before: b84ab2ee2c764c63b1624ca3c788d0a7c28b9d0f
plan_head_after: 017ced15955a3cafe1e2fe405ce7e3dee0da32f8
coverage:
  - deliverable: "Amount parsing: Polish formats, NBSP/narrow NBSP; null for foreign currency, <= 0, >= 1 000 000, garbage"
    human_judgment: false
    verification:
      - kind: test
        ref: "tests/FinanceBot.Tests/ParsingTests.cs#Amount_ParsesPolishAndCommonFormats"
        status: pass
      - kind: test
        ref: "tests/FinanceBot.Tests/ParsingTests.cs#Amount_ReturnsNullForInvalidOrForeign"
        status: pass
  - deliverable: "Time parsing as Europe/Warsaw incl. DST gap and repeated hour"
    human_judgment: false
    verification:
      - kind: test
        ref: "tests/FinanceBot.Tests/ParsingTests.cs#Time_ReadsPhoneTimeAsWarsaw"
        status: pass
      - kind: test
        ref: "tests/FinanceBot.Tests/ParsingTests.cs#Time_ReturnsNullForUnknownFormat"
        status: pass
  - deliverable: "Week bounds Mon-Sun Warsaw, 167 h / 169 h DST weeks"
    human_judgment: false
    verification:
      - kind: test
        ref: "tests/FinanceBot.Tests/WeekTests.cs#Containing_ReturnsMondayToMondayInWarsaw"
        status: pass
  - deliverable: "Confirmation texts D-01..D-04: green, at limit, overspend 🔴 bold, NBSP grouping, escaping, null merchant, unparsed, truncation"
    human_judgment: false
    verification:
      - kind: test
        ref: "tests/FinanceBot.Tests/MessagesTests.cs"
        status: pass
---

# Phase 1 Plan 02: Честные цифры в подтверждении Summary

**Подтверждение показывает перерасход строкой `🔴 <b>Потрачено 237,50/200 · Перерасход 37,50 zł</b>` вместо «Осталось −37,50», длинная неразобранная сумма обрезается до 100 символов, а разбор сумм, времени и границы недели (включая недели 167 и 169 ч) закреплены 44 unit-тестами без Docker.**

## Performance

- **Duration:** 3 min (2026-11-16T20:57:50Z → 2026-11-16T21:00:46Z)
- **Tasks:** 2/2
- **Files:** 3 созданных теста, 1 изменённый файл кода
- **Tests:** unit `--filter-class "*ParsingTests" "*WeekTests" "*MessagesTests"` → итог 44, сбой 0, exit 0 (без Docker). Полный `dotnet test` с Docker → итог 54, сбой 0, успешно 54, exit 0 (≈11 с).

## Accomplishments

- `ParsingTests`: 23 случая сумм (польский формат, U+00A0 и U+202F как разделитель групп, `1.234,56` и `1,234.56`; null для €/$/£/EUR/USD/GBP, нуля, минуса, `1 000 000,00`, пустых и мусорных строк) и 7 случаев времени (дыра перехода на летнее, двойной час, пробелы по краям, мусор).
- `WeekTests`: 4 случая из RESEARCH Pattern 4 с точными границами в UTC и длительностью 168/168/167/169 ч. Вход строится из `LocalDateTime` через `InZoneLeniently`, системные часы не трогаются.
- `Messages.Confirmation`: ветка перерасхода D-02 (строго `spent > 200`), обрезка `AmountRaw` до 100 символов + `…` до экранирования.
- `MessagesTests`: 10 тестов на точные тексты D-01..D-04; все невидимые символы (U+2212, U+00B7, U+00A0, «», U+26A0 U+FE0F, U+2026) записаны как `\u`-escape.
- Код `Parsing.cs` и `Week.cs` не менялся: все 34 случая прошли на коде трассера с первого раза.

## Task Commits

1. **Task 1: таблицы тестов сумм, времени и недели** — `c4ed829` test(01-02)
2. **Task 2 (TDD): перерасход, формат, экранирование, обрезка**
   - `4a16ff8` test(01-02): RED
   - `017ced1` feat(01-02): GREEN (REFACTOR не понадобился)

## TDD Gate Compliance

RED (`4a16ff8`, до правки Messages.cs): `dotnet test --filter-class "*MessagesTests"` → итог 10, сбой 2, успешно 8, exit 2.

| Тест | RED | Причина |
|------|-----|---------|
| Overspend_RedBoldWithOverrun | сбой | `Expected: "🔴 <b>Потрачено 237,50/200 · Перерасход 37,50 zł</"··· Actual: "🟢 Осталось -37,50 из 200 zł"` — ветки перерасхода не было (известная заглушка из 01-01) |
| UnparsedLong_TruncatedTo100CharsWithEllipsis | сбой | на позиции 123 ожидалось `…» · Lidl`, пришли дальнейшие `x`: обрезки не было |
| Остальные 8 | зелёные сразу | зелёная строка, NBSP, экранирование, продавец null и ⚠️ уже реализованы трассером 01-01 |

Оба сбоя падают на запланированном поведении, а не на сборке. GREEN (`017ced1`): 10/10, полный набор 54/54.

## Почему так (для собеседования)

- **Своя NumberFormatInfo вместо русской культуры.** Русская культура берёт разделители из ICU на Linux и из NLS на Windows, и они расходятся (обычный пробел, U+00A0 или U+202F). В урезанном контейнере ICU нет вовсе. Своя NumberFormatInfo с запятой и U+00A0 даёт `1 234,50` везде одинаково, и тест это проверяет.
- **Ручное экранирование трёх символов вместо HtmlEncode.** Telegram в режиме HTML требует экранировать только `&`, `<`, `>`. Стандартный HTML-энкодер превращает `ó` в `&#243;`, сообщение становится нечитаемым для поиска и тестов. Три `Replace` делают ровно то, что нужно.
- **NodaTime вместо «понедельник + 7 дней».** В неделю перехода на летнее время 167 часов, на зимнее 169. Если прибавлять 7×24 часа, граница уедет на час, и трата в понедельник 00:30 попадёт не в ту неделю. NodaTime считает начало дня в зоне Europe/Warsaw, тест закрепляет обе такие недели.
- **Обрезка до экранирования.** Если сначала экранировать, а потом резать, можно разрезать `&amp;` пополам, и Telegram отклонит сообщение из-за битого HTML. Если резать сначала, экранирование работает с готовой строкой. 100 символов держат сообщение далеко от лимита Telegram в 4096.
- **Перерасход строго больше лимита.** Ровно 200 — это «Осталось 0,00», ещё зелёный: лимит не превышен. Красная строка появляется только когда реально потрачено больше.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Комментарии в Messages.cs нарушали критерии `grep -c "ru-RU"` == 0 и `grep -c "HtmlEncode"` == 0**
- **Found during:** Task 2, проверка acceptance criteria
- **Issue:** код не использовал ни ru-RU, ни HtmlEncode, но слова были в поясняющих комментариях из 01-01.
- **Fix:** комментарии переформулированы («the Russian CultureInfo», «the framework HTML encoder»), смысл сохранён.
- **Files modified:** src/FinanceBot/Messages.cs
- **Commit:** 017ced1

**2. [Rule 3 - Blocking] Инструмент записи файлов превращал ` ` в настоящий символ**
- **Found during:** Task 1 (`grep -c "u202F"` дал 0)
- **Fix:** escape-последовательности восстановлены через perl; в MessagesTests сразу записаны через заглушку и замену. Проверено grep: в файлах тестов литералы `\u...`, а не сами символы.
- **Commit:** c4ed829, 4a16ff8

**Total deviations:** 2 auto-fixed (Rule 3). **Impact:** поведение не изменилось.

## Known Stubs

None. Заглушка 01-01 (`Messages.cs`: нет ветки перерасхода) закрыта этим планом.

## Issues Encountered

None.

## Next Phase Readiness

Готово к 01-03. Новые тесты unit, к ingest-эндпоинту не обращаются, поэтому общее окно rate limit (30/мин) не нагружают.

## Self-Check: PASSED

- Файлы ParsingTests.cs, WeekTests.cs, MessagesTests.cs существуют; Messages.cs содержит `Перерасход`.
- Коммиты c4ed829, 4a16ff8, 017ced1 есть в HEAD; `git rev-list --count b84ab2e..HEAD` = 3.
- Acceptance: Task 1 — 34 случая, exit 0; `u202F` 1, `EUR` 1, `29.03.2026 02:30:00` 1, `169` 1, `DateTime.Now` 0. Task 2 — 10 тестов, exit 0; `Перерасход` 1, `ru-RU` 0, `HtmlEncode` 0; RED записан; полный `dotnet test` 54/54.
