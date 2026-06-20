using LolTracker.Api.Data;
using LolTracker.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 云平台(Render / Docker)通过 PORT 环境变量指定监听端口;本地开发无此变量,走默认端口。
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// 数据源选择:配置了 Riot API Key 就走真实 Riot API,否则用 demo 数据源(无需密钥即可运行)。
var apiKey = builder.Configuration["Riot:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey))
{
    builder.Services.AddSingleton<IRiotClient, DemoRiotClient>();
}
else
{
    builder.Services.AddSingleton<ChampionCatalog>();
    builder.Services.AddHttpClient<IRiotClient, RiotClient>(c =>
        c.Timeout = TimeSpan.FromSeconds(20));
}

builder.Services.AddScoped<SummonerService>();
builder.Services.AddHostedService<SnapshotWorker>();

var app = builder.Build();

// SQLite 自动建库建表(demo 项目用 EnsureCreated,免去迁移工具链)。
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

app.UseSwagger();
app.UseSwaggerUI();

app.UseDefaultFiles();   // 根路径返回 wwwroot/index.html
app.UseStaticFiles();

app.MapControllers();

app.Run();
