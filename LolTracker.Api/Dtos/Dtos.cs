namespace LolTracker.Api.Dtos;

public record TrackRequest(string GameName, string TagLine, string? Region);

public record CurrentRankDto(
    string QueueType,
    string Tier,
    string Division,
    int LeaguePoints,
    int Wins,
    int Losses,
    double WinRate,
    int RankScore);

public record SummonerDto(
    int Id,
    string GameName,
    string TagLine,
    string Region,
    int ProfileIconId,
    int SummonerLevel,
    DateTime LastUpdated,
    bool IsDemo,
    CurrentRankDto? Current);

public record HistoryPointDto(
    DateTime TakenAt,
    string Tier,
    string Division,
    int LeaguePoints,
    int RankScore,
    int Wins,
    int Losses);
