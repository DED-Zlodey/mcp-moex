using MoexMcp.Application.Services;
using MoexMcp.Domain.Repositories;
using MoexMcp.Infrastructure.Cache;
using MoexMcp.Infrastructure.Moex;
using MoexMcp.Infrastructure.Redis;
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
var redisConn = builder.Configuration["Redis:ConnectionString"];

// Infrastructure
builder.Services.AddHttpClient<IMoexRepository, MoexRepository>(client =>
{
    client.BaseAddress = new Uri(issBaseUrl.TrimEnd('/'));
    client.Timeout = TimeSpan.FromSeconds(15);
});
// Redis опционален — только как быстрый TTL-кэш; без строки подключения кэш in-memory
if (!string.IsNullOrWhiteSpace(redisConn))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn));
    builder.Services.AddSingleton<ICacheRepository, RedisCacheRepository>();
}
else
{
    builder.Services.AddSingleton<ICacheRepository, MemoryCacheRepository>();
}

// Application
builder.Services.AddSingleton<IMarketDataService, MarketDataService>();
builder.Services.AddSingleton<IHistoryService, HistoryService>();
builder.Services.AddSingleton<IComparisonService, ComparisonService>();

// MCP-сервер (HTTP-транспорт)
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapMcp("/mcp");

app.Run();
