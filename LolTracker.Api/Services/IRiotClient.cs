namespace LolTracker.Api.Services;

/// <summary>
/// 数据源抽象。真实实现走 Riot API;没有 API Key 时换成 demo 实现。
/// 上层(SummonerService / 后台任务)完全不关心数据来自哪里。
/// </summary>
public interface IRiotClient
{
    /// <summary>是否为 demo 数据源(用于在响应里告诉前端"这是演示数据")。</summary>
    bool IsDemo { get; }

    /// <summary>根据 Riot ID 拉取召唤师当前资料 + 段位。找不到返回 null。</summary>
    Task<RiotProfile?> FetchProfileAsync(string gameName, string tagLine, string region, CancellationToken ct = default);

    /// <summary>拉取最近 count 场对局的表现,用于评分。数据不可用返回 null。</summary>
    Task<IReadOnlyList<MatchPerf>?> FetchRecentMatchesAsync(string puuid, string region, int count, CancellationToken ct = default);

    /// <summary>查询该召唤师当前所在对局的阵容(含每人段位)。不在对局中返回 null。</summary>
    Task<LiveGame?> FetchActiveGameAsync(string puuid, string region, CancellationToken ct = default);
}
