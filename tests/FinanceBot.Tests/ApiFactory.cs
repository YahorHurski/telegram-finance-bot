using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Telegram.Bot;
using Testcontainers.PostgreSql;

// One host + one real Postgres 18 (same tag as prod) for every integration test class in the "Api" collection
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Low-entropy fakes on purpose: real-looking secrets would trip gitleaks
    public const string IngestToken = "test-ingest-token-that-is-at-least-32-chars";
    public const string BotToken = "123:fake-bot-token-sentinel";
    public const long OwnerChatId = 42;

    readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:18-alpine").Build();
    public ITelegramBotClient Bot { get; } = Substitute.For<ITelegramBotClient>();

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        b.UseEnvironment("Production");
        b.UseSetting("ConnectionStrings:Default", _pg.GetConnectionString());
        b.UseSetting("Bot:IngestToken", IngestToken);
        b.UseSetting("Bot:TelegramToken", BotToken);
        b.UseSetting("Bot:OwnerChatId", OwnerChatId.ToString());
        b.ConfigureTestServices(s => s.AddSingleton(Bot));
    }

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        // Also fails the run if a model change has no migration
        await scope.ServiceProvider.GetRequiredService<AppDb>().Database.MigrateAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _pg.DisposeAsync();
    }
}

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
