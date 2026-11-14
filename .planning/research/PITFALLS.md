# Pitfalls Research

**Domain:** Multi-user personal expense tracker. Telegram bot plus an ASP.NET Core API, with Apple Pay transactions pushed in by the iOS Shortcuts "Transaction" automation. Poland, PLN, Europe/Warsaw timezone, Russian-language UI.
**Researched:** 2026-11-14
**Confidence:** MEDIUM overall. Telegram, EF Core and Npgsql facts are HIGH (checked against official docs). iOS Shortcuts behaviour is MEDIUM/LOW: Apple publishes no documentation for the Transaction trigger, so those findings come from Apple Developer Forums threads, Reddit and vendor help pages, and need an on-device spike.

> Phase names below refer to topic areas, not fixed numbers, so the roadmapper can map them:
> **Foundation** (solution skeleton, DB, money/time primitives, Docker, CI, secrets) ·
> **Ingestion** (Shortcut endpoint, tokens, idempotency, the Shortcut itself) ·
> **Bot Core** (webhook, /start, manual input, confirmation messages, inline buttons) ·
> **Categorization** ·
> **Limits & Balance** (weekly limit, carry-over, alerts, balance) ·
> **Reports** (/week, /month, charts, scheduled summaries) ·
> **Hardening & Launch** (deploy, GDPR, onboarding other users, README).

---

## Critical Pitfalls

### Pitfall 1: Trusting the Shortcut "Amount" to be a clean number

**What goes wrong:**
The Transaction trigger passes Amount as a currency-formatted value that follows the phone's region settings. On a Polish- or Russian-region iPhone the text looks like `23,50 zł` or `1 234,56 zł`. The group separator there is a no-break space (U+00A0) or a narrow no-break space (U+202F), not an ordinary space. Depending on how the value is put into the request body, the server may receive `23,50 zł`, `PLN 23.50`, `23.5`, or, if the Shortcut uses "Get Numbers from Input" or a Number-typed JSON field, a corrupted `2350` or `23` with the decimals gone. Community reports confirm that decimals get lost when Shortcuts converts comma-decimal text to a number, and that the way Amount is selected from Shortcut Input broke between iOS 17.2 and 17.4.

**Why it happens:**
Developers test with one phone in one locale and call `decimal.Parse(input)` with the server culture. The Docker container runs with the invariant culture, the dev Windows machine runs ru-RU or pl-PL, so the same code parses differently on each.

**How to avoid:**
- In the Shortcut, send the amount as **Text** (a raw string field in JSON). Never use a Number field or "Get Numbers from Input".
- Write one server-side `AmountParser` that strips currency symbols and codes (`zł`, `PLN`, `€`, `EUR`, `$`), removes every whitespace class (`char.IsWhiteSpace` plus U+00A0, U+202F, U+2009), detects the decimal separator from the last `,` or `.` followed by 1–2 digits, then calls `decimal.Parse(..., CultureInfo.InvariantCulture)`.
- Keep the original string (`RawAmount`) on the transaction row so you can debug and reprocess it.
- Unit-test with a table of real samples: `23,50 zł`, `zł 23,50`, `1 234,56 zł` (NBSP), `1 234,56 zł` (U+202F), `PLN 23.50`, `23`, `-23,50 zł`, `€5,00`, `5,00 EUR`, `0,99 zł`.
- Reject anything the parser can't handle: return HTTP 422 and have the bot tell the user "couldn't parse, here is the raw value". Never store 0.

**Warning signs:**
Amounts that are 100x too big or have lost their decimals. Every amount is a whole number. Parsing behaves differently on the dev machine and in the container.

**Phase to address:** Ingestion. The parser plus test table is a gate before the endpoint ships. Run an on-device spike first: log the raw payload the real Shortcut sends from your own iPhone with the PL region.

---

### Pitfall 2: Treating every Shortcut fire as a unique purchase (no source transaction ID)

**What goes wrong:**
The trigger gives you Card/Pass, Merchant, Amount and Name, and **no transaction ID**. Duplicates come from several places:
- **Declined then retried taps.** Forum reports say the trigger fires even when a transaction is declined. A failed tap followed by a successful one is a common situation at Polish terminals, and it produces two identical events.
- The user re-running a failed Shortcut by hand.
- Your own retry/queue logic in the Shortcut, if you add one (see Pitfall 3).
- Network timeouts: the server committed the row, but the response never reached the phone.

On the other hand, buying two identical 6,50 zł coffees a minute apart is legitimate, so you can't simply dedupe by (merchant, amount).

**Why it happens:**
"Idempotent" ends up implemented as "hash of the payload". That either drops legitimate repeats or misses retries that carry different timestamps.

**How to avoid:**
Use two layers:
1. **Hard idempotency (exact retries):** at the start of every run the Shortcut generates a client UUID (e.g. a "Text" action combining Current Date with milliseconds and a random number; Shortcuts has no UUID action, so build one) and sends it as `Idempotency-Key`. The server keeps a unique index on `(user_id, client_txn_id)` and uses `INSERT ... ON CONFLICT DO NOTHING`. On conflict it returns 200 with the **original** result, so retries get the same answer.
2. **Soft duplicate detection (declined + retry, double fire):** if a transaction for the same user, merchant and amount already exists within about 3 minutes, still record the new one but mark it `PossibleDuplicate`. The confirmation message then shows "Possible duplicate of 12:31 transaction" with a one-tap **Delete duplicate** button. Never silently drop it, because the core value says "without duplicates" and also "nothing gets lost".
- Integration-test both paths against real PostgreSQL. A race test should fire the same key concurrently, 10 times in parallel, and expect exactly 1 row.

**Warning signs:**
The weekly total doesn't match the bank app. Users delete the same merchant twice in a row. A unique-violation exception shows up in logs as a 500 instead of being handled as a 200.

**Phase to address:** Ingestion (hard key plus unique index). Bot Core (soft-duplicate UX and button).

---

### Pitfall 3: Assuming the automation always fires and the request always arrives

**What goes wrong:**
Transactions silently go missing, which breaks the core value. Known failure modes, all reported on Apple Developer Forums and in community posts:
- The trigger **times out waiting for the Wallet notification** ("Hit timeout waiting for transaction with identifier", FB14035016 / FB16379100). The issuer's notification can arrive hours late. Some reports involve Mastercard specifically.
- The trigger doesn't fire for some card/merchant combinations, or stopped working after an iOS 18 update.
- After an iOS update the automation can flip back to **"Ask Before Running"**, or get disabled after a restore or iCloud sync.
- Low Power Mode delays or skips background automations. "Summarize Notifications" for Wallet has been reported to break the trigger.
- **Online and in-app Apple Pay** (Bolt, Uber, Glovo, web shops) reportedly **do not trigger** it; only in-person Wallet taps do. Payments made from Mac or iPad don't trigger it either. Apple Watch payments have been inconsistent across iOS versions.
- **No network** at the moment of payment (metro, underground shop): "Get Contents of URL" fails, the Shortcut stops, and there is no built-in retry queue.
- **HTTP errors are not failures in Shortcuts.** "Get Contents of URL" returns the body for 4xx/5xx without stopping, so a server 500 looks like success on the phone.
- iOS never shows a "your automation did not run" notification.

**Why it happens:**
The happy path works on the developer's phone on Wi-Fi, so the system gets designed as if ingestion were reliable.

**How to avoid:**
- **Design for missing data from day 1.** Make manual entry (`45 такси`) first-class and fast; it is the fallback for everything above, not a secondary feature.
- **The bot confirmation is the user's receipt.** Every ingested transaction gets a Telegram message. Tell users during onboarding: "no message from the bot = not recorded, type it manually".
- **Make the Shortcut check the response.** The server returns a small JSON `{ "ok": true, ... }`. The Shortcut checks `ok` and shows a local notification "Not saved: <amount> <merchant>" otherwise. That notification is the user's cue to type it manually.
- **Optional offline queue (spike first):** the Shortcut appends each event to a file in the Shortcuts iCloud folder, then POSTs the whole pending batch; on success it clears the file. A failed run leaves the file intact and the next payment flushes it. Client UUIDs (Pitfall 2) make re-sending safe. This is MEDIUM complexity and Shortcuts error-halting behaviour needs on-device validation, so treat it as a v1.x enhancement and keep it out of the MVP gate.
- In the onboarding instructions, tell users to check the automation after iOS updates and to turn off notification summary for Wallet. Name online payments as a known gap so it doesn't come as a surprise.
- Server side: record `received_at` alongside the device-reported time. A weekly "no Apple Pay transactions from you in 5 days — is the automation still on?" nudge catches silent breakage.

**Warning signs:**
A user's ingestion volume drops to zero after an iOS update. The weekly total is well below the bank statement. Shortcut runs show up in History with errors.

**Phase to address:** Ingestion (response contract, Shortcut error notification, on-device spike). Bot Core (manual entry parity). Reports (inactivity nudge). Flag Ingestion for **deeper on-device research**.

---

### Pitfall 4: Leaking per-user tokens through the shared Shortcut, URLs and logs

**What goes wrong:**
- The developer builds the Shortcut with their own token hard-coded, shares the iCloud link, and every user posts into the developer's account. Or the link is posted in the README and anyone can write into it.
- The token goes in the query string (`/ingest?token=...`), so it lands in Caddy/nginx access logs, ASP.NET request logs, and error trackers.
- Users screenshot their Shortcut while asking for help, and the token is visible.
- The token sent in the Telegram chat stays in chat history forever.

**Why it happens:**
Shortcuts makes it easy to paste a value inline. iCloud shortcut links are public snapshots; you can't patch them afterwards, only publish a new link.

**How to avoid:**
- Publish a **template Shortcut with no token**, using Shortcuts **Import Questions** ("Set up this shortcut") so the user pastes their own token on import. Verify the shared link contains no secret before publishing; open it from a second Apple ID.
- Send the token in an `Authorization: Bearer <token>` **header**, never in the URL. Configure logging to redact the `Authorization` header.
- Generate 32 random bytes with `RandomNumberGenerator`, encoded base64url, with a readable prefix (`fb_...`) so leaks are greppable. Store **SHA-256(token)** in a unique index and look up by hash. Don't use bcrypt/Argon2: high-entropy tokens don't need slow hashing, and you can't index a salted slow hash for lookup.
- `/token` rotation invalidates the old hash immediately. Optionally delete the bot message containing the token once the user confirms setup.
- Rate-limit the ingest endpoint per IP and per token using the built-in ASP.NET Core rate limiter.

**Warning signs:**
Transactions show up under a user who didn't make them. Tokens appear in `docker logs` output. The token is visible in the README or in screenshots.

**Phase to address:** Ingestion (header auth, hashing, template Shortcut). Hardening & Launch (log redaction audit, link check).

---

### Pitfall 5: Week boundaries and scheduled jobs computed in UTC (DST bugs, Europe/Warsaw)

**What goes wrong:**
- "Week = Monday 00:00 to next Monday 00:00" computed as `start + TimeSpan.FromDays(7)` in UTC. The weeks containing the last Sunday of March (167 h) and the last Sunday of October (169 h) are off by an hour, so a Sunday-23:30 or Monday-00:30 purchase lands in the wrong week, and so does its limit.
- The "Sunday evening" summary cron is defined in UTC and drifts by an hour twice a year. The "1st of month" summary computed in UTC fires on the 31st at 23:00 or 22:00 local.
- `DateTime.Now` in a Docker container is UTC, while on the Windows dev machine it is local time, so tests pass locally and fail in prod.
- Npgsql 6+ **throws** when writing a `DateTime` with `Kind=Unspecified/Local` to `timestamptz` ("Cannot write DateTime with Kind=Unspecified..."). The "fix" people google, `EnableLegacyTimestampBehavior`, brings the ambiguity back.
- Chiseled .NET images (`*-chiseled`) contain **no tzdata**, so `TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")` throws in production only. You need the `-chiseled-extra` variant or a Debian-based image.
- The Shortcut sends "Current Date" as a localized string (`14 lis 2026 o 12:30`) unless it is explicitly formatted ISO 8601 with offset.
- The Shortcut runs when the Wallet notification arrives, which can be hours after the purchase, so device time is not exactly purchase time.

**Why it happens:**
Time looks simple. DST bugs show up only twice a year, and the first one usually appears months after launch.

**How to avoid:**
- Store instants as `timestamptz` (UTC). For bucketing, compute and **store a `LocalDate` column (Europe/Warsaw date of the purchase)** on each transaction, plus the user's timezone ID on the user row, default `Europe/Warsaw`. Week membership becomes `local_date BETWEEN monday AND sunday`, which is DST-proof and fast to query.
- Use **NodaTime** for domain time logic (`LocalDate`, `IsoDayOfWeek`, `DateTimeZoneProviders.Tzdb`, which bundles its own tzdb and doesn't depend on the OS) with the `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime` plugin. If NodaTime feels heavy, use `TimeProvider` (built-in) plus `TimeZoneInfo` and a single `WeekCalculator` service, but **never** call `DateTime.Now` anywhere. Inject the clock for tests.
- The Shortcut uses "Format Date → ISO 8601, include time". The server accepts it, then **clamps**: if the device time is in the future or more than 48 h old, use `received_at`. Let the user edit the date via the "Edit" button.
- Schedule summaries in **local time** (Quartz `InTimeZone`, or a homegrown loop that compares the user's local time), not with UTC cron.
- Tests: purchases at 2026-03-29 01:59/03:00 and 2026-10-25 02:30 (ambiguous hour), Sunday 23:59:59 and Monday 00:00:00 local, a week spanning a month boundary, and a week spanning a year boundary (ISO week 53).

**Warning signs:**
Any `DateTime.Now` or `DateTime.Today` in the codebase. Week totals change when you query at a different hour. "Kind=Unspecified" exceptions. Different results on Windows and Linux.

**Phase to address:** Foundation (time primitives, NodaTime/clock abstraction, DST test suite, image choice). Reports (timezone-aware scheduler).

---

### Pitfall 6: Floating-point money and sloppy percentage/rounding math

**What goes wrong:**
`double` sums drift (`0.1 + 0.2`), so a limit of 200.00 shows "spent 199.99999" or the 100% alert fires at 199.99. `Math.Round(decimal)` uses banker's rounding (`MidpointRounding.ToEven`) by default, so displayed values disagree with the user's mental math. Mapping `decimal` to Postgres without explicit precision gives unconstrained `numeric`, and computed carry-over values grow 20+ decimal places.

**How to avoid:**
- C# `decimal` everywhere, Postgres `numeric(12,2)` set explicitly in configuration (`HasPrecision(12,2)`). Alternatively `long` grosze; pick one and enforce it via a `Money` value object (EF Core complex type or value converter).
- Compare thresholds in integer or decimal arithmetic: `spent * 100 >= limit * 80`, not `spent / limit >= 0.8` on doubles.
- Round only at display time, with `MidpointRounding.AwayFromZero`. Format with an explicit culture: `ru-RU` produces `23,50 zł`. Put the currency suffix in yourself, not from the culture (ru-RU would print ₽).
- Refunds and negative amounts are their own type (`Refund`), not a negative `Expense` that silently reduces the weekly spend (see Pitfall 9).

**Warning signs:**
`double`/`float` in any entity or DTO. Unconstrained `numeric` in migrations. Snapshot tests showing `,4999999`.

**Phase to address:** Foundation (Money type plus DB mapping). Limits & Balance (threshold math tests).

---

### Pitfall 7: Carry-over and history math that rewrites the past (or freezes stale numbers)

**What goes wrong:**
- **Carry-over mode** makes week N+1's available limit depend on week N's spending. When the user edits or deletes a transaction from three weeks ago, re-categorizes something as out-of-limit, or backdates a manual entry, every later week's remaining amount shifts. That includes weeks whose Sunday summary has already been sent.
- **Changing the limit** (200 → 250) or **switching mode** recomputes all history with the new settings if the settings are stored as one current value.
- **Overspend spiral:** one big week (−400 zł) carried forward makes the next weeks negative before they start, and the user gives up.
- **Partial first week:** a user registering on Thursday gets a full 200 zł for 4 days, and the first carry-over is skewed.
- **Alert dedupe:** the 80% alert fires twice (two concurrent Apple Pay events), or never (a jump from 75% to 105% only sends the 100% alert, or neither if the logic checks `==`). After a deletion drops spending below 80% and it rises again, should the alert fire again? Undefined behaviour shows up as a bug report.
- **Balance as a mutable counter** (`user.Balance -= amount`): concurrent transactions lose updates, and editing or deleting a transaction has to remember to reverse the delta. Drift is guaranteed.

**Why it happens:**
Storing derived numbers (remaining, balance) as mutable columns feels efficient. Settings get modelled as one current value with no history.

**How to avoid:**
- **The ledger is the source of truth; everything else is derived.** Remaining limit, carry-over and balance are computed from transactions plus settings history. At this scale (hundreds of rows per user per month) computing on the fly is trivially cheap.
- **Settings history table:** `LimitSettings(user_id, effective_from_week, amount, mode)`. Each week uses the settings in force at that week. Changing the limit takes effect from the current week, never retroactively.
- **Carry-over chain** starts at the week the mode was enabled. Decide the policy on edits to past weeks explicitly. Recommended: allow edits, recompute live, and don't resend summaries; the next report reflects the change. Optionally cap carry-over, e.g. at ±1 week's limit, to prevent the spiral. Make it a documented setting, not an accident.
- **First week:** either pro-rate (`limit * remaining_days / 7`) or start limits from the next Monday. Pick one and show it in `/start`.
- **Alerts:** an `AlertsSent(user_id, week_start, threshold)` table with a unique constraint. Insert-on-conflict-do-nothing decides who sends. Check `>=` for every threshold crossed by a transaction, so 75→105 sends 100 (and optionally skips 80). Document whether alerts re-arm after deletions; recommended: no re-arm within the same week.
- **Balance** = latest `/balance` anchor + incomes after the anchor − expenses after the anchor (excluding transfers). An anchor is an event row, not an overwrite. Then edits and deletes need no reverse logic.
- Optimistic concurrency (Postgres `xmin` as a concurrency token) on the transaction row for button edits.

**Warning signs:**
Columns named `RemainingLimit`, `Balance` or `CarryOver` on mutable rows. "Recalculate" admin commands appearing. Unit tests that only cover the current week.

**Phase to address:** Limits & Balance. This needs a written spec of the rules (edit policy, first week, cap, re-arm) **before** coding; flag it for a discuss/spec step.

---

### Pitfall 8: Telegram webhook processing that duplicates, stalls or leaks between users

**What goes wrong:**
- **Retries:** if the webhook doesn't answer 2xx, Telegram re-sends the same update "and gives up after a reasonable amount of attempts" (official docs). A slow handler (chart rendering, DB) or a redeploy produces duplicate processing: double "45 такси" entries, double category changes.
- **Pending updates are kept up to 24 h.** `drop_pending_updates=true` on deploy throws away users' messages and button taps.
- **No webhook authentication.** Without `secret_token` anyone who learns the URL can POST fake updates, forging a `from.id` and writing into another user's data. Telegram does not sign payloads; the `X-Telegram-Bot-Api-Secret-Token` header is the only check.
- **Local dev vs prod:** `getUpdates` doesn't work while a webhook is set (official). Calling `deleteWebhook` for local polling with the prod token silently breaks prod. Use a separate dev bot.
- **Concurrency:** `max_connections` defaults to 40, so two button taps from the same user can be processed in parallel.
- **Group chats:** if the bot can be added to groups, financial replies get posted where others can see them.

**How to avoid:**
- Verify the `X-Telegram-Bot-Api-Secret-Token` header (constant-time compare) as the first middleware step, and put a random path segment in the webhook URL.
- Dedupe on `update_id`: a `ProcessedUpdates(update_id PK, processed_at)` table, insert-first, cleanup after 48 h. Return 200 quickly. Either process inline and keep it fast (no chart rendering in the webhook request), or enqueue to a `Channel<T>`/DB queue. Catch all exceptions and still return 200, logging the failure, so one poison update can't trigger endless retries.
- Config-driven update mode: **long polling in Development, webhook in Production**, with separate bot tokens (`@financebot_dev`).
- BotFather: disable "Allow Groups"; in code, ignore any `chat.type != private`.
- Use `long` for Telegram user and chat IDs. The official docs warn they exceed 32 bits (up to 52 significant bits), so `int` silently corrupts them.
- Library: **Telegram.Bot 22.x**. Most tutorials target v19–21 (`SendTextMessageAsync`), which is now `SendMessage`. Follow the official v22 migration guide, not old blog posts.

**Warning signs:**
`getWebhookInfo` shows a growing `pending_update_count` or `last_error_message`. Duplicate bot replies. An `int TelegramId` property.

**Phase to address:** Bot Core.

---

### Pitfall 9: Refunds, foreign currency and pre-authorizations treated as normal PLN expenses

**What goes wrong:**
- **Foreign currency:** paying €5 in Berlin, Wallet reports `5,00 €`. Treated as PLN, it understates spending about 4x. The PLN amount the bank charges is only known days later.
- **Refunds/returns:** no source confirms whether a refund fires the Transaction trigger, or with what sign. If one arrives as a positive amount, it is recorded as a new expense, so it gets double-counted.
- **Pre-authorizations:** fuel-pump, hotel and parking pre-auths report a hold amount that differs from the final charge. Tips added afterwards aren't reported.
- **Out-of-scope multi-currency** ("PLN only") makes people skip handling it entirely, instead of detecting and flagging it.

**How to avoid:**
- The parser extracts currency. **If it isn't PLN, store `OriginalAmount` and `OriginalCurrency`**. Then either convert with the NBP public rates API (free, official, Table A mid-rate), marking the transaction `Estimated`, or ask the user in the confirmation message "Enter the PLN amount", defaulting to the estimate. This stays within "PLN only": the ledger is PLN, the source currency is metadata.
- A negative amount or a `Refund` keyword is recorded as type `Refund`. Policy decision: refunds reduce the spend of the **week of the original purchase** if linked via a "this is a refund for…" button, otherwise the current week. Never store it as a positive expense.
- The confirmation message's **Edit** button is the universal escape hatch for pre-auths and tips. Make editing the amount a 2-tap flow.

**Warning signs:**
Payloads containing `€`, `$` or `EUR` and getting parsed as PLN. Negative amounts throwing exceptions in the parser.

**Phase to address:** Ingestion (detect and flag). Limits & Balance (refund semantics). A real on-device test of a refund, a foreign payment and a declined tap needs to be in the Ingestion spike.

---

### Pitfall 10: Callback buttons: the 64-byte limit, untrusted data and stale messages

**What goes wrong:**
- `callback_data` is limited to **1–64 bytes** (official). `cat:{txGuid}:{categoryGuid}` is 4 + 36 + 1 + 36 = 77 bytes, and Telegram rejects the whole message with `BUTTON_DATA_INVALID`, so the confirmation is never sent.
- **callback_data is client-controlled.** Modified clients can send arbitrary data, so `del:12345` for another user's transaction is an IDOR that breaks the "data fully isolated" requirement.
- **Stale buttons:** every confirmation message keeps working buttons forever. Taps on a 3-week-old message hit transactions that were deleted, re-categorized or already moved out of the limit. Editing a message with identical content returns `400 Bad Request: message is not modified` and becomes an unhandled exception. `deleteMessage` only works for messages under 48 h old.
- Forgetting `answerCallbackQuery` leaves the spinner on the button. The official docs say you must answer even with no notification.

**How to avoid:**
- Compact callback format: `v1|d|<txId base36>`, with integer/bigint IDs or a short public ID, and category codes as 1–2 char keys. Add a unit test asserting `Encoding.UTF8.GetByteCount(data) <= 64` for every keyboard builder, using max-length IDs.
- **Every callback handler loads the entity with `WHERE id = @id AND user_id = @callerUserId`.** Better: enforce it with an EF Core global query filter bound to the current user. Write an integration test where user B presses user A's callback data.
- Handlers are idempotent and state-aware: "already deleted", "already in category X". Always `answerCallbackQuery`. Catch "message is not modified" and treat it as success.
- Version prefix (`v1|`) so old buttons can still be parsed after you change the format.

**Warning signs:**
GUIDs in callback strings. Handlers that take an ID from callback data without filtering by user. Unanswered spinners in manual testing.

**Phase to address:** Bot Core (button builder, auth, idempotent handlers). Categorization (category picker keyboard sizes).

---

### Pitfall 11: Merchant strings are inconsistent; naive exact-match rules don't learn

**What goes wrong:**
The same shop shows up as `Żabka`, `ZABKA Z2834 K.1`, `ZABKA POLSKA SP. Z O.O.` or `Zabka`. Wallet sometimes enriches names via Apple Maps data and sometimes passes the raw card descriptor. Biedronka comes through as `JMP S.A. BIEDRONKA 1234`, Uber/Bolt as `BOLT.EU/O/2311...`, PayPal as `PAYPAL *SELLER`. An exact-match rule learned from one correction never fires again, so "the bot learns" fails visibly. Over-broad rules are a problem too: `BOLT` covers both rides (transport) and Bolt Food (food).

**How to avoid:**
- Normalize before matching: Unicode NFKD, strip diacritics (`ż→z`, `ł→l` needs an explicit mapping because NFKD doesn't decompose `ł`), uppercase, strip legal suffixes (`SP. Z O.O.`, `S.A.`, `SP.K.`), strip store numbers and trailing city tokens, collapse whitespace. Store both `MerchantRaw` and `MerchantNormalized`.
- Rule types: exact normalized match, then **prefix/contains** keyword (`ZABKA`, `BIEDRONKA`, `LIDL`, `ROSSMANN`, `ORLEN`), with priority **personal rule > global rule > Other**.
- When a user picks a category for a merchant, offer to **apply it retroactively** to their other "Other" items with the same normalized merchant.
- Seed rules from a real list of popular Polish chains, and test the normalizer with real descriptors collected from your own first 2–4 weeks of transactions.

**Warning signs:**
`/other` keeps filling with the same store under different spellings. The rule count grows linearly with transactions.

**Phase to address:** Categorization. Collect real descriptors during Ingestion so the normalizer is built from data, not guesses.

---

### Pitfall 12: Chart rendering that works on Windows and breaks in Linux Docker

**What goes wrong:**
- `System.Drawing.Common` is Windows-only since .NET 6, yet many tutorials still use it. It throws `PlatformNotSupportedException` on Linux.
- SkiaSharp (also the engine under ScottPlot 5) needs native `libSkiaSharp.so` plus `libfontconfig`. Without the right NativeAssets package you get `DllNotFoundException: libSkiaSharp`. A mismatch between the managed SkiaSharp version and the NativeAssets version causes "incompatible native library" errors.
- **Cyrillic shows up as boxes (tofu).** Slim and chiseled images ship with no fonts, and Skia does **not** fall back to another typeface for missing glyphs. Category names like "Продукты" render as `□□□□□□□□`.
- Alpine (musl) and ARM64 VPS (cheap Hetzner CAX) need matching native assets.

**How to avoid:**
- Use **ScottPlot 5** (or raw SkiaSharp) + `SkiaSharp.NativeAssets.Linux.NoDependencies`, pinned to the same version as the SkiaSharp that ScottPlot references.
- **Bundle a font file** (e.g. Noto Sans or Inter with Cyrillic, OFL-licensed) as an embedded resource or copied file. Load it explicitly (`SKTypeface.FromStream`/ScottPlot's custom font registration) and set it on every label. Don't rely on system fonts, so Windows and Linux render identically.
- Use a Debian-based `aspnet` image (not Alpine) unless you deliberately go chiseled; with chiseled, use `-extra` and copy the font in.
- Add a **CI smoke test that renders a chart with Cyrillic labels inside the production Docker image** and checks for a non-empty PNG. Optionally compare a pixel sample against a reference, or render a single glyph and assert it isn't the .notdef box.

**Warning signs:**
The chart works in `dotnet run` on Windows but fails in `docker run`. Squares in the labels. `DllNotFoundException` in container logs.

**Phase to address:** Reports. Better: put a 1-hour chart spike in Foundation, since the Docker image choice depends on it.

---

### Pitfall 13: Scheduled summaries that get missed, double-sent, or hit rate limits

**What goes wrong:**
- An in-process `BackgroundService` with `Task.Delay` until Sunday 20:00: a deploy or restart at 19:59 means **no summary**, and restarting after sending means **a second summary**.
- Sending 50 users' summaries plus chart photos in a tight loop breaks Telegram's limits. Official FAQ: about **1 message/sec per chat**, about **30 messages/sec** overall for bulk. The bot gets `429` with `retry_after` and drops messages.
- Users who blocked the bot return `403 Forbidden: bot was blocked by the user` on every attempt, forever, and the job crashes or retries endlessly.
- The monthly summary needs the 1st in **local** time (see Pitfall 5).

**How to avoid:**
- An idempotent "outbox" style scheduler: a `ReportDeliveries(user_id, kind, period_key)` unique row, e.g. `(42, 'week', '2026-W46')`. A loop running every minute finds users whose local time is past their send time and whose row is missing, inserts the row (claim), and sends. Restarts catch up automatically and duplicates can't happen. Quartz.NET with a persistent store, or Hangfire on Postgres, also work, but the simple table is easier to test and explain in a portfolio.
- Throttle sends (e.g. `System.Threading.RateLimiting` token bucket, around 20 msg/s globally). Honour `retry_after` on 429.
- On 403 "blocked", set `user.IsActive = false` and skip the user until they `/start` again.
- Don't send summaries for empty weeks, or send a short one; decide explicitly.

**Warning signs:**
Summaries arriving twice after deploys. Gaps in the summary history. 429s in logs.

**Phase to address:** Reports.

---

### Pitfall 14: Data isolation enforced by convention instead of by construction

**What goes wrong:**
Multi-tenancy where each query "remembers" `.Where(t => t.UserId == userId)`. One forgotten filter, in a report, a callback or the `/other` list, leaks one person's finances to another. Also: EF Core global query filters capture a value at model-build time if written incorrectly (a closure over a local variable instead of a DbContext property), so every user sees the first user's data. `IgnoreQueryFilters()` and raw SQL bypass filters silently.

**How to avoid:**
- An `ICurrentUser` scoped service, set from the authenticated token (ingest) or `from.id` (bot). The DbContext exposes `CurrentUserId` as an instance property, and `HasQueryFilter(e => e.UserId == CurrentUserId)` applies to every user-owned entity.
- Background jobs (summaries) run with an explicit per-user scope, never with filters disabled globally.
- Ban `IgnoreQueryFilters` outside a reviewed allowlist (an analyzer or grep check in CI).
- **Isolation integration tests:** seed users A and B, then run every query handler and callback as B against A's IDs and assert nothing is returned or changed.
- Consider Postgres Row-Level Security as a second layer. It looks good in a portfolio, but it's optional; the filter plus tests is enough for v1.

**Warning signs:**
`.Where(x => x.UserId == ...)` scattered through handlers. No test project for isolation.

**Phase to address:** Foundation (filter infrastructure). Every later phase adds its handlers to the isolation test suite.

---

### Pitfall 15: Bot message formatting crashes on merchant names (MarkdownV2)

**What goes wrong:**
MarkdownV2 requires escaping `_ * [ ] ( ) ~ \` > # + - = | { } . !` (official list). Merchant names like `JMP S.A. BIEDRONKA`, `MC-DONALD'S`, `Kebab (Mokotów)` and every amount with a `.` break parsing. Telegram returns `400 can't parse entities` and **the confirmation is never sent**, which looks exactly like "transaction lost".

**How to avoid:**
Use **`ParseMode.Html`** and `WebUtility.HtmlEncode` (or a minimal `&`, `<`, `>` escaper) for every user- or merchant-derived string. Centralise all bot text in a message-builder class with snapshot tests that include hostile merchant names. If sending with formatting fails, retry as plain text so the confirmation still gets through.

**Phase to address:** Bot Core.

---

### Pitfall 16: GDPR treated as "not my problem" once other people use the bot

**What goes wrong:**
For the developer alone, the household exemption arguably applies. The moment friends or recruiters register, the operator becomes a **data controller** for other people's financial behaviour: what they buy, where, when. The household exemption is read narrowly and doesn't cover running a service for others. The project is in Poland, so the supervisory authority is UODO. Typical gaps: no privacy notice, no way to delete data, logs and backups full of merchants and amounts, a VPS outside the EEA, and real user data in the public README or demo.

**How to avoid (proportionate, not lawyer-grade):**
- `/privacy`: a short notice covering what is stored (Telegram ID, name, amounts, merchants, card label), why, where (EU VPS), retention, and contact.
- `/deleteme`: hard-delete all of the user's data (cascade), with a confirmation button. This is the right to erasure. `/export` to CSV is a cheap right-to-access implementation and a nice feature.
- Data minimisation: store the card **label** only. Don't log request bodies. Structured logs carry IDs, not merchants or amounts.
- Host in the EU (Hetzner/OVH/Contabo EU). Encrypt backups. Keep the DB port off the public internet.
- Demo/README uses a **seeded fake user**, never real screenshots.
- Limit v1 to an invite-only/closed group (e.g. an `/start <invite-code>` gate or allowlist). That keeps blast radius and obligations small.
- Note: this is engineering guidance, not legal advice. Confidence MEDIUM.

**Phase to address:** Hardening & Launch. `/deleteme` and log hygiene are cheap, so build them alongside Bot Core.

---

## Moderate Pitfalls

### Shortcut distribution and versioning
**What goes wrong:** You change the payload (add a field, rename `amount`→`rawAmount`). Every user's installed Shortcut is a frozen copy, and iCloud links can't be updated in place, so old clients break silently.
**Prevention:** Version the endpoint (`/api/v1/transactions`). Include `shortcutVersion` in the payload. Accept old shapes for a long time. The bot tells users with an outdated version to reinstall, via a link from `/setup`. Keep the Shortcut export (`.shortcut` file) and screenshots in the repo under `/docs/shortcut`.

### Trigger card selection
**What goes wrong:** The Transaction automation fires only for the cards selected when it was created. A new bank card added to Wallet later (e.g. the user switches from PKO to mBank) isn't covered. *(MEDIUM/LOW confidence. Verify on device.)*
**Prevention:** Include in onboarding instructions and the `/help` FAQ. The inactivity nudge (Pitfall 3) catches it.

### Manual input parsing ambiguity
**What goes wrong:** `45 такси`, `45,50 такси`, `45.5 такси`, `такси 45`, `2 кофе 30`, `+5000 зп`, `320 куртка !`, `1 200 диван`, and a stray `!` inside a word. Regex grows uncontrolled and mis-parses ("2 кофе 30" becomes 2 zł).
**Prevention:** Define a small grammar: optional leading `+` (income), then an amount token at the **start** (with `,` or `.` decimals and optional thin-space groups), then free-text description, then an optional trailing standalone `!`. Anything else gets a helpful error with examples. Keep a table-driven test of 30+ cases. Reuse the same `AmountParser` as ingestion.

### EF Core migrations in production
**What goes wrong:** A rename generated as drop + add column means data loss. `EnsureCreated` combined with `Migrate` fails. Since EF Core 9, `Migrate()` **throws when the model has pending changes**, so a forgotten migration crashes the container at startup in prod.
**Prevention:** Review every generated migration. Run `dotnet ef migrations has-pending-model-changes` in CI. Ship a **migration bundle** (`dotnet ef migrations bundle`) run as a one-shot step in the deploy pipeline before the app container starts. This is Microsoft's recommended automated approach; runtime `MigrateAsync()` is acceptable for a single-instance VPS since EF 9 added migration locking, but the bundle is the stronger portfolio signal and keeps the app's DB user least-privileged. Back up (`pg_dump`) before each migration.

### Npgsql DateTime Kind errors
**What goes wrong:** `Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'`. Developers flip `Npgsql.EnableLegacyTimestampBehavior`.
**Prevention:** Never enable legacy behaviour. All instants are UTC (or NodaTime `Instant`). Add a test that round-trips an entity through real Postgres via Testcontainers.

### HTTPS and webhook prerequisites
**What goes wrong:** Shortcuts refuses self-signed certs. Telegram webhooks only accept ports **443, 80, 88, 8443** (official). Certificate renewal fails silently after 90 days and both ingestion and the bot die at once.
**Prevention:** Caddy reverse proxy with automatic Let's Encrypt in docker-compose. Use a real domain (a cheap or free DDNS name works). A `/health` endpoint plus an external uptime monitor (free UptimeRobot/Healthchecks.io) that alerts you on Telegram.

### Secrets in the public portfolio repo
**What goes wrong:** Bot token in `appsettings.json` or `.env` gets committed. Once pushed, rewriting history doesn't help; the token has to be revoked. A leaked bot token lets anyone read all incoming user messages, which is every user's finances.
**Prevention:** `dotnet user-secrets` in dev. Environment variables or Docker secrets in prod, injected from GitHub Actions secrets. Add `.env` and `appsettings.*.local.json` to `.gitignore` from commit #1, with **gitleaks** in a pre-commit hook and a CI job. On leak, revoke via BotFather `/revoke`, rotate the DB password, then clean history. Set up `.gitignore` and gitleaks **before the first commit**.

### Concurrent updates for the same user
**What goes wrong:** An Apple Pay event and a button tap, or two quick taps, race and give lost updates on a category, or two alerts.
**Prevention:** Unique constraints for "exactly once" facts (alerts, deliveries, idempotency keys). `xmin` concurrency token on mutable rows. Catch `DbUpdateConcurrencyException`, reload, and reply with the current state.

---

## Minor Pitfalls

### "Run Immediately" plus "Notify When Run" noise
**What goes wrong:** Users turn off notifications and then never see the Shortcut's failure notification either. Or they leave "Notify When Run" on and get double notifications (iOS + bot).
**Prevention:** The onboarding guide specifies the exact toggles: Run Immediately ON, Notify When Run OFF, and the Shortcut's own failure notification stays.

### Russian plurals and number formatting in messages
**What goes wrong:** "1 трат", "5 траты"; `23.5 zł` instead of `23,50 zł`.
**Prevention:** A small Russian plural helper (one/few/many). Format with an explicit `ru-RU` culture and a manual ` zł` suffix. Snapshot tests.

### Telegram user ID vs username
**What goes wrong:** Keying users by `@username`, which can change or be empty.
**Prevention:** Key on `from.id` (`long`). Username is display-only.

### Category deletion and renaming
**What goes wrong:** Deleting a category orphans transactions and rules. Renaming a built-in category for one user changes it for all.
**Prevention:** Soft-delete or reassign to "Other". Built-in categories are global templates; per-user overrides are separate rows.

### Bot command menu drift
**What goes wrong:** `/commands` in BotFather get out of sync with the code.
**Prevention:** Call `SetMyCommands` at startup from the code's command registry.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| `double` for money | Simpler math | Rounding drift, wrong alerts, rewriting every query | Never |
| Mutable `Balance`/`Remaining` columns | Easy reads | Drift, lost updates, recalculation scripts | Never. Compute from the ledger (cache later if ever needed) |
| Dedupe only by payload hash | Quick idempotency | Drops legit repeats or misses retries | Never. Use client key plus soft-dup flag |
| `Database.MigrateAsync()` at startup | One-step deploy | Crashes on pending changes, app needs DDL rights | Acceptable in MVP on single-instance VPS (EF 9+ locking). Move to a bundle before inviting other users |
| In-memory `BackgroundService` timers for summaries | 20 lines of code | Missed or duplicate summaries on restart | Only during MVP with yourself as sole user |
| Polling in production | No HTTPS needed for the bot | Second hosting model, doesn't show webhook skills | Never in prod (HTTPS is needed for Shortcuts anyway). Polling is fine for dev |
| Hard-coding Europe/Warsaw everywhere | Fewer columns | Can't support a user who moves or travels | Acceptable if centralised in one `UserTimeZone` lookup with Warsaw as default |
| Skip the settings-history table (one current limit) | Simpler schema | Changing the limit rewrites history and carry-over | Only if carry-over mode ships later than the basic limit |
| Microservices / message broker / CQRS+MediatR+event sourcing | "Impressive" architecture | Weeks of plumbing, nothing shipped, harder to explain in an interview | Never for v1. A modular monolith with clean layering is the stronger portfolio story |

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| iOS Shortcuts → API | Amount sent as Number, token in URL, no response check | Amount as Text, `Authorization` header, client UUID, check `ok` and notify on failure |
| iOS Shortcuts → API | Date sent as localized string | Format Date ISO 8601 with offset; server clamps to `received_at` |
| Telegram webhook | No `secret_token`; slow handlers; `drop_pending_updates` on deploy | Verify header, dedupe `update_id`, ack fast, keep pending updates |
| Telegram webhook | Using prod token for local polling (deletes prod webhook) | Separate dev bot token; polling only in Development |
| Telegram sendMessage | MarkdownV2 with unescaped merchant names | HTML parse mode + HtmlEncode; plain-text fallback |
| Telegram inline keyboards | GUIDs in callback_data (>64 bytes); no ownership check | Compact versioned format, byte-length test, user-scoped lookup |
| Telegram sending | No 429/403 handling | Throttle, honour `retry_after`, deactivate on "blocked by the user" |
| PostgreSQL via Npgsql | `DateTime.Now`/Unspecified kinds; legacy timestamp switch | UTC/NodaTime only; Testcontainers round-trip tests |
| SkiaSharp/ScottPlot in Docker | Relying on system fonts; version-mismatched native assets | Bundled Cyrillic font, pinned NoDependencies assets, in-container render test |
| NBP rates API (if used for foreign txns) | Using today's rate for a weekend purchase (NBP publishes only on business days) | Use the last published Table A rate on or before the purchase date; mark the transaction as estimated |

## Performance Traps

Expected scale is about 1–50 users with tens of transactions per week each. Most traps don't apply; these do.

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Rendering charts inside the webhook request | Telegram retries, duplicate replies | Render in a background worker; send "preparing report..." first if needed | Any time a render takes more than a few seconds (first Skia call is slow, cold start) |
| Sending all summaries at the same second | 429s, missing summaries | Token-bucket throttle, spread sends over minutes | ~30+ users at once (official ~30 msg/s bulk limit; sendPhoto counts too) |
| Recomputing carry-over from the beginning of time on every message | Slow `/week` after a year | Index `(user_id, local_date)`; compute only from the mode start week; cache weekly aggregates only if measured slow | Years of data per user. Unlikely in v1, don't pre-optimise |
| No index on `(user_id, local_date)` / `(user_id, client_txn_id)` | Slow queries, slow idempotency checks | Add both in the first migration | Thousands of rows |
| Unbounded `ProcessedUpdates` table | Table grows forever | Delete rows older than 48 h in a cleanup job | Months of operation |

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Template Shortcut published with a real token embedded | Anyone can write into that account | Import Questions; verify the link from a second Apple ID before publishing |
| Ingest token in query string | Token in proxy/app logs | Bearer header; redact `Authorization` in logs |
| Tokens stored in plaintext | DB leak equals write access to every account | SHA-256 hash with a unique index; show the token once; `/token` rotation |
| Webhook without `secret_token` | Forged updates impersonate any user | Verify `X-Telegram-Bot-Api-Secret-Token` in constant time |
| Trusting `callback_data` IDs | Cross-user read/modify/delete (IDOR) | User-scoped lookups, global query filter, isolation tests |
| Bot allowed in groups | Financial replies visible to group members | Disable groups in BotFather; ignore non-private chats |
| Bot token committed to public repo | Attacker reads all users' incoming messages | gitleaks from first commit; revoke immediately on leak |
| Logging request bodies and merchant/amount data | GDPR exposure, leaky logs | Structured logs with IDs only; no body logging on ingest/webhook |
| Postgres port exposed on VPS | Brute force / data theft | Docker internal network only; no published 5432 port; firewall (ufw) |
| Open registration from day 1 | Unknown users' financial data, abuse, GDPR scope | Invite code / allowlist for v1 |

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Silent failure when ingestion fails | User trusts a wrong "remaining" number, which defeats the core value | Every success gets a bot message; every failure gets a Shortcut notification; inactivity nudge |
| Confirmation message overloaded with buttons | Visual noise on every coffee | One compact line plus 3–4 buttons. Category picker only for "Other" and on demand |
| Carry-over overspend spiral | Week starts at −150 zł, user gives up | Optional cap, clear message "you started the week with −X due to last week" |
| 80% and 100% alerts as separate extra messages after the confirmation | Notification spam | Fold the alert into the confirmation message (bold warning line) unless it is the scheduled summary |
| Confusing "out of limit" (`!`) semantics | User can't tell why the limit didn't move | Confirmation explicitly says "вне лимита, лимит не изменился" |
| Partial first week with a full limit | Fake success, distorted carry-over | Pro-rate or start next Monday; explain in `/start` |
| Asking for a category for every unknown merchant immediately | Interruptions while paying in a queue | Record to "Other" immediately (already planned), ask with non-blocking buttons, plus `/other` batch triage |
| Long setup instructions for the Shortcut | Friends abandon onboarding | Step-by-step with screenshots or a short video, a test endpoint ("send a test transaction, the bot will reply"), `/setup` re-runnable |

## "Looks Done But Isn't" Checklist

- [ ] **Shortcut ingestion:** tested on a real iPhone with **PL region** and with **RU language**, amounts ≥ 1 000 zł (group separator), sub-1 zł, a declined tap, a foreign-currency payment, airplane mode during payment, and Apple Watch.
- [ ] **Idempotency:** concurrent duplicate POST test (10 parallel, same key → 1 row, all 200 with the same body).
- [ ] **Shortcut failure path:** server returns 500 or 401 and the phone shows a "not saved" notification.
- [ ] **Week math:** tests across both 2026/2027 DST transitions, Sunday 23:59 / Monday 00:00 local, month and year boundaries.
- [ ] **Carry-over:** test that editing a transaction 2 weeks back changes this week's remaining amount as specified, and that changing the limit doesn't rewrite past weeks.
- [ ] **Alerts:** jump from 75% to 105% in one transaction; two concurrent transactions crossing 80% → exactly one alert.
- [ ] **Callback buttons:** byte-length test on every keyboard; user B pressing user A's callback → rejected; pressing on deleted transaction → friendly answer; spinner always cleared.
- [ ] **Message formatting:** merchant names with `. - ( ) ! _ * < > &` render correctly.
- [ ] **Charts:** rendered **inside the production Docker image** in CI, Cyrillic labels visible (no boxes).
- [ ] **Timezone in container:** `Europe/Warsaw` resolves in the prod image (no `TimeZoneNotFoundException`).
- [ ] **Scheduled summaries:** restart the container at the scheduled minute → exactly one summary; a user who blocked the bot → marked inactive, job continues.
- [ ] **Webhook:** request without/with wrong secret header → 401; replayed `update_id` → no double effect; handler exception → still 200 and logged.
- [ ] **Isolation:** automated test runs every handler as a foreign user.
- [ ] **Migrations:** CI runs `has-pending-model-changes`; deploy applies the bundle before starting the app; a `pg_dump` backup cron exists and **a restore was tested once**.
- [ ] **Secrets:** gitleaks passes on full history; README screenshots contain only seeded fake data.
- [ ] **GDPR basics:** `/privacy` and `/deleteme` work; logs contain no merchants or amounts.

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Bot token leaked | LOW | BotFather `/revoke`, update secret, redeploy, `setWebhook` again; audit git history with gitleaks |
| Ingest token leaked | LOW | User runs `/token`; old hash invalidated; user re-runs Shortcut import questions |
| Amounts mis-parsed for a period | MEDIUM | Re-parse from the stored `RawAmount` column with the fixed parser (one-off job + notify affected users). **Only possible if raw is stored**, which is why it's mandated |
| Duplicates ingested | LOW | Soft-dup flags let the user delete them; admin query finds same merchant/amount within N minutes |
| Week boundaries wrong around DST | MEDIUM | Recompute `LocalDate` from `OccurredAt` UTC plus timezone with a fixed calculator (data migration); derived totals fix themselves because they're computed |
| Stored balance/remaining drifted | HIGH if stored, LOW if derived | Derived design means nothing to recover. With stored counters you need a full replay and the users' trust is already damaged |
| Missed weekly summary | LOW | Delivery table plus catch-up loop sends it late automatically |
| Destructive migration applied | HIGH | Restore from the `pg_dump` taken before migration; that is why the pre-migration backup step is mandatory |
| Chart rendering broken in prod | LOW | Reports degrade to text-only (catch render exceptions, send text), fix the image, redeploy |

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| 1. Amount parsing/locale | Ingestion (spike in Foundation) | Table-driven parser tests built from real on-device payloads |
| 2. Duplicates / idempotency | Ingestion + Bot Core | Concurrent POST test; soft-dup button E2E |
| 3. Automation not firing / offline | Ingestion (spike) + Bot Core + Reports | Shortcut failure notification demo; manual entry parity; inactivity nudge |
| 4. Token leakage | Ingestion + Hardening | Shared link verified token-free; logs grep for `fb_` prefix returns nothing |
| 5. Time/DST/Npgsql kinds | Foundation | DST test suite green; prod image resolves Europe/Warsaw |
| 6. Money types/rounding | Foundation | No double/float in domain (analyzer/grep); `numeric(12,2)` in migration |
| 7. Carry-over/history/alerts/balance | Limits & Balance (spec first) | Written rules doc + tests for edit-past-week, limit change, alert jumps |
| 8. Webhook retries/auth/dev-prod | Bot Core | Secret header test; replay test; separate dev bot documented |
| 9. Refunds/FX/pre-auth | Ingestion (detect) + Limits & Balance (semantics) | Foreign and negative payloads flagged, not counted as plain PLN |
| 10. Callback data limits/IDOR/stale | Bot Core + Categorization | Byte-length tests; cross-user callback test |
| 11. Merchant normalization | Categorization | Real-descriptor test set; rule hit rate on own data |
| 12. Charts in Docker / Cyrillic | Foundation spike + Reports | In-container render smoke test in CI |
| 13. Scheduler reliability/rate limits | Reports | Restart-at-send-time test; 403 handling test |
| 14. Data isolation | Foundation (infra) + every phase | Isolation test suite extended per phase |
| 15. MarkdownV2 escaping | Bot Core | Hostile-merchant snapshot tests |
| 16. GDPR | Bot Core (`/deleteme`, log hygiene) + Hardening & Launch | `/privacy`, `/deleteme` work; invite gate in place |
| Secrets in repo | Foundation (before first commit) | gitleaks in pre-commit and CI |
| EF migrations in prod | Foundation (pipeline) + Hardening | Bundle step in GitHub Actions deploy; backup before migrate |

## Research Flags for the Roadmap

- **Ingestion needs an on-device spike before planning:** the exact Amount string per region/language, the Date format, behaviour for declined/refund/foreign/Watch/online payments, offline error-halting, and whether Import Questions can hold the token. None of this is documented by Apple; everything above about Shortcuts is community-sourced (MEDIUM/LOW).
- **Limits & Balance needs a written rules spec** (carry-over edits, first week, cap, alert re-arm, refunds) before implementation. These are product decisions, not technical ones.
- **Reports:** a 1-hour chart-in-Docker spike early (Foundation) de-risks the image choice.
- Standard patterns, no extra research needed: EF Core/Npgsql setup, Telegram.Bot webhook wiring, Testcontainers, GitHub Actions deploy.

## Sources

**Official (HIGH):**
- Telegram Bot API (core.telegram.org/bots/api), checked 2026-11-14 (Bot API 10.4): webhook retry "reasonable amount of attempts", 24 h update retention, `secret_token` header, ports 443/80/88/8443, `callback_data` 1–64 bytes, `answerCallbackQuery` requirement, MarkdownV2 escape list, deleteMessage 48 h limit, IDs up to 52 bits.
- Telegram Bots FAQ (core.telegram.org/bots/faq): 1 msg/s per chat, 20 msg/min per group, ~30 msg/s bulk, 429 behaviour, no getUpdates while webhook set.
- Microsoft Learn: EF Core "Applying Migrations" (updated 2026-08): bundles recommended, runtime migration tradeoffs, EF 9 migration locking, `PendingModelChangesWarning` throws, `has-pending-model-changes`.
- Microsoft Learn / dotnet-docker distroless docs: chiseled images lack tzdata/ICU; `-extra` variant includes them.
- Npgsql docs and EF Core 6.0 release notes: DateTime Kind mapping to timestamptz, legacy timestamp switch.
- Telegram.Bot v22 migration guide (telegrambots.github.io/book/migrate/Version-22.x.html): Async suffix removal, `SendTextMessageAsync` → `SendMessage`.

**Community / forum (MEDIUM–LOW):**
- Apple Developer Forums 765516, 773745, 758053, 797233: Transaction trigger timeouts waiting for Wallet notification, iOS 18 regressions, fires on declined transactions, Mastercard pattern, Summarize Notifications workaround (FB14035016, FB16379100).
- r/shortcuts threads (16vbk91, 17dqih9, 1cc9tqg): Amount selection broke between iOS 17.2 and 17.4, setup workarounds.
- Apple Community thread 253827696: comma-decimal keypad, decimals lost on text→number conversion.
- Vendor help pages (travel-spend, finerd, Finny blog): online/in-app Apple Pay doesn't trigger; Run Immediately reset after iOS updates; Low Power Mode; no "did not run" notification.
- Lunch Money / Budget Flow feedback boards: locale separator bugs and foreign-currency misreads in Shortcut-based trackers.
- Telegram duplicate-processing write-ups (dev.to, Telegraph docs): dedupe by `update_id` with unique index.
- SkiaSharp/Aspose/Syncfusion/OxyPlot/ScottPlot forums: libSkiaSharp/fontconfig in Docker, NoDependencies package, native version mismatch, no glyph fallback leading to Cyrillic boxes, ScottPlot custom typeface.
- GDPR commentary (Chambers "One-man controller", freevacy, sota.io): household exemption narrow, platform operator is controller.

**Inferred from domain knowledge (flagged MEDIUM/LOW in text):** Polish merchant descriptor formats, pre-authorization amounts, card-selection scope of the trigger, refund sign behaviour, NBP weekend rate gaps.

---
*Pitfalls research for: Telegram + iOS Shortcuts expense tracker (C#/.NET, PLN, Europe/Warsaw)*
*Researched: 2026-11-14*
