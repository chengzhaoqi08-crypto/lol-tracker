namespace LolTracker.Api.Services;

public record RatingBreakdown(
    double WinRatePct,
    double AvgKda,
    double AvgCsPerMin,
    double AvgKillParticipationPct);

/// <summary>近期表现评分结果:0-100 分 + 五档评级 + 明细 + 参与计算的对局。</summary>
public record PerformanceRating(
    int Score,
    string Tier,        // S / A / B / C / D
    string TierLabel,
    int Games,
    int Wins,
    RatingBreakdown Breakdown,
    IReadOnlyList<MatchPerf> Matches);

/// <summary>
/// 把最近 N 场对局压成一个 0-100 的近期状态分,再分成五档。
/// 权重:胜率 35 + 场均KDA 30 + 补刀/分 15 + 参团率 20。
/// </summary>
public static class RatingCalculator
{
    public static PerformanceRating From(IReadOnlyList<MatchPerf> matches)
    {
        if (matches.Count == 0)
            return new PerformanceRating(0, "—", "暂无对局", 0, 0,
                new RatingBreakdown(0, 0, 0, 0), matches);

        var wins = matches.Count(m => m.Win);
        var winRate = (double)wins / matches.Count;
        var avgKda = matches.Average(m => (m.Kills + m.Assists) / (double)Math.Max(1, m.Deaths));
        var avgCs = matches.Average(m => m.CsPerMin);
        var avgKp = matches.Average(m => m.KillParticipation);

        var score =
            35 * winRate +
            30 * Math.Clamp(avgKda / 5.0, 0, 1) +    // KDA 5+ 满分
            15 * Math.Clamp(avgCs / 9.0, 0, 1) +     // 9 补刀/分 满分
            20 * Math.Clamp(avgKp, 0, 1);            // 参团率 100% 满分

        var s = (int)Math.Round(score);
        var (tier, label) = Bucket(s);

        return new PerformanceRating(
            s, tier, label, matches.Count, wins,
            new RatingBreakdown(
                Math.Round(winRate * 100, 1),
                Math.Round(avgKda, 2),
                Math.Round(avgCs, 1),
                Math.Round(avgKp * 100, 1)),
            matches);
    }

    private static (string Tier, string Label) Bucket(int score) => score switch
    {
        >= 82 => ("S", "火热 · carry 中"),
        >= 68 => ("A", "优秀"),
        >= 54 => ("B", "稳健"),
        >= 40 => ("C", "一般"),
        _ => ("D", "低迷"),
    };
}
