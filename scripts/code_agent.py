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
import textwrap
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
    # I gratuiti Groq hanno una finestra di 6000 token al minuto complessivi:
    # prompt piu' max_tokens la supera e Groq risponde 413. Per un file che va
    # riemesso intero servono i gratuiti OpenRouter, che prendono 8192 di budget.
    # Entrambi hanno superato i due banchi: vedi scripts/free_roster.json.
    "gratis_grande": "nex-agi/nex-n2.5-mini:free",
    "gratis_lento": "nvidia/nemotron-3-super-120b-a12b:free",
    # Gratuito specializzato sul codice, 27 s misurati, banchi A e B pieni.
    "gratis_codice": "cohere/north-mini-code:free",
    "gratis_visione": "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free",
    "simple": "deepseek-v4-flash",
    "complex": "qwen/qwen3-coder-30b-a3b-instruct",
}
OLLAMA_URL = os.getenv("OLLAMA_LOCAL_URL", "http://localhost:11434/api/generate")
PREFLIGHT_MODEL = "google/gemini-2.5-flash-lite"

FENCE = re.compile(r"`{3}[a-zA-Z]*\s*\n(.*?)\n\s*`{3}", re.S)
FORBIDDEN = re.compile(r"\b(TODO|FIXME|XXX|pass\s*#\s*implementare)\b")

# Suffisso -> grammatica, la stessa mappa di scripts/build_function_index.py.
LINGUAGGI = {".py": "python", ".cs": "c_sharp"}

# Il segnaposto del C#, equivalente di raise NotImplementedError.
NON_IMPLEMENTATO_CS = re.compile(r"throw\s+new\s+NotImplementedException")

# Letterali stringa Python, per escluderli dalla ricerca dei segnaposto: un
# segnaposto scritto dentro una stringa non e' un promemoria lasciato a meta',
# ed e' cosi' che questo file bocciava se stesso quando lo si dava in pasto
# alla catena (il commento stesso, non uno spuntato dal modello, cadeva sotto
# FORBIDDEN perche' un commento non e' una stringa e non viene ripulito).
STRINGHE = re.compile(r"(?:[rbuRBU]{0,2})('''|\"\"\"|'|\")(?:\\.|(?!\1).)*\1", re.S)

SYSTEM_PROMPT = (
    "Sei un Senior Infiller. Ricevi il contratto di un modulo e il suo scheletro. "
    "Implementa TUTTI i corpi delle funzioni rispettando il contratto alla lettera. "
    "Le firme, i nomi, le annotazioni di tipo e l'ordine dei parametri non sono modificabili. "
    "Niente segnaposto, niente scorciatoie, niente dipendenze fuori da quelle dichiarate nel "
    "contratto. Restituisci il file Python completo e nient'altro."
)

SYSTEM_PROMPT_CSHARP = (
    "Sei un Senior Infiller. Ricevi il contratto di un modulo e il suo scheletro. "
    "Implementa TUTTI i corpi dei metodi rispettando il contratto alla lettera. "
    "Le firme, i nomi, i tipi di ritorno e l'ordine dei parametri non sono modificabili. "
    "Non modificare namespace, direttive using, modificatori di accesso ne' la gerarchia "
    "delle classi. Niente segnaposto, niente scorciatoie, niente dipendenze fuori da quelle "
    "dichiarate nel contratto. Restituisci il file C# completo e nient'altro."
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


def _intervalli_funzioni(source: str) -> dict[str, tuple[int, int]]:
    """Per ogni funzione di primo livello e metodo dentro una classe, le righe
    che occupa, decoratori inclusi.

    Un nome che esiste sia come funzione di modulo sia come metodo di una
    classe risolve sulla funzione di modulo: non e' un'ambiguita'. Lo stesso
    nome di metodo su due classi diverse lo e', e solleva ValueError. Le
    chiusure (funzioni dentro un'altra funzione) non si raccolgono: si
    cammina solo dentro le ClassDef, mai dentro il body di una funzione.
    """
    testo = source
    prima_riga_non_vuota = next((riga for riga in testo.splitlines() if riga.strip()), "")
    if prima_riga_non_vuota[:1] in (" ", "\t"):
        testo = textwrap.dedent(testo)

    albero = ast.parse(testo)
    di_modulo: dict[str, tuple[int, int]] = {}
    di_metodo: dict[str, tuple[int, int]] = {}
    classe_del_metodo: dict[str, str] = {}

    def cammina_classe(nodo_classe: ast.ClassDef, nome_classe: str) -> None:
        for figlio in nodo_classe.body:
            if isinstance(figlio, (ast.FunctionDef, ast.AsyncFunctionDef)):
                vista_su = classe_del_metodo.get(figlio.name)
                if vista_su is not None and vista_su != nome_classe:
                    raise ValueError(
                        "Nome di metodo ambiguo fra piu' classi: " + figlio.name)
                classe_del_metodo[figlio.name] = nome_classe
                inizio = min([figlio.lineno] + [d.lineno for d in figlio.decorator_list])
                di_metodo[figlio.name] = (inizio, figlio.end_lineno)
            elif isinstance(figlio, ast.ClassDef):
                cammina_classe(figlio, figlio.name)

    for nodo in albero.body:
        if isinstance(nodo, (ast.FunctionDef, ast.AsyncFunctionDef)):
            inizio = min([nodo.lineno] + [d.lineno for d in nodo.decorator_list])
            di_modulo[nodo.name] = (inizio, nodo.end_lineno)
        elif isinstance(nodo, ast.ClassDef):
            cammina_classe(nodo, nodo.name)

    return {**di_metodo, **di_modulo}


def innesta_funzioni(skeleton: str, risposta: str, nomi, linguaggio: str = "python") -> str:
    """Lo scheletro con le sole funzioni dichiarate sostituite da quelle della risposta.

    Serve per i file troppo grandi da riemettere interi: su model_scout.py, 26.693
    byte, l'uscita necessaria sfiorava il budget e il modello emetteva la sola
    funzione cambiata, che il validatore respingeva come quindici funzioni scomparse.

    Il perimetro e' l'incarico: cio' che il modello manda e non era dichiarato viene
    ignorato. Il file risultante viene poi validato per intero come sempre, quindi
    le garanzie non cambiano.

    ``linguaggio`` sceglie quale rilevatore di intervalli usare: "python" (default,
    ast.parse) o "c_sharp" (tree_sitter, _intervalli_funzioni_csharp). Il default
    mantiene invariata ogni chiamata esistente, incluse quelle nei test.
    """
    if not nomi:
        return skeleton

    rileva = _intervalli_funzioni_csharp if linguaggio == "c_sharp" else _intervalli_funzioni
    nuove = rileva(risposta)   # solleva SyntaxError se la risposta e' rotta
    vecchie = rileva(skeleton)

    assenti_risposta = [n for n in nomi if n not in nuove]
    if assenti_risposta:
        raise ValueError(
            "Funzioni dichiarate nell'incarico e assenti dalla risposta: "
            + ", ".join(sorted(assenti_risposta)))
    assenti_scheletro = [n for n in nomi if n not in vecchie]
    if assenti_scheletro:
        raise ValueError(
            "Funzioni dichiarate nell'incarico e assenti dallo scheletro: "
            + ", ".join(sorted(assenti_scheletro))
            + ". La modifica parziale sostituisce, non aggiunge: per una funzione nuova "
              "serve un incarico senza solo_funzioni.")

    righe_vecchie = skeleton.splitlines(keepends=True)
    righe_nuove = risposta.splitlines(keepends=True)

    # Dal basso verso l'alto, cosi' gli indici delle righe ancora da sostituire
    # non si spostano.
    for nome in sorted(nomi, key=lambda n: vecchie[n][0], reverse=True):
        da, a = vecchie[nome]
        nda, na = nuove[nome]
        blocco = righe_nuove[nda - 1:na]
        if blocco and not blocco[-1].endswith("\n"):
            blocco[-1] += "\n"
        righe_vecchie[da - 1:a] = blocco

    return "".join(righe_vecchie)


def prompt_parziale(contract: str, nome_file: str, skeleton: str, nomi) -> str:
    """Il prompt che chiede SOLO le funzioni dichiarate, non il file intero."""
    elenco = ", ".join(nomi)
    return (
        "CONTRATTO:\n{}\n\n"
        "FILE ESISTENTE ({}):\n{}\n\n"
        "PERIMETRO: restituisci SOLO queste funzioni, complete e nella loro forma "
        "definitiva: {}.\n"
        "NON restituire il file intero. NON restituire le altre funzioni. Non "
        "cambiare le firme. Il resto del file viene conservato automaticamente."
    ).format(contract, nome_file, skeleton, elenco)


def linguaggio_del_file(target: Path) -> str:
    """Grammatica da usare per validare il file, dedotta dal suffisso.

    Solleva ValueError su un suffisso ignoto: validare con il parser sbagliato
    e' peggio che fermarsi, perche' produce un verdetto senza significato.
    """
    suffisso = Path(target).suffix.lower()
    if suffisso not in LINGUAGGI:
        raise ValueError("Nessuna grammatica per il suffisso {}".format(suffisso or "(nessuno)"))
    return LINGUAGGI[suffisso]


def _senza_stringhe(code: str) -> str:
    """Il codice con i letterali stringa svuotati, per cercare i segnaposto.

    Un segnaposto scritto dentro una stringa non e' un promemoria lasciato a
    meta': e' dato. I commenti restano, quindi un vero segnaposto scritto
    come commento viene ancora bocciato.
    """
    return STRINGHE.sub('""', code)


def _firme_csharp(source: str):
    """Per ogni classe C#, i suoi metodi come (nome, parametri, tipo di ritorno).

    Usa tree_sitter con la grammatica c_sharp, la stessa scelta di
    scripts/build_function_index.py. Solleva SyntaxError quando l'albero contiene
    nodi ERROR o mancanti, che e' il modo in cui tree_sitter segnala la sintassi rotta.
    """
    import tree_sitter_c_sharp
    from tree_sitter import Language, Parser

    dati = source.encode("utf-8")
    albero = Parser(Language(tree_sitter_c_sharp.language())).parse(dati)
    if albero.root_node.has_error:
        raise SyntaxError("C#: l'albero contiene nodi ERROR o mancanti")

    def testo(nodo):
        return dati[nodo.start_byte:nodo.end_byte].decode("utf-8", "replace")

    def normalizza(valore):
        return re.sub(r"\s+", " ", valore).strip()

    def nome_di(nodo):
        for figlio in nodo.children:
            if figlio.type == "identifier":
                return testo(figlio)
        return "?"

    def firma_metodo(nodo):
        """Il nome del metodo e' l'identificatore seguito dalla lista parametri."""
        figli = list(nodo.children)
        for indice, figlio in enumerate(figli):
            successivo = figli[indice + 1] if indice + 1 < len(figli) else None
            if figlio.type == "identifier" and successivo is not None \
                    and successivo.type == "parameter_list":
                precedente = figli[indice - 1] if indice else None
                ritorno = normalizza(testo(precedente)) if precedente is not None \
                    and precedente.type not in ("modifier", "attribute_list") else ""
                return testo(figlio), normalizza(testo(successivo)), ritorno
        return nome_di(nodo), "", ""

    CONTENITORI = (
        "class_declaration", "struct_declaration",
        "interface_declaration", "record_declaration",
    )
    risultato = {}

    def visita(nodo, contenitore=None):
        for figlio in nodo.children:
            if figlio.type in CONTENITORI:
                proprio = nome_di(figlio)
                risultato.setdefault(proprio, [])
                visita(figlio, proprio)
            elif figlio.type == "method_declaration" and contenitore is not None:
                risultato[contenitore].append(firma_metodo(figlio))
                visita(figlio, contenitore)
            else:
                visita(figlio, contenitore)

    visita(albero.root_node)
    return risultato


def _intervalli_funzioni_csharp(source: str) -> dict[str, tuple[int, int]]:
    """Per ogni method_declaration dentro un contenitore C# (classe/struct/interface/record),
    ovunque si trovi nell'albero (dentro un namespace, annidato), il nome del metodo mappato
    alle righe 1-based (start_line, end_line) che occupa. Equivalente C# di _intervalli_funzioni
    (Python, ast.parse), usato da innesta_funzioni quando il linguaggio bersaglio e' C#."""
    import tree_sitter_c_sharp
    from tree_sitter import Language, Parser

    dati = source.encode("utf-8")
    albero = Parser(Language(tree_sitter_c_sharp.language())).parse(dati)
    if albero.root_node.has_error:
        raise SyntaxError("C#: l'albero contiene nodi ERROR o mancanti")

    def testo(nodo):
        return dati[nodo.start_byte:nodo.end_byte].decode("utf-8", "replace")

    def nome_di(nodo):
        for figlio in nodo.children:
            if figlio.type == "identifier":
                return testo(figlio)
        return "?"

    def nome_metodo(nodo):
        """Il nome del metodo e' l'identificatore seguito dalla lista parametri
        (stessa regola di _firme_csharp.firma_metodo): il primo identifier di un
        method_declaration e' spesso il TIPO DI RITORNO, non il nome, quando quel
        tipo non e' primitivo (es. "AutoplayCycleResult ExecuteOneCycle(...)")."""
        figli = list(nodo.children)
        for indice, figlio in enumerate(figli):
            successivo = figli[indice + 1] if indice + 1 < len(figli) else None
            if figlio.type == "identifier" and successivo is not None and successivo.type == "parameter_list":
                return testo(figlio)
        return nome_di(nodo)

    CONTENITORI = ("class_declaration", "struct_declaration", "interface_declaration", "record_declaration")
    risultato: dict[str, tuple[int, int]] = {}

    # Un frammento restituito senza la classe che lo racchiude (lo stesso
    # perimetro gia' concesso in Python, dove "solo la funzione" e' legale a
    # livello di modulo) non e' sintassi C# valida di per se': tree_sitter lo
    # analizza come local_function_statement invece di method_declaration.
    # Lo si accetta comunque, con lo stesso trattamento: il controllo di
    # ambiguita' sotto resta la stessa rete di sicurezza in entrambi i casi.
    METODI = ("method_declaration", "local_function_statement")

    def visita(nodo, contenitore=None):
        for figlio in nodo.children:
            if figlio.type in CONTENITORI:
                visita(figlio, nome_di(figlio))
            elif figlio.type in METODI:
                nome = nome_metodo(figlio)
                inizio = figlio.start_point.row + 1
                fine = figlio.end_point.row + 1
                if nome in risultato:
                    raise ValueError("metodo ambiguo, presente in piu' di un contenitore: " + nome)
                risultato[nome] = (inizio, fine)
                visita(figlio, contenitore)
            else:
                visita(figlio, contenitore)

    visita(albero.root_node)
    return risultato


def _valida_csharp(code: str, skeleton: str, task: dict):
    """Sul C# valgono le stesse regole del Python: si aggiunge e si riempie, non si toglie."""
    errors = []
    try:
        ottenute = _firme_csharp(code)
    except SyntaxError as exc:
        return ["Sintassi C# non valida: {}".format(exc)]
    attese = _firme_csharp(skeleton)

    classi_mancanti = sorted(set(attese) - set(ottenute))
    if classi_mancanti:
        errors.append("Classi scomparse rispetto allo scheletro: " + ", ".join(classi_mancanti))

    for classe in sorted(set(attese) & set(ottenute)):
        membri_attesi = {m[0]: m for m in attese[classe]}
        membri_ottenuti = {m[0]: m for m in ottenute[classe]}
        mancanti = sorted(set(membri_attesi) - set(membri_ottenuti))
        if mancanti:
            errors.append("Metodi scomparsi da {}: {}".format(classe, ", ".join(mancanti)))
        for metodo in sorted(set(membri_attesi) & set(membri_ottenuti)):
            if membri_attesi[metodo] != membri_ottenuti[metodo]:
                errors.append(
                    "Firma alterata in {}.{}: atteso {} ottenuto {}".format(
                        classe, metodo,
                        membri_attesi[metodo][1:], membri_ottenuti[metodo][1:]))

    consentiti = set(task.get("allow_stub", []))
    if NON_IMPLEMENTATO_CS.search(code) and not consentiti:
        errors.append("Corpi ancora non implementati: throw new NotImplementedException")

    segnaposto = sorted({m.group(0) for m in FORBIDDEN.finditer(_senza_stringhe(code))})
    if segnaposto:
        errors.append("Segnaposto presenti nel codice: " + ", ".join(segnaposto))

    return errors


def validate_implementation(code: str, skeleton: str, task: dict):
    if linguaggio_del_file(ROOT / task["file"]) == "c_sharp":
        return _valida_csharp(code, skeleton, task)
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

    segnaposto = sorted({m.group(0) for m in FORBIDDEN.finditer(_senza_stringhe(code))})
    if segnaposto:
        errors.append("Segnaposto presenti nel codice: " + ", ".join(segnaposto))

    return errors


def build_check(target: Path) -> list:
    """Verifica che il file appena scritto stia in piedi, secondo il suo linguaggio.

    Sul Python delega a import_check. Sul C# compila il progetto che contiene il
    file: il compilatore e' un controllo di firme piu' severo di qualunque
    confronto di alberi, perche' se una firma cambia i chiamanti non compilano.
    """
    if linguaggio_del_file(target) == "python":
        return import_check(target)

    progetto = None
    cartella = Path(target).resolve().parent
    while cartella != cartella.parent:
        trovati = sorted(cartella.glob("*.csproj"))
        if trovati:
            progetto = trovati[0]
            break
        cartella = cartella.parent
    if progetto is None:
        return ["Nessun .csproj trovato risalendo da " + str(target)]

    proc = subprocess.run(
        ["dotnet", "build", str(progetto), "--nologo", "-v", "q"],
        cwd=str(ROOT), capture_output=True, text=True,
    )
    if proc.returncode == 0:
        return []
    righe = [r.strip() for r in (proc.stdout + proc.stderr).splitlines()
             if ": error" in r or ": warning CS" in r]
    return righe[:10] or ["dotnet build fallito senza righe di errore riconoscibili"]


def import_check(target: Path) -> list:
    """Importa il modulo in un processo separato: un errore di import non deve
    contaminare questo processo."""
    parti = target.relative_to(ROOT).with_suffix("").parts
    if all(p.isidentifier() for p in parti):
        comando = "import {}".format(".".join(parti))
    else:
        # Una cartella come tools/open-webui/ non e' un identificatore Python
        # valido (il trattino e' l'operatore di sottrazione): "import a.b-c.d"
        # e' un SyntaxError che non ha niente a che fare col file caricato,
        # come misurato il 2026-09-11 su tools/open-webui/nosai_free_router.py.
        # Si carica direttamente dal percorso, che non ha questo vincolo.
        comando = (
            "import importlib.util; "
            "spec = importlib.util.spec_from_file_location('_import_check_target', r'{}'); "
            "modulo = importlib.util.module_from_spec(spec); "
            "spec.loader.exec_module(modulo)"
        ).format(str(target))
    proc = subprocess.run(
        [sys.executable, "-c", comando],
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

    solo = list(task.get("solo_funzioni", []))

    for tentativo in range(1, MAX_ATTEMPTS + 1):
        if solo:
            prompt = prompt_parziale(contract, task["file"], skeleton, solo)
        else:
            prompt = "CONTRATTO:\n{}\n\nSCHELETRO DA IMPLEMENTARE ({}):\n{}".format(
                contract, task["file"], skeleton
            )
        if contesto:
            prompt += "\n\nCONTESTO DI SOLA LETTURA:" + contesto
        if errors:
            prompt += "\n\nIL TENTATIVO PRECEDENTE E' STATO RIFIUTATO. Correggi questi difetti:\n"
            prompt += "\n".join("- " + e for e in errors)
            prompt += "\n\nCodice rifiutato:\n" + code

        # Azzerato solo ORA, dopo essere stato letto per il feedback del prompt:
        # un tentativo il cui innesto riesce non deve ereditare l'errore del
        # tentativo precedente. Prima di questa riga, un innesto riuscito al
        # tentativo N saltava validate_implementation (riga sotto: "if not (solo
        # and errors)") perche' errors era ancora quello, non azzerato, del
        # tentativo N-1 -- osservato su C-310: il tentativo 2 rispondeva con
        # prosa invece di codice (rifiutato per sintassi), il tentativo 3
        # produceva codice valido, ma l'esito finale restava quello del
        # tentativo 2.
        errors = []

        budget = budget_token(model)
        sistema = SYSTEM_PROMPT_CSHARP \
            if linguaggio_del_file(ROOT / task["file"]) == "c_sharp" else SYSTEM_PROMPT
        testo, usage = call_model(model, prompt, sistema, max_tokens=budget)
        chiamate += 1
        costo += cost_of(model, usage)
        code = extract_code(testo)
        if solo:
            try:
                code = innesta_funzioni(skeleton, code, solo, linguaggio_del_file(ROOT / task["file"]))
            except (SyntaxError, ValueError) as exc:
                errors = ["Innesto parziale rifiutato: {}".format(exc)]
                code = ""
        if not (solo and errors):
            errors = validate_implementation(code, skeleton, task)

        if not errors:
            target.write_text(code.rstrip() + "\n", encoding="utf-8")
            errors = build_check(target)
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
