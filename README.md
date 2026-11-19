# Finance Bot

Telegram-бот для контроля личных расходов. iOS-Команда при каждой оплате Apple Pay отправляет сумму и продавца на сервер, сервер сохраняет трату и пишет в Telegram, сколько осталось от недельного лимита (200 zł).

Статус: ходячий скелет (Phase 1). Работает весь путь «Команда → сервер → база → Telegram» для одного пользователя.

## Стек

C# / .NET 10, ASP.NET Core (minimal API), EF Core 10 + PostgreSQL 18, NodaTime, Telegram.Bot, Serilog.
Тесты: xUnit v3, Testcontainers (настоящий Postgres), WebApplicationFactory.
Запуск: Docker Compose (migrator + app + postgres + Caddy с автоматическим HTTPS).

## API

`POST /api/v1/transactions`

- Заголовок `Authorization: Bearer <токен>` (значение `Bot__IngestToken`).
- Тело — JSON, все поля строками, как их отдаёт автоматизация «Транзакция»:

  ```json
  { "amount": "45,99 zł", "merchant": "Żabka", "card": "Visa", "time": "16.11.2026 12:00:00" }
  ```

- Ответы:
  - `201 {"id": "..."}` — трата сохранена (в том числе если сумму не удалось разобрать: она сохраняется со статусом «не разобрано»);
  - `401` — нет токена или токен неверный;
  - `413` — тело больше 64 KB;
  - `429` — больше 30 запросов в минуту.

`GET /health` — `Healthy`, если сервис жив и база доступна, иначе `503`.

## Локальный запуск

Нужен Docker Desktop.

```bash
docker build --target migrator -t ghcr.io/yahorhurski/telegram-finance-bot-migrator:local .
docker build --target app -t ghcr.io/yahorhurski/telegram-finance-bot:local .
cp .env.example .env
# заполни .env: DOMAIN=localhost, TAG=local, остальное по комментариям в файле
docker compose up -d                    # migrator применит миграции до старта app
curl -k https://localhost/health        # Healthy (-k: локальный сертификат Caddy)
```

Один Dockerfile даёт два образа: `migrator` (EF migration bundle) и `app`. Приложение схему базы не меняет.

Остановить: `docker compose down` (данные сохраняются). Удалить вместе с данными: `docker compose down -v`.

## Тесты

```bash
dotnet test
```

Интеграционные тесты поднимают настоящий Postgres через Testcontainers, поэтому Docker должен быть запущен.

## Деплой

Сервер: Oracle Cloud Always Free (arm64) + DuckDNS.

Build once, deploy many: PR → проверки (`build-test`, `image`: сборка arm64-образов и smoke) → мерж в main → образы уходят в GHCR с тегом = SHA коммита → кнопка Approve → `deploy.sh <sha>` на VM: бэкап → миграции → новая версия → `/health`, при провале откат на предыдущий SHA.

Пошаговая инструкция и правило миграций: [docs/deploy.md](docs/deploy.md).

## iPhone

Как собрать iOS-Команду: [docs/shortcut.md](docs/shortcut.md).
