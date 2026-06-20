using LolTracker.Api.Data;
using LolTracker.Api.Dtos;
using LolTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LolTracker.Api.Services;

/// <summary>
/// 业务核心:缓存逻辑(阶段二)+ 历史快照累积(阶段三)。
/// </summary>
public class SummonerService
{
    private readonly AppDbContext _db;
    private readonly IRiotClient _riot;
    private readonly int _cacheMinutes;

    public SummonerService(AppDbContext db, IRiotClient riot, IConfiguration config)
    {
        _db = db;
        _riot = riot;
        _cacheMinutes = config.GetValue<int?>("Tracker:CacheMinutes") ?? 10;
    }

    public bool IsDemo => _riot.IsDemo;

    /// <summary>开始追踪一个召唤师:确保入库、立刻拉一次、首次入库时补 demo 历史。返回 null 表示查无此人。</summary>
    public async Task<Summoner?> TrackAsync(string gameName, string tagLine, string region, CancellationToken ct)
    {
        region = string.IsNullOrWhiteSpace(region) ? "na1" : region.ToLowerInvariant();
        var profile = await _riot.FetchProfileAsync(gameName, tagLine, region, ct);
        if (profile is null) return null;

        var existing = await _db.Summoners
            .FirstOrDefaultAsync(s => s.Region == region && s.GameName == profile.GameName && s.TagLine == profile.TagLine, ct);

        var summoner = existing ?? new Summoner();
        summoner.Puuid = profile.Puuid;
        summoner.GameName = profile.GameName;
        summoner.TagLine = profile.TagLine;
        summoner.Region = region;
        summoner.ProfileIconId = profile.ProfileIconId;
        summoner.SummonerLevel = profile.SummonerLevel;
        summoner.LastUpdated = DateTime.UtcNow;

        var isNew = existing is null;
        if (isNew) _db.Summoners.Add(summoner);
        await _db.SaveChangesAsync(ct);

        if (isNew && _riot.IsDemo)
            await SeedDemoHistoryAsync(summoner, profile, ct);

        await AppendSnapshotAsync(summoner, profile, ct);
        return summoner;
    }

    /// <summary>缓存或拉取:数据够新就什么都不做;过期了才打外部 API 并追加一条快照。后台任务也用它。</summary>
    public async Task RefreshIfStaleAsync(Summoner summoner, CancellationToken ct)
    {
        if (DateTime.UtcNow - summoner.LastUpdated < TimeSpan.FromMinutes(_cacheMinutes))
            return;

        var profile = await _riot.FetchProfileAsync(summoner.GameName, summoner.TagLine, summoner.Region, ct);
        if (profile is null) return;

        summoner.ProfileIconId = profile.ProfileIconId;
        summoner.SummonerLevel = profile.SummonerLevel;
        summoner.LastUpdated = DateTime.UtcNow;
        await AppendSnapshotAsync(summoner, profile, ct);
    }

    public async Task<List<SummonerDto>> GetAllAsync(CancellationToken ct)
    {
        var summoners = await _db.Summoners.AsNoTracking().ToListAsync(ct);
        var result = new List<SummonerDto>();
        foreach (var s in summoners)
        {
            var latest = await _db.Snapshots.AsNoTracking()
                .Where(x => x.SummonerId == s.Id && x.QueueType == "RANKED_SOLO_5x5")
                .OrderByDescending(x => x.TakenAt)
                .FirstOrDefaultAsync(ct);
            result.Add(ToDto(s, latest));
        }
        return result.OrderByDescending(r => r.Current?.RankScore ?? 0).ToList();
    }

    public async Task<SummonerDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var s = await _db.Summoners.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return null;
        var latest = await _db.Snapshots.AsNoTracking()
            .Where(x => x.SummonerId == id && x.QueueType == "RANKED_SOLO_5x5")
            .OrderByDescending(x => x.TakenAt)
            .FirstOrDefaultAsync(ct);
        return ToDto(s, latest);
    }

    /// <summary>近 10 局战绩评分(五档)。召唤师不存在返回 null。</summary>
    public async Task<PerformanceRating?> GetRatingAsync(int summonerId, CancellationToken ct)
    {
        var s = await _db.Summoners.AsNoTracking().FirstOrDefaultAsync(x => x.Id == summonerId, ct);
        if (s is null) return null;
        var matches = await _riot.FetchRecentMatchesAsync(s.Puuid, s.Region, 10, ct);
        return RatingCalculator.From(matches ?? Array.Empty<MatchPerf>());
    }

    /// <summary>拉近期战绩,单人失败(如真实模式被限速)不抛出,返回空表退化为"—"。</summary>
    private async Task<IReadOnlyList<MatchPerf>> SafeRecentMatchesAsync(string puuid, string region, CancellationToken ct)
    {
        try { return await _riot.FetchRecentMatchesAsync(puuid, region, 10, ct) ?? Array.Empty<MatchPerf>(); }
        catch { return Array.Empty<MatchPerf>(); }
    }

    /// <summary>当前对局阵容。召唤师不存在返回 null;不在对局中返回 InGame=false。</summary>
    public async Task<LiveGame?> GetLiveGameAsync(int summonerId, CancellationToken ct)
    {
        var s = await _db.Summoners.AsNoTracking().FirstOrDefaultAsync(x => x.Id == summonerId, ct);
        if (s is null) return null;

        var game = await _riot.FetchActiveGameAsync(s.Puuid, s.Region, ct);
        if (game is null)
            return new LiveGame(false, "", 0, Array.Empty<LiveParticipant>());

        // 给每个人算近 10 局评分(并行),并把被追踪者那行换成 DB 里的真实 Riot ID + 标记。
        var parts = await Task.WhenAll(game.Participants.Select(async p =>
        {
            var rating = RatingCalculator.From(await SafeRecentMatchesAsync(p.Puuid, s.Region, ct));
            var enriched = p with
            {
                RecentScore = rating.Games > 0 ? rating.Score : 0,
                RecentTier = rating.Games > 0 ? rating.Tier : "—",
            };
            return p.Puuid == s.Puuid
                ? enriched with { GameName = s.GameName, TagLine = s.TagLine, IsTracked = true }
                : enriched;
        }));
        return game with { Participants = parts.ToList() };
    }

    public async Task<List<HistoryPointDto>> GetHistoryAsync(int summonerId, string queueType, CancellationToken ct)
    {
        var snaps = await _db.Snapshots.AsNoTracking()
            .Where(x => x.SummonerId == summonerId && x.QueueType == queueType)
            .OrderBy(x => x.TakenAt)
            .ToListAsync(ct);

        return snaps.Select(x => new HistoryPointDto(
            x.TakenAt, x.Tier, x.Division, x.LeaguePoints,
            RankMath.Score(x.Tier, x.Division, x.LeaguePoints),
            x.Wins, x.Losses)).ToList();
    }

    public async Task<bool> DeleteAsync(int summonerId, CancellationToken ct)
    {
        var s = await _db.Summoners.FindAsync(new object?[] { summonerId }, ct);
        if (s is null) return false;
        _db.Summoners.Remove(s);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public SummonerDto ToDto(Summoner s, RankSnapshot? latest)
    {
        CurrentRankDto? current = null;
        if (latest is not null)
        {
            var games = latest.Wins + latest.Losses;
            current = new CurrentRankDto(
                latest.QueueType, latest.Tier, latest.Division, latest.LeaguePoints,
                latest.Wins, latest.Losses,
                games > 0 ? Math.Round((double)latest.Wins / games * 100, 1) : 0,
                RankMath.Score(latest.Tier, latest.Division, latest.LeaguePoints));
        }
        return new SummonerDto(s.Id, s.GameName, s.TagLine, s.Region,
            s.ProfileIconId, s.SummonerLevel, s.LastUpdated, _riot.IsDemo, current);
    }

    private async Task AppendSnapshotAsync(Summoner s, RiotProfile p, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        foreach (var r in p.Ranks)
        {
            _db.Snapshots.Add(new RankSnapshot
            {
                SummonerId = s.Id,
                QueueType = r.QueueType,
                Tier = r.Tier,
                Division = r.Division,
                LeaguePoints = r.LeaguePoints,
                Wins = r.Wins,
                Losses = r.Losses,
                TakenAt = now,
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>给新追踪的 demo 召唤师补 30 天爬分历史,让趋势曲线一上来就有内容。</summary>
    private async Task SeedDemoHistoryAsync(Summoner s, RiotProfile p, CancellationToken ct)
    {
        var solo = p.Ranks.FirstOrDefault(r => r.QueueType == "RANKED_SOLO_5x5");
        if (solo is null) return;

        var rng = new Random(s.GameName.GetHashCode() ^ (s.TagLine.GetHashCode() << 1));
        const int days = 30;
        var endScore = RankMath.Score(solo.Tier, solo.Division, solo.LeaguePoints);
        var startScore = Math.Max(50, endScore - rng.Next(250, 650));
        var endGames = solo.Wins + solo.Losses;
        var startGames = Math.Max(10, endGames - rng.Next(60, 200));
        var now = DateTime.UtcNow;

        for (var d = days; d >= 1; d--)
        {
            var frac = (double)(days - d) / days;
            var score = (int)(startScore + (endScore - startScore) * frac) + rng.Next(-25, 26);
            score = Math.Clamp(score, 0, endScore + 20);
            var (tier, div, lp) = RankMath.FromScore(score);

            var games = (int)(startGames + (endGames - startGames) * frac);
            var wr = 0.45 + rng.NextDouble() * 0.15;
            var wins = (int)(games * wr);

            _db.Snapshots.Add(new RankSnapshot
            {
                SummonerId = s.Id,
                QueueType = "RANKED_SOLO_5x5",
                Tier = tier,
                Division = div,
                LeaguePoints = lp,
                Wins = wins,
                Losses = games - wins,
                TakenAt = now.AddDays(-d),
            });
        }
        await _db.SaveChangesAsync(ct);
    }
}
