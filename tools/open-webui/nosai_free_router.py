"""
title: NosAi Free Router
author: NosAi
version: 0.1.0
description: Cascata di ripiego sui modelli gratuiti e lucchetto sulla spesa.
"""
from typing import Optional

from pydantic import BaseModel, Field


class Filter:
    """Filtro che applica al corpo della richiesta le leve verificate di OpenRouter."""

    class Valves(BaseModel):
        """Interruttori regolabili dall'interfaccia di Open WebUI."""

        abilitato: bool = Field(
            default=True,
            description="Se disattivato, il corpo della richiesta passa invariato.",
        )
        ripieghi: str = Field(
            default=(
                "nex-agi/nex-n2.5-mini:free,"
                "dots-studio/dots-3-note-preview:free,"
                "nvidia/nemotron-3-super-120b-a12b:free"
            ),
            description="Modelli gratuiti di ripiego, separati da virgola.",
        )
        provider_guasti: str = Field(
            default="Novita",
            description="Provider da escludere sempre, separati da virgola.",
        )
        lucchetto_sui_gratuiti: bool = Field(
            default=True,
            description="Applica max_price zero quando il modello scelto e' gratuito.",
        )

    def __init__(self) -> None:
        self.valves = self.Valves()

    def e_gratuito(self, modello: str) -> bool:
        """Dice se l'identificativo indica un modello a costo zero."""
        if not modello or not isinstance(modello, str):
            return False
        nome = modello.strip()
        if not nome:
            return False
        if nome == "openrouter/free":
            return True
        return nome.endswith(":free")

    async def inlet(self, body: dict, __user__: Optional[dict] = None) -> dict:
        """Arricchisce la richiesta prima che parta verso il provider."""
        # Con il filtro spento il corpo non viene toccato in alcun modo.
        if not self.valves.abilitato:
            return body

        # Il contenitore provider deve esistere per poterci scrivere le leve.
        provider = body.get("provider")
        if not isinstance(provider, dict):
            provider = {}
            body["provider"] = provider

        # L'esclusione dei provider guasti vale sempre, anche senza modello scelto.
        provider["ignore"] = [
            voce.strip()
            for voce in self.valves.provider_guasti.split(",")
            if voce.strip()
        ]

        modello = body.get("model")
        if not modello or not isinstance(modello, str):
            # Nessun modello scelto: si restituisce il corpo con il solo ignore.
            return body

        if not self.e_gratuito(modello):
            # Scelta deliberata di un modello a pagamento: nessun lucchetto,
            # nessuna cascata di ripiego.
            return body

        if self.valves.lucchetto_sui_gratuiti:
            provider["max_price"] = {"prompt": 0, "completion": 0}

        # Cascata: il modello scelto per primo, poi i ripieghi non ancora presenti.
        cascata = [modello]
        for voce in self.valves.ripieghi.split(","):
            ripiego = voce.strip()
            if not ripiego or ripiego in cascata:
                continue
            cascata.append(ripiego)
            if len(cascata) >= 3:
                break

        # OpenRouter accetta al massimo 3 elementi nell'array models.
        body["models"] = cascata[:3]
        return body

    async def outlet(self, body: dict, __user__: Optional[dict] = None) -> dict:
        """Restituisce la risposta senza modificarla."""
        return body
