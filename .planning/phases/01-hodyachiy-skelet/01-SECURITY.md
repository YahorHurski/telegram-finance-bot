---
phase: "01"
slug: "hodyachiy-skelet"
status: verified
# threats_open = count of OPEN threats at or above workflow.security_block_on severity (the blocking gate)
threats_open: 0
asvs_level: 1
created: "2026-11-17"
---

# Phase 01 — Security

> Per-phase security contract: threat register, accepted risks, and audit trail.

---

## Trust Boundaries

| Boundary | Description | Data Crossing |
|----------|-------------|---------------|
| iOS Shortcut / internet → caddy:443 → POST /api/v1/transactions | единственная публичная точка входа и граница аутентификации | Bearer-токен, сумма, продавец, карта (финансовые данные, high) |
| caddy → app:8080 (сеть compose) | внутренний HTTP, наружу не опубликован | те же данные после TLS-терминации |
| app → postgres:5432 (сеть compose) | внутренняя БД, порт не опубликован | недоверенные строки, финансовые данные |
| app → api.telegram.org | исходящий вызов, токен бота в пути URL | токен бота (secret), текст подтверждения с суммой |
| app → stdout / docker logs | всё залогированное видно с VM | метаданные запросов (без тел и заголовков) |
| рабочая копия → GitHub | всё закоммиченное публично навсегда | код, документация, .planning |
| build context → image | что попало в контекст, может попасть в слои | исходники (без .env) |
| интернет → VM (22, 80, 443) | Security List + iptables | SSH-управление, HTTP(S) |
| ~/finance-bot/.env на VM | все секреты прода | токены, пароль БД (secret) |
| аккаунт Oracle | финансовая граница (при PAYG) | биллинг |

---

## Threat Register

| Threat ID | Category | Component | Severity | Disposition | Mitigation | Status |
|-----------|----------|-----------|----------|-------------|------------|--------|
| T-01-01 | Spoofing | Ingest.TokenMatches | high | mitigate | Program.cs ValidateOnStart ≥32; токен проверяется до чтения тела; SHA-256 + FixedTimeEquals; 401 без тела; IngestTests Missing/Wrong/NonBearer; live 401 | closed |
| T-01-02 | Tampering | Ingest → AppDb | low | mitigate | только EF LINQ/SaveChanges; сырого SQL в src нет | closed |
| T-01-03 | Tampering | Messages.Confirmation (HTML) | medium | mitigate | Messages.Esc `&<>` для Merchant и AmountRaw; MessagesTests | closed |
| T-01-04 | Denial of Service | POST /api/v1/transactions | medium | mitigate | окно 30/мин → 429 с партициями owner/anon (CR-01); тело ≤64 KB; IngestTests JunkTraffic / Owner429 / Body64KB | closed |
| T-01-05 | Repudiation | порядок сохранения и отправки | medium | mitigate | SaveChanges до SendMessage; подсчёт и отправка под try/catch, таймаут 10 с; всегда 201; TelegramFailure test | closed |
| T-01-06 | Information Disclosure | секреты в git | high | mitigate | .gitignore `.env` с первого коммита кода; тестовые токены низкой энтропии; gitleaks CI + локально (0) | closed |
| T-01-07 | Tampering | Parsing.Amount | medium | mitigate | allow-list валют, типографский минус; 0 < v < 1 000 000; null вне лимита; ParsingTests | closed |
| T-01-08 | Tampering | Messages.Confirmation (HTML) | medium | mitigate | обрезка AmountRaw до 100 до экранирования; MessagesTests | closed |
| T-01-09 | Information Disclosure | Serilog / Program.cs | high | mitigate | уровни в коде; TelegramBotClient вне IHttpClientFactory; request logging без тел/заголовков; LogPrivacyTests на Production; live-логи 0 совпадений | closed |
| T-01-10 | Information Disclosure | git-история | high | mitigate | ci.yml fetch-depth 0 + gitleaks-action + check-ignore; локальный gitleaks чист | closed |
| T-01-11 | Tampering | сторонние GitHub Actions | low | accept | AR-01 | closed |
| T-01-12 | Information Disclosure | compose.yaml (postgres, app) | high | mitigate | app/postgres без ports, наружу только caddy; live 5432 закрыт | closed |
| T-01-13 | Information Disclosure | Dockerfile / .dockerignore | high | mitigate | .dockerignore `.env`; в образ копируются только global.json, Directory.Packages.props, src/; `.env` в образе 0 | closed |
| T-01-14 | Elevation of Privilege | контейнер app | medium | mitigate | USER $APP_UID; `id -u` = 1654 | closed |
| T-01-15 | Spoofing / Tampering | транспорт до Команды | medium | mitigate | Caddy авто-HTTPS; live 308 + Let's Encrypt | closed |
| T-01-16 | Information Disclosure | docs/*.md, README.md | medium | mitigate | только плейсхолдеры; grep реальных значений = 0 | closed |
| T-01-17 | Spoofing / Elevation | SSH на VM | high | mitigate | `sudo sshd -T` 2026-11-17: passwordauthentication no, kbdinteractiveauthentication no, permitrootlogin without-password; открыты только 22/80/443 (iptables, 01-05) | closed |
| T-01-18 | Information Disclosure | ~/finance-bot/.env | high | mitigate | генерация на VM, `stat` → 600 (01-05), значения не печатались; regex токена в репо 0, gitleaks 0 | closed |
| T-01-19 | Denial of Service | VM (отбор простаивающих) | medium | accept | AR-02 | closed |
| T-01-20 | Elevation / финансовый | аккаунт Oracle | medium | mitigate | A1.Flex 1/6 в пределах Always Free; бюджет с email-алертом; без PAYG списание невозможно (перепроверить при включении PAYG) | closed |
| T-01-21 | Tampering | Let's Encrypt (лимит валидаций) | low | mitigate | проба :80 до Caddy; сертификат с первой попытки | closed |
| T-01-22 | Information Disclosure | токен бота при вводе | high | mitigate | ввод пользователем на VM, агент только `grep -c`; regex токена: репо 0, логи 0 | closed |
| T-01-23 | Information Disclosure | логи прода | high | mitigate | live grep app+caddy: 0 по regex токена, «bearer», суммам, продавцу | closed |
| T-01-24 | Spoofing | токен приёма в iOS-Команде | medium | mitigate | docs/shortcut.md: не делиться Командой + процедура ротации | closed |
| T-01-25 | Information Disclosure | Postgres 5432 | high | mitigate | без ports; live: 5432 снаружи не соединяется | closed |
| T-01-26 | Tampering | проверочные траты в проде | low | mitigate | amount NULL вне лимита; 4 пустые тестовые строки в лимит не входят | closed |
| T-01-SC | Tampering | NuGet, Docker-образы, apt, спайк ScottPlot | high | mitigate | все пакеты закреплены, ни одной 11.x; ScottPlot вне slnx и образа; только официальные образы; docker.io из архива Ubuntu | closed |

*Status: open · closed · open — below high threshold (non-blocking)*
*Severity: critical > high > medium > low — only open threats at or above workflow.security_block_on count toward threats_open*
*Disposition: mitigate (implementation required) · accept (documented risk) · transfer (third-party)*

---

## Accepted Risks Log

| Risk ID | Threat Ref | Rationale | Accepted By | Date |
|---------|------------|-----------|-------------|------|
| AR-01 | T-01-11 | Официальные GitHub Actions закреплены мажорными тегами, не SHA; GITHUB_TOKEN только на чтение, деплоя и секретов в workflow нет. Пересмотреть, когда в CI появятся деплой и push в GHCR. | owner (plan 01-03 threat model) | 2026-11-16 |
| AR-02 | T-01-19 | Перевод Oracle на PAYG отложен владельцем (нужно заморозить ~400 zł). Риск: Oracle может отобрать простаивающую VM, траты в простое потеряются (Команда покажет «Трата не сохранена»). Переезд — `deploy.sh` по docs/deploy.md; бэкапы вне Oracle — Phase 2. Пересмотреть при включении PAYG. | owner (решение в сессии) | 2026-11-17 |

*Accepted risks do not resurface in future audit runs.*

---

## Security Audit Trail

| Audit Date | Threats Total | Closed | Open | Run By |
|------------|---------------|--------|------|--------|
| 2026-11-17 | 27 | 27 | 0 | gsd-security-auditor (ASVS L1, block_on high) + orchestrator live checks (sshd -T, ports, logs) |

Notes (outside the register): IN-07 (log privacy on the 500 path) deferred to Phase 6; IN-01 (merchant length vs Telegram 4096) deferred to Phase 3; CI not yet run on GitHub (SC-5, /gsd-ship); .planning is committed and contains the VM IP, hostname and chat id (low sensitivity).

---

## Sign-Off

- [x] All threats have a disposition (mitigate / accept / transfer)
- [x] Accepted risks documented in Accepted Risks Log
- [x] `threats_open: 0` confirmed
- [x] `status: verified` set in frontmatter

**Approval:** verified 2026-11-17
