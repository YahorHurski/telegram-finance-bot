using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NSubstitute;
using Telegram.Bot;
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
}
