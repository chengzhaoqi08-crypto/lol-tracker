using LolTracker.Api.Dtos;
using LolTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LolTracker.Api.Controllers;

[ApiController]
[Route("api/summoners")]
public class SummonersController : ControllerBase
{
    private readonly SummonerService _service;
    private readonly IConfiguration _config;

    public SummonersController(SummonerService service, IConfiguration config)
    {
        _service = service;
        _config = config;
    }

    /// <summary>数据源信息,前端用来显示"演示数据"横幅。</summary>
    [HttpGet("meta")]
    public IActionResult Meta() => Ok(new
    {
        isDemo = _service.IsDemo,
        defaultRegion = _config["Riot:DefaultRegion"] ?? "na1",
    });

    /// <summary>所有被追踪的召唤师(含当前段位),按段位分降序。</summary>
    [HttpGet]
    public async Task<ActionResult<List<SummonerDto>>> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    /// <summary>开始追踪一个召唤师(Riot ID)。</summary>
    [HttpPost("track")]
    public async Task<ActionResult<SummonerDto>> Track([FromBody] TrackRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.GameName) || string.IsNullOrWhiteSpace(req.TagLine))
            return BadRequest(new { error = "需要 gameName 和 tagLine(Riot ID 形如 名字#标签)。" });

        var region = string.IsNullOrWhiteSpace(req.Region) ? (_config["Riot:DefaultRegion"] ?? "na1") : req.Region!;
        var summoner = await _service.TrackAsync(req.GameName.Trim(), req.TagLine.Trim(), region, ct);
        if (summoner is null)
            return NotFound(new { error = $"找不到召唤师 {req.GameName}#{req.TagLine}({region})。" });

        var dto = await _service.GetByIdAsync(summoner.Id, ct);
        return Ok(dto);
    }

    /// <summary>某召唤师的段位历史(时序),用于画趋势曲线。</summary>
    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<List<HistoryPointDto>>> History(
        int id, [FromQuery] string queue = "RANKED_SOLO_5x5", CancellationToken ct = default)
        => Ok(await _service.GetHistoryAsync(id, queue, ct));

    /// <summary>近 10 局战绩评分(S/A/B/C/D 五档)。</summary>
    [HttpGet("{id:int}/rating")]
    public async Task<ActionResult<PerformanceRating>> Rating(int id, CancellationToken ct)
    {
        var r = await _service.GetRatingAsync(id, ct);
        return r is null ? NotFound() : Ok(r);
    }

    /// <summary>当前对局阵容(双方 10 人 + 各自段位)。</summary>
    [HttpGet("{id:int}/live")]
    public async Task<ActionResult<LiveGame>> Live(int id, CancellationToken ct)
    {
        var g = await _service.GetLiveGameAsync(id, ct);
        return g is null ? NotFound() : Ok(g);
    }

    /// <summary>取消追踪。</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => await _service.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
