"""Sending work to an agent from the console, and watching it arrive.

The inspector could only watch. This module gives the page the other half: a
prompt typed in the browser reaches a worker or a Claude session, and what comes
back is streamed token by token to whoever is looking.

Three kinds of destination, one Job shape for all of them:
  * Ollama on this PC        - HTTP, one JSON object per chunk
  * DeepSeek's API           - HTTP, SSE, usage delivered at the end
  * Claude Code, headless    - a child process with --output-format stream-json

Standard library only, like the rest of the inspector: this must keep working
when the MCP environment is not installed.

The prompt goes to `claude` over stdin, never as an argument: a command line has
quoting rules, a pipe does not.
"""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import threading
import time
import urllib.error
import urllib.request
import uuid

OLLAMA_URL = "http://localhost:11434/api/generate"
DEEPSEEK_URL = "https://api.deepseek.com/chat/completions"
OPENROUTER_URL = "https://openrouter.ai/api/v1/chat/completions"

# A worker call that has produced nothing for this long is not going to.
IDLE_TIMEOUT = 300.0


def api_key(name: str) -> str:
    return os.environ.get(name, "")


def catalogue() -> list[dict]:
    """The agents this console can address, and whether it actually can.

    `ready` false is not an error to hide: the page has to say why an agent
    cannot be commanded - a missing key is a fact about the machine, not a bug.
    """
    deepseek = bool(api_key("DEEPSEEK_API_KEY"))
    openrouter = bool(api_key("OPENROUTER_API_KEY"))
    return [
        {
            "id": "local-7b", "kind": "worker", "name": "Qwen 2.5 Coder 7B",
            "host": "RTX 5060 - Ollama locale", "model": "qwen2.5-coder:7b",
            "cost": "gratis", "ready": True,
            "why": "",
        },
        {
            "id": "local-worker", "kind": "worker", "name": "Qwen worker (Modelfile)",
            "host": "RTX 5060 - Ollama locale", "model": "qwen-worker",
            "cost": "gratis", "ready": True,
            "why": "",
        },
        {
            "id": "deepseek-chat", "kind": "worker", "name": "DeepSeek Chat",
            "host": "api.deepseek.com", "model": "deepseek-chat",
            "cost": "a pagamento", "ready": deepseek,
            "why": "" if deepseek else "DEEPSEEK_API_KEY non presente nell'ambiente",
        },
        {
            "id": "deepseek-reasoner", "kind": "worker", "name": "DeepSeek Reasoner",
            "host": "api.deepseek.com", "model": "deepseek-reasoner",
            "cost": "a pagamento", "ready": deepseek,
            "why": "" if deepseek else "DEEPSEEK_API_KEY non presente nell'ambiente",
        },
        {
            "id": "openrouter-32b", "kind": "worker", "name": "Qwen3 32B",
            "host": "openrouter.ai", "model": "qwen/qwen3-32b",
            "cost": "a pagamento", "ready": openrouter,
            "why": "" if openrouter else "OPENROUTER_API_KEY non presente nell'ambiente",
        },
        {
            "id": "claude-new", "kind": "claude", "name": "Nuovo agente Claude",
            "host": "claude -p, processo figlio", "model": "predefinito",
            "cost": "a pagamento", "ready": bool(shutil.which("claude")),
            "why": "" if shutil.which("claude") else "eseguibile `claude` non trovato nel PATH",
        },
    ]


class Job:
    """One request in flight, and everything the page needs to draw it."""

    def __init__(self, agent: str, agent_name: str, model: str, prompt: str,
                 session_id: str = "") -> None:
        self.id = uuid.uuid4().hex[:12]
        self.agent = agent
        self.agent_name = agent_name
        self.model = model
        self.prompt = prompt
        self.session_id = session_id
        self.started = time.time()
        self.ended = None
        self.text = ""
        self.trace = ""     # ragionamento del reasoner, o gli strumenti che Claude usa
        self.error = ""
        self.ok = None
        self.usage: dict = {}
        self.process: subprocess.Popen | None = None
        self.cancelled = False

    def record(self) -> dict:
        """The finished exchange, in the same shape the chat log already uses."""
        row = {
            "id": self.id,
            "at": self.started,
            "endedAt": self.ended or time.time(),
            "agent": self.agent,
            "agentName": self.agent_name,
            "model": self.model,
            "op": "console",
            "ok": bool(self.ok),
            "durationMs": int(((self.ended or time.time()) - self.started) * 1000),
            "error": self.error[:400],
            "prompt": self.prompt,
            "response": self.text,
            "trace": self.trace[:4000],
            "sessionId": self.session_id,
        }
        row.update(self.usage)
        return row


class Runner:
    """Runs jobs in their own threads and reports every step to a listener."""

    def __init__(self, publish, on_finished) -> None:
        self.publish = publish              # publish(dict) -> to every viewer
        self.on_finished = on_finished      # on_finished(record) -> persisted
        self.jobs: dict[str, Job] = {}
        self._lock = threading.Lock()

    # -- public -----------------------------------------------------------

    def start(self, agent_id: str, prompt: str, options: dict) -> dict:
        agents = {a["id"]: a for a in catalogue()}
        session_id = ""
        if agent_id.startswith("claude:"):
            session_id = agent_id.split(":", 1)[1]
            spec = {"id": agent_id, "kind": "claude", "name": "sessione " + session_id[:8],
                    "model": "predefinito", "ready": bool(shutil.which("claude")),
                    "why": "eseguibile `claude` non trovato nel PATH"}
        elif agent_id in agents:
            spec = agents[agent_id]
        else:
            return {"error": "agente sconosciuto: " + agent_id}
        if not spec.get("ready"):
            return {"error": spec.get("why") or "agente non disponibile"}

        job = Job(agent_id, spec["name"], spec.get("model", ""), prompt, session_id)
        with self._lock:
            self.jobs[job.id] = job
        self.publish({"kind": "job", "job": self._state(job)})

        if spec["kind"] == "claude":
            target = self._run_claude
        elif agent_id.startswith("local"):
            target = self._run_ollama
        elif agent_id.startswith("deepseek"):
            target = self._run_deepseek
        else:
            target = self._run_openrouter
        threading.Thread(target=self._guard, args=(target, job, options),
                         daemon=True).start()
        return {"jobId": job.id}

    def stop(self, job_id: str) -> dict:
        with self._lock:
            job = self.jobs.get(job_id)
        if job is None:
            return {"error": "job sconosciuto"}
        job.cancelled = True
        if job.process is not None and job.process.poll() is None:
            try:
                job.process.terminate()
            except OSError:
                pass
        return {"stopped": job_id}

    def running(self) -> list[dict]:
        with self._lock:
            return [self._state(j) for j in self.jobs.values() if j.ok is None]

    # -- plumbing ---------------------------------------------------------

    def _state(self, job: Job) -> dict:
        return {
            "id": job.id,
            "agent": job.agent,
            "agentName": job.agent_name,
            "model": job.model,
            "prompt": job.prompt,
            "at": job.started,
            "running": job.ok is None,
        }

    def _chunk(self, job: Job, text: str, channel: str = "text") -> None:
        if not text:
            return
        if channel == "text":
            job.text += text
        else:
            job.trace += text
        self.publish({"kind": "chunk", "jobId": job.id, "agent": job.agent,
                      "channel": channel, "text": text})

    def _guard(self, target, job: Job, options: dict) -> None:
        try:
            target(job, options)
            if job.ok is None:
                job.ok = not job.error
        except Exception as error:                     # noqa: BLE001 - reported, never raised at a viewer
            job.ok = False
            job.error = str(error)
        job.ended = time.time()
        record = job.record()
        # Il record arriva ai viewer per una sola via: on_finished lo scrive nel
        # registro, il tailer lo legge e lo pubblica. Pubblicarlo anche qui lo
        # farebbe comparire due volte nella stessa conversazione.
        self.publish({"kind": "jobEnd", "jobId": job.id, "ok": job.ok,
                      "error": job.error})
        self.on_finished(record)

    # -- destinations -----------------------------------------------------

    def _post(self, url: str, payload: dict, headers: dict):
        body = json.dumps(payload).encode("utf-8")
        request = urllib.request.Request(url, data=body, method="POST")
        request.add_header("Content-Type", "application/json")
        for key, value in headers.items():
            request.add_header(key, value)
        return urllib.request.urlopen(request, timeout=IDLE_TIMEOUT)

    def _run_ollama(self, job: Job, options: dict) -> None:
        payload = {
            "model": job.model,
            "prompt": job.prompt,
            "stream": True,
            "options": {"temperature": float(options.get("temperature", 0.2)),
                        "num_ctx": 12288},
        }
        try:
            stream = self._post(OLLAMA_URL, payload, {})
        except (urllib.error.URLError, OSError) as error:
            job.ok, job.error = False, "Ollama non raggiungibile: " + str(error)
            return
        with stream:
            for line in stream:
                if job.cancelled:
                    job.ok, job.error = False, "fermato dall'operatore"
                    return
                line = line.decode("utf-8", "replace").strip()
                if not line:
                    continue
                try:
                    row = json.loads(line)
                except json.JSONDecodeError:
                    continue
                self._chunk(job, row.get("response") or "")
                if row.get("done"):
                    job.usage = {
                        "promptTokens": row.get("prompt_eval_count", 0),
                        "completionTokens": row.get("eval_count", 0),
                        "tokens": (row.get("prompt_eval_count", 0)
                                   + row.get("eval_count", 0)),
                        "channel": "LOCAL_5060",
                        "free": True,
                    }
                    job.ok = True
                    return
        job.ok = True

    def _run_openai_style(self, job: Job, url: str, headers: dict,
                          payload: dict) -> None:
        try:
            stream = self._post(url, payload, headers)
        except urllib.error.HTTPError as error:
            detail = error.read().decode("utf-8", "replace")[:300]
            job.ok, job.error = False, f"HTTP {error.code}: {detail}"
            return
        except (urllib.error.URLError, OSError) as error:
            job.ok, job.error = False, str(error)
            return
        usage = {}
        with stream:
            for raw in stream:
                if job.cancelled:
                    job.ok, job.error = False, "fermato dall'operatore"
                    return
                raw = raw.decode("utf-8", "replace").strip()
                if not raw.startswith("data:"):
                    continue
                data = raw[5:].strip()
                if data == "[DONE]":
                    break
                try:
                    row = json.loads(data)
                except json.JSONDecodeError:
                    continue
                if row.get("usage"):
                    usage = row["usage"]
                for choice in row.get("choices") or []:
                    delta = choice.get("delta") or {}
                    self._chunk(job, delta.get("content") or "")
                    self._chunk(job, delta.get("reasoning_content") or "", "reasoning")
        job.usage = {
            "promptTokens": usage.get("prompt_tokens", 0),
            "completionTokens": usage.get("completion_tokens", 0),
            "reasoningTokens": ((usage.get("completion_tokens_details") or {})
                                .get("reasoning_tokens", 0)),
            "cacheHitTokens": usage.get("prompt_cache_hit_tokens", 0),
            "tokens": usage.get("total_tokens", 0),
            "channel": job.agent.upper().replace("-", "_"),
            "free": False,
        }
        job.ok = True

    def _run_deepseek(self, job: Job, options: dict) -> None:
        self._run_openai_style(
            job, DEEPSEEK_URL,
            {"Authorization": "Bearer " + api_key("DEEPSEEK_API_KEY")},
            {
                "model": job.model,
                "messages": [{"role": "user", "content": job.prompt}],
                "stream": True,
                "stream_options": {"include_usage": True},
            })

    def _run_openrouter(self, job: Job, options: dict) -> None:
        self._run_openai_style(
            job, OPENROUTER_URL,
            {"Authorization": "Bearer " + api_key("OPENROUTER_API_KEY")},
            {
                "model": job.model,
                "messages": [{"role": "user", "content": job.prompt}],
                "stream": True,
                "stream_options": {"include_usage": True},
            })

    def _run_claude(self, job: Job, options: dict) -> None:
        exe = shutil.which("claude")
        if not exe:
            job.ok, job.error = False, "eseguibile `claude` non trovato"
            return
        command = ["cmd", "/c", exe, "-p", "--output-format", "stream-json", "--verbose"]
        if job.session_id:
            command += ["--resume", job.session_id]
        model = options.get("model") or ""
        if model and model != "predefinito":
            command += ["--model", model]
        if options.get("canEdit"):
            # Chosen in the page, one job at a time: an agent that may write is
            # never the default.
            command += ["--permission-mode", "acceptEdits"]
        job.process = subprocess.Popen(
            command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.PIPE, text=True, encoding="utf-8", errors="replace",
            cwd=options.get("cwd") or None,
        )
        try:
            job.process.stdin.write(job.prompt)
            job.process.stdin.close()
        except OSError as error:
            job.ok, job.error = False, "prompt non consegnato: " + str(error)
            return
        for line in job.process.stdout:
            if job.cancelled:
                job.ok, job.error = False, "fermato dall'operatore"
                return
            line = line.strip()
            if not line:
                continue
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                self._chunk(job, line + "\n")
                continue
            self._absorb_claude(job, row)
        job.process.wait()
        if job.ok is None:
            stderr = (job.process.stderr.read() or "").strip()
            if job.process.returncode != 0:
                job.ok = False
                job.error = stderr[:400] or ("uscita " + str(job.process.returncode))
            else:
                job.ok = True

    def _absorb_claude(self, job: Job, row: dict) -> None:
        kind = row.get("type")
        if kind == "system" and row.get("subtype") == "init":
            job.session_id = row.get("session_id") or job.session_id
            self.publish({"kind": "job", "job": self._state(job)})
            return
        if kind == "assistant":
            message = row.get("message") or {}
            for block in message.get("content") or []:
                if not isinstance(block, dict):
                    continue
                if block.get("type") == "text":
                    self._chunk(job, block.get("text") or "")
                elif block.get("type") == "tool_use":
                    args = block.get("input") or {}
                    hint = (args.get("description") or args.get("file_path")
                            or args.get("command") or args.get("pattern") or "")
                    self._chunk(job, "\n[" + str(block.get("name")) + "] "
                                + str(hint)[:120] + "\n", "tool")
            return
        if kind == "result":
            usage = row.get("usage") or {}
            job.usage = {
                "promptTokens": usage.get("input_tokens", 0),
                "completionTokens": usage.get("output_tokens", 0),
                "cacheHitTokens": usage.get("cache_read_input_tokens", 0),
                "cacheWriteTokens": usage.get("cache_creation_input_tokens", 0),
                "tokens": (usage.get("input_tokens", 0) + usage.get("output_tokens", 0)),
                "costUsd": row.get("total_cost_usd"),
                "turns": row.get("num_turns"),
                "channel": "CLAUDE_HEADLESS",
                "free": False,
            }
            job.session_id = row.get("session_id") or job.session_id
            if row.get("is_error"):
                job.ok = False
                job.error = str(row.get("result") or "errore riportato da claude")
            else:
                job.ok = True
                if not job.text:
                    job.text = str(row.get("result") or "")
