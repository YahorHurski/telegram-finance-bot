# Phase 1: Ходячий скелет - Context

**Gathered:** 2026-11-16
**Status:** Ready for planning

<domain>
## Phase Boundary

Одна реальная оплата Apple Pay проходит весь путь: iOS-Команда → HTTPS (Caddy, DuckDNS) на Oracle Cloud Always Free VM → ASP.NET Core → PostgreSQL → сообщение владельцу в Telegram с остатком недельного лимита. Один проект ASP.NET Core, один пользователь, токен из `.env`, лимит 200 zł и неделя пн–вс (Europe/Warsaw) — константы.

Вне фазы: приём сообщений/вебхук Telegram (Phase 3), Idempotency-Key и повторы (Phase 3), migrator и автодеплой (Phase 2), категории, кнопки, мультипользовательность.

</domain>

<decisions>
## Implementation Decisions

### Сообщение в Telegram
- **D-01:** Подтверждение короткое, две строки: `− 45,99 zł · Żabka` и `🟢 Осталось 154,01 из 200 zł`. Без карты, времени, прогресс-бара.
- **D-02:** При перерасходе вторая строка заменяется на `🔴 **Потрачено 237,50/200 · Перерасход 37,50 zł**` (жирный через parse mode). Цвет текста в Telegram невозможен — подсветка только эмодзи 🟢/🔴 + жирный. Светофор 🟡 (80%) не делаем — алерты это Phase 5.
- **D-03:** Если сумма не разобрана: трата сохраняется (сырое тело + статус «сумма не разобрана»), в лимит не идёт, бот пишет `⚠️ Не разобрал сумму: «<исходная строка>» · <продавец>`. Запрос «введи сумму» — в Phase 3 (см. Deferred).
- **D-04:** Формат чисел — русская локаль: запятая как десятичный разделитель, `zł` после суммы.

### Контракт приёма трат
- **D-05:** Эндпоинт `POST /api/v1/transactions` с первого дня (не `/tx`), чтобы Команду на iPhone не перенастраивать в Phase 3. Success criteria ROADMAP с `POST /tx` читать как этот путь. — **Reversibility:** costly — URL прописан в iOS-Команде на телефоне; смена = ручная правка Команды.
- **D-06:** Аутентификация: `Authorization: Bearer <token>`, токен из `.env`; без токена / неверный → 401. (Хэширование токенов в БД — Phase 7; сравнение в постоянном времени уже сейчас.)
- **D-07:** Тело — JSON, все поля строками как отдаёт автоматизация «Транзакция», без обработки на iPhone: `amount`, `merchant`, `card`, `time`. Сырое тело запроса сохраняется всегда целиком (корпус для SPIKE-01).
- **D-08:** `time` приходит в формате `дд.мм.гггг чч:мм:сс` (локальное время телефона) и интерпретируется как Europe/Warsaw. Это время траты (определяет неделю). Если не разобралось — берётся время приёма на сервере.
- **D-09:** Ответ: `201` + JSON `{ "id": ... }`. Команда при успехе ничего не показывает (подтверждение придёт в Telegram); при ошибке (401/5xx/нет сети) показывает уведомление «Трата не сохранена». Инструкция по сборке Команды — часть фазы.
- **D-10:** Неразобранная сумма — не ошибка запроса: ответ тоже 201 (трата сохранена со статусом), см. D-03.

### CI и тесты
- **D-11:** GitHub Actions на push в main: сборка + тесты + gitleaks. Автодеплоя нет.
- **D-12:** Тесты в скелете: сквозной интеграционный тест на реальном Postgres (Testcontainers + WebApplicationFactory): POST с токеном → строка в БД + вызов Telegram (подменённый клиент); без токена → 401; логи не содержат сумм, продавцов и токенов (PRIV-03). Плюс unit-тесты разбора суммы, разбора времени и границ недели пн–вс по Europe/Warsaw.
- **D-13:** Деплой в фазе 1 руками: SSH на VM → `git pull` → `docker compose up -d --build` (образ собирается на самой VM, без GHCR и кросс-сборки). Команды записаны в README или скрипте. Миграции — ручной шаг (migrator в Phase 2).

### Claude's Discretion
- Инфраструктура (пользователь не выбрал для обсуждения): создание VM Oracle (ARM Ampere A1 или x86), перевод на PAYG, бюджетный алерт, поддомен DuckDNS, бот в @BotFather — план включает их как ручные шаги (checkpoint:human-action) с пошаговыми инструкциями. Что уже сделано, уточнить у пользователя на чекпоинте.
- Схема таблиц (с `user_id` с первой миграции), формат статуса неразобранной суммы, структура проекта, базовый образ (учесть ARM64 и ICU для ru-RU).
- Тексты ошибок, кроме зафиксированных выше.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Scope и требования
- `.planning/ROADMAP.md` §Phase 1 — цель, success criteria, Notes (один проект, только SendMessage, ручные миграции, ARM64)
- `.planning/REQUIREMENTS.md` — FND-02, FND-05, PRIV-03, упрощённые ING-03 и MAN-03; ING-09 (для deferred)
- `.planning/PROJECT.md` — constraints и Key Decisions
- `.planning/STATE.md` — накопленные решения и blockers (Oracle reclamation, ARM64)

### Стек и архитектура
- `.claude/CLAUDE.md` — зафиксированный стек и версии (.NET 10, Telegram.Bot 22.10.4, EF Core 10.0.15 pin, Npgsql 10.0.3, NodaTime, Serilog, xUnit v3, Testcontainers)
- `.planning/research/STACK.md` — подробности стека
- `.planning/research/ARCHITECTURE.md` — контракт приёма (§ingestion, AmountParser как граница доверия), логирование
- `.planning/research/PITFALLS.md` — известные ловушки

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- Нет — репозиторий пока содержит только планировочные документы (`.planning/`, `.claude/CLAUDE.md`). Фаза создаёт проект с нуля.

### Established Patterns
- Конвенций кода ещё нет; ориентир — `.claude/CLAUDE.md` (minimal APIs, NodaTime, snake_case, Central Package Management).

### Integration Points
- iOS-Команда (внешний клиент) → `POST /api/v1/transactions`
- Telegram Bot API (только исходящие `SendMessage`)

</code_context>

<specifics>
## Specific Ideas

- Подтверждение должно читаться прямо из пуш-уведомления: главное — сумма, продавец, остаток.
- Перерасход явно бросается в глаза: 🔴 + жирный, формулировка «Потрачено X/200 · Перерасход Y zł».

</specifics>

<deferred>
## Deferred Ideas

- **Phase 3 (расширение ING-09):** если сумма не разобрана, бот просит ввести сумму и дописывает её в ту же сохранённую трату. Требует приёма сообщений (вебхук), поэтому не в скелете.
- **Phase 5:** предупреждение 🟡 при 80% лимита (уже покрыто алертами LIM).

</deferred>

---

*Phase: 01-hodyachiy-skelet*
*Context gathered: 2026-11-16*
