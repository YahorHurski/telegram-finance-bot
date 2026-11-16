using System.Text;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;

// PRIV-03: logs never contain amounts, merchants, cards, raw bodies or tokens.
// Sentinels are ASCII on purpose: the JSON formatter may escape non-ASCII, and a search for "Żabka" would falsely report "clean".
[Collection("Api")]
public sealed class LogPrivacyTests(ApiFactory f)
{
    const string WrongToken = "SENTINEL-WRONG-TOKEN-0000000000000000000000";

    static readonly string[] Forbidden =
    [
        "SENTINEL-MERCHANT", "SENTINEL-CARD", "SENTINEL-RAW-BODY", "SENTINEL-WRONG",
        "987,65", "987.65", ApiFactory.IngestToken, ApiFactory.BotToken,
    ];

    // Both tests need the same four requests; send them once (tests in a class run sequentially) to stay under the 30/min limit
    static Task? _sent;
    Task SendAll() => _sent ??= SendFourRequests();

    async Task SendFourRequests()
    {
        // Telegram failure only for this merchant, so other tests in the shared fixture are unaffected
        f.Bot.SendRequest(Arg.Is<SendMessageRequest>(r => r.Text.Contains("SENTINEL-MERCHANT-FAIL")), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException("Bad Request: simulated", 400));

        // Week of 1 June 2026: no other test uses it
        await Post("""{"amount":"987,65 zł","merchant":"SENTINEL-MERCHANT-7F3A","card":"SENTINEL-CARD-9C1B","time":"01.06.2026 12:00:00"}""");
        await Post("""{"amount":"1,00 zł","merchant":"SENTINEL-MERCHANT-FAIL","card":"SENTINEL-CARD-9C1B","time":"02.06.2026 12:00:00"}""");
        await Post("""{"amount":"987,65 zł","merchant":"SENTINEL-MERCHANT-401"}""", WrongToken);
        await Post("{not json SENTINEL-RAW-BODY 987,65");
    }

    async Task Post(string body, string token = ApiFactory.IngestToken)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        (await f.CreateClient().SendAsync(req, TestContext.Current.CancellationToken)).Dispose();
    }

    [Fact]
    public async Task NoSensitiveDataInLogs()
    {
        await SendAll();
        var all = f.Logs.All;
        foreach (var s in Forbidden) Assert.DoesNotContain(s, all);
    }

    [Fact]
    public async Task LogsAreCaptured()
    {
        await SendAll();
        // Guards against a vacuous pass: the privacy check means nothing on an empty log
        Assert.True(f.Logs.Count > 0, "no log events captured");
        Assert.Contains("/api/v1/transactions", f.Logs.All);   // request logging line
        Assert.Contains("Telegram send failed", f.Logs.All);   // warning from the failure branch
    }
}
