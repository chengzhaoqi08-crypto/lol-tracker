namespace LolTracker.Api.Models;

/// <summary>
/// 一个被追踪的召唤师(玩家)。对应原方案里的"角色表"。
/// </summary>
public class Summoner
{
    public int Id { get; set; }

    /// <summary>Riot 全局唯一 ID,真实模式下由 Account-V1 返回。</summary>
    public string Puuid { get; set; } = "";

    /// <summary>Riot ID 的游戏名部分,例如 Faker。</summary>
    public string GameName { get; set; } = "";

    /// <summary>Riot ID 的标签部分(#后面),例如 KR1。</summary>
    public string TagLine { get; set; } = "";

    /// <summary>平台/大区,例如 na1 / euw1 / kr。</summary>
    public string Region { get; set; } = "na1";

    public int ProfileIconId { get; set; }
    public int SummonerLevel { get; set; }

    /// <summary>上次成功从 Riot(或 demo 源)刷新的时间,缓存逻辑用它判断新鲜度。</summary>
    public DateTime LastUpdated { get; set; }

    public List<RankSnapshot> Snapshots { get; set; } = new();
}
