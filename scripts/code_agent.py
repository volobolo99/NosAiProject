#!/usr/bin/env python3
"""Agente di implementazione: porta uno scheletro approvato allo stato implementato
usando il modello cloud previsto dalla politica di routing, senza far passare il
codice dal contesto di Claude.

Fase 3 e Fase 4 del protocollo in .claude/CLAUDE.md:
  - infilling dei corpi delle funzioni, firme immutabili;
  - pre-flight automatico prima di qualsiasi build o test.

Uso:
    python scripts/code_agent.py <incarico.json> [--no-preflight]

L'incarico e' un JSON (oggetto o lista) con:
    task_id         identificatore del task
    file            file da implementare, gia' presente come scheletro
    contract_file   contratto JSON di riferimento
    tier            "simple" -> DeepSeek V4 Flash, "complex" -> Qwen3 Coder 30B
    allow_stub      elenco di funzioni che possono restare non implementate
    extra_context   file da allegare al prompt in sola lettura
"""
from __future__ import annotations

import ast
import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path

import requests
from dotenv import load_dotenv

try:  # eseguito come scripts/code_agent.py oppure importato come scripts.code_agent
    import free_chain
except ModuleNotFoundError:  # pragma: no cover
    from scripts import free_chain

ROOT = Path(__file__).resolve().parents[1]
load_dotenv(ROOT / ".env")

OPENROUTER_URL = os.getenv("OPENROUTER_URL", "https://openrouter.ai/api/v1/chat/completions")
OPENROUTER_KEY = os.getenv("OPENROUTER_API_KEY", "")
DEEPSEEK_URL = os.getenv("DEEPSEEK_URL", "https://api.deepseek.com/chat/completions")
DEEPSEEK_KEY = os.getenv("DEEPSEEK_API_KEY", "")
# Groq serve modelli gratuiti su hardware LPU: il banco lo ha misurato piu' veloce
# dei modelli a pagamento del roster. Si indirizza con il prefisso "groq:".
GROQ_URL = os.getenv("GROQ_URL", "https://api.groq.com/openai/v1/chat/completions")
GROQ_KEY = os.getenv("GROQ_API_KEY", "")
GROQ_PREFISSO = "groq:"

PRICES = json.loads((ROOT / "scripts" / "model_prices.json").read_text(encoding="utf-8"))["models"]
LEDGER = ROOT / "data" / "ai_task_ledger.jsonl"
MAX_ATTEMPTS = 3
# Quante volte si aspetta il Retry-After prima di arrendersi su una quota.
ATTESE_QUOTA = 2

TIER_MODEL = {
    "local": "qwen2.5-coder:7b",
    # Gratuito e piu' veloce dei modelli a pagamento: misurato dal banco su Groq.
    "gratis": "groq:openai/gpt-oss-120b",
    "gratis_rapido": "groq:qwen/qwen3.8-27b",
    "simple": "deepseek-v4-flash",
    "complex": "qwen/qwen3-coder-30b-a3b-instruct",
}
OLLAMA_URL = os.getenv("OLLAMA_LOCAL_URL", "http://localhost:11434/api/generate")
PREFLIGHT_MODEL = "google/gemini-2.5-flash-lite"

FENCE = re.compile(r"`{3}[a-zA-Z]*\s*\n(.*?)\n\s*`{3}", re.S)
FORBIDDEN = re.compile(r"\b(TODO|FIXME|XXX|pass\s*#\s*implementare)\b")

SYSTEM_PROMPT = (
    "Sei un Senior Infiller. Ricevi il contratto di un modulo e il suo scheletro. "
    "Implementa TUTTI i corpi delle funzioni rispettando il contratto alla lettera. "
    "Le firme, i nomi, le annotazioni di tipo e l'ordine dei parametri non sono modificabili. "
    "Niente segnaposto, niente scorciatoie, niente dipendenze fuori da quelle dichiarate nel "
    "contratto. Restituisci il file Python completo e nient'altro."
)


# ---------------------------------------------------------------- chiamate API


def call_model(model: str, prompt: str, system_prompt: str, max_tokens: int = 8192):
    """Restituisce (testo, usage). DeepSeek passa solo dalla sua API nativa."""
    if ":" in model and "/" not in model:  # tag Ollama, per esempio qwen2.5-coder:7b
        response = requests.post(
            OLLAMA_URL,
            json={
                "model": model,
                "prompt": system_prompt + "\n\n" + prompt,
                "stream": False,
                "options": {"temperature": 0.1, "num_ctx": 16384, "num_predict": 4096},
            },
            timeout=1800,
        )
        response.raise_for_status()
        body = response.json()
        text = body.get("response", "")
        if not text.strip():
            raise RuntimeError("{} non ha prodotto contenuto".format(model))
        return text, {
            "prompt_tokens": body.get("prompt_eval_count", 0),
            "completion_tokens": body.get("eval_count", 0),
        }

    if model.startswith(GROQ_PREFISSO):
        if not GROQ_KEY:
            raise RuntimeError("GROQ_API_KEY non impostata")
        # Il prefisso indirizza il fornitore e non fa parte dell'identificativo.
        model = model[len(GROQ_PREFISSO):]
        url, key = GROQ_URL, GROQ_KEY
    elif model.startswith("deepseek"):
        if not DEEPSEEK_KEY:
            raise RuntimeError("DEEPSEEK_API_KEY non impostata")
        url, key = DEEPSEEK_URL, DEEPSEEK_KEY
    else:
        if "deepseek" in model.lower():
            raise RuntimeError("DeepSeek non passa da OpenRouter")
        if not OPENROUTER_KEY:
            raise RuntimeError("OPENROUTER_API_KEY non impostata")
        url, key = OPENROUTER_URL, OPENROUTER_KEY

    corpo = {
        "model": model,
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": prompt},
        ],
        "temperature": 0.1,
        "max_tokens": max_tokens,
    }
    if url == OPENROUTER_URL:
        # Un provider che risponde 200 con il contenuto vuoto ferma la catena:
        # l'elenco dei guasti e' quello gia' misurato dal banco in free_chain.
        corpo["provider"] = {"ignore": list(free_chain.PROVIDER_GUASTI)}

    # Una quota esaurita non e' un guasto: Groq rigenera i token in una quindicina
    # di secondi, quindi conviene aspettare invece di buttare via l'incarico.
    for tentativo in range(ATTESE_QUOTA + 1):
        response = requests.post(
            url,
            headers={"Authorization": "Bearer " + key, "Content-Type": "application/json"},
            json=corpo,
            timeout=900,
        )
        if response.status_code != 429:
            break
        if tentativo == ATTESE_QUOTA:
            raise RuntimeError(
                "{}: quota esaurita, 429 dopo {} attese".format(model, ATTESE_QUOTA)
            )
        time.sleep(float(response.headers.get("retry-after", 20)))

    response.raise_for_status()
    payload = response.json()
    text = payload["choices"][0]["message"]["content"] or ""
    if not text.strip():
        # Un contenuto vuoto e' quasi sempre il provider che tace, non il budget
        # esaurito dal ragionamento: la diagnosi deve dire quale dei due.
        raise RuntimeError(
            "{} non ha prodotto contenuto (provider: {}, max_tokens={})".format(
                model, payload.get("provider", "sconosciuto"), max_tokens
            )
        )
    return text, payload.get("usage", {})


def budget_token(model: str) -> int:
    """Tetto di token in uscita, per fornitore.

    DeepSeek ragiona prima di rispondere e serve un budget piu' largo del solo
    output. Groq applica un limite di 6000 token al minuto e rifiuta in partenza
    una richiesta il cui max_tokens, sommato al prompt, lo supererebbe.
    """
    if model.startswith(GROQ_PREFISSO):
        return 3500
    if model.startswith("deepseek"):
        return 16000
    return 8192


def cost_of(model: str, usage: dict) -> float:
    price = PRICES.get(model)
    if not price or price.get("input") is None:
        return 0.0
    return round(
        usage.get("prompt_tokens", 0) * price["input"]
        + usage.get("completion_tokens", 0) * price["output"],
        6,
    )


# ---------------------------------------------------------------- validazione


def extract_code(text: str) -> str:
    blocks = FENCE.findall(text)
    if blocks:
        return max(blocks, key=len).strip()
    return text.strip()


def signatures(source: str) -> dict:
    """Mappa nome qualificato -> firma normalizzata, per ogni funzione del modulo."""
    tree = ast.parse(source)
    found = {}

    def walk(node, prefix=""):
        for child in node.body:
            if isinstance(child, ast.ClassDef):
                walk(child, prefix + child.name + ".")
            elif isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef)):
                args = ast.unparse(child.args)
                returns = ast.unparse(child.returns) if child.returns else ""
                found[prefix + child.name] = "({}) -> {}".format(args, returns)

    walk(tree)
    return found


def dataclass_fields(source: str) -> dict:
    """Mappa classe -> elenco (campo, annotazione), per non perdere i campi dei contratti."""
    tree = ast.parse(source)
    found = {}
    for node in ast.walk(tree):
        if isinstance(node, ast.ClassDef):
            campi = [
                (stmt.target.id, ast.unparse(stmt.annotation))
                for stmt in node.body
                if isinstance(stmt, ast.AnnAssign) and isinstance(stmt.target, ast.Name)
            ]
            if campi:
                found[node.name] = campi
    return found


def validate_implementation(code: str, skeleton: str, task: dict):
    errors = []
    try:
        ast.parse(code)
    except SyntaxError as exc:
        return ["Sintassi non valida alla riga {}: {}".format(exc.lineno, exc.msg)]

    attese = signatures(skeleton)
    ottenute = signatures(code)
    mancanti = sorted(set(attese) - set(ottenute))
    if mancanti:
        errors.append("Funzioni scomparse rispetto allo scheletro: " + ", ".join(mancanti))
    alterate = sorted(n for n in set(attese) & set(ottenute) if attese[n] != ottenute[n])
    if alterate:
        dettaglio = "; ".join(
            "{}: atteso {} ottenuto {}".format(n, attese[n], ottenute[n]) for n in alterate
        )
        errors.append("Firme alterate rispetto allo scheletro: " + dettaglio)

    campi_attesi = dataclass_fields(skeleton)
    campi_ottenuti = dataclass_fields(code)
    for classe, campi in campi_attesi.items():
        if campi_ottenuti.get(classe) != campi:
            errors.append(
                "Campi alterati nella classe {}: atteso {} ottenuto {}".format(
                    classe, campi, campi_ottenuti.get(classe)
                )
            )

    consentiti = set(task.get("allow_stub", []))
    residui = []
    for node in ast.walk(ast.parse(code)):
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            if node.name in consentiti:
                continue
            corpo = [
                n
                for n in node.body
                if not (isinstance(n, ast.Expr) and isinstance(n.value, ast.Constant))
            ]
            if len(corpo) == 1 and isinstance(corpo[0], ast.Raise):
                if "NotImplementedError" in ast.dump(corpo[0]):
                    residui.append(node.name)
    if residui:
        errors.append("Corpi ancora non implementati: " + ", ".join(sorted(set(residui))))

    segnaposto = sorted({m.group(0) for m in FORBIDDEN.finditer(code)})
    if segnaposto:
        errors.append("Segnaposto presenti nel codice: " + ", ".join(segnaposto))

    return errors


def import_check(target: Path) -> list:
    """Importa il modulo in un processo separato: un errore di import non deve
    contaminare questo processo."""
    modulo = ".".join(target.relative_to(ROOT).with_suffix("").parts)
    proc = subprocess.run(
        [sys.executable, "-c", "import {}".format(modulo)],
        cwd=str(ROOT),
        capture_output=True,
        text=True,
    )
    if proc.returncode != 0:
        ultima = [r for r in proc.stderr.strip().splitlines() if r.strip()]
        return ["Import fallito: " + (ultima[-1] if ultima else "errore sconosciuto")]
    return []


def preflight(contract: str, code: str):
    """Fase 4: controllo economico prima di build e test."""
    prompt = "CONTRATTO:\n{}\n\nCODICE PRODOTTO:\n{}".format(contract, code)
    system = (
        "Sei il Controllore di Qualita' di Pre-Flight. Verifica se il codice rispetta il "
        "contratto: firme, precondizioni, postcondizioni e vincoli dichiarati. Rispondi con la "
        "sola parola APPROVED se e' impeccabile, altrimenti elenca in modo sintetico i problemi "
        "critici, uno per riga."
    )
    testo, usage = call_model(PREFLIGHT_MODEL, prompt, system, max_tokens=1024)
    approvato = testo.strip().upper().startswith("APPROVED")
    return approvato, testo.strip(), usage


# ---------------------------------------------------------------- esecuzione


def record(entry: dict) -> None:
    LEDGER.parent.mkdir(parents=True, exist_ok=True)
    with LEDGER.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(entry, ensure_ascii=False) + "\n")


def run(task: dict, do_preflight: bool) -> dict:
    target = ROOT / task["file"]
    skeleton = target.read_text(encoding="utf-8")
    contract = (ROOT / task["contract_file"]).read_text(encoding="utf-8")
    model = TIER_MODEL[task.get("tier", "simple")]

    contesto = ""
    for extra in task.get("extra_context", []):
        contesto += "\n\n--- {} ---\n{}".format(extra, (ROOT / extra).read_text(encoding="utf-8"))

    errors = []
    costo = 0.0
    chiamate = 0
    code = ""
    esito_preflight = ""

    for tentativo in range(1, MAX_ATTEMPTS + 1):
        prompt = "CONTRATTO:\n{}\n\nSCHELETRO DA IMPLEMENTARE ({}):\n{}".format(
            contract, task["file"], skeleton
        )
        if contesto:
            prompt += "\n\nCONTESTO DI SOLA LETTURA:" + contesto
        if errors:
            prompt += "\n\nIL TENTATIVO PRECEDENTE E' STATO RIFIUTATO. Correggi questi difetti:\n"
            prompt += "\n".join("- " + e for e in errors)
            prompt += "\n\nCodice rifiutato:\n" + code

        budget = budget_token(model)
        testo, usage = call_model(model, prompt, SYSTEM_PROMPT, max_tokens=budget)
        chiamate += 1
        costo += cost_of(model, usage)
        code = extract_code(testo)
        errors = validate_implementation(code, skeleton, task)

        if not errors:
            target.write_text(code.rstrip() + "\n", encoding="utf-8")
            errors = import_check(target)
            if errors:
                target.write_text(skeleton, encoding="utf-8")

        if not errors and task.get("tests_command"):
            # Fase 5: il modulo non e' implementato finche' i suoi test non passano.
            proc = subprocess.run(
                task["tests_command"],
                shell=True,
                cwd=str(ROOT),
                capture_output=True,
                text=True,
            )
            if proc.returncode != 0:
                coda = (proc.stdout + proc.stderr).strip().splitlines()
                errors = ["Test falliti:\n" + "\n".join(coda[-40:])]
                target.write_text(skeleton, encoding="utf-8")

        if not errors and do_preflight:
            approvato, esito_preflight, usage_pf = preflight(contract, code)
            costo += cost_of(PREFLIGHT_MODEL, usage_pf)
            if not approvato:
                errors = ["Pre-flight non approvato: " + esito_preflight[:500]]
                target.write_text(skeleton, encoding="utf-8")

        if not errors:
            break
        # L'ultimo tentativo respinto resta su disco per la diagnosi, fuori dal sorgente.
        scarto = ROOT / ".claude" / "tasks" / ("rejected_" + task["task_id"] + ".py")
        scarto.parent.mkdir(parents=True, exist_ok=True)
        scarto.write_text(code, encoding="utf-8")

    if not errors:
        status = "completed"
    elif chiamate < MAX_ATTEMPTS:
        status = "needs_revision"
    else:
        status = "blocked"

    record(
        {
            "task_id": task["task_id"],
            "model": model,
            "calls": chiamate,
            "estimated_cost_usd": round(costo, 6),
            "status": status,
            "file": task["file"],
            "words": len(code.split()),
        }
    )
    return {
        "status": status,
        "file": task["file"],
        "model": model,
        "calls": chiamate,
        "estimated_cost_usd": round(costo, 6),
        "preflight": esito_preflight[:200] if esito_preflight else "non eseguito",
        "missing_items": errors,
    }


def main() -> int:
    if len(sys.argv) < 2:
        print(json.dumps({"status": "blocked", "missing_items": ["incarico non fornito"]}))
        return 2
    payload = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    do_preflight = "--no-preflight" not in sys.argv
    tasks = payload if isinstance(payload, list) else [payload]
    results = []
    for task in tasks:
        try:
            results.append(run(task, do_preflight))
        except Exception as exc:  # l'incarico successivo non deve saltare
            results.append(
                {
                    "status": "blocked",
                    "file": task.get("file", "?"),
                    "missing_items": ["{}: {}".format(type(exc).__name__, exc)],
                }
            )
    print(json.dumps(results if len(results) > 1 else results[0], ensure_ascii=False, indent=2))
    return 0 if all(r["status"] == "completed" for r in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
