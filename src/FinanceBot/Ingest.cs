using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

static class Ingest
{
    public static async Task<IResult> Handle(HttpRequest req, AppDb db, IClock clock,
        ITelegramBotClient bot, IOptions<BotOptions> opt, ILogger<Program> log)
    {
        // Token first, before the body is read (D-06)
        if (!TokenMatches(req.Headers.Authorization.ToString(), opt.Value.IngestToken))
            return Results.Unauthorized();

        // Body read by hand, not bound to a parameter: it is stored verbatim even when it is not valid JSON (D-07)
        using var reader = new StreamReader(req.Body);
        var raw = await reader.ReadToEndAsync();
        Payload? p = null;
        try { p = JsonSerializer.Deserialize<Payload>(raw, JsonSerializerOptions.Web); }
        catch (JsonException) { }   // malformed JSON or amount as a number: saved as "not parsed" (D-10), body kept

        var now = clock.GetCurrentInstant();
        var tx = new Transaction
        {
            Id = Guid.CreateVersion7(), UserId = User.OwnerId,
            Amount = Parsing.Amount(p?.Amount), AmountRaw = p?.Amount,
            Merchant = p?.Merchant, Card = p?.Card,
            OccurredAt = Parsing.Time(p?.Time) ?? now, ReceivedAt = now, RawBody = raw,
        };
        db.Transactions.Add(tx);
        await db.SaveChangesAsync();

        decimal spent = 0;
        if (tx.Amount is not null)   // unparsed amount: not part of the limit, no sum needed
        {
            // Week of the purchase, not of the arrival: a late payment still lands in its own week
            var (start, end) = Week.Containing(tx.OccurredAt);
            spent = await db.Transactions
                .Where(t => t.UserId == tx.UserId && t.Amount != null && t.OccurredAt >= start && t.OccurredAt < end)
                .SumAsync(t => t.Amount) ?? 0m;
        }

        try
        {
            // Own timeout, not RequestAborted: the send must finish even if the phone disconnects
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await bot.SendMessage(opt.Value.OwnerChatId, Messages.Confirmation(tx, spent),
                                  ParseMode.Html, cancellationToken: cts.Token);
        }
        catch (Exception ex)  // already saved: the Shortcut still gets 201, otherwise the user re-enters it and gets a duplicate
        {
            log.LogWarning("Telegram send failed for transaction {TransactionId}: {ErrorType}", tx.Id, ex.GetType().Name);
        }
        return Results.Json(new { id = tx.Id }, statusCode: StatusCodes.Status201Created);
    }

    // SHA-256 equalises lengths, so FixedTimeEquals leaks neither content nor token length
    static bool TokenMatches(string header, string expected)
    {
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal)) return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(header[prefix.Length..])),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }

    record Payload(string? Amount, string? Merchant, string? Card, string? Time);
}
