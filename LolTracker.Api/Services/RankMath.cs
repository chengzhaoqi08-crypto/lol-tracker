namespace LolTracker.Api.Services;

/// <summary>
/// 把"段位 + 小段 + LP"压成一个单调的数值分,方便在折线图上画成一条连续的爬分曲线。
/// </summary>
public static class RankMath
{
    private static readonly Dictionary<string, int> TierBase = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IRON"] = 0,
        ["BRONZE"] = 400,
        ["SILVER"] = 800,
        ["GOLD"] = 1200,
        ["PLATINUM"] = 1600,
        ["EMERALD"] = 2000,
        ["DIAMOND"] = 2400,
        ["MASTER"] = 2800,
        ["GRANDMASTER"] = 2800,
        ["CHALLENGER"] = 2800,
    };

    private static readonly Dictionary<string, int> DivisionOffset = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IV"] = 0,
        ["III"] = 100,
        ["II"] = 200,
        ["I"] = 300,
    };

    public static bool IsApex(string tier) =>
        tier.Equals("MASTER", StringComparison.OrdinalIgnoreCase)
        || tier.Equals("GRANDMASTER", StringComparison.OrdinalIgnoreCase)
        || tier.Equals("CHALLENGER", StringComparison.OrdinalIgnoreCase);

    /// <summary>数值化段位分。大师以上没有小段,LP 直接累加(可超过 100)。</summary>
    public static int Score(string? tier, string? division, int lp)
    {
        if (string.IsNullOrEmpty(tier) || tier.Equals("UNRANKED", StringComparison.OrdinalIgnoreCase))
            return 0;

        var baseScore = TierBase.TryGetValue(tier, out var tb) ? tb : 0;
        if (IsApex(tier))
            return baseScore + lp;

        var divScore = DivisionOffset.TryGetValue(division ?? "", out var db) ? db : 0;
        return baseScore + divScore + lp;
    }

    private static readonly string[] LadderTiers =
        { "IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND" };
    private static readonly string[] LadderDivisions = { "IV", "III", "II", "I" };

    /// <summary>Score 的逆运算:把数值分还原成段位文字(给 demo 历史曲线生成用)。</summary>
    public static (string Tier, string Division, int Lp) FromScore(int score)
    {
        score = Math.Max(0, score);
        if (score >= 2800)
            return ("MASTER", "", score - 2800);

        var tierIdx = Math.Clamp(score / 400, 0, LadderTiers.Length - 1);
        var within = score - tierIdx * 400;          // 0..399
        var divIdx = Math.Clamp(within / 100, 0, 3);  // 0=IV .. 3=I
        var lp = Math.Clamp(within - divIdx * 100, 0, 99);
        return (LadderTiers[tierIdx], LadderDivisions[divIdx], lp);
    }
}
