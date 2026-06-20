using LolTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LolTracker.Api.Services;

/// <summary>
/// 阶段三的核心:.NET BackgroundService 定时后台任务。
/// 周期性遍历所有被追踪的召唤师,按缓存策略刷新并追加快照,慢慢累积出时序数据。
/// </summary>
public class SnapshotWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<SnapshotWorker> _log;
    private readonly int _intervalSeconds;

    public SnapshotWorker(IServiceProvider sp, IConfiguration config, ILogger<SnapshotWorker> log)
    {
        _sp = sp;
        _log = log;
        _intervalSeconds = config.GetValue<int?>("Tracker:SnapshotIntervalSeconds") ?? 3600;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("SnapshotWorker 已启动,间隔 {Seconds}s", _intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // BackgroundService 是单例,DbContext 是 scoped —— 每轮开一个新作用域。
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var svc = scope.ServiceProvider.GetRequiredService<SummonerService>();

                var summoners = await db.Summoners.ToListAsync(stoppingToken);
                foreach (var s in summoners)
                    await svc.RefreshIfStaleAsync(s, stoppingToken);

                if (summoners.Count > 0)
                    _log.LogInformation("快照轮询完成,共 {Count} 个召唤师", summoners.Count);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "快照轮询失败");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
