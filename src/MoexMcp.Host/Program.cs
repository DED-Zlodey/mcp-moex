using MoexMcp.Application.Services;
using MoexMcp.Domain.Repositories;
using MoexMcp.Infrastructure.Moex;
using MoexMcp.Infrastructure.Redis;
using MoexMcp.Infrastructure.Snapshots;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var seqUrl = builder.Configuration["Seq:Url"] ?? "http://localhost:5341";
builder.Host.UseSerilog((ctx, _, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.Seq(seqUrl));

var issBaseUrl = builder.Configuration["Moex:BaseUrl"] ?? "https://iss.moex.com/iss";
var redisConn = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";

// Infrastructure
builder.Services.AddHttpClient<IMoexRepository, MoexRepository>(client =>
{
    client.BaseAddress = new Uri(issBaseUrl.TrimEnd('/'));
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn));
builder.Services.AddSingleton<ICacheRepository, RedisCacheRepository>();
builder.Services.AddSingleton<ISnapshotRepository, RedisSnapshotRepository>();

// Application
builder.Services.AddSingleton<IMarketDataService, MarketDataService>();
builder.Services.AddSingleton<IHistoryService, HistoryService>();
builder.Services.AddSingleton<IComparisonService, ComparisonService>();

// Воркер снапшотов
var snapshotInterval = TimeSpan.FromMinutes(builder.Configuration.GetValue("Snapshots:IntervalMinutes", 5));
var snapshotRetention = TimeSpan.FromDays(builder.Configuration.GetValue("Snapshots:RetentionDays", 7));
builder.Services.AddHostedService(sp => new MarketSnapshotWorker(
    sp.GetRequiredService<IMoexRepository>(),
    sp.GetRequiredService<ISnapshotRepository>(),
    sp.GetRequiredService<ILogger<MarketSnapshotWorker>>(),
    snapshotInterval,
    snapshotRetention));

// MCP-сервер (HTTP-транспорт)
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapMcp("/mcp");

app.Run();
