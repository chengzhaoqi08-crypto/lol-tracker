using System.Net;
using System.Text.Json;

namespace LolTracker.Api.Services;

/// <summary>
/// 真实数据源:走 Riot Games API。
/// 调用链:Account-V1 (Riot ID -> puuid) -> Summoner-V4 (puuid -> summonerId)
///        -> League-V4 (summonerId -> 段位条目)。
/// 练到:async/await、HttpClient、System.Text.Json 反序列化、外部 REST 集成、OAuth 风格的 API Key 鉴权。
/// </summary>
public class RiotClient : IRiotClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<RiotClient> _log;
    private readonly ChampionCatalog _champions;

    public bool IsDemo => false;

    public RiotClient(HttpClient http, IConfiguration config, ILogger<RiotClient> log, ChampionCatalog champions)
    {
        _http = http;
        _apiKey = config["Riot:ApiKey"] ?? "";
        _log = log;
        _champions = champions;
    }

    // 平台大区 -> Account-V1 的区域路由值。
    private static string RegionalRoute(string platform) => platform.ToLowerInvariant() switch
    {
        "na1" or "br1" or "la1" or "la2" or "oc1" => "americas",
        "kr" or "jp1" => "asia",
        "ph2" or "sg2" or "th2" or "tw2" or "vn2" => "asia",
        "euw1" or "eun1" or "tr1" or "ru" or "me1" => "europe",
        _ => "americas",
    };

    public async Task<RiotProfile?> FetchProfileAsync(string gameName, string tagLine, string region, CancellationToken ct = default)
    {
        region = string.IsNullOrWhiteSpace(region) ? "na1" : region.ToLowerInvariant();
        var regional = RegionalRoute(region);

        // 1) Riot ID -> puuid
        var accountUrl = $"https://{regional}.api.riotgames.com/riot/account/v1/accounts/by-riot-id/" +
                         $"{Uri.EscapeDataString(gameName)}/{Uri.EscapeDataString(tagLine)}";
        using var accountDoc = await GetJsonAsync(accountUrl, ct);
        if (accountDoc is null) return null; // 404 = 查无此人
        var account = accountDoc.RootElement;
        var puuid = account.GetProperty("puuid").GetString()!;
        var resolvedName = account.TryGetProperty("gameName", out var gn) ? gn.GetString() ?? gameName : gameName;
        var resolvedTag = account.TryGetProperty("tagLine", out var tl) ? tl.GetString() ?? tagLine : tagLine;

        // 2) puuid -> 头像 / 等级(注:Riot 已弃用 encrypted summonerId,Summoner-V4 不再返回 "id")
        var summonerUrl = $"https://{region}.api.riotgames.com/lol/summoner/v4/summoners/by-puuid/{puuid}";
        using var summonerDoc = await GetJsonAsync(summonerUrl, ct);
        if (summonerDoc is null) return null;
        var summoner = summonerDoc.RootElement;
        var profileIconId = summoner.TryGetProperty("profileIconId", out var pi) ? pi.GetInt32() : 0;
        var summonerLevel = summoner.TryGetProperty("summonerLevel", out var sl) ? sl.GetInt32() : 0;

        // 3) puuid -> 段位条目(League-V4 by-puuid,免去已废弃的 summonerId)
        var leagueUrl = $"https://{region}.api.riotgames.com/lol/league/v4/entries/by-puuid/{puuid}";
        using var leagueDoc = await GetJsonAsync(leagueUrl, ct);
        var ranks = new List<RankEntry>();
        if (leagueDoc is not null)
        {
            foreach (var e in leagueDoc.RootElement.EnumerateArray())
            {
                ranks.Add(new RankEntry(
                    QueueType: e.TryGetProperty("queueType", out var q) ? q.GetString() ?? "" : "",
                    Tier: e.TryGetProperty("tier", out var t) ? t.GetString() ?? "" : "",
                    Division: e.TryGetProperty("rank", out var r) ? r.GetString() ?? "" : "",
                    LeaguePoints: e.TryGetProperty("leaguePoints", out var lp) ? lp.GetInt32() : 0,
                    Wins: e.TryGetProperty("wins", out var w) ? w.GetInt32() : 0,
                    Losses: e.TryGetProperty("losses", out var l) ? l.GetInt32() : 0));
            }
        }

        return new RiotProfile(puuid, resolvedName, resolvedTag, profileIconId, summonerLevel, ranks);
    }

    public async Task<IReadOnlyList<MatchPerf>?> FetchRecentMatchesAsync(string puuid, string region, int count, CancellationToken ct = default)
    {
        region = string.IsNullOrWhiteSpace(region) ? "na1" : region.ToLowerInvariant();
        var regional = RegionalRoute(region);

        // 1) 最近 count 场对局 ID(只取排位)
        var idsUrl = $"https://{regional}.api.riotgames.com/lol/match/v5/matches/by-puuid/{puuid}/ids?start=0&count={count}&type=ranked";
        using var idsDoc = await GetJsonAsync(idsUrl, ct);
        if (idsDoc is null) return null;
        var ids = idsDoc.RootElement.EnumerateArray().Select(e => e.GetString()!).ToList();

        var perf = new List<MatchPerf>();
        foreach (var id in ids)
        {
            using var matchDoc = await GetJsonAsync($"https://{regional}.api.riotgames.com/lol/match/v5/matches/{id}", ct);
            if (matchDoc is null) continue;
            var info = matchDoc.RootElement.GetProperty("info");

            var durationSec = info.GetProperty("gameDuration").GetInt32();
            if (durationSec > 10000) durationSec /= 1000; // 兼容旧版以毫秒计的对局
            var durMin = Math.Max(1.0, durationSec / 60.0);

            var participants = info.GetProperty("participants");

            // 找到本人 + 统计本队总击杀(算参团率)
            JsonElement me = default;
            var found = false;
            var teamKills = new Dictionary<int, int>();
            foreach (var p in participants.EnumerateArray())
            {
                var teamId = p.GetProperty("teamId").GetInt32();
                teamKills[teamId] = teamKills.GetValueOrDefault(teamId) + p.GetProperty("kills").GetInt32();
                if (p.GetProperty("puuid").GetString() == puuid) { me = p; found = true; }
            }
            if (!found) continue;

            var kills = me.GetProperty("kills").GetInt32();
            var deaths = me.GetProperty("deaths").GetInt32();
            var assists = me.GetProperty("assists").GetInt32();
            var cs = me.GetProperty("totalMinionsKilled").GetInt32()
                     + (me.TryGetProperty("neutralMinionsKilled", out var nm) ? nm.GetInt32() : 0);
            var win = me.GetProperty("win").GetBoolean();
            var champ = me.TryGetProperty("championName", out var cn) ? cn.GetString() ?? "" : "";
            var myTeam = me.GetProperty("teamId").GetInt32();
            var tk = teamKills.GetValueOrDefault(myTeam);
            var kp = tk > 0 ? Math.Min(1.0, (kills + assists) / (double)tk) : 0;

            perf.Add(new MatchPerf(champ, win, kills, deaths, assists,
                Math.Round(cs / durMin, 1), Math.Round(kp, 2), (int)durMin));
        }
        return perf;
    }

    public async Task<LiveGame?> FetchActiveGameAsync(string puuid, string region, CancellationToken ct = default)
    {
        region = string.IsNullOrWhiteSpace(region) ? "na1" : region.ToLowerInvariant();
        var url = $"https://{region}.api.riotgames.com/lol/spectator/v5/active-games/by-summoner/{puuid}";
        using var doc = await GetJsonAsync(url, ct);
        if (doc is null) return null; // 404 = 不在对局中

        var root = doc.RootElement;
        var gameMode = root.TryGetProperty("gameMode", out var gm) ? gm.GetString() ?? "" : "";
        var length = root.TryGetProperty("gameLength", out var gl) ? gl.GetInt64() : 0;

        var parts = new List<LiveParticipant>();
        foreach (var p in root.GetProperty("participants").EnumerateArray())
        {
            var pPuuid = p.TryGetProperty("puuid", out var pu) ? pu.GetString() ?? "" : "";
            var champId = p.TryGetProperty("championId", out var ci) ? ci.GetInt32() : 0;
            var teamId = p.TryGetProperty("teamId", out var ti) ? ti.GetInt32() : 0;

            string name = "", tag = "";
            if (p.TryGetProperty("riotId", out var rid) && rid.GetString() is { } rs && rs.Contains('#'))
            {
                var idx = rs.LastIndexOf('#');
                name = rs[..idx];
                tag = rs[(idx + 1)..];
            }

            // 个别玩家可能没有 puuid(观战数据缺失)或拿不到段位(限速)—— 单人失败不影响整局。
            string tier = "UNRANKED", div = "";
            int lp = 0, wins = 0, losses = 0;
            if (!string.IsNullOrEmpty(pPuuid))
            {
                try { (tier, div, lp, wins, losses) = await FetchSoloRankAsync(pPuuid, region, ct); }
                catch (RiotApiException) { }
            }
            parts.Add(new LiveParticipant(pPuuid, name, tag, teamId, champId,
                await _champions.NameAsync(champId, ct), tier, div, lp, wins, losses));
        }

        return new LiveGame(true, gameMode, length, parts);
    }

    /// <summary>League-V4 by-puuid 取单双排段位。无段位返回 UNRANKED。</summary>
    private async Task<(string Tier, string Division, int Lp, int Wins, int Losses)> FetchSoloRankAsync(
        string puuid, string region, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(puuid)) return ("UNRANKED", "", 0, 0, 0);
        var url = $"https://{region}.api.riotgames.com/lol/league/v4/entries/by-puuid/{puuid}";
        using var doc = await GetJsonAsync(url, ct);
        if (doc is null) return ("UNRANKED", "", 0, 0, 0);

        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (e.TryGetProperty("queueType", out var q) && q.GetString() == "RANKED_SOLO_5x5")
                return (
                    e.TryGetProperty("tier", out var t) ? t.GetString() ?? "UNRANKED" : "UNRANKED",
                    e.TryGetProperty("rank", out var r) ? r.GetString() ?? "" : "",
                    e.TryGetProperty("leaguePoints", out var lp) ? lp.GetInt32() : 0,
                    e.TryGetProperty("wins", out var w) ? w.GetInt32() : 0,
                    e.TryGetProperty("losses", out var l) ? l.GetInt32() : 0);
        }
        return ("UNRANKED", "", 0, 0, 0);
    }

    /// <summary>带 API Key 头发 GET。404 返回 null;其它错误抛异常并带上说明。</summary>
    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Riot-Token", _apiKey);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            _log.LogWarning("Riot API {Status} for {Url}: {Body}", (int)resp.StatusCode, url, body);
            throw new RiotApiException((int)resp.StatusCode, body);
        }

        var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }
}

public class RiotApiException : Exception
{
    public int StatusCode { get; }
    public RiotApiException(int statusCode, string body)
        : base($"Riot API returned {statusCode}: {body}")
    {
        StatusCode = statusCode;
    }
}
