# Feature Research

**Domain:** Multi-user personal expense-tracking Telegram bot (Poland, PLN, Russian UI). Expenses arrive automatically from Apple Pay through the iOS Shortcuts "Transaction"/Wallet personal automation; other expenses are entered by hand.
**Researched:** 2026-11-14
**Confidence:** MEDIUM overall. Telegram facts come straight from the Bot API spec (10.4, Nov 9 2026). Enable Banking terms come from its own docs and changelog. iOS Shortcuts behavior comes from several third-party guides plus Apple Developer Forums threads; Apple has not published reference docs for the trigger payload. Competitor patterns are MEDIUM.

> Confidence labels follow the project seam (`classify-confidence`). Single web sources are LOW. Facts that agree across two or more independent sources, or that come from the vendor's own spec, are labeled MEDIUM.

---

## 0. Ground Truth: What the Data Source Actually Gives Us

Every downstream feature depends on these facts, so they come first.

### iOS Shortcuts "Transaction" (Wallet) personal automation

| Aspect | Finding | Confidence |
|--------|---------|------------|
| Availability | Added in iOS 17. Named "Transaction" on iOS 17–25 and "Wallet" on iOS 26+. iOS 27 also lets apps ship a prebuilt automation that is added switched off. | MEDIUM (multiple guides agree) |
| Trigger | "When I tap any of [cards]". Filters: specific cards or Any Card, **Categories** (Apple's merchant categories), and specific merchants. Pass types: Payment, Transit, Access, Identity. | MEDIUM |
| Shortcut Input fields | A `Transaction` object with **Amount**, **Merchant**, and **Card or Pass** (the card name). Some app-intent actions also show Date and Currency Code, but those are the receiving app's parameters. **No MCC and no category field.** Category exists only as a trigger filter. **No transaction ID.** | MEDIUM |
| Amount format | Not a clean decimal. It is a localized currency value. Sources show strings like `€8.00` and `USD 14.50`, and on a Polish locale expect `45,99 zł` or `PLN 45,99`. Several guides call the format unresolved and say each device should be tested. | LOW. **Spike on the user's iPhone in Phase 1.** |
| Run without confirmation | Yes. "Run Immediately" (iOS 17–26) runs in the background with no prompt. Reports say iOS updates sometimes reset automations to "Ask Before Running". | MEDIUM |
| HTTP POST | "Get Contents of URL" with Method=POST and a JSON body works inside the automation. Some older reports say it stalls inside personal automations. | MEDIUM |
| Scope | Only **in-store contactless taps with a Wallet card** (iPhone, and possibly Apple Watch). **No triggers** for online or in-app Apple Pay (per CashJot), physical card dips or taps, **BLIK** (very common in Poland), cash, or payments made from a Mac or iPad. | MEDIUM |
| Refunds | Not captured. The trigger only fires on taps. | MEDIUM |
| Declined payments | **The trigger fires even when the payment is declined** (Apple Dev Forums 765516). | LOW (single forum thread, plausible) |
| Missing or late data | Some issuers don't send amount or merchant to Wallet, so fields arrive empty or as `0.00`. On iOS 18+ the trigger can **time out** ("Hit timeout waiting for transaction") when the bank's Wallet notification is late, and the shortcut then fails silently. One report says an Apple Watch payment failed to trigger it. | MEDIUM (several independent reports) |
| Transit taps | Transit gates send no price, which causes errors. Untick Transport in the Categories filter. | LOW |
| Duplicates | No native ID. CashJot dedupes on same amount + merchant + card within 90 seconds. | LOW |

**Implications for features (opinionated):**
1. **Manual entry is a core input, not a fallback.** In Poland, BLIK, online shopping, and failed triggers together account for a real share of spending. Manual entry has to be as fast as the automatic path.
2. **The server parses the raw amount string.** The Shortcut sends the raw `Amount` text, `Merchant`, `Card or Pass`, and a device timestamp. The server parses the decimal comma, the `zł`/`PLN` marker, and foreign currencies. Do not run "Get Numbers from Input" in the Shortcut, because comma and locale bugs would be invisible.
3. **The client creates the idempotency key.** The Shortcut builds a key such as `Current Date` formatted to the second plus amount plus merchant and sends it in a header or body field. The server also applies a natural-key safety net (same user, amount, merchant, and card within about 2 minutes). Because declined payments trigger too, the dedupe must not stop the user from entering a legitimate second payment, and **Delete must be one tap away**.
4. **Handle partial payloads explicitly.** If merchant is missing, record "Неизвестно" and ask for a category. If the amount is missing or zero, **record nothing in the ledger**. Instead, send "Была оплата картой X в HH:MM, но банк не передал сумму — введите сумму" with a reply prompt.
5. **Treat non-PLN currencies separately.** PLN-only is a project constraint, but Apple Pay abroad will send EUR and other currencies. Store the original amount and currency, and ask the user for the PLN amount (or keep the entry outside the limit until it's provided). Never treat €20 as 20 zł.
6. **Category hint via Wallet category filters (optional differentiator).** The payload has no category, so the only way to get Apple's category is one automation per category filter (Food & Drinks → sends `hint=food`). This is clever but makes setup much harder. Leave it out of v1 and document it as an option.

---

## Feature Landscape

### Table Stakes (Users Expect These)

Without these the bot feels broken or untrustworthy. Every serious expense app or bot has them (Copilot, Monarch, YNAB, Actual, CashJot, plus the GitHub Telegram bots: cheapskate, marbot, DollarBot, financebot).

| # | Feature | Why Expected | Complexity | Notes |
|---|---------|--------------|------------|-------|
| T1 | `/start` onboarding plus a per-user secret ingest token | Multi-user requires identity; the token is the only auth the Shortcut can carry | LOW | Store the token hashed (SHA-256 is enough for a high-entropy random token). Show it once. Send it in an HTTP header (`Authorization: Bearer`), not in the URL, so it stays out of access logs. |
| T2 | Ready-made iCloud Shortcut link plus step-by-step setup guide | Building the automation by hand is the main drop-off point | MEDIUM | Use Shortcuts **Import Questions** so the user pastes the token when importing. On iOS ≤26 the iCloud link adds the shortcut but **not the automation**, so the user still creates the automation and points it at the shortcut. The guide must cover "Run Immediately", "Any Card", and unticking Transport. Screenshots in Russian help. |
| T3 | **Setup verification ("test payment")** | Users don't know whether the automation works until a real purchase fails to appear | LOW | The Shortcut sends `test=true` when run by hand (Shortcut Input is empty), and the bot replies "Команда настроена ✅". Low cost, removes a lot of confusion. |
| T4 | `/token` reissue (old token invalidated) | Leaked or lost tokens | LOW | After reissue, remind the user to re-import or edit the shortcut. |
| T5 | Idempotent ingest endpoint | Duplicate expenses destroy trust in the limit number | MEDIUM | Client key plus a natural-key window (see §0). Return 200 for duplicates (not 409) so the Shortcut doesn't show an error. |
| T6 | Robust amount parsing (comma or dot, `zł`/`PLN`/symbols, NBSP thousands separators) | The locale-formatted string from iOS | MEDIUM | Unit-test with a corpus of real strings captured during the spike. |
| T7 | Manual entry by message: `45 такси`, `12,50 кофе`, `такси 45` | Fastest input; needed for BLIK and cash | MEDIUM | Accept amount first or last, comma or dot decimals, `!` suffix for "out of limit", `+` prefix for income. Optional date tokens (`вчера`, `12.11`) belong in v1.x. Reject ambiguous input with a short syntax hint rather than guessing. |
| T8 | Confirmation after every expense: amount, merchant, category, **remaining weekly limit** | This is the Core Value, "I always see what's left" | LOW | One message per expense, kept compact. Use HTML parse mode. |
| T9 | Inline keyboard under each expense: Category · Не в лимит/В лимит · Изменить · Удалить | Standard pattern in Telegram expense bots; fixing mistakes must be one tap | MEDIUM | **Edit the same message in place** (`editMessageText`/`editMessageReplyMarkup`) instead of sending new messages. Always call `answerCallbackQuery`. `callback_data` is limited to **1–64 bytes**, so use compact payloads like `c:{txId}:{catId}` and never put names in them. Bot API 10.x adds button `style` (`danger`/`success`/`primary`), so make Delete red. |
| T10 | Delete with undo, not "are you sure?" | Declined-payment triggers and mistakes are common | LOW | Soft delete, then edit the message to "Удалено · [Вернуть]". The Bot API cannot delete messages older than 48 h. Editing your own messages has no time limit, so **edit instead of deleting the message.** |
| T11 | Built-in categories plus merchant → category rules for Polish merchants | Without it every expense lands in "Другое" | MEDIUM | Seed rules for Żabka, Biedronka, Lidl, Kaufland, Carrefour, Auchan, Rossmann, Hebe, Orlen, BP, Shell, Circle K, Uber, Bolt, FreeNow, Jakdojade, Wolt, Glovo, Pyszne.pl, McDonald's, KFC, Starbucks, Costa, Allegro, Zalando, Empik, IKEA, Media Expert, RTV Euro AGD, Apteka (Gemini, DOZ), and others. **Merchant normalization is essential** (`ZABKA Z1234 K.1 WARSZAWA` → `zabka`): case-fold, strip diacritics, drop store numbers and city suffixes. Match on normalized prefix or keyword. |
| T12 | Uncategorized → "Другое" immediately plus a category question in the same message | Limit math stays correct; nothing gets lost | LOW | Already in the plan. This is the right pattern; Copilot and Monarch work the same way (categorize first, review later). |
| T13 | Learn from corrections (personal rule per merchant) | "Teach once" is what users expect after Copilot and Monarch | MEDIUM | Personal rule takes precedence over global rule, which takes precedence over "Другое". When the user changes a category, **offer** "Запомнить для всех трат в «Żabka»? [Да] [Только эту]" instead of silently creating a rule. Copilot's name rules also recategorize history; offer "и прошлые траты" as an option. |
| T14 | `/other` review queue | Deferred triage (Copilot's "To review" equivalent) | LOW | Paginated list with category buttons per item. Empty state: "Всё разобрано 🎉". |
| T15 | Weekly limit (Mon–Sun, Europe/Warsaw), configurable | Core Value | MEDIUM | Calculate weeks in **Europe/Warsaw local time**, not UTC. A payment at 00:30 Monday local time belongs to the new week. Handle DST. |
| T16 | Rollover mode: reset each week, or carry over both surplus **and** overspend | Monarch, Copilot, and Actual all carry positive and negative balances; users expect symmetry | MEDIUM | Define a rollover start week (Copilot's "first month with rollover"). **Compute derived, don't snapshot:** effective limit = base limit + Σ(previous weeks' base − spent) from the start week. Then backdated edits or deletes and limit changes stay consistent. Decide whether a limit change applies to past weeks (recommendation: limit history with effective-from dates). |
| T17 | "Out of limit" flag (`!` suffix and toggle button) | Copilot's "Exclude" and Cleo's "Exclude from budget". Large one-off purchases would otherwise wreck the weekly number | LOW | Excluded expenses still count toward balance and monthly statistics, shown in a separate section. Copilot's cash-flow view lets users add excluded spend back into totals, so the month report should show both "в лимите" and "всего". |
| T18 | Alerts at 80% and 100% of the weekly limit | Standard in every budgeting app; this is the behavioral nudge the user wants | LOW–MEDIUM | **Fire once per threshold per week.** Store the alert state. If the user deletes an expense and drops back below the threshold, re-arm the alert. A 100% alert replaces an 80% alert when one payment crosses both. |
| T19 | `/week` and `/month` reports | Core reporting | MEDIUM | `/week`: spent/limit, remaining, Unicode progress bar (`▓▓▓▓▓▓░░░░ 62%`), categories sorted by amount, out-of-limit expenses separately. `/month`: categories, weekly totals, change vs last month (Δ zł and %), out-of-limit separately. Use `<pre>` blocks for aligned columns. |
| T20 | Server-rendered PNG chart attached to reports | Every bot reviewed has some `/chart` | MEDIUM | `sendPhoto` limits: photo ≤10 MB, width+height ≤10000, **caption ≤1024 chars**. Send the text report as its own message (≤4096 chars) and the chart as a photo with a short caption, or put the chart first with a 1–2 line caption. A donut or horizontal bar chart of categories reads better on a phone than a pie with many slices. Group the long tail into "Прочее". |
| T21 | Scheduled summaries (Sunday evening week, 1st of month) | Users expect a push recap | MEDIUM | Background job keyed on Europe/Warsaw. Must be idempotent (don't double-send after a restart). Respect a 30 msg/s broadcast limit, which only matters at scale. |
| T22 | Manual balance (`/balance 3200`) plus income (`+5000 зп`) | In scope. Gives a rough "what's in the account" | LOW | Balance = anchor + incomes − all expenses since the anchor (including out-of-limit). Each `/balance` call creates a new anchor. |
| T23 | Data isolation between users | Required for multi-user | MEDIUM | Every query scoped by `UserId`. Integration tests prove that user A cannot see, edit, or delete user B's data through callback IDs. Callback payloads contain IDs, so **always check ownership** on callback handling. |
| T24 | `/help` and a registered command menu (`setMyCommands`, Russian descriptions) | Discoverability in Telegram | LOW | |
| T25 | List and edit recent expenses (`/last` or `/history`) | Users want to fix something from 3 days ago without scrolling | LOW–MEDIUM | Last N expenses with per-item buttons. Add filtering later. |

### Differentiators (Competitive Advantage)

These line up with the Core Value ("every Apple Pay payment lands automatically, no duplicates; I always see what's left") and with the portfolio goal.

| # | Feature | Value Proposition | Complexity | Notes |
|---|---------|-------------------|------------|-------|
| D1 | **Zero-touch Apple Pay capture via a server endpoint** (not a local app) | Most Shortcuts-based trackers are iOS apps (CashJot, Moneyfetch, Actuali). A server plus Telegram works without installing an app, supports multiple users, and is a strong backend showcase | MEDIUM | The core of the product. |
| D2 | **Silent-failure detection / ingestion health** | The trigger times out and gets reset to "Ask" after iOS updates (§0). The user won't notice that capture stopped | LOW–MEDIUM | If a user who normally has Apple Pay traffic gets **no ingest in N days** (e.g., 3) while still entering manually, send: "Похоже, автоматизация перестала срабатывать — проверьте «Выполнять сразу»". A last-ingest timestamp is shown in `/status`. |
| D3 | **Partial-payload rescue** ("bank didn't pass the amount, enter it") | Turns a lost expense into a 2-second fix | LOW | Uses ForceReply or a reply-to-message prompt. See §0. |
| D4 | **Foreign-currency capture with PLN prompt** | Apple Pay abroad keeps working without breaking the PLN-only model | LOW–MEDIUM | Store `OriginalAmount` and `OriginalCurrency`. Optional NBP exchange-rate API suggestion (`api.nbp.pl`, free, official) is a nice v1.x touch. |
| D5 | **Smart "remember this?" category learning with retroactive apply** | Better than silent rules (unpredictable) and better than none (tedious) | MEDIUM | Built on T13. Also a strong portfolio talking point. |
| D6 | **Rollover-aware remaining amount shown in every confirmation** | Most apps show the remaining budget only on a dashboard. Showing it on every payment is the behavioral hook against impulse buys | LOW | Depends on T15–T16. Example: "Осталось 54 zł из 200 (перенос +20)". |
| D7 | **Per-day pace hint inside the week** | "Сегодня среда, по плану можно ~85 zł до воскресенья" gives a gentle nudge without a daily-limit system | LOW | Not a daily limit (out of scope). It's one derived line in `/week`. |
| D8 | **Undo-first destructive actions** | Faster and friendlier than confirm dialogs | LOW | T10. |
| D9 | **Duplicate-suspect prompt** instead of silent drop for near-duplicates outside the strict key window | Covers the declined-then-retried case: two identical taps 40 seconds apart, where one was declined | MEDIUM | If the strict idempotency key differs but amount, merchant, and card match within 5 minutes, record the expense and attach "Похоже на дубль? [Удалить]". |
| D10 | CSV export (`/export`) | Low cost, signals data ownership; good for the portfolio | LOW | |
| D11 | Rich messages / tables (Bot API 10.2+ `sendRichMessage` with `RichBlockTable`) | Native tables instead of `<pre>` alignment | MEDIUM | Very new (July 2026). Check that the Telegram.Bot .NET library supports it. **Don't build v1 on it**; use plain HTML first. |

### Anti-Features (Commonly Requested, Often Problematic)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| LLM/AI categorization in v1 | "Smart" and trendy | Cost, latency, nondeterminism, prompt-injection surface via merchant names, harder tests. Rules plus learning cover about 90% for one user's merchants | Rules + normalization + personal learning (T11, T13). Revisit in v2 as a fallback for unknown merchants only. |
| Parsing bank push notifications or SMS | "Capture BLIK too" | Not possible on iOS. Third-party app notifications can't trigger Shortcuts. The "Message" trigger only sees iMessage/SMS, and Polish banks mostly use push | Fast manual entry (T7). Open Banking reconciliation in v2. |
| Shortcut → Telegram directly | Skip the server | A bot can't receive messages sent by itself; the Bot API is not a client API | Server endpoint (already decided). |
| Silently dropping suspected duplicates | "No duplicates!" | Two real identical coffees get lost, and the user never knows | Strict key dedupe plus a visible duplicate-suspect prompt (D9). |
| Confirmation dialog before every delete or edit | "Safety" | Friction on the most frequent correction path | Undo (T10). |
| Daily limit / "до зарплаты" | Common in RU budget apps | Explicitly out of scope. Competes with the weekly mental model | Pace hint (D7). |
| Snapshotting weekly balances in a table | Looks simpler | Backdated edits, deletes, limit changes, and timezone fixes corrupt the rollover chain | Derived computation from expenses plus limit history (T16). |
| Full multi-currency accounts | Travel | Large complexity (FX rates, per-account currencies), out of scope | Store the original currency and ask for PLN (D4). |
| Receipt OCR, voice input, QR receipts (seen in RU bots) | Flashy | Off-core. Polish receipts have no standardized QR like Russian fiscal receipts | Defer indefinitely. |
| Group or family shared budgets | Seen in @budgetmoneybot etc. | Changes the isolation model and the auth model | v2+ if ever. Design `UserId` scoping so a later `BudgetId` is possible. |
| Recurring and subscription tracking | Common in apps | Not about impulse spending | Defer. |
| Web dashboard / Mini App in v1 | Nicer charts | Doubles the UI surface before the core is stable | v2 (already decided). The server-centric API keeps the door open. |
| Ingest token in URL query string | Easy in Shortcuts | Leaks into proxy and access logs | `Authorization` header. "Get Contents of URL" supports headers. |

---

## Feature Dependencies

```
[T1 Registration + token]
    └──requires──> [T23 Data isolation model]
[T2 Shortcut + guide] ──requires──> [T1] and [T5 Ingest endpoint]
[T3 Setup test] ──requires──> [T5]
[T5 Idempotent ingest] ──requires──> [T6 Amount parsing] ──requires──> [§0 on-device spike]
[T8 Confirmation] ──requires──> [T11 Categorization] and [T15 Weekly limit calc]
[T9 Inline buttons] ──requires──> [T8] ; enables [T10 Delete/undo], [T13 Learning], [T17 Out-of-limit toggle]
[T12 "Другое" + ask] ──requires──> [T9] ; feeds [T13 Learning] ; listed by [T14 /other]
[T16 Rollover] ──requires──> [T15] + limit history
[T18 Alerts] ──requires──> [T15]/[T16] (alerts must use rollover-adjusted limit)
[T19 Reports] ──requires──> [T15], [T16], [T17] ; [T20 Chart] ──enhances──> [T19]
[T21 Scheduled summaries] ──requires──> [T19] + background scheduler
[T22 Balance] ──requires──> expense ledger + income entries (shares parser with [T7])
[D2 Health check] ──requires──> [T5] (last-ingest timestamps)
[D3 Partial rescue], [D4 FX prompt], [D9 Dup-suspect] ──require──> [T5] + [T9]
[T7 Manual entry] ──shares──> parsing/ledger with [T5] (same domain command "AddExpense")

[Snapshot balances] ──conflicts──> [T16 derived rollover]
[Silent dedupe] ──conflicts──> [D9 visible dup prompt]
```

### Dependency Notes

- **The amount-parsing spike blocks ingest.** Until real `Amount` strings from the user's iPhone (PKO BP, mBank, Millennium cards) are captured, the parser and idempotency design are guesses. Do this first, ideally with a throwaway endpoint that logs raw bodies.
- **Ingest and manual entry should go through one domain operation** (`RecordExpense(source, amount, merchant, …)`). Confirmation, categorization, alerts, and buttons then behave the same for both sources. This is also cleaner to show in a portfolio.
- **Alerts depend on the final limit semantics.** Build T15 and T16 (with the rollover formula) before T18. Otherwise the alert thresholds need rework.
- **The learning loop (T13) depends on the button infrastructure (T9) and merchant normalization (T11).** Rules keyed on unnormalized merchant strings won't generalize across store numbers.
- **Reports (T19/T20) are the last consumers.** They need categories, limits, out-of-limit flags, and income settled first.

---

## MVP Definition

### Launch With (v1)

- [ ] T1, T4, T23: registration, token, reissue, isolation. Foundation for multi-user.
- [ ] T2, T3: Shortcut link, guide, test ping. Without a verified setup, no automatic data arrives.
- [ ] T5, T6: idempotent ingest plus robust parsing. Core Value ("without duplicates").
- [ ] T7: manual entry with `!` and `+`. BLIK and cash are a large share of Polish spending.
- [ ] T8, T9, T10: confirmation, inline actions, undo-delete. Correcting mistakes is part of the core loop.
- [ ] T11, T12, T13, T14: rules, "Другое" + ask, learning, `/other`.
- [ ] T15, T16, T17, T18: weekly limit, rollover mode, out-of-limit, 80/100% alerts. Core Value ("how much is left").
- [ ] T19, T20, T21: `/week`, `/month`, PNG chart, scheduled summaries.
- [ ] T22: manual balance and income.
- [ ] D2, D3, D4 (prompt only, no NBP API yet), D6: cheap and directly protect the Core Value against known iOS trigger failures.

### Add After Validation (v1.x)

- [ ] D9 duplicate-suspect prompt, if declined-payment double triggers turn up in real use.
- [ ] D7 weekly pace hint, after the user has lived with `/week` for a while.
- [ ] T25 `/history` with filters, and date tokens in manual entry (`вчера`), when backdating becomes a need.
- [ ] D10 CSV export.
- [ ] NBP exchange-rate suggestion for foreign payments (D4+).
- [ ] D11 rich-message tables, once Telegram.Bot supports them.

### Future Consideration (v2+)

- [ ] **Open Banking reconciliation** (see feasibility below), as a second source used to **reconcile and catch missed expenses** (BLIK, failed triggers). Not a replacement for real-time Apple Pay capture: PSD2 data is booked with a lag of hours to days.
- [ ] Web dashboard / Telegram Mini App.
- [ ] LLM fallback categorization for unknown merchants only.
- [ ] Wallet-category hint via multiple automations (§0, point 6).

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Idempotent ingest + parsing (T5, T6) | HIGH | MEDIUM | P1 |
| Registration/token/isolation (T1, T4, T23) | HIGH | MEDIUM | P1 |
| Shortcut + guide + test (T2, T3) | HIGH | MEDIUM | P1 |
| Manual entry (T7) | HIGH | MEDIUM | P1 |
| Confirmation + inline actions + undo (T8–T10) | HIGH | MEDIUM | P1 |
| Weekly limit + rollover + exclude (T15–T17) | HIGH | MEDIUM | P1 |
| Rules + "Другое" + learning + /other (T11–T14) | HIGH | MEDIUM | P1 |
| Alerts 80/100 (T18) | HIGH | LOW | P1 |
| /week, /month (T19) | HIGH | MEDIUM | P1 |
| PNG chart (T20) | MEDIUM | MEDIUM | P1 (in scope; portfolio value) |
| Scheduled summaries (T21) | MEDIUM | MEDIUM | P1 |
| Balance + income (T22) | MEDIUM | LOW | P1 |
| Ingest health check (D2) | HIGH | LOW | P1 |
| Partial-payload rescue (D3) | MEDIUM | LOW | P1 |
| Foreign-currency prompt (D4) | MEDIUM | LOW | P1 |
| Duplicate-suspect prompt (D9) | MEDIUM | MEDIUM | P2 |
| Pace hint (D7) | MEDIUM | LOW | P2 |
| History/filters, date tokens (T25) | MEDIUM | LOW–MEDIUM | P2 |
| CSV export (D10) | LOW | LOW | P2 |
| Rich tables (D11) | LOW | MEDIUM | P3 |
| Open Banking reconciliation | HIGH (for coverage) | HIGH | P3 (v2) |

---

## Competitor Feature Analysis

| Feature | Copilot / Monarch (apps) | CashJot / Moneyfetch / Actuali (Shortcuts-based iOS apps) | OSS Telegram bots (cheapskate, marbot, DollarBot, financebot) | Our Approach |
|---------|--------------------------|----------------------------------------------------------|---------------------------------------------------------------|--------------|
| Ingestion | Bank aggregation | Wallet automation → local app intent; Run Immediately | Manual only | Wallet automation → **server HTTPS** + manual entry |
| Dedupe | Aggregator IDs | Amount + merchant + card within 90 s (CashJot) | n/a | Client key + natural-key window + visible duplicate-suspect prompt |
| Missing data | n/a | "No amount" queue (CashJot), error notification (Actuali) | n/a | Bot asks for the amount in chat (D3) |
| Categorization | ML + name rules, rules can recategorize history | Suggest from past expenses at the same merchant | User picks from keyboard | Seed rules + normalization + "remember?" learning with optional retroactive apply |
| Exclude from budget | Copilot "Exclude", excluded categories, add back in cash flow | n/a | Rare | `!` suffix + toggle; shown separately in reports |
| Rollover | Monthly, per-category, positive and negative carry, start month | n/a | Rare | **Weekly**, global, positive and negative carry, start week, derived computation |
| Alerts | Budget alerts | Per-capture notification | Rare | 80/100% once per week, re-arming |
| Reports/charts | Rich dashboards | In-app | `/chart` pie (financebot, MyDollarBot), QuickChart (marbot) | Text report + server-rendered PNG; scheduled summaries |
| Refunds | Aggregator handles | Not captured | n/a | Not captured automatically. Manual `+` income or deleting the expense. Document it. |

---

## Appendix: Open Banking (PSD2) Feasibility for v2, Individual Developer in Poland

| Provider | Status for an individual (Nov 2026) | PKO BP / mBank / Millennium | Verdict | Confidence |
|----------|-------------------------------------|-----------------------------|---------|------------|
| **Enable Banking** | **Free for personal use.** A production app in **restricted mode** is activated by linking your own accounts in the Control Panel. The ToS says production use is limited to Linked Accounts and free "for evaluation purposes or for the personal use of private individuals". The Dec 2025 changelog confirms "Personal usage, Sandbox and evaluation usage continue to be free of charge". Production pricing has been quote-based since April 2026 (third-party claim). | The Poland market page lists PKO BP (SCA in IKO), mBank, Millennium, Pekao, Santander, ING, and Alior. All use redirect flow with SCA in the bank app. | **Best option, but personal use only.** Restricted mode **returns only accounts linked by the app owner**. A multi-user bot can't onboard other people's banks on the free tier. Options: (a) Open Banking only for the owner's account, (b) "bring your own Enable Banking app" (each user registers and uploads their app ID and private key; awkward but legal), (c) a commercial agreement. | MEDIUM (vendor docs + ToS + changelog agree) |
| **GoCardless Bank Account Data (ex-Nordigen)** | **New signups disabled** (`bankaccountdata.gocardless.com/new-signups-disabled`, per a DEV.to report). The official announcement page sends people to sales. The former free tier (10 accounts) was removed. Existing users keep working. | Was good | **Not viable** for a new account. | MEDIUM (third-party + official redirect to sales) |
| **Salt Edge** | Sandbox free; production sales-led. The Spectre page mentions a "Basic, no cost, 100 live connections" tier, but third parties disagree, and it is aimed at companies. | Strong in CEE | **Unlikely** for an individual. Ask them if Enable Banking fails. | LOW |
| **Tink (Visa)** | Enterprise-only; indie developers are redirected to partners. | Good | **Not viable.** | LOW–MEDIUM |
| **Kontomatik** | Polish B2B AIS provider; no public pricing or personal tier found. | Native PL coverage | **Not viable** without a company and a contract. | LOW |
| open-banking.io (new, self-serve) | €3/month for the first account, €1 for each additional, read-only AIS (the founder's own article). | Unknown | Watch list only; very young, check PL coverage. | LOW |

**v2 feature implications:** Open Banking should feed a **reconciliation** feature: import booked transactions, match them to existing Apple Pay or manual expenses by amount ± date window, surface unmatched BLIK and card expenses for one-tap categorization, and optionally replace the manual balance with the real one. Expect SCA re-consent every ~180 days (EU RTS change; check per bank) and booking lag. Keep the ledger's `Source` field (`ApplePayShortcut | Manual | OpenBanking`) and an `ExternalId` column from v1 so that v2 needs no migration headache.

---

## Sources

- iOS Shortcuts Transaction/Wallet automation:
  - [CashJot: Apple Pay automation guide](https://www.cashjot.com/blog/apple-pay-expense-tracking) (fields, iOS 18–27 differences, refunds/transit/duplicates)
  - [Actuali: Wallet automation](https://actuali.mfazz.com/guides/wallet-automation/) (Run Immediately, timing and missing data)
  - [GitHub issue: PersonalFinanceTracker #231](https://github.com/RizzoG99/PersonalFinanceTracker/issues/231) (iOS 17+, Amount/Merchant/Card, amount format unresolved)
  - [Matthew Cassinelli: iOS 17 Transaction automation](https://matthewcassinelli.com/shortcuts-automations-ios-ipados-transaction-display-stage-manager/) (filters: card, category, merchant; pass types)
  - [Apple Developer Forums 765516: Transaction trigger timeouts](https://developer.apple.com/forums/thread/765516) (timeouts, fires on declined payments)
  - [Apple Developer Forums 850369: "Always Allow" not persisting for Wallet trigger](https://developer.apple.com/forums/thread/850369)
  - [iDropNews: iOS 17 Transaction shortcut](https://www.idropnews.com/ios-17/this-new-ios-17-shortcut-will-help-you-keep-track-of-your-spending/197589/), [Moneyfetch](https://moneyfetch.app/apple-pay-expense-tracking-shortcut/), [Moneko](https://moneko.io/blogs/how-to-automatically-log-apple-pay-transactions-iphone-shortcuts)
- Telegram Bot API (official spec, 10.4, Nov 9 2026): https://core.telegram.org/bots/api (callback_data 1–64 bytes; deleteMessage <48 h; sendPhoto ≤10 MB, caption ≤1024; button `style`; rich messages / RichBlockTable since 10.2; 30 msg/s broadcast)
- Budgeting app patterns:
  - [Copilot: Budget rollovers](https://help.copilot.money/en/articles/3790828-budget-rollovers), [Copilot: Excluded transactions](https://help.copilot.money/en/articles/9718801-excluded-transactions), [Copilot: Name rules](https://intercom.help/copilotmoney/en/articles/3971270-creating-name-rules)
  - [Monarch: Rollover budgets](https://help.monarch.com/hc/en-us/articles/4411119762196-Rollover-Budgets), [Monarch: Transaction rules](https://help.monarch.com/hc/en-us/articles/360048393372)
  - [Actual Budget: how it works](https://actualbudget.com/docs/budgeting/how-it-works)
- Telegram expense bots: [cheapskate](https://www.github.com/melax08/cheapskate), [MyDollarBot](https://github.com/mtkumar123/MyDollarBot), [DollarBot](https://github.com/ymdatta/DollarBot), [financebot (Go)](https://pkg.go.dev/github.com/Masynchin/financebot), [marbot](https://awesome.ecosyste.ms/projects/github.com%2Fwildandhya%2Fmarbot), [vc.ru overview](https://vc.ru/services/1747883-idealnyi-telegram-bot-dlya-ucheta-dohodov-i-rashodov)
- Open Banking:
  - [Enable Banking: linked accounts / restricted mode](https://enablebanking.com/docs/api/linked-accounts/), [Enable Banking Terms](https://enablebanking.com/terms), [Enable Banking changelog Dec 2025](https://enablebanking.com/blog/2026/01/15/enable-banking-changelog-december-2025), [Enable Banking: Poland market specifics](https://enablebanking.com/docs/markets/pl/)
  - [DEV.to: GoCardless BAD alternatives when signups are disabled](https://dev.to/johnfrandsen/gocardless-bank-account-data-alternatives-what-to-use-when-signups-are-disabled-326d), [GoCardless BAD announcement](https://gocardless.com/bank-account-data/announcement)
  - [DEV.to: cheapest open banking APIs for indie builders (Aug 2026, author-biased)](https://dev.to/johnfrandsen/the-cheapest-open-banking-apis-for-small-businesses-and-indie-builders-in-2026-5cab)
  - [Salt Edge Spectre](https://saltedge.com/products/spectre)

---
*Feature research for: multi-user Apple-Pay-fed expense-tracking Telegram bot (PL/PLN/RU)*
*Researched: 2026-11-14*
