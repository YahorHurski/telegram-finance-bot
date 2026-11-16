# Деплой на Oracle Cloud (Always Free)

Пошаговая инструкция: от пустого аккаунта до работающего `https://<поддомен>.duckdns.org/health`.
Настоящие токены, пароли и IP сюда не пишутся. Везде, где стоит `<...>`, подставь своё значение.
Названия пунктов в консоли Oracle могут немного отличаться от текста: интерфейс периодически меняется.

## 1. Что понадобится

- Аккаунт Oracle Cloud (нужна банковская карта для проверки).
- Аккаунт на [duckdns.org](https://www.duckdns.org) (вход через GitHub или Google).
- Telegram на телефоне.
- SSH-ключ на ноутбуке: `~/.ssh/id_ed25519.pub`. Если файла нет, создай ключ командой `ssh-keygen -t ed25519`.

## 2. Виртуальная машина

1. Compute → Instances → Create instance.
2. Image: **Ubuntu 24.04** (aarch64).
3. Shape: **VM.Standard.A1.Flex** (Ampere, arm64), **2 OCPU / 12 GB** (весь лимит Always Free) или 1 OCPU / 6 GB, если хочешь оставить запас под второй инстанс.
4. Networking: публичная подсеть, «Assign a public IPv4 address» включено.
5. SSH keys: вставь содержимое `~/.ssh/id_ed25519.pub`.
6. Create. Запиши публичный IP: он понадобится для DuckDNS и SSH (`ssh ubuntu@<IP>`).

**Почему A1, а не x86 Micro:** образ приложения собирается прямо на VM. У E2.1.Micro 1 GB RAM, и сборки .NET рядом с Postgres на ней не хватит.

Если Oracle пишет **«Out of capacity»**, выбери другой availability domain или повтори попытку позже. Для A1 это обычная ситуация.

## 3. Pay As You Go и бюджетный алерт

1. Billing → Upgrade and Manage Payment → **Upgrade to Pay As You Go**. Возможна временная авторизация небольшой суммы на карте. Обратного перехода на Free, вероятно, нет [не проверено].
2. Billing & Cost Management → **Budgets** → Create Budget:
   - Target: корневой compartment (root);
   - сумма в месяц: **1** (в валюте аккаунта);
   - Alert rule: тип **Actual Spend**, порог 1 %, получатель — твой email.

Внутри лимитов Always Free платить не нужно. Платным становится всё, что сверх **1 500 OCPU-часов и 9 000 GB-часов в месяц** (например, вторая VM с 2 OCPU), и алерт ловит именно такую ошибку.

## 4. Сеть: два уровня фаервола

У Oracle порты закрыты в двух местах, и открыть нужно оба.

**Уровень 1, облако.** Networking → Virtual Cloud Networks → твоя VCN → **Security List** (Default Security List) → Add Ingress Rules:
- Source CIDR `0.0.0.0/0`, IP Protocol TCP, Destination Port `80`;
- то же для порта `443`.

**Уровень 2, сама VM.** В образе Ubuntu от Oracle работает iptables, и в цепочке INPUT стоит правило REJECT. Правила ACCEPT должны стоять **выше** него:

```bash
sudo iptables -L INPUT -n --line-numbers          # найди номер строки с REJECT, например 6
sudo iptables -I INPUT 6 -p tcp --dport 80  -m state --state NEW -j ACCEPT
sudo iptables -I INPUT 6 -p tcp --dport 443 -m state --state NEW -j ACCEPT
sudo netfilter-persistent save                     # сохранить правила после перезагрузки
```

Не выполняй `iptables -F`: удалятся и служебные правила Oracle.

## 5. DuckDNS

1. На duckdns.org придумай поддомен, например `<поддомен>.duckdns.org`.
2. В поле «current ip» впиши публичный IP VM и нажми «update ip».
   Или в своём терминале: `curl "https://www.duckdns.org/update?domains=<поддомен>&token=<токен DuckDNS>&ip=<IP VM>"` (ответ `OK`).
3. Проверь: `nslookup <поддомен>.duckdns.org` возвращает IP VM.

Токен DuckDNS в репозиторий не попадает. Публичный IP переживает stop/start VM, поэтому обновлять запись нужно только при пересоздании VM.

## 6. Telegram-бот

1. В Telegram открой **@BotFather** → `/newbot` → имя и username бота. BotFather пришлёт токен.
2. Открой своего нового бота и нажми **Start** (`/start`). Без этого бот не сможет написать тебе первым.
3. Узнай свой chat id: напиши **@userinfobot**, он ответит числом `Id`.

Токен бота хранится только в `.env` на VM. Не вставляй его в браузер и в чаты.

## 7. Docker на VM

```bash
ssh ubuntu@<IP VM>
sudo apt-get update
sudo apt-get install -y docker.io docker-compose-v2 git
sudo systemctl enable --now docker
sudo usermod -aG docker $USER
exit                                    # перелогинься, чтобы группа docker применилась
ssh ubuntu@<IP VM>
docker compose version                  # должно напечатать версию v2
```

## 8. Код и секреты

```bash
git clone https://github.com/YahorHurski/telegram-finance-bot.git ~/finance-bot
cd ~/finance-bot
# пока ветка фазы не слита в main: git checkout gsd/phase-01-hodyachiy-skelet
cp .env.example .env
chmod 600 .env
openssl rand -hex 32                    # выполни дважды: для POSTGRES_PASSWORD и для Bot__IngestToken
nano .env                               # заполни все пять переменных
```

| Переменная | Значение |
|------------|----------|
| `DOMAIN` | `<поддомен>.duckdns.org` |
| `POSTGRES_PASSWORD` | первый результат `openssl rand -hex 32` |
| `Bot__IngestToken` | второй результат `openssl rand -hex 32` (этот же токен впишешь в iOS-Команду) |
| `Bot__TelegramToken` | токен от @BotFather |
| `Bot__OwnerChatId` | твой chat id из шага 6 |

Секреты в hex, потому что в hex нет символов `;`, `=` и пробелов, которые сломали бы строку подключения к БД.
`POSTGRES_PASSWORD` применяется только при первом создании базы: если поменять его потом в `.env`, пароль в самой БД не изменится.

## 9. Первый запуск: строго по порядку

Caddy начинает получать сертификат Let's Encrypt сразу при старте. Если DNS или порты ещё не готовы, проверки провалятся, и после нескольких неудач Let's Encrypt временно заблокирует имя. Поэтому:

1. **DNS:** `nslookup <поддомен>.duckdns.org` возвращает IP VM (шаг 5).
2. **Порты:** оба уровня открыты (шаг 4). Проверка с ноутбука: `nc -vz <IP VM> 80` и `nc -vz <IP VM> 443`. Пока ничего не запущено, ответ «connection refused» — это нормально: значит, пакеты доходят до VM. Плохой знак — таймаут.
3. **Запуск:** `./deploy.sh`

`deploy.sh` делает: `git pull --ff-only` → сборка образа → миграции БД (`docker compose run --rm app migrate`) → `docker compose up -d` → проверка `https://<DOMAIN>/health`. В конце должно напечататься `Healthy OK`.

## 10. Обновление

```bash
cd ~/finance-bot && ./deploy.sh
```

После мержа ветки фазы в main переключи VM на main: `git checkout main`, затем `./deploy.sh`.

## 11. Проверка

```bash
curl https://<поддомен>.duckdns.org/health            # Healthy
docker compose ps                                       # app, postgres, caddy в статусе running
docker compose logs app | tail -20                      # нет сумм, продавцов и токенов
```

## 12. Риски

- **Отбор простаивающих инстансов.** Oracle может остановить Always Free VM, если за 7 дней загрузка CPU, сети и памяти ниже 20 %. Бот почти всё время простаивает. Что даёт Pay As You Go, решается в плане 01-05; там же будет уточнена эта формулировка. Искусственную нагрузку против простоя не создавай.
- **Страховка:** всё работает в Docker, поэтому переезд на другой сервер (например, Hetzner) не требует переделок. Бэкапы вне Oracle появятся в Phase 2.
