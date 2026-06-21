namespace LolTracker.Api.Services;

/// <summary>Riot queueId -> 友好模式名。</summary>
public static class Queues
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [400] = "匹配",
        [430] = "匹配",
        [480] = "匹配",
        [490] = "匹配",
        [420] = "排位单双",
        [440] = "排位灵活",
        [450] = "大乱斗",
        [700] = "冠军杯赛",
        [720] = "大乱斗冠军杯",
        [900] = "无限火力",
        [1010] = "无限火力",
        [1020] = "克隆模式",
        [1300] = "神临",
        [1400] = "终极魔典",
        [1700] = "斗魂竞技场",
        [1710] = "斗魂竞技场",
        [1744] = "斗魂竞技场",
        [1750] = "斗魂竞技场",
        [1900] = "无限火力",
    };

    public static string Name(int queueId) =>
        Names.TryGetValue(queueId, out var n) ? n : (queueId > 0 ? $"模式 {queueId}" : "");

    /// <summary>斗魂竞技场(2v2v2v2)—— 没有传统胜负,用名次(前 4 名算赢)判断。</summary>
    public static bool IsArena(int queueId) => queueId is 1700 or 1710 or 1744 or 1750;
}
