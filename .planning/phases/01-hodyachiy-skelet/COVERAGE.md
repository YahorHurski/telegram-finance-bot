# API Coverage — Telegram Bot API (Telegram.Bot 22.10.4)

> Full coverage by default. Opt-outs are explicit, reasoned decisions.
> Phase 1 (walking skeleton): бот только отправляет сообщения владельцу (ROADMAP Phase 1 Notes). Остальные возможности закреплены за фазами, где они нужны.

| capability | decision | reason |
|---|---|---|
| sendMessage | INTEGRATE | |
| parse_mode HTML (форматирование текста) | INTEGRATE | |
| getMe | OPT-OUT | not needed — токен проверяется реальной отправкой при деплое (план 01-06) |
| getUpdates (long polling) | OPT-OUT | not needed yet — приём сообщений появляется в Phase 3 (ACC-07, MAN-01) |
| setWebhook / deleteWebhook / getWebhookInfo | OPT-OUT | not needed yet — вебхук с секретным заголовком в Phase 3 (ACC-07) |
| reply_markup inline keyboards + answerCallbackQuery | OPT-OUT | not needed yet — кнопки под тратой в Phase 4 (MAN-04, CAT-02) |
| editMessageText / editMessageReplyMarkup | OPT-OUT | not needed yet — обновление подтверждения на месте в Phase 4 (MAN-04, MAN-06) |
| deleteMessage | OPT-OUT | not needed yet — undo и правки в Phase 4 рассматриваются там |
| sendPhoto | OPT-OUT | not needed yet — PNG-диаграммы в Phase 6 (REP-03) |
| sendDocument | OPT-OUT | explicitly out of scope — экспорт CSV отложен в v2 (EXP-01) |
| setMyCommands / getMyCommands | OPT-OUT | not needed yet — команды бота появляются с Phase 3 |
| sendChatAction | OPT-OUT | not needed — ответы мгновенные |
| forwardMessage / copyMessage | OPT-OUT | explicitly out of scope — продукту не нужно |
| getChat / getChatMember | OPT-OUT | not needed yet — проверка «только личные чаты» в Phase 3 (ACC-07) по типу чата из update |
| chat administration (ban/promote/pin/permissions) | OPT-OUT | explicitly out of scope — групповые чаты вне scope (REQUIREMENTS Out of Scope) |
| polls / location / contact / venue / dice | OPT-OUT | explicitly out of scope — продукту не нужно |
| sendMediaGroup / sendVideo / sendAudio / sendVoice / sendAnimation | OPT-OUT | explicitly out of scope — бот шлёт только текст и (в Phase 6) PNG |
| inline mode (answerInlineQuery) | OPT-OUT | explicitly out of scope — бот работает только в личных чатах |
| payments / Telegram Stars (sendInvoice и др.) | OPT-OUT | explicitly out of scope — платёжных функций нет |
| stickers / games / gifts / stories | OPT-OUT | explicitly out of scope — продукту не нужно |
| business connections / Telegram Passport | OPT-OUT | explicitly out of scope — продукту не нужно |
| Web Apps / Mini Apps | OPT-OUT | explicitly out of scope — веб-дашборд отложен в v2 (WEB-01) |
