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

    def __init__(self) -> None:
        raise NotImplementedError

    def e_gratuito(self, modello: str) -> bool:
        """Dice se l'identificativo indica un modello a costo zero."""
        raise NotImplementedError

    async def inlet(self, body: dict, __user__: Optional[dict] = None) -> dict:
        """Arricchisce la richiesta prima che parta verso il provider."""
        raise NotImplementedError

    async def outlet(self, body: dict, __user__: Optional[dict] = None) -> dict:
        """Restituisce la risposta senza modificarla."""
        raise NotImplementedError
