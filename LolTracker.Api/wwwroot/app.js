const TIERS = ["IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND"];
const DIVS = ["IV", "III", "II", "I"];

const $ = (id) => document.getElementById(id);
const iconUrl = (id) =>
  `https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/profile-icons/${id}.jpg`;

function scoreLabel(score) {
  if (score >= 2800) return "MASTER+";
  const ti = Math.min(Math.floor(score / 400), TIERS.length - 1);
  const within = score - ti * 400;
  const di = Math.min(Math.floor(within / 100), 3);
  return `${TIERS[ti]} ${DIVS[di]}`;
}

let chart = null;

async function loadMeta() {
  try {
    const m = await (await fetch("/api/summoners/meta")).json();
    if (m.isDemo) {
      const b = $("demoBanner");
      b.textContent = "🧪 演示模式:当前为模拟数据。在 appsettings.json 填入 Riot API Key 即可切换真实数据。";
      b.classList.remove("hidden");
    }
    // 默认大区
    if (m.defaultRegion) $("region").value = m.defaultRegion;
  } catch { /* ignore */ }
}

async function loadCards() {
  const cards = $("cards");
  const list = await (await fetch("/api/summoners")).json();
  if (!list.length) {
    cards.innerHTML = `<div class="status">还没有追踪任何召唤师,试试上面搜索一个 Riot ID。</div>`;
    return;
  }
  cards.innerHTML = "";
  for (const s of list) cards.appendChild(renderCard(s));
}

function renderCard(s) {
  const el = document.createElement("div");
  el.className = "card";
  const c = s.current;
  const rankHtml = c
    ? `<div class="rankline">
         <span class="tier t-${c.tier}">${c.tier} ${c.division}</span>
         <span class="lp">${c.leaguePoints} LP</span>
       </div>
       <div class="wl">
         <span><span class="w">${c.wins}胜</span> / <span class="l">${c.losses}负</span></span>
         <span class="wr">胜率 ${c.winRate}%</span>
       </div>`
    : `<div class="unranked">暂无排位数据</div>`;

  el.innerHTML = `
    <button class="del" title="取消追踪">✕</button>
    <div class="top">
      <img class="icon" src="${iconUrl(s.profileIconId)}"
           onerror="this.style.visibility='hidden'" alt="" />
      <div>
        <div class="name">${escapeHtml(s.gameName)} <small>#${escapeHtml(s.tagLine)}</small></div>
        <div class="lvl">Lv.${s.summonerLevel} · ${s.region.toUpperCase()}</div>
      </div>
    </div>
    ${rankHtml}
    <div class="card-actions">
      <button class="mini trend-btn">📈 趋势 / 评分</button>
      <button class="mini live-btn">⚔️ 当前对局</button>
    </div>`;

  el.querySelector(".trend-btn").addEventListener("click", (e) => { e.stopPropagation(); showHistory(s); });
  el.querySelector(".live-btn").addEventListener("click", (e) => { e.stopPropagation(); showLive(s); });
  el.addEventListener("click", (e) => {
    if (e.target.closest(".del") || e.target.closest(".card-actions")) return;
    showHistory(s);
  });
  el.querySelector(".del").addEventListener("click", async (e) => {
    e.stopPropagation();
    await fetch(`/api/summoners/${s.id}`, { method: "DELETE" });
    loadCards();
  });
  return el;
}

async function showHistory(s) {
  $("chartPanel").classList.remove("hidden");
  $("chartTitle").textContent = `${s.gameName}#${s.tagLine} — 单双排爬分曲线`;

  renderRating(s.id);

  const points = await (await fetch(`/api/summoners/${s.id}/history?queue=RANKED_SOLO_5x5`)).json();

  const data = points.map((p) => ({ x: new Date(p.takenAt), y: p.rankScore, raw: p }));

  if (chart) chart.destroy();
  chart = new Chart($("trendChart"), {
    type: "line",
    data: {
      datasets: [{
        label: "段位分",
        data,
        borderColor: "#c8aa6e",
        backgroundColor: "rgba(200,170,110,0.12)",
        fill: true,
        tension: 0.25,
        pointRadius: 2,
        pointHoverRadius: 5,
      }],
    },
    options: {
      responsive: true,
      interaction: { intersect: false, mode: "index" },
      scales: {
        x: { type: "time", time: { unit: "day" }, grid: { color: "#1e3a5c" },
             ticks: { color: "#7b96b0" } },
        y: {
          grid: { color: "#1e3a5c" },
          ticks: { color: "#7b96b0", stepSize: 100, callback: (v) => scoreLabel(v) },
        },
      },
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            label: (ctx) => {
              const r = ctx.raw.raw;
              return `${r.tier} ${r.division} · ${r.leaguePoints} LP  (${r.wins}胜${r.losses}负)`;
            },
          },
        },
      },
    },
  });
  $("chartPanel").scrollIntoView({ behavior: "smooth" });
}

async function renderRating(id) {
  const box = $("ratingBox");
  box.innerHTML = `<div class="rating-meta"><div class="bd">近期评分计算中…</div></div>`;
  let r;
  try {
    r = await (await fetch(`/api/summoners/${id}/rating`)).json();
  } catch {
    box.innerHTML = `<div class="rating-meta"><div class="bd">评分获取失败</div></div>`;
    return;
  }
  if (!r || r.games === 0) {
    box.innerHTML = `<div class="grade g-none">—</div>
      <div class="rating-meta"><div class="score">暂无近期对局</div></div>`;
    return;
  }
  const b = r.breakdown;
  const pips = r.matches.map((m) => {
    const kda = `${m.kills}/${m.deaths}/${m.assists}`;
    return `<div class="pip ${m.win ? "win" : "loss"}" title="${escapeHtml(m.championName)} · ${kda} · 补刀/分 ${m.csPerMin}">${m.win ? "胜" : "负"}</div>`;
  }).join("");

  box.innerHTML = `
    <div class="grade g-${r.tier}">${r.tier}</div>
    <div class="rating-meta">
      <div class="score">近10局评分 <span class="num">${r.score}</span> / 100 · ${r.tier} 档 · ${escapeHtml(r.tierLabel)}</div>
      <div class="bd">${r.wins}胜${r.games - r.wins}负 · 胜率 ${b.winRatePct}% · 场均KDA ${b.avgKda} · 补刀/分 ${b.avgCsPerMin} · 参团率 ${b.avgKillParticipationPct}%</div>
      <div class="pips">${pips}</div>
    </div>`;
}

const champIcon = (id) =>
  `https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/champion-icons/${id}.png`;

async function showLive(s) {
  const panel = $("livePanel");
  panel.classList.remove("hidden");
  $("liveTitle").textContent = `${s.gameName}#${s.tagLine} — 当前对局`;
  $("liveBody").innerHTML = `<div class="status">查询对局中…</div>`;
  panel.scrollIntoView({ behavior: "smooth" });

  let g;
  try {
    g = await (await fetch(`/api/summoners/${s.id}/live`)).json();
  } catch {
    $("liveBody").innerHTML = `<div class="status">查询失败。</div>`;
    return;
  }
  if (!g.inGame) {
    $("liveBody").innerHTML = `<div class="status">该召唤师当前不在对局中。</div>`;
    return;
  }

  const mins = Math.floor(g.gameLengthSeconds / 60);
  const secs = String(g.gameLengthSeconds % 60).padStart(2, "0");
  const blue = g.participants.filter((p) => p.teamId === 100);
  const red = g.participants.filter((p) => p.teamId === 200);

  $("liveBody").innerHTML = `
    <div class="live-meta">模式 ${escapeHtml(g.gameMode)} · 已进行 ${mins}:${secs} · 彩色徽章为各玩家近 10 局评分(S/A/B/C/D)</div>
    <div class="teams">
      ${renderTeam("蓝队", blue, "blue")}
      ${renderTeam("红队", red, "red")}
    </div>`;
}

function renderTeam(label, players, cls) {
  const rows = players.map((p) => {
    const rank = p.tier === "UNRANKED"
      ? `<span class="muted">未定级</span>`
      : `<span class="tier t-${p.tier}">${p.tier} ${p.division}</span> <span class="lp">${p.leaguePoints}LP</span>`;
    const games = p.wins + p.losses;
    const wr = games > 0 ? Math.round((p.wins / games) * 100) : 0;
    const gCls = p.recentTier === "—" ? "g-none" : "g-" + p.recentTier;
    const gTxt = p.recentTier === "—" ? "—" : `${p.recentTier}<small>${p.recentScore}</small>`;
    return `
      <div class="prow ${p.isTracked ? "tracked" : ""}">
        <img class="champ" src="${champIcon(p.championId)}" onerror="this.style.visibility='hidden'" alt="" />
        <div class="pinfo">
          <div class="pname">${escapeHtml(p.gameName)}${p.tagLine ? `<small>#${escapeHtml(p.tagLine)}</small>` : ""}</div>
          <div class="pchamp">${escapeHtml(p.championName)}</div>
        </div>
        <div class="pgrade ${gCls}" title="近10局评分 ${p.recentTier === "—" ? "(无数据)" : p.recentScore + " 分"}">${gTxt}</div>
        <div class="prank">${rank}<div class="pwr">${games} 局 · 胜率 ${wr}%</div></div>
      </div>`;
  }).join("");
  return `<div class="team ${cls}"><div class="team-head">${label}</div>${rows}</div>`;
}

async function track() {
  const gameName = $("gameName").value.trim();
  const tagLine = $("tagLine").value.trim();
  const region = $("region").value;
  const status = $("status");
  if (!gameName || !tagLine) {
    status.textContent = "请输入 游戏名 和 标签。";
    return;
  }
  status.textContent = "追踪中…";
  $("trackBtn").disabled = true;
  try {
    const resp = await fetch("/api/summoners/track", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ gameName, tagLine, region }),
    });
    if (!resp.ok) {
      const err = await resp.json().catch(() => ({}));
      status.textContent = err.error || `失败 (${resp.status})`;
      return;
    }
    status.textContent = "✓ 已追踪";
    $("gameName").value = "";
    $("tagLine").value = "";
    await loadCards();
  } catch (e) {
    status.textContent = "网络错误";
  } finally {
    $("trackBtn").disabled = false;
  }
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

$("trackBtn").addEventListener("click", track);
$("tagLine").addEventListener("keydown", (e) => { if (e.key === "Enter") track(); });
$("gameName").addEventListener("keydown", (e) => { if (e.key === "Enter") track(); });
$("closeChart").addEventListener("click", () => $("chartPanel").classList.add("hidden"));
$("closeLive").addEventListener("click", () => $("livePanel").classList.add("hidden"));

loadMeta();
loadCards();
