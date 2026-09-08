"""NosAi Traffic Inspector - every worker in one page, pushed live.

The Colab notebook already serves a page like this one, but from inside Colab:
it sees the 14B and nothing else, and it refreshes by reloading itself every
three seconds. This server runs on the PC, where the local Ollama, the DeepSeek
calls and the Claude sessions actually are, and pushes changes over SSE instead
of reloading.

Standard library only: the requests package used by the orchestrator is not
imported here, so the inspector keeps running when the MCP environment is not
installed.

Colab endpoint discovery follows resolve_colab_url in orchestrator_mcp.py - the
ntfy channel with poll=1, never a hard-coded tunnel name, because quick tunnels
change name at every restart.
"""

from __future__ import annotations

import json
import os
import queue
import threading
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

PORT = int(os.environ.get("NOSAI_INSPECTOR_PORT", "8787"))
REPO_ROOT = Path(__file__).resolve().parents[2]
EVENT_LOG = REPO_ROOT / "data" / "traffic" / "events.jsonl"

OLLAMA_LOCAL = "http://localhost:11434"
SYNC_CHANNEL = "nosai-worker-sync-volob"
NTFY_URL = "https://ntfy.sh/" + SYNC_CHANNEL + "/raw?poll=1&since=all"

CLAUDE_SESSIONS = (
    Path.home() / ".claude" / "projects" / "C--Users-volob-Desktop-NosAiProject"
)

LOCAL_POLL_SECONDS = 2.0
COLAB_POLL_SECONDS = 6.0
DISCOVERY_SECONDS = 60.0
CLAUDE_POLL_SECONDS = 4.0
EVENT_TAIL_SECONDS = 0.5
CLAUDE_ACTIVE_WINDOW = 120.0

MAX_EVENTS = 200


def _get_json(url: str, timeout: float):
    """A GET that answers with the parsed body, or a reason it could not."""
    request = urllib.request.Request(url, headers={"User-Agent": "nosai-inspector"})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return json.loads(response.read().decode("utf-8", "replace")), None
    except urllib.error.HTTPError as error:
        return None, "HTTP " + str(error.code)
    except urllib.error.URLError as error:
        return None, str(error.reason)
    except (TimeoutError, json.JSONDecodeError, OSError) as error:
        return None, str(error)


def _get_text(url: str, timeout: float):
    request = urllib.request.Request(url, headers={"User-Agent": "nosai-inspector"})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return response.read().decode("utf-8", "replace"), None
    except urllib.error.HTTPError as error:
        return None, "HTTP " + str(error.code)
    except urllib.error.URLError as error:
        return None, str(error.reason)
    except (TimeoutError, OSError) as error:
        return None, str(error)


def _agent(agent_id: str, name: str, host: str, models=None, polled: bool = True) -> dict:
    """One row of the board.

    `polled` says whether something asks this agent how it is. The two Ollama
    endpoints and the Claude session folder can be interrogated; a remote API
    cannot, so DeepSeek learns its state only from the calls that pass through
    the orchestrator - which is why its status must never be written by a poller
    that does not exist.
    """
    return {
        "id": agent_id,
        "name": name,
        "host": host,
        "status": "UNKNOWN",
        "detail": "non ancora interrogato" if polled else "nessuna chiamata osservata",
        "models": models or [],
        "loaded": [],
        "calls": 0,
        "tokens": 0,
        "lastSeen": None,
        "polled": polled,
    }


class State:
    """What every subscriber sees, guarded by one lock."""

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._subscribers: list[queue.Queue] = []
        self.agents: dict[str, dict] = {
            "local": _agent("local", "Qwen locale", "RTX 5060 - Ollama"),
            "colab": _agent("colab", "Qwen 14B Colab", "Tesla T4 - tunnel"),
            "deepseek": _agent(
                "deepseek", "DeepSeek Flash", "API remota", ["deepseek-v4-flash"],
                polled=False),
            "claude": _agent("claude", "Claude Code", "direttore e sotto-agenti"),
        }
        self.agents["colab"]["endpoint"] = None
        self.events: list[dict] = []

    def subscribe(self) -> queue.Queue:
        channel: queue.Queue = queue.Queue(maxsize=64)
        with self._lock:
            self._subscribers.append(channel)
        return channel

    def unsubscribe(self, channel: queue.Queue) -> None:
        with self._lock:
            if channel in self._subscribers:
                self._subscribers.remove(channel)

    def _publish(self, message: dict) -> None:
        payload = json.dumps(message, ensure_ascii=False)
        with self._lock:
            channels = list(self._subscribers)
        for channel in channels:
            try:
                channel.put_nowait(payload)
            except queue.Full:
                # A viewer that cannot keep up loses this delta and catches up on
                # its next reconnect: dropping beats blocking every other viewer.
                pass

    def update_agent(self, agent_id: str, **fields) -> None:
        with self._lock:
            agent = self.agents[agent_id]
            changed = {k: v for k, v in fields.items() if agent.get(k) != v}
            if not changed:
                return
            agent.update(changed)
            snapshot = dict(agent)
        self._publish({"kind": "agent", "agent": snapshot})

    def add_event(self, event: dict) -> None:
        snapshot = None
        with self._lock:
            self.events.append(event)
            del self.events[:-MAX_EVENTS]
            agent = self.agents.get(event.get("agent"))
            if agent is not None:
                starting = event.get("phase") == "start"
                if not starting:
                    agent["calls"] += 1
                    agent["tokens"] += int(event.get("tokens") or 0)
                    agent["lastSeen"] = event.get("at")
                if not agent.get("polled", True):
                    # Nothing else can tell this one apart from silence.
                    if starting:
                        agent["status"] = "ATTIVO"
                        agent["detail"] = "chiamata in corso"
                    elif event.get("ok"):
                        agent["status"] = "PRONTO"
                        agent["detail"] = "ultima chiamata riuscita"
                    else:
                        agent["status"] = "ERRORE"
                        agent["detail"] = str(event.get("error") or "chiamata fallita")
                snapshot = dict(agent)
        self._publish({"kind": "event", "event": event})
        if snapshot is not None:
            self._publish({"kind": "agent", "agent": snapshot})

    def snapshot(self) -> dict:
        with self._lock:
            return {
                "kind": "snapshot",
                "agents": [dict(a) for a in self.agents.values()],
                "events": list(self.events),
                "serverTime": time.time(),
            }


STATE = State()


def poll_local() -> None:
    """The Ollama on this machine: which models exist, which are resident."""
    while True:
        tags, tags_error = _get_json(OLLAMA_LOCAL + "/api/tags", timeout=4)
        if tags is None:
            STATE.update_agent(
                "local",
                status="OFFLINE",
                detail="Ollama non risponde: " + str(tags_error),
                models=[],
                loaded=[],
            )
        else:
            names = [m.get("name", "?") for m in tags.get("models", [])]
            running, _ = _get_json(OLLAMA_LOCAL + "/api/ps", timeout=4)
            loaded = [m.get("name", "?") for m in (running or {}).get("models", [])]
            STATE.update_agent(
                "local",
                status="ATTIVO" if loaded else "PRONTO",
                detail=(
                    str(len(loaded)) + " modello in memoria" if loaded
                    else str(len(names)) + " modelli installati, nessuno caricato"
                ),
                models=names,
                loaded=loaded,
            )
        time.sleep(LOCAL_POLL_SECONDS)


def resolve_colab_endpoint():
    """The tunnel the Colab cell published, or None when the channel is empty."""
    body, _ = _get_text(NTFY_URL, timeout=5)
    if not body:
        return None
    lines = [line.strip() for line in body.strip().split("\n") if line.strip()]
    for line in reversed(lines):
        if line.startswith("http"):
            return line.rstrip("/")
    return None


def poll_colab() -> None:
    endpoint = None
    discovered_at = 0.0
    while True:
        now = time.monotonic()
        if endpoint is None or now - discovered_at > DISCOVERY_SECONDS:
            found = resolve_colab_endpoint()
            if found and found != endpoint:
                endpoint = found
                STATE.update_agent("colab", endpoint=endpoint)
            discovered_at = now

        if not endpoint:
            STATE.update_agent(
                "colab",
                status="ASSENTE",
                detail="nessun tunnel pubblicato sul canale di sincronizzazione",
                models=[],
                loaded=[],
            )
        else:
            tags, tags_error = _get_json(endpoint + "/api/tags", timeout=8)
            if tags is None:
                STATE.update_agent(
                    "colab",
                    status="IRRAGGIUNGIBILE",
                    detail=endpoint + ": " + str(tags_error),
                    models=[],
                    loaded=[],
                )
            else:
                names = [m.get("name", "?") for m in tags.get("models", [])]
                running, _ = _get_json(endpoint + "/api/ps", timeout=8)
                loaded = [m.get("name", "?") for m in (running or {}).get("models", [])]
                STATE.update_agent(
                    "colab",
                    status="ATTIVO" if loaded else "PRONTO",
                    detail=(
                        str(len(loaded)) + " modello in memoria" if loaded
                        else str(len(names)) + " modelli disponibili sul tunnel"
                    ),
                    models=names,
                    loaded=loaded,
                )
        time.sleep(COLAB_POLL_SECONDS)


def poll_claude() -> None:
    """Claude sessions, by file activity only.

    The transcripts are consulted for their modification time and nothing else:
    the inspector reports that a session is working, never what it is saying.
    """
    while True:
        if not CLAUDE_SESSIONS.is_dir():
            STATE.update_agent(
                "claude",
                status="UNKNOWN",
                detail="cartella sessioni non trovata: " + str(CLAUDE_SESSIONS),
            )
        else:
            now = time.time()
            recent = []
            for path in CLAUDE_SESSIONS.glob("*.jsonl"):
                try:
                    modified = path.stat().st_mtime
                except OSError:
                    continue
                if now - modified <= CLAUDE_ACTIVE_WINDOW:
                    recent.append((now - modified, path.stem[:8], modified))
            recent.sort()
            if recent:
                STATE.update_agent(
                    "claude",
                    status="ATTIVO",
                    detail=(
                        str(len(recent)) + " sessioni attive negli ultimi "
                        + str(int(CLAUDE_ACTIVE_WINDOW)) + " s: "
                        + ", ".join(name for _, name, _ in recent[:4])
                    ),
                    lastSeen=recent[0][2],
                )
            else:
                STATE.update_agent(
                    "claude",
                    status="INATTIVO",
                    detail="nessuna sessione ha scritto negli ultimi 2 minuti",
                )
        time.sleep(CLAUDE_POLL_SECONDS)


def tail_events() -> None:
    """Follows the event log the orchestrator writes, across truncation."""
    position = 0
    signature = None
    while True:
        try:
            if EVENT_LOG.exists():
                stat = EVENT_LOG.stat()
                current = (stat.st_ino, stat.st_dev)
                if signature is not None and (current != signature or stat.st_size < position):
                    position = 0  # rotated or truncated: start over
                signature = current
                if stat.st_size > position:
                    with EVENT_LOG.open("r", encoding="utf-8", errors="replace") as handle:
                        handle.seek(position)
                        for line in handle:
                            line = line.strip()
                            if not line:
                                continue
                            try:
                                STATE.add_event(json.loads(line))
                            except json.JSONDecodeError:
                                continue
                        position = handle.tell()
        except OSError:
            pass
        time.sleep(EVENT_TAIL_SECONDS)


PAGE = r"""<!DOCTYPE html>
<html lang="it">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>NosAi Traffic Inspector</title>
<style>
  :root { color-scheme: dark; }
  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
         background: #0b0f19; color: #f8fafc; margin: 0; padding: 24px; }
  h1 { font-size: 20px; margin: 0 0 4px; }
  h2 { font-size: 12px; color: #94a3b8; text-transform: uppercase; letter-spacing: .05em; }
  .sub { color: #94a3b8; font-size: 13px; margin-bottom: 20px; display: flex;
         gap: 10px; align-items: center; flex-wrap: wrap; }
  .live { display: inline-flex; align-items: center; gap: 6px; }
  .dot { width: 8px; height: 8px; border-radius: 50%; background: #22c55e; }
  .dot.off { background: #ef4444; }
  .dot.pulse { animation: pulse 1.6s ease-in-out infinite; }
  @keyframes pulse { 0%,100% { opacity: 1 } 50% { opacity: .25 } }
  .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
          gap: 16px; margin-bottom: 24px; }
  .card { background: #131d31; border-radius: 10px; padding: 16px;
          border: 1px solid #1e293b; transition: border-color .3s; }
  .card.hit { border-color: #38bdf8; }
  .name { font-size: 13px; font-weight: 600; }
  .host { color: #64748b; font-size: 11px; margin-top: 2px; }
  .state { font-size: 20px; font-weight: bold; margin-top: 10px; }
  .ATTIVO { color: #22c55e } .PRONTO { color: #38bdf8 } .INATTIVO { color: #94a3b8 }
  .OFFLINE, .IRRAGGIUNGIBILE, .ASSENTE, .ERRORE { color: #f97316 } .UNKNOWN { color: #a78bfa }
  .detail { color: #94a3b8; font-size: 12px; margin-top: 8px; min-height: 32px; }
  .counters { display: flex; gap: 18px; margin-top: 10px; font-size: 10px;
              color: #64748b; text-transform: uppercase; }
  .counters b { color: #e2e8f0; font-size: 14px; display: block; text-transform: none; }
  .wrap { overflow-x: auto; }
  table { width: 100%; border-collapse: collapse; background: #131d31;
          border-radius: 10px; overflow: hidden; border: 1px solid #1e293b; }
  th, td { padding: 9px 12px; text-align: left; font-size: 12px;
           border-bottom: 1px solid #1e293b; white-space: nowrap; }
  th { color: #94a3b8; text-transform: uppercase; font-size: 10px; letter-spacing: .04em; }
  td.mono { font-variant-numeric: tabular-nums; color: #cbd5e1; }
  tr.fresh { animation: enter 1s ease-out; }
  @keyframes enter { from { background: #1d3a5c } to { background: transparent } }
  .ok { color: #22c55e } .ko { color: #f87171 } .run { color: #fbbf24 }
  .empty { color: #64748b; font-size: 13px; padding: 16px; white-space: normal; }
</style>
</head>
<body>
<h1>NosAi Traffic Inspector</h1>
<div class="sub">
  <span class="live"><span class="dot pulse" id="conn"></span><span id="connLabel">in diretta</span></span>
  <span>|</span><span id="clock">--</span>
  <span>|</span><span>spinto dal server, nessun ricaricamento di pagina</span>
</div>
<div class="grid" id="agents"></div>
<h2>Flusso</h2>
<div class="wrap">
<table>
  <thead><tr><th>Ora</th><th>Agente</th><th>Modello</th><th>Operazione</th>
  <th>Durata</th><th>Token</th><th>Esito</th></tr></thead>
  <tbody id="flow"><tr><td colspan="7" class="empty">
    In attesa del primo passaggio: ogni delega ai worker compare qui appena parte.
  </td></tr></tbody>
</table>
</div>
<script>
const agentsEl = document.getElementById('agents');
const flowEl = document.getElementById('flow');
const cards = new Map();

function esc(v) {
  return String(v == null ? '' : v).replace(/[&<>"']/g, c => (
    {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}
function fmtTime(v) {
  if (!v) return '--';
  return new Date(v * 1000).toLocaleTimeString('it-IT', {hour12: false});
}
function fmtAge(v) {
  if (!v) return 'mai';
  const s = Math.max(0, Math.round(Date.now() / 1000 - v));
  if (s < 60) return s + ' s fa';
  if (s < 3600) return Math.round(s / 60) + ' min fa';
  return Math.round(s / 3600) + ' h fa';
}

function renderAgent(a) {
  let card = cards.get(a.id);
  if (!card) {
    card = document.createElement('div');
    card.className = 'card';
    agentsEl.appendChild(card);
    cards.set(a.id, card);
  }
  const models = (a.loaded && a.loaded.length ? a.loaded : a.models) || [];
  const modelLine = models.length
    ? '<br><span style="color:#64748b">' + esc(models.slice(0, 3).join(', ')) + '</span>'
    : '';
  card.innerHTML =
    '<div class="name">' + esc(a.name) + '</div>' +
    '<div class="host">' + esc(a.host) + '</div>' +
    '<div class="state ' + esc(a.status) + '">' + esc(a.status) + '</div>' +
    '<div class="detail">' + esc(a.detail) + modelLine + '</div>' +
    '<div class="counters">' +
      '<span>chiamate<b>' + (a.calls || 0) + '</b></span>' +
      '<span>token<b>' + (a.tokens || 0).toLocaleString('it-IT') + '</b></span>' +
      '<span>ultima<b data-age="' + (a.lastSeen || '') + '">' + fmtAge(a.lastSeen) + '</b></span>' +
    '</div>';
}

function renderEvent(e, fresh) {
  const placeholder = flowEl.querySelector('.empty');
  if (placeholder) flowEl.innerHTML = '';
  // One row per call: the end of a call rewrites the row its start opened,
  // so a slow delegation stays a single line that changes from "in corso"
  // to its outcome instead of appearing twice.
  const existing = e.id ? flowEl.querySelector('tr[data-call="' + e.id + '"]') : null;
  const row = existing || document.createElement('tr');
  if (e.id) row.dataset.call = e.id;
  if (fresh) row.className = 'fresh';
  const outcome = e.phase === 'start'
    ? '<span class="run">in corso</span>'
    : (e.ok ? '<span class="ok">ok</span>'
            : '<span class="ko">' + esc(e.error || 'errore') + '</span>');
  row.innerHTML =
    '<td class="mono">' + fmtTime(e.at) + '</td>' +
    '<td>' + esc(e.agentName || e.agent || '?') + '</td>' +
    '<td class="mono">' + esc(e.model || '--') + '</td>' +
    '<td>' + esc(e.op || '--') + '</td>' +
    '<td class="mono">' + (e.durationMs != null ? (e.durationMs / 1000).toFixed(1) + ' s' : '--') + '</td>' +
    '<td class="mono">' + (e.tokens != null ? e.tokens : '--') + '</td>' +
    '<td>' + outcome + '</td>';
  if (!existing) flowEl.prepend(row);
  while (flowEl.children.length > 200) flowEl.lastChild.remove();
  const card = cards.get(e.agent);
  if (card && fresh) {
    card.classList.add('hit');
    setTimeout(() => card.classList.remove('hit'), 1400);
  }
}

function connect() {
  const source = new EventSource('/events');
  source.onopen = () => {
    document.getElementById('conn').className = 'dot pulse';
    document.getElementById('connLabel').textContent = 'in diretta';
  };
  source.onerror = () => {
    document.getElementById('conn').className = 'dot off';
    document.getElementById('connLabel').textContent = 'riconnessione...';
  };
  source.onmessage = (msg) => {
    const data = JSON.parse(msg.data);
    if (data.kind === 'snapshot') {
      agentsEl.innerHTML = '';
      cards.clear();
      data.agents.forEach(a => renderAgent(a));
      flowEl.innerHTML = '';
      if (!data.events.length) {
        flowEl.innerHTML = '<tr><td colspan="7" class="empty">' +
          'In attesa del primo passaggio: ogni delega ai worker compare qui appena parte.' +
          '</td></tr>';
      } else {
        data.events.forEach(e => renderEvent(e, false));
      }
    } else if (data.kind === 'agent') {
      renderAgent(data.agent);
    } else if (data.kind === 'event') {
      renderEvent(data.event, true);
    }
  };
}
connect();

setInterval(() => {
  document.getElementById('clock').textContent =
    new Date().toLocaleTimeString('it-IT', {hour12: false});
  document.querySelectorAll('[data-age]').forEach(el => {
    const v = parseFloat(el.dataset.age);
    if (v) el.textContent = fmtAge(v);
  });
}, 1000);
</script>
</body>
</html>
"""


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *_args) -> None:  # the page is the log
        pass

    def do_GET(self) -> None:  # noqa: N802 - name fixed by BaseHTTPRequestHandler
        if self.path.startswith("/events"):
            self._serve_events()
        elif self.path.startswith("/api/state"):
            self._serve_json(STATE.snapshot())
        elif self.path in ("/", "/index.html"):
            self._serve_page()
        else:
            self.send_error(404)

    def _serve_page(self) -> None:
        body = PAGE.encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _serve_json(self, payload: dict) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _serve_events(self) -> None:
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream; charset=utf-8")
        self.send_header("Cache-Control", "no-cache")
        self.send_header("Connection", "keep-alive")
        self.end_headers()
        channel = STATE.subscribe()
        try:
            self._send_sse(json.dumps(STATE.snapshot(), ensure_ascii=False))
            while True:
                try:
                    message = channel.get(timeout=15)
                except queue.Empty:
                    self.wfile.write(b": keepalive\n\n")
                    self.wfile.flush()
                    continue
                self._send_sse(message)
        except (BrokenPipeError, ConnectionResetError, OSError):
            pass
        finally:
            STATE.unsubscribe(channel)

    def _send_sse(self, payload: str) -> None:
        self.wfile.write(b"data: " + payload.encode("utf-8") + b"\n\n")
        self.wfile.flush()


def main() -> None:
    EVENT_LOG.parent.mkdir(parents=True, exist_ok=True)
    for worker in (poll_local, poll_colab, poll_claude, tail_events):
        threading.Thread(target=worker, daemon=True).start()
    server = ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
    server.daemon_threads = True
    print("NosAi Traffic Inspector: http://localhost:" + str(PORT))
    print("Eventi seguiti da: " + str(EVENT_LOG))
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
