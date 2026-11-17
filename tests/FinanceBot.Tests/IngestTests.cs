using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types.Enums;

// Each test uses its own merchant and its own week: tests share one database and run sequentially
[Collection("Api")]
public sealed class IngestTests(ApiFactory f)
{
    async Task<HttpResponseMessage> Post(string body, string? auth = "Bearer " + ApiFactory.IngestToken)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (auth is not null) req.Headers.TryAddWithoutValidation("Authorization", auth);
        return await f.CreateClient().SendAsync(req, TestContext.Current.CancellationToken);
    }

    async Task<Transaction?> Row(Guid id)
    {
        await using var scope = f.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDb>().Transactions.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == id, TestContext.Current.CancellationToken);
    }

    static async Task<Guid> Id(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<IdDto>(TestContext.Current.CancellationToken))!.Id;

    sealed record IdDto(Guid Id);

    [Fact]
    public async Task ValidToken_SavesRowAndSendsConfirmation()
    {
        f.Bot.ClearReceivedCalls();
        const string body = """{"amount":"45,99 zł","merchant":"Żabka","card":"PKO","time":"05.01.2026 12:00:00"}""";

        var res = await Post(body);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var id = await Id(res);
        var row = await Row(id);
        Assert.NotNull(row);
        Assert.Equal(1, row.UserId);
        Assert.Equal(45.99m, row.Amount);
        Assert.Equal("Żabka", row.Merchant);
        Assert.Equal(body, row.RawBody);
        Assert.Equal(Instant.FromUtc(2026, 1, 5, 11, 0, 0), row.OccurredAt);   // 12:00 Warsaw (CET, UTC+1)

        // SendRequest, not SendMessage: SendMessage is an extension method NSubstitute cannot intercept
        await f.Bot.Received(1).SendRequest(
            Arg.Is<SendMessageRequest>(r =>
                r.ChatId!.Identifier == ApiFactory.OwnerChatId &&
                r.ParseMode == ParseMode.Html &&
                r.Text == "− 45,99 zł · Żabka\n🟢 Осталось 154,01 из 200 zł"),
            Arg.Any<CancellationToken>());
    }

    static string Body(string merchant, string time, string amount = "10,00 zł") =>
        $$"""{"amount":"{{amount}}","merchant":"{{merchant}}","card":"PKO","time":"{{time}}"}""";

    async Task AssertNoTrace(string merchant)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        Assert.False(await db.Transactions.AnyAsync(t => t.Merchant == merchant, TestContext.Current.CancellationToken));
        await f.Bot.DidNotReceive().SendRequest(
            Arg.Is<SendMessageRequest>(r => r.Text.Contains(merchant)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingToken_Returns401WithoutSideEffects()
    {
        var res = await Post(Body("T2-MISSING-TOKEN", "12.01.2026 12:00:00"), auth: null);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Empty(await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        await AssertNoTrace("T2-MISSING-TOKEN");
    }

    [Fact]
    public async Task WrongToken_Returns401WithoutSideEffects()
    {
        var res = await Post(Body("T2-WRONG-TOKEN", "19.01.2026 12:00:00"), auth: "Bearer wrong-token-of-a-different-length");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        await AssertNoTrace("T2-WRONG-TOKEN");
    }

    [Fact]
    public async Task NonBearerScheme_Returns401()
    {
        var res = await Post(Body("T2-BASIC-SCHEME", "26.01.2026 12:00:00"), auth: "Basic " + ApiFactory.IngestToken);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        await AssertNoTrace("T2-BASIC-SCHEME");
    }

    [Fact]
    public async Task InvalidJson_SavedAsUnparsed()
    {
        f.Bot.ClearReceivedCalls();
        const string body = "{not json SENTINEL";

        var res = await Post(body);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var row = await Row(await Id(res));
        Assert.NotNull(row);
        Assert.Null(row.Amount);
        Assert.Equal(body, row.RawBody);
        await f.Bot.Received(1).SendRequest(
            Arg.Is<SendMessageRequest>(r => r.Text.StartsWith("⚠️ Не разобрал сумму:")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmountAsNumber_SavedAsUnparsed()
    {
        const string body = """{"amount":45.99,"merchant":"T2-AMOUNT-NUMBER","card":"PKO","time":"02.02.2026 12:00:00"}""";

        var res = await Post(body);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var row = await Row(await Id(res));
        Assert.NotNull(row);
        Assert.Null(row.Amount);
        Assert.Equal(body, row.RawBody);
    }

    [Fact]
    public async Task UnparsedAmount_WarnsAndStaysOutOfTheLimit()
    {
        var res = await Post(Body("T2-UNPARSED", "09.02.2026 12:00:00", amount: "abc"));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var row = await Row(await Id(res));
        Assert.NotNull(row);
        Assert.Null(row.Amount);
        await f.Bot.Received(1).SendRequest(
            Arg.Is<SendMessageRequest>(r => r.Text == "⚠️ Не разобрал сумму: «abc» · T2-UNPARSED"),
            Arg.Any<CancellationToken>());

        // Same week, parsed amount: the unparsed one must not count toward the 200 zł
        var next = await Post(Body("T2-UNPARSED-NEXT", "10.02.2026 12:00:00", amount: "10,00 zł"));
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
        await f.Bot.Received(1).SendRequest(
            Arg.Is<SendMessageRequest>(r => r.Text == "− 10,00 zł · T2-UNPARSED-NEXT\n🟢 Осталось 190,00 из 200 zł"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnparsedTime_FallsBackToReceivedAt()
    {
        var res = await Post(Body("T2-UNPARSED-TIME", "garbage"));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var row = await Row(await Id(res));
        Assert.NotNull(row);
        Assert.Equal(row.ReceivedAt, row.OccurredAt);
    }

    [Fact]
    public async Task TelegramFailure_StillReturns201AndKeepsRow()
    {
        // Throw only for this test's merchant, so other tests in the shared fixture are unaffected
        f.Bot.SendRequest(Arg.Is<SendMessageRequest>(r => r.Text.Contains("T2-TELEGRAM-FAIL")), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException("Bad Request: simulated", 400));

        var res = await Post(Body("T2-TELEGRAM-FAIL", "16.02.2026 12:00:00"));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var row = await Row(await Id(res));
        Assert.NotNull(row);
        Assert.Equal("T2-TELEGRAM-FAIL", row.Merchant);
    }

    [Fact]
    public async Task JunkTraffic_DoesNotStarveOwnerRateLimit()
    {
        // Own host = own limiter state, so the exhausted anonymous budget doesn't leak into other tests' 401 checks
        using var client = f.WithWebHostBuilder(_ => { }).CreateClient();
        var ct = TestContext.Current.CancellationToken;
        async Task<HttpStatusCode> Send(string body, string auth)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
                { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            req.Headers.TryAddWithoutValidation("Authorization", auth);
            return (await client.SendAsync(req, ct)).StatusCode;
        }

        for (int i = 0; i < 30; i++) await Send("{}", "Bearer wrong");   // exhausts the anonymous budget
        Assert.Equal(HttpStatusCode.TooManyRequests, await Send("{}", "Bearer wrong"));

        Assert.Equal(HttpStatusCode.Created, await Send(Body("T2-RATE-LIMIT", "17.02.2026 12:00:00"), "Bearer " + ApiFactory.IngestToken));
    }
}
