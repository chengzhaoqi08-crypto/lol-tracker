# LoL 段位追踪平台 (LoL Rank Tracker)

调 Riot Games API 拉召唤师段位 → 存进本地库做**历史快照** → 看板展示**爬分趋势曲线**。

一个证明「外部 API 集成 + 数据缓存 + 时序分析」能力的全栈作品集项目。

## 技术栈

- **后端**:ASP.NET Core 8 Web API(`HttpClient` 异步调外部 API、`System.Text.Json` 反序列化)
- **数据**:EF Core + SQLite(角色表 + 历史快照表;换 SQL Server 只需改 `Program.cs` 里一行 `UseSqlite` → `UseSqlServer`)
- **后台任务**:`BackgroundService` 定时拉取并累积时序快照
- **前端**:静态看板 + Chart.js 画 LP 趋势曲线
- **API 文档**:Swagger UI(`/swagger`)

## 架构

```
Riot API (Account-V1 → Summoner-V4 → League-V4)
        │  HttpClient + X-Riot-Token
        ▼
ASP.NET Core Web API ──► SQLite (Summoner 表 + RankSnapshot 表)
   缓存逻辑 + BackgroundService 定时快照
        │  REST + JSON
        ▼
看板 (wwwroot, Chart.js 趋势曲线)
```

## 运行

```bash
cd LolTracker.Api
dotnet run
```

打开 `http://localhost:5xxx`(控制台会打印实际端口),搜索一个 Riot ID 即可。

**无需任何密钥就能跑** —— 没配置 Riot API Key 时自动进入**演示模式**,
生成逼真的段位数据并补 30 天历史曲线。

### 接入真实 Riot 数据

1. 去 https://developer.riotgames.com 登录,拿一个 Development API Key(免费,24h 有效)。
2. 填进 `LolTracker.Api/appsettings.json`:
   ```json
   "Riot": { "ApiKey": "RGAPI-xxxx", "DefaultRegion": "na1" }
   ```
3. 重启,即走真实 API。用真实 Riot ID 搜索(例如 `Hide on bush#KR1`,大区选 KR)。

> 注:Riot Development Key 有较严的速率限制(20 req/s,100 req/2min),仅供本地演示。

## 主要接口

| 方法 | 路径 | 说明 |
|---|---|---|
| `POST` | `/api/summoners/track` | 追踪一个 Riot ID(body: `{gameName, tagLine, region}`) |
| `GET` | `/api/summoners` | 所有被追踪召唤师 + 当前段位 |
| `GET` | `/api/summoners/{id}/history?queue=RANKED_SOLO_5x5` | 段位历史时序(画曲线用) |
| `GET` | `/api/summoners/{id}/rating` | **近 10 局战绩评分(S/A/B/C/D 五档)** |
| `GET` | `/api/summoners/{id}/live` | **当前对局阵容(双方 10 人 + 各自段位/胜率)** |
| `DELETE` | `/api/summoners/{id}` | 取消追踪 |

> **当前对局**走 Spectator-V5(观战 API),给出对局阵容:每人的英雄、队伍、段位、胜率,
> **外加每人的近 10 局五档评分**(并行对 10 人各拉一次 Match-V5 算分)。
> 注:比赛**进行中的实时 KDA / 补刀 / 经济**公开 API 不提供(仅本机 Live Client Data API 可读),
> 这与 OP.GG 的「当前对局」页一致。英雄名通过 Data Dragon 解析([ChampionCatalog.cs](LolTracker.Api/Services/ChampionCatalog.cs))。
>
> ⚠️ **真实模式成本**:当前对局给 10 人各算评分 ≈ 10 × 11 = 110 次 Match-V5 调用/查询,
> 开发 Key 限速(100 次/2 分钟)会被打满 —— 已做容错:**单人失败只让那人显示「—」,不影响整局**。
> Demo 模式即时,无此问题。若上生产可加 Redis 缓存评分 / 降低拉取局数。

### 近期评分规则(0–100 → 五档)

拉最近 10 场对局(真实模式走 Match-V5,demo 模式按段位生成),加权求分:

```
分数 = 35×胜率 + 30×min(场均KDA/5,1) + 15×min(补刀每分/9,1) + 20×参团率
```

| 分段 | 档位 | 含义 |
|---|---|---|
| ≥82 | **S** | 火热 · carry 中 |
| 68–81 | **A** | 优秀 |
| 54–67 | **B** | 稳健 |
| 40–53 | **C** | 一般 |
| <40 | **D** | 低迷 |

## 配置 (`appsettings.json`)

| 键 | 含义 | 默认 |
|---|---|---|
| `Riot:ApiKey` | Riot API Key(空 = 演示模式) | `""` |
| `Riot:DefaultRegion` | 默认大区 | `na1` |
| `Tracker:CacheMinutes` | 缓存新鲜度窗口,窗口内不打外部 API | `10` |
| `Tracker:SnapshotIntervalSeconds` | 后台快照轮询间隔 | `60` |

## 后续可加分

- [ ] 接 Blizzard 式 OAuth2 / Riot RSO(简历硬通货)
- [ ] 多召唤师对比看板
- [ ] Redis 做缓存层
- [ ] EF Core Migrations 替代 `EnsureCreated`
- [x] Match-V5 拉对局明细 → 近 10 局五档评分
- [ ] 评分按英雄/位置细分,展示对局明细列表
