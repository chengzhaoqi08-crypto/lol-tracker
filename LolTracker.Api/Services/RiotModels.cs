namespace LolTracker.Api.Services;

/// <summary>从数据源(真实 Riot API 或 demo)拉回来的一个段位条目。</summary>
public record RankEntry(
    string QueueType,
    string Tier,
    string Division,
    int LeaguePoints,
    int Wins,
    int Losses);

/// <summary>一次拉取的完整结果:召唤师基本信息 + 各队列的段位。</summary>
public record RiotProfile(
    string Puuid,
    string GameName,
    string TagLine,
    int ProfileIconId,
    int SummonerLevel,
    IReadOnlyList<RankEntry> Ranks);

/// <summary>单场对局里该召唤师的表现(Match-V5 抽取的关键指标),用来算近期评分。</summary>
public record MatchPerf(
    string ChampionName,
    bool Win,
    int Kills,
    int Deaths,
    int Assists,
    double CsPerMin,
    double KillParticipation, // 0..1
    int DurationMinutes);

/// <summary>当前对局里的一名玩家(Spectator-V5 阵容 + 段位富化)。</summary>
public record LiveParticipant(
    string Puuid,
    string GameName,
    string TagLine,
    int TeamId,          // 100 蓝 / 200 红
    int ChampionId,
    string ChampionName,
    string Tier,
    string Division,
    int LeaguePoints,
    int Wins,
    int Losses,
    bool IsTracked = false,
    int RecentScore = 0,     // 近 10 局评分 0-100(0 = 无数据)
    string RecentTier = "—"); // 近 10 局档位 S/A/B/C/D

/// <summary>当前对局快照。InGame=false 表示该召唤师不在游戏中。</summary>
public record LiveGame(
    bool InGame,
    string GameMode,
    long GameLengthSeconds,
    IReadOnlyList<LiveParticipant> Participants);
