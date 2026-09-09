"""Ricognizione periodica del catalogo OpenRouter.

Segnala i modelli che potrebbero migliorare il roster. Non cambia il roster:
i candidati a pagamento restano proposte finche' l'operatore non le approva.
"""
from __future__ import annotations

from pathlib import Path
from typing import Any, Dict, List, Tuple

import requests

CATALOG_URL = "https://openrouter.ai/api/v1/models"

# Il roster in uso, allineato a scripts/mcp_server.py.
ROSTER = {
    "worker": "qwen/qwen3-coder-30b-a3b-instruct",
    "auditor": "deepseek-v4-flash",
    "preflight": "google/gemini-2.5-flash-lite",
    "local_scaffold": "qwen2.5-coder:7b",
}

REPORT_PATH = Path(__file__).resolve().parents[1] / "reports" / "model-scout.md"


def fetch_catalog(timeout: int = 60) -> List[Dict[str, Any]]:
    """Scarica il catalogo. RuntimeError con il codice HTTP se non e' 200."""
    response = requests.get(CATALOG_URL, timeout=timeout)
    if response.status_code != 200:
        raise RuntimeError(f"Errore HTTP {response.status_code}")
    return response.json()["data"]


def is_free(model: Dict[str, Any]) -> bool:
    """True se prompt e completion costano entrambi zero."""
    pricing = model["pricing"]
    return (
        float(pricing["prompt"]) == 0.0
        and float(pricing["completion"]) == 0.0
    )


def cost_per_mtok(model: Dict[str, Any]) -> Tuple[float, float]:
    """(prezzo input, prezzo output) per milione di token."""
    pricing = model["pricing"]
    return (
        float(pricing["prompt"]) * 1_000_000,
        float(pricing["completion"]) * 1_000_000,
    )


def compare_to_roster(
    catalog: List[Dict[str, Any]], roster: Dict[str, str]
) -> Dict[str, Any]:
    """Confronto fra i modelli in uso e i candidati del catalogo."""
    confronto: Dict[str, Any] = {"ruoli": {}, "roster_mancanti": []}

    for ruolo, modello_in_uso in roster.items():
        modello_attuale = next(
            (modello for modello in catalog if modello["id"] == modello_in_uso),
            None,
        )

        if modello_attuale is None:
            confronto["roster_mancanti"].append(modello_in_uso)
            confronto["ruoli"][ruolo] = {
                "in_uso": modello_in_uso,
                "prezzo_attuale": [0.0, 0.0],
                "gratuiti_nuovi": [],
                "pagamento_piu_economici": [],
            }
            continue

        prezzo_attuale = cost_per_mtok(modello_attuale)
        somma_attuale = prezzo_attuale[0] + prezzo_attuale[1]
        context_attuale = modello_attuale["context_length"]
        gratuiti_nuovi: List[str] = []
        pagamento_piu_economici: List[Dict[str, Any]] = []

        for modello in catalog:
            if modello["id"] == modello_in_uso:
                continue

            if is_free(modello):
                gratuiti_nuovi.append(modello["id"])
                continue

            prezzo_candidato = cost_per_mtok(modello)
            somma_candidato = prezzo_candidato[0] + prezzo_candidato[1]
            if (
                modello["context_length"] >= context_attuale
                and somma_candidato < somma_attuale
            ):
                risparmio_pct = (
                    (somma_attuale - somma_candidato)
                    / somma_attuale
                    * 100
                )
                pagamento_piu_economici.append(
                    {
                        "id": modello["id"],
                        "prezzo": list(prezzo_candidato),
                        "risparmio_pct": round(risparmio_pct, 1),
                    }
                )

        confronto["ruoli"][ruolo] = {
            "in_uso": modello_in_uso,
            "prezzo_attuale": list(prezzo_attuale),
            "gratuiti_nuovi": gratuiti_nuovi,
            "pagamento_piu_economici": pagamento_piu_economici,
        }

    return confronto


def render_report(confronto: Dict[str, Any]) -> str:
    """Rapporto Markdown in italiano."""
    linee: List[str] = ["# Report OpenRouter", ""]

    roster_mancanti = confronto.get("roster_mancanti", [])
    if roster_mancanti:
        linee.extend(
            [
                "## Modelli del roster non presenti su OpenRouter",
                "",
            ]
        )
        linee.extend(f"- `{modello}`" for modello in roster_mancanti)
        linee.append("")

    for ruolo, dettagli in confronto["ruoli"].items():
        prezzo_attuale = dettagli["prezzo_attuale"]
        gratuiti = dettagli["gratuiti_nuovi"]
        candidati = dettagli["pagamento_piu_economici"]

        linee.extend(
            [
                f"## Ruolo: {ruolo}",
                "",
                f"- Modello in uso: `{dettagli['in_uso']}`",
                f"- Prezzo attuale: prompt ${prezzo_attuale[0]:.6f}/M, "
                f"completion ${prezzo_attuale[1]:.6f}/M",
                "",
                "- Modelli gratuiti nuovi: "
                f"{'adottabili senza consenso' if gratuiti else 'nessuno'}",
            ]
        )

        if gratuiti:
            linee.extend(f"  - `{modello}`" for modello in gratuiti)
        else:
            linee.append("  - Nessuno")

        if candidati:
            linee.extend(
                [
                    "",
                    "### Candidati a pagamento: consenso dell'operatore",
                    "",
                    "L'adozione di questi candidati a pagamento richiede "
                    "l'approvazione esplicita dell'operatore.",
                    "",
                ]
            )
            for candidato in candidati:
                prezzo = candidato["prezzo"]
                linee.append(
                    f"- `{candidato['id']}`: prompt ${prezzo[0]:.6f}/M, "
                    f"completion ${prezzo[1]:.6f}/M, risparmio "
                    f"{candidato['risparmio_pct']:.1f}%"
                )
            linee.append("")

    return "\n".join(linee).rstrip() + "\n"


def main() -> int:
    """Scrive il rapporto e restituisce 0, oppure 1 su errore di rete."""
    try:
        catalogo = fetch_catalog()
    except RuntimeError as errore:
        print(f"Errore nel caricamento del catalogo: {errore}")
        return 1

    confronto = compare_to_roster(catalogo, ROSTER)
    REPORT_PATH.parent.mkdir(parents=True, exist_ok=True)
    REPORT_PATH.write_text(render_report(confronto), encoding="utf-8")

    ruoli = confronto["ruoli"]
    gratuiti = sum(
        len(dettagli["gratuiti_nuovi"]) for dettagli in ruoli.values()
    )
    pagamento = sum(
        len(dettagli["pagamento_piu_economici"])
        for dettagli in ruoli.values()
    )
    print(
        f"Report generato in {REPORT_PATH}: {len(ruoli)} ruoli, "
        f"{gratuiti} nuovi modelli gratuiti e {pagamento} candidati a pagamento."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
