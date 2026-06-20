using System.Net.Http.Json;
using System.Text.Json;

namespace LolTracker.Api.Services;

/// <summary>
/// 英雄 ID → 名称映射。Spectator/Match API 只给 championId,显示名字要查 Data Dragon。
/// 单例 + 懒加载一次缓存(DDragon 是无需密钥的静态资源)。
/// </summary>
public class ChampionCatalog
{
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<ChampionCatalog> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<int, string>? _map;

    public ChampionCatalog(IHttpClientFactory factory, ILogger<ChampionCatalog> log)
    {
        _factory = factory;
        _log = log;
    }

    public async Task<string> NameAsync(int championId, CancellationToken ct)
    {
        var map = await GetMapAsync(ct);
        if (map.TryGetValue(championId, out var name)) return name;
        return championId > 0 ? $"Champion {championId}" : "";
    }

    private async Task<Dictionary<int, string>> GetMapAsync(CancellationToken ct)
    {
        if (_map is not null) return _map;
        await _lock.WaitAsync(ct);
        try
        {
            if (_map is not null) return _map;
            var http = _factory.CreateClient();

            var versions = await http.GetFromJsonAsync<string[]>(
                "https://ddragon.leagueoflegends.com/api/versions.json", ct);
            var v = versions is { Length: > 0 } ? versions[0] : "14.10.1";

            await using var stream = await http.GetStreamAsync(
                $"https://ddragon.leagueoflegends.com/cdn/{v}/data/en_US/champion.json", ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var map = new Dictionary<int, string>();
            foreach (var champ in doc.RootElement.GetProperty("data").EnumerateObject())
            {
                if (int.TryParse(champ.Value.GetProperty("key").GetString(), out var key))
                    map[key] = champ.Value.GetProperty("name").GetString() ?? champ.Name;
            }
            _map = map;
            return _map;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "加载 Data Dragon 英雄列表失败,降级为 ID 显示");
            return _map ??= new Dictionary<int, string>();
        }
        finally
        {
            _lock.Release();
        }
    }
}
