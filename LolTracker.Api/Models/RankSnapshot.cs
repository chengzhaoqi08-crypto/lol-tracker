namespace LolTracker.Api.Models;

/// <summary>
/// 某个时间点的段位快照。历史快照表 —— 这是整个项目的核心:
/// 随时间累积出时序数据,才能画 LP 爬分趋势曲线。
/// </summary>
public class RankSnapshot
{
    public int Id { get; set; }

    public int SummonerId { get; set; }
    public Summoner? Summoner { get; set; }

    /// <summary>排位队列类型,例如 RANKED_SOLO_5x5 / RANKED_FLEX_SR。</summary>
    public string QueueType { get; set; } = "RANKED_SOLO_5x5";

    /// <summary>大段位:IRON / BRONZE / ... / CHALLENGER。</summary>
    public string Tier { get; set; } = "UNRANKED";

    /// <summary>小段位:IV / III / II / I(大师以上为空)。</summary>
    public string Division { get; set; } = "";

    /// <summary>胜点 LP。</summary>
    public int LeaguePoints { get; set; }

    public int Wins { get; set; }
    public int Losses { get; set; }

    public DateTime TakenAt { get; set; }
}
