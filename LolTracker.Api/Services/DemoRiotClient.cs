namespace LolTracker.Api.Services;

/// <summary>
/// Demo 数据源:没有配置 Riot API Key 时启用。
/// 根据 Riot ID 稳定地生成一份逼真的段位资料,这样整个项目无需任何密钥就能跑起来、能验证。
/// 真实历史曲线由 SummonerService 在首次追踪时补 30 天数据。
/// </summary>
public class DemoRiotClient : IRiotClient
{
    private static readonly string[] Tiers =
        { "IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND" };
    private static readonly string[] Divisions = { "IV", "III", "II", "I" };
    private static readonly string[] Champs =
        { "Ahri", "Lee Sin", "Jinx", "Thresh", "Yasuo", "Lux", "Ezreal", "Darius",
          "Kai'Sa", "Sett", "Viego", "Caitlyn", "Orianna", "Zed", "Senna" };

    // 英雄名 + 真实 championId(让 demo 也能正确显示英雄头像)。
    private static readonly (string Name, int Id)[] Roster =
    {
        ("Ahri", 103), ("Lee Sin", 64), ("Jinx", 222), ("Thresh", 412), ("Yasuo", 157),
        ("Lux", 99), ("Ezreal", 81), ("Darius", 122), ("Kai'Sa", 145), ("Sett", 875),
        ("Viego", 234), ("Caitlyn", 51), ("Orianna", 61), ("Zed", 238), ("Senna", 235),
        ("Garen", 86), ("Ashe", 22), ("Malphite", 54), ("Katarina", 55), ("Jhin", 202),
    };

    private static readonly string[] NamePool =
        { "DarkWolf", " MidDiff", "庄周梦蝶", "Faketop", "OnePunch", "SilentBlade", "雷电将军",
          "GankGod", "BaronSteal", "SmiteKing", "影流之主", "FlashGod", "QSS爱好者", "Carry机器",
          "TpToWin", "WardMaster", "千珏小王子", "AfkAndy", "RoamBot", "破败King" };
    private static readonly string[] Tags = { "KR1", "NA1", "EUW", "BR1", "JP1", "CN" };
    // demo 近期对局的模式分布(排位为主,混入大乱斗/匹配/竞技场)。
    private static readonly int[] DemoQueues = { 420, 420, 420, 420, 450, 450, 400, 490, 1700 };

    public bool IsDemo => true;

    public Task<RiotProfile?> FetchProfileAsync(string gameName, string tagLine, string region, CancellationToken ct = default)
    {
        var seed = StableSeed($"{gameName}#{tagLine}|{region}");
        var rng = new Random(seed);

        var tier = Tiers[rng.Next(Tiers.Length)];
        var division = Divisions[rng.Next(Divisions.Length)];
        var baseLp = rng.Next(0, 100);

        // 让 LP 随真实时间轻微漂移,后台任务每次抓到的数据略有不同 —— 演示"实时刷新"。
        var drift = (int)(20 * Math.Sin(DateTime.UtcNow.TimeOfDay.TotalMinutes / 30.0 + seed));
        var lp = Math.Clamp(baseLp + drift, 0, 99);

        var games = rng.Next(40, 320);
        var winRate = 0.40 + rng.NextDouble() * 0.25; // 40% - 65%
        var wins = (int)(games * winRate);

        var profile = new RiotProfile(
            Puuid: "demo-" + ((uint)seed).ToString("x8"),
            GameName: gameName,
            TagLine: tagLine,
            ProfileIconId: rng.Next(1, 28),
            SummonerLevel: rng.Next(30, 600),
            Ranks: new List<RankEntry>
            {
                new("RANKED_SOLO_5x5", tier, division, lp, wins, games - wins)
            });

        return Task.FromResult<RiotProfile?>(profile);
    }

    public Task<IReadOnlyList<MatchPerf>?> FetchRecentMatchesAsync(string puuid, string region, int count, CancellationToken ct = default)
    {
        var seed = RecoverSeed(puuid);
        var skill = SkillFromPuuid(puuid); // 与展示段位挂钩,段位越高近期表现整体越好
        var rng = new Random(seed ^ 0x5f3759);

        var list = new List<MatchPerf>(count);
        for (var i = 0; i < count; i++)
        {
            var win = rng.NextDouble() < (0.40 + skill * 0.25); // 40% - 65% 按 skill
            var deaths = rng.Next(1, 9);
            var kills = (int)Math.Max(0, rng.Next(0, 12) * (0.6 + skill));
            var assists = rng.Next(2, 16);
            var cs = Math.Round(4.5 + skill * 4.5 + (rng.NextDouble() - 0.5), 1);
            var kp = Math.Round(Math.Clamp(0.45 + skill * 0.30 + (rng.NextDouble() - 0.5) * 0.2, 0, 1), 2);
            var dur = rng.Next(22, 40);
            var q = DemoQueues[rng.Next(DemoQueues.Length)];
            list.Add(new MatchPerf(Champs[rng.Next(Champs.Length)], win, kills, deaths, assists, cs, kp, dur, q, Queues.Name(q)));
        }
        return Task.FromResult<IReadOnlyList<MatchPerf>?>(list);
    }

    public Task<LiveGame?> FetchActiveGameAsync(string puuid, string region, CancellationToken ct = default)
    {
        var trackedSeed = RecoverSeed(puuid);
        var trackedTierIdx = new Random(trackedSeed).Next(Tiers.Length); // 与其资料里的段位一致
        var rng = new Random(); // .NET Core 默认随机种子,每次刷新换一桌人

        var usedChamps = new HashSet<int>();
        var usedNames = new HashSet<int>();
        var parts = new List<LiveParticipant>(10);

        for (var i = 0; i < 10; i++)
        {
            int c; do { c = rng.Next(Roster.Length); } while (!usedChamps.Add(c));
            var champ = Roster[c];

            // i==0 是被追踪者本人(段位与其资料一致);其余按其段位上下浮动。
            var tierIdx = i == 0 ? trackedTierIdx
                                 : Math.Clamp(trackedTierIdx + rng.Next(-1, 2), 0, Tiers.Length - 1);
            var games = rng.Next(40, 320);
            var wins = (int)(games * (0.42 + rng.NextDouble() * 0.18));

            int n; do { n = rng.Next(NamePool.Length); } while (i != 0 && !usedNames.Add(n));

            // 对手 puuid 里编入段位档位,算近期评分时能还原出一致的实力。
            parts.Add(new LiveParticipant(
                Puuid: i == 0 ? puuid : $"demo-opp-{tierIdx}-{rng.Next(1_000_000)}",
                GameName: i == 0 ? "(你追踪的召唤师)" : NamePool[n].Trim(),
                TagLine: i == 0 ? "" : Tags[rng.Next(Tags.Length)],
                TeamId: i < 5 ? 100 : 200,
                ChampionId: champ.Id,
                ChampionName: champ.Name,
                Tier: Tiers[tierIdx],
                Division: Divisions[rng.Next(Divisions.Length)],
                LeaguePoints: rng.Next(0, 100),
                Wins: wins,
                Losses: games - wins,
                IsTracked: i == 0));
        }

        return Task.FromResult<LiveGame?>(new LiveGame(true, "CLASSIC", rng.Next(120, 1800), parts));
    }

    private static int StableSeed(string s)
    {
        unchecked
        {
            int h = 17;
            foreach (var c in s) h = h * 31 + c;
            return h;
        }
    }

    // 把 puuid 映射成实力值 0..1,与该召唤师展示的段位保持一致。
    private static double SkillFromPuuid(string puuid)
    {
        // 对手:demo-opp-{tierIdx}-{rand},档位直接编在里面。
        if (puuid.StartsWith("demo-opp-", StringComparison.Ordinal))
        {
            var rest = puuid["demo-opp-".Length..];
            var dash = rest.IndexOf('-');
            if (dash > 0 && int.TryParse(rest.AsSpan(0, dash), out var ti))
                return Math.Clamp(ti, 0, Tiers.Length - 1) / (double)(Tiers.Length - 1);
            return 0.5;
        }
        // 被追踪者:demo-{hex seed},重放出资料里的 tier 序号。
        var seed = RecoverSeed(puuid);
        return new Random(seed).Next(Tiers.Length) / (double)(Tiers.Length - 1);
    }

    // demo 的 puuid 形如 "demo-xxxxxxxx",把 seed 还原回来,保证战绩与资料一致。
    private static int RecoverSeed(string puuid)
    {
        if (puuid.StartsWith("demo-", StringComparison.Ordinal) &&
            uint.TryParse(puuid.AsSpan(5), System.Globalization.NumberStyles.HexNumber, null, out var u))
            return unchecked((int)u);
        return StableSeed(puuid);
    }
}
