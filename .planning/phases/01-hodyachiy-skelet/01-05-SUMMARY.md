---
phase: 01-hodyachiy-skelet
plan: 05
subsystem: infra
tags: [oracle-cloud, arm64, docker, iptables, duckdns, telegram, payg]

requires:
  - phase: 01-hodyachiy-skelet
    provides: "compose.yaml (app, postgres, caddy), deploy.sh, docs/deploy.md (план 01-04)"
provides:
  - "VM Oracle A1 (arm64, Ubuntu 24.04) с Docker 29.1.3 и Compose 2.40.3, готовая к ./deploy.sh"
  - "~/finance-bot на ветке gsd/phase-01-hodyachiy-skelet, .env (600) с секретами, сгенерированными на VM"
  - "Порты 80/443 открыты на двух уровнях (Security List + iptables, сохранено), порт 80 проверен снаружи"
  - "Честная формулировка SC-1 / FND-02 про PAYG (option-a) в ROADMAP и REQUIREMENTS"
  - "Ветка фазы запушена в origin после чистого прогона gitleaks"
affects: [01-06]

actuals:
  tokens: 9000
  tasks: 3
  commits: 1
plan_head_before: ac85d13090bb3c937d02d50bdf21bc595aa4d6b3
plan_head_after: 55be0590308f1d662f9c6c072624faa1b2624039

tech-stack:
  added: [docker.io 29.1.3 (Ubuntu archive), docker-compose-v2 2.40.3]
  patterns:
    - "Секреты генерируются и подставляются на самой VM (umask 077 + sed), значения никогда не печатаются"
    - "Проверка DNS и порта 80 временным контейнером до первого запуска Caddy"

key-files:
  created:
    - ".planning/phases/01-hodyachiy-skelet/01-05-SUMMARY.md"
  modified:
    - ".planning/ROADMAP.md"
    - ".planning/REQUIREMENTS.md"

key-decisions:
  - "SC-1 / FND-02 закрываются формулировкой option-a: письмо Oracle 2023 принято как доказательство, остаточный риск закрыт Docker и бэкапами вне Oracle (Phase 2)"
  - "Перевод на Pay As You Go отложен пользователем (авторизация ~400 zł на карте); PAYG не активен, это открытый блокер"
  - "VM 1 OCPU / 6 GB (выбор пользователя), регион Germany Central (Frankfurt), эфемерный публичный IP"

patterns-established:
  - "Правила iptables вставляются перед первой строкой REJECT и только если их ещё нет (iptables -C), затем netfilter-persistent save; цепочки не сбрасываются"
  - "netfilter-persistent save выполнен ДО установки Docker, чтобы правила Docker не попали в rules.v4"

requirements-completed: []

coverage:
  - id: D1
    description: "ROADMAP SC-1 и REQUIREMENTS FND-02 переписаны по option-a (содержат «письмо Oracle») с пометкой, что PAYG ещё не включён"
    requirement: FND-02
    verification:
      - kind: other
        ref: "grep -c 'письмо Oracle' .planning/ROADMAP.md .planning/REQUIREMENTS.md → 1 и 1"
        status: pass
    human_judgment: false
  - id: D2
    description: "VM готова к деплою: Docker Compose, ~/finance-bot на ветке фазы, .env 600 с 64-hex секретами, iptables 80/443"
    requirement: FND-02
    verification:
      - kind: other
        ref: "ssh ubuntu@79.76.107.24 'sudo docker compose version; stat -c %a ~/finance-bot/.env; sudo iptables -C INPUT ... 80/443; git -C ~/finance-bot rev-parse --abbrev-ref HEAD'"
        status: pass
    human_judgment: false
  - id: D3
    description: "DNS и порт 80 доступны снаружи по поддомену (до запуска Caddy)"
    requirement: FND-02
    verification:
      - kind: other
        ref: "curl -s -o /dev/null -w '%{http_code}' http://finance-bot-yh.duckdns.org/ → 200 (контейнер portprobe)"
        status: pass
    human_judgment: false
  - id: D4
    description: "Аккаунт Oracle переведён на Pay As You Go и это защищает VM от отбора простаивающих инстансов"
    requirement: FND-02
    verification: []
    human_judgment: true
    rationale: "PAYG не включён (отложен пользователем). Проверить можно только в консоли Oracle; защита от отбора подтверждена лишь письмом Oracle 2023"

duration: 3min
completed: 2026-11-17
status: complete
---

# Phase 1 Plan 05: Боевая площадка Oracle Summary

**VM Oracle A1 (arm64, Ubuntu 24.04, Frankfurt) готова к `./deploy.sh`: Docker 29.1.3 + Compose 2.40.3, порты 80/443 открыты на двух уровнях и проверены снаружи (HTTP 200 по `finance-bot-yh.duckdns.org`), `.env` с секретами сгенерирован на VM; SC-1 записан по option-a, но сам PAYG пока не включён.**

## Performance

- **Duration:** около 3 мин на задачу 3 (задачи 1–2 выполнял пользователь в консолях, раньше)
- **Started:** 2026-11-17T01:08:34Z
- **Completed:** 2026-11-17T01:10:35Z
- **Tasks:** 3 (2 чекпоинта решены пользователем, 1 auto)
- **Files modified:** 2

## Ответы пользователя

- **Задача 1 (решение по PAYG), дословно:** «а» → option-a «Принять письмо Oracle как доказательство».
- **Задача 2:** VM создана, бюджет создан (месячная сумма 1, alert по Actual Spend на email), Security List 80/443 добавлен, поддомен DuckDNS заведён, бот создан, /start отправлен. Разрешение на пуш, дословно: «пуш ок».
- **Отклонение пользователя:** перевод на Pay As You Go **отложен** (Oracle запросил авторизацию примерно 400 zł на карте). PAYG **не активен**.

## Несекретные координаты для плана 01-06

| Что | Значение |
|-----|----------|
| Публичный IP VM | `79.76.107.24` (эфемерный: назначен после создания) |
| Пользователь SSH | `ubuntu`, ключ `~/.ssh/id_ed25519` |
| Форма / регион | VM.Standard.A1.Flex, 1 OCPU / 6 GB (`free -g` показывает 5 GB), Germany Central (Frankfurt) |
| ОС / архитектура | Ubuntu 24.04.5 LTS, `aarch64`, диск 45 GB (занято 3.2 GB) |
| Поддомен | `finance-bot-yh.duckdns.org` → 79.76.107.24 |
| Telegram owner Id | 902664320 (не секрет) |
| Docker на VM | Engine 29.1.3 (arm64), Docker Compose 2.40.3+ds1-0ubuntu1~24.04.1 |
| Код на VM | `~/finance-bot`, ветка `gsd/phase-01-hodyachiy-skelet`, коммит 55be059 |

## Accomplishments

- ROADMAP SC-1 и REQUIREMENTS FND-02 переписаны по option-a, с явной пометкой, что перевод на PAYG отложен.
- Полная история (45 коммитов) прошла `gitleaks git --redact` («no leaks found»), затем ветка фазы запушена в origin.
- На VM в INPUT вставлены ACCEPT для NEW tcp 80 и 443 выше REJECT и сохранены в `/etc/iptables/rules.v4`.
- Установлены Docker Engine и плагин compose из архива Ubuntu; пользователь `ubuntu` в группе docker (новая SSH-сессия выполняет `docker` без sudo).
- Склонирован репозиторий; `~/finance-bot/.env` создан под umask 077, права 600. `POSTGRES_PASSWORD` и `Bot__IngestToken` сгенерированы `openssl rand -hex 32` на VM, их значения нигде не печатались. `Bot__TelegramToken` пустой: его впишет пользователь в 01-06.
- Внешняя проверка: временный `nginx:alpine` на порту 80 ответил 200 на `http://finance-bot-yh.duckdns.org/` с машины разработчика. Затем контейнер остановлен и образ удалён.

## Вывод проверок

```
nslookup finance-bot-yh.duckdns.org        → Address: 79.76.107.24
gitleaks git --redact                      → 45 commits scanned, no leaks found
git ls-remote --heads origin <ветка>       → 55be0590… refs/heads/gsd/phase-01-hodyachiy-skelet
sudo iptables -L INPUT (после)             → 4 ACCEPT tcp 22 | 5 ACCEPT tcp 443 NEW | 6 ACCEPT tcp 80 NEW | 7 REJECT
rules.v4                                   → строки 11–12 (443, 80) выше строки 13 (REJECT)
sudo docker compose version                → Docker Compose version 2.40.3+ds1-0ubuntu1~24.04.1
stat -c %a ~/finance-bot/.env              → 600
grep -c '^Bot__IngestToken=[0-9a-f]{64}$'  → 1   (POSTGRES_PASSWORD: тоже 1)
sudo iptables -C ... 443 / 80              → exit 0 / exit 0
git -C ~/finance-bot rev-parse --abbrev-ref HEAD → gsd/phase-01-hodyachiy-skelet
curl http://finance-bot-yh.duckdns.org/    → 200 (portprobe запущен)
curl https://finance-bot-yh.duckdns.org/   → exit 7 «connection refused»: пакеты доходят, 443 не режется
docker ps -q (после остановки portprobe)   → пусто
```

## Task Commits

1. **Задача 1: решение по PAYG**: чекпоинт, ответ «а», без коммита
2. **Задача 2: ручные шаги в консолях**: чекпоинт, выполнено пользователем, без коммита
3. **Задача 3: подготовка VM, формулировка SC-1, пуш**: `55be059` (docs). Остальная работа задачи 3 сделана на VM и в репозиторий не попадает.

## Files Created/Modified

- `.planning/ROADMAP.md`: SC-1 Phase 1 в формулировке option-a, пометка про отложенный PAYG
- `.planning/REQUIREMENTS.md`: FND-02 в той же формулировке

## Decisions Made

- **Option-a для SC-1/FND-02.** Почему так: официальная документация Oracle про PAYG молчит, а дожидаться документа, которого может не быть, бессмысленно. Честнее записать, на чём держится уверенность (письмо Oracle 2023), и чем закрыт риск, если она не оправдается: Docker-переносимость и бэкапы вне Oracle.
- **Два уровня фаервола.** Почему так: у Oracle порт закрыт и в облаке (Security List), и на самой VM (iptables с REJECT в конце INPUT). Открыть только один уровень недостаточно. Правило ACCEPT добавляется именно выше REJECT, потому что iptables читает правила сверху вниз и останавливается на первом совпадении.
- **Порядок DNS → порты → Caddy.** Почему так: Caddy при первом старте сразу просит сертификат у Let's Encrypt. Если DNS или порт 80 не готовы, проверка провалится, а после нескольких неудач Let's Encrypt на время блокирует имя. Поэтому сначала простая проверка временным nginx: она бесплатна и ничего не блокирует.
- **Секреты генерируются на VM.** Почему так: пароль БД и токен iOS-Команды не проходят через ноутбук, чат и логи агента, поэтому утечь могут только с самой VM. `umask 077` закрывает файл ещё в момент создания, а не после `chmod`.
- **`netfilter-persistent save` до установки Docker.** Почему так: Docker сам создаёт свои правила при каждом старте. Если сохранить их в `rules.v4`, при загрузке они могут задвоиться или конфликтовать.

## Deviations from Plan

### Отклонения со стороны пользователя

**1. Pay As You Go не включён (отложен пользователем)**
- **Found during:** задача 2
- **Issue:** при апгрейде Oracle запросил временную авторизацию около 400 zł на карте, и пользователь отложил перевод. Must-have «аккаунт Oracle в статусе Pay As You Go» **не выполнен**.
- **Effect:** без PAYG простаивающая VM подпадает под правило отбора: если за 7 дней загрузка CPU, сети и памяти ниже 20 %, Oracle может её остановить. Бот почти всё время простаивает.
- **Handling:** формулировка SC-1/FND-02 записана по option-a с пометкой «перевод на PAYG отложен и ещё не выполнен». В STATE.md добавлен блокер. FND-02 **не** отмечен выполненным.

**2. Форма 1 OCPU / 6 GB вместо 2/12.** Это альтернатива, предусмотренная планом. Сборка образа на VM будет медленнее, но RAM на app + postgres + caddy хватает.

### Auto-fixed Issues

None.

**Total deviations:** 0 автоисправлений; 1 существенное отклонение со стороны пользователя (PAYG отложен).
**Impact on plan:** VM технически готова к деплою, но защита от отбора простаивающих VM не включена. Это нужно сделать в ближайшие дни.

## Issues Encountered

- `grep` по `/etc/iptables/rules.v4` без sudo: «Permission denied». Повторено через `sudo grep`, правила на месте.

## User Setup Required

**PAYG (сделать в ближайшие дни, иначе Oracle может остановить VM по правилу 7 дней простоя):**
1. Открой https://cloud.oracle.com и войди в аккаунт.
2. Меню (☰) → **Billing & Cost Management** → **Upgrade and Manage Payment**.
3. Нажми **Upgrade to Pay As You Go** и подтверди карту. Авторизация около 400 zł временная: банк снимет блокировку сам, обычно в течение нескольких дней.
4. Через несколько минут на той же странице должно появиться **Pay As You Go**. Напиши агенту «PAYG включён».

Бюджет с alert rule уже создан, поэтому выход за лимиты Always Free придёт письмом.

**Токен бота (план 01-06):** токен от @BotFather впишешь сам прямо на VM, в чат его не присылай.

## Known Stubs

- `~/finance-bot/.env` на VM: `Bot__TelegramToken=` пустой. Это сделано намеренно: заполняется пользователем в плане 01-06.

## Next Phase Readiness

- Готово к 01-06: пользователь вписывает токен бота в `.env` на VM, затем `./deploy.sh` и проверка `https://finance-bot-yh.duckdns.org/health`.
- **Блокер:** PAYG не включён. Деплою он не мешает, но до включения VM рискует попасть под отбор.
- Публичный IP эфемерный. Он переживает stop/start, но при пересоздании VM поменяется, и тогда нужно обновить запись DuckDNS.

---
*Phase: 01-hodyachiy-skelet*
*Completed: 2026-11-17*
## Self-Check: PASSED

Files: 01-05-SUMMARY.md, ROADMAP.md, REQUIREMENTS.md exist; commit 55be059 on HEAD; all task-3 acceptance criteria re-run above and passed.
