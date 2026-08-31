namespace NosAi.Runtime.Testing;

/// <summary>
/// The operator's test page: every test known to the repository, and what it saw.
/// </summary>
/// <remarks>
/// The design rule for this page is the project's rule: never show certainty that
/// does not exist. A test that has never run is grey and says so, a result carries
/// its age, and a suite that could not execute says why instead of disappearing.
/// </remarks>
internal static class TestConsoleHtml
{
    public static string Render() => """
<!DOCTYPE html>
<html lang="it">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>NosAi — Console dei test</title>
  <style>
    body { font-family: Segoe UI, sans-serif; background:#0f172a; color:#f8fafc; margin:0; padding:24px; }
    a { color:#38bdf8; }
    h1 { font-size:22px; margin:0 0 4px; }
    .muted { color:#94a3b8; font-size:13px; margin:0 0 16px; }
    .bar { display:flex; flex-wrap:wrap; gap:8px; align-items:center; margin:16px 0; }
    button { background:#0284c7; color:white; border:0; padding:9px 14px; border-radius:6px;
             cursor:pointer; font-weight:600; font-size:13px; }
    button:disabled { background:#334155; color:#64748b; cursor:not-allowed; }
    .tiles { display:grid; grid-template-columns:repeat(auto-fit,minmax(130px,1fr)); gap:12px; margin:16px 0; }
    .tile { background:#1e293b; border:1px solid #334155; border-radius:10px; padding:12px; }
    .tile .n { font-size:24px; font-weight:700; }
    .tile .l { font-size:11px; color:#94a3b8; letter-spacing:.4px; text-transform:uppercase; }
    .pass .n { color:#4ade80; } .fail .n { color:#f87171; }
    .notrun .n { color:#94a3b8; } .skip .n { color:#fbbf24; }
    .status { background:#1e293b; border:1px solid #334155; border-radius:8px; padding:10px 14px;
              font-size:13px; margin-bottom:12px; }
    .running { border-color:#0284c7; color:#7dd3fc; }
    input[type=text] { background:#0b1220; border:1px solid #334155; color:#f8fafc;
                       padding:8px 10px; border-radius:6px; font-size:13px; min-width:220px; }
    table { width:100%; border-collapse:collapse; font-size:13px; }
    th { text-align:left; color:#94a3b8; font-weight:600; font-size:11px; letter-spacing:.5px;
         text-transform:uppercase; padding:8px; border-bottom:1px solid #334155; position:sticky; top:0;
         background:#0f172a; }
    td { padding:8px; border-bottom:1px solid #1e293b; vertical-align:top; }
    tr.t:hover { background:#16233b; }
    .badge { font-size:10px; font-weight:700; letter-spacing:.5px; padding:3px 7px; border-radius:4px;
             white-space:nowrap; }
    .b-Passed { background:#14532d; color:#86efac; }
    .b-Failed, .b-Errored { background:#7f1d1d; color:#fca5a5; }
    .b-Skipped { background:#78350f; color:#fcd34d; }
    .b-NotRun { background:#1e293b; color:#94a3b8; border:1px solid #334155; }
    .name { font-family: Consolas, monospace; font-size:12px; }
    .suite { color:#7dd3fc; font-size:11px; }
    .age { color:#64748b; font-size:11px; }
    .obs { margin-top:6px; font-size:11px; }
    .obs div { padding:2px 0; color:#cbd5e1; }
    .obs .k { color:#94a3b8; }
    .src { font-size:9px; letter-spacing:.5px; padding:1px 5px; border-radius:3px; margin-left:6px; }
    .s-LIVE { background:#0c4a6e; color:#7dd3fc; }
    .s-SIMULATED { background:#4c1d95; color:#c4b5fd; }
    .s-UNKNOWN { background:#334155; color:#94a3b8; }
    .s-DERIVED { background:#164e63; color:#67e8f9; }
    .s-CACHED { background:#374151; color:#d1d5db; }
    pre.msg { background:#020617; color:#fca5a5; padding:8px; border-radius:6px; font-size:11px;
              margin:6px 0 0; white-space:pre-wrap; max-height:170px; overflow:auto; }
    .empty { color:#64748b; font-style:italic; font-size:11px; }
  </style>
</head>
<body>
  <h1>Console dei test</h1>
  <p class="muted">
    Ogni test conosciuto dal repository: xUnit, pytest e le certificazioni di gate.
    L'elenco è <strong>scoperto</strong>, non scritto a mano, quindi un test nuovo compare qui da solo.
    Un test mai eseguito resta grigio: «non controllato» e «funziona» non sono la stessa cosa.
    · <a href="/">torna alla dashboard</a>
  </p>

  <div class="tiles">
    <div class="tile"><div class="n" id="t-total">…</div><div class="l">Totale</div></div>
    <div class="tile pass"><div class="n" id="t-pass">…</div><div class="l">Superati</div></div>
    <div class="tile fail"><div class="n" id="t-fail">…</div><div class="l">Falliti</div></div>
    <div class="tile skip"><div class="n" id="t-skip">…</div><div class="l">Ignorati</div></div>
    <div class="tile notrun"><div class="n" id="t-notrun">…</div><div class="l">Mai eseguiti</div></div>
    <div class="tile"><div class="n" id="t-obs">…</div><div class="l">Con dati</div></div>
  </div>

  <div class="status" id="status">…</div>

  <div class="bar" id="targets"></div>

  <div class="bar">
    <input type="text" id="filter" placeholder="filtra per nome, suite o esito…" oninput="render()">
    <button onclick="setOnly('')" style="background:#334155">Tutti</button>
    <button onclick="setOnly('Failed')" style="background:#7f1d1d">Solo falliti</button>
    <button onclick="setOnly('NotRun')" style="background:#334155">Solo mai eseguiti</button>
  </div>

  <table>
    <thead>
      <tr><th style="width:96px">Esito</th><th>Test</th><th style="width:90px">Durata</th>
          <th style="width:150px">Eseguito</th></tr>
    </thead>
    <tbody id="rows"></tbody>
  </table>

<script>
  let DATA = { summary:{}, state:{}, targets:[], tests:[] };

  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[c];
    });
  }

  function age(seconds) {
    if (seconds == null) return '<span class="age">mai</span>';
    var s = Math.floor(seconds);
    if (s < 60)    return '<span class="age">' + s + 's fa</span>';
    if (s < 3600)  return '<span class="age">' + Math.floor(s/60) + 'm fa</span>';
    if (s < 86400) return '<span class="age">' + Math.floor(s/3600) + 'h fa</span>';
    return '<span class="age">' + Math.floor(s/86400) + 'g fa</span>';
  }

  function setOnly(v) { document.getElementById('filter').value = v; render(); }

  async function start(target) {
    const res = await fetch('/api/tests/run', { method:'POST', body: JSON.stringify({ target: target }) });
    const body = await res.json();
    if (!body.started) alert('Non avviato: ' + body.reason);
    refresh();
  }

  function render() {
    const q = document.getElementById('filter').value.toLowerCase();
    const s = DATA.summary || {};
    document.getElementById('t-total').textContent  = s.total ?? '—';
    document.getElementById('t-pass').textContent   = s.passed ?? '—';
    document.getElementById('t-fail').textContent   = s.failed ?? '—';
    document.getElementById('t-skip').textContent   = s.skipped ?? '—';
    document.getElementById('t-notrun').textContent = s.notRun ?? '—';
    document.getElementById('t-obs').textContent    = s.withObservations ?? '—';

    const st = DATA.state || {};
    const box = document.getElementById('status');
    box.className = 'status' + (st.running ? ' running' : '');
    if (st.running) {
      box.textContent = 'In esecuzione: ' + st.target + ' — avviato ' +
        (st.startedAtUtc ? new Date(st.startedAtUtc).toLocaleTimeString() : '?') +
        '. I risultati compaiono man mano.';
    } else if (st.lastError) {
      box.textContent = 'Ultima esecuzione terminata con errore: ' + st.lastError;
    } else if (st.lastFinishedAtUtc) {
      box.textContent = 'Nessuna esecuzione in corso. Ultima conclusa alle ' +
        new Date(st.lastFinishedAtUtc).toLocaleTimeString() + '.';
    } else {
      box.textContent = 'Nessuna esecuzione in questa sessione. I risultati mostrati, se presenti, ' +
        'vengono dall’ultima volta e portano la loro età.';
    }

    const bar = document.getElementById('targets');
    if (bar.childElementCount !== (DATA.targets || []).length) {
      bar.innerHTML = (DATA.targets || []).map(function (t) {
        return '<button onclick="start(\'' + t.key + '\')">Esegui: ' + esc(t.label) + '</button>';
      }).join('');
    }
    Array.prototype.forEach.call(bar.querySelectorAll('button'), function (b) { b.disabled = !!st.running; });

    const rows = (DATA.tests || []).filter(function (t) {
      if (!q) return true;
      return (t.name + ' ' + t.suite + ' ' + t.outcome).toLowerCase().indexOf(q) >= 0;
    });

    document.getElementById('rows').innerHTML = rows.map(function (t) {
      var obs = (t.observations || []).map(function (o) {
        return '<div><span class="k">' + esc(o.key) + ':</span> ' + esc(o.value) +
               '<span class="src s-' + esc(o.source) + '">' + esc(o.source) + '</span>' +
               (o.note ? ' <span class="age">' + esc(o.note) + '</span>' : '') + '</div>';
      }).join('');

      if (!obs) {
        obs = t.outcome === 'NotRun'
          ? '<div class="empty">Nessun dato: il test non è mai stato eseguito qui. Premi «Esegui» per rilevarlo ora.</div>'
          : '<div class="empty">Eseguito senza osservazioni registrate.</div>';
      }

      return '<tr class="t"><td><span class="badge b-' + esc(t.outcome) + '">' +
             esc(t.outcome.toUpperCase()) + '</span></td>' +
             '<td><div class="name">' + esc(t.name) + '</div>' +
             '<div class="suite">' + esc(t.suite) + '</div>' +
             '<div class="obs">' + obs + '</div>' +
             (t.message ? '<pre class="msg">' + esc(t.message) + '</pre>' : '') + '</td>' +
             '<td>' + (t.ranAtUtc ? t.durationMs + ' ms' : '—') + '</td>' +
             '<td>' + age(t.ageSeconds) + '</td></tr>';
    }).join('') || '<tr><td colspan="4" class="empty">Nessun test corrisponde al filtro.</td></tr>';
  }

  async function refresh() {
    try {
      DATA = await (await fetch('/api/tests')).json();
      render();
    } catch (e) {
      document.getElementById('status').textContent = 'Console dei test non raggiungibile.';
    }
  }

  refresh();
  setInterval(refresh, 2000);
</script>
</body>
</html>
""";
}
