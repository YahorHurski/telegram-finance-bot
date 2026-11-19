using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((sp, lc) => lc
    // Levels live in code, not in appsettings: an env var in prod cannot make logs more verbose (PRIV-03)
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)  // Telegram URLs carry the bot token in the path
    .ReadFrom.Services(sp)   // tests plug in a capturing ILogEventSink here
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

// Config is read inside DI factories, not at the top level: `dotnet ef` and the migration bundle run without secrets
builder.Services.AddOptions<BotOptions>().BindConfiguration("Bot")
    .Validate(o => o.IngestToken.Length >= 32 && o.TelegramToken.Length > 0 && o.OwnerChatId != 0,
              "Bot__IngestToken (>=32), Bot__TelegramToken, Bot__OwnerChatId are required")
    .ValidateOnStart();
builder.Services.AddDbContext<AppDb>((sp, o) => o
    .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default"), x => x.UseNodaTime())
    .UseSnakeCaseNamingConvention());
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
// Own HttpClient, not IHttpClientFactory: factory logs print the request URL, which contains the bot token
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
    new TelegramBotClient(sp.GetRequiredService<IOptions<BotOptions>>().Value.TelegramToken));
// /health = 503 when Postgres is unreachable: the post-deploy rollback and UptimeRobot see a dead database, not only a dead process
builder.Services.AddHealthChecks().AddDbContextCheck<AppDb>();
// The ingest endpoint is public: cap request rate and body size (ASVS L1)
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Separate budgets for the owner and for everyone else: junk 401 traffic must not starve real payments
    o.AddPolicy("ingest", ctx => RateLimitPartition.GetFixedWindowLimiter(
        Ingest.TokenMatches(ctx.Request.Headers.Authorization.ToString(),
            ctx.RequestServices.GetRequiredService<IOptions<BotOptions>>().Value.IngestToken) ? "owner" : "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
});
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 64 * 1024);

var app = builder.Build();

app.UseSerilogRequestLogging();   // one line per request: method, path, status, time; no body, no headers
app.UseRateLimiter();
app.MapHealthChecks("/health");
app.MapPost("/api/v1/transactions", Ingest.Handle).RequireRateLimiting("ingest");
app.Run();

public sealed class BotOptions
{
    public string IngestToken { get; set; } = "";
    public string TelegramToken { get; set; } = "";
    public long OwnerChatId { get; set; }
}
