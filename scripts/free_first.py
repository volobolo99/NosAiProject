from __future__ import annotations
import time
from dataclasses import dataclass, field
from typing import Callable, Dict, List, Optional, Sequence, Tuple

try:  # importabile sia da scripts/ nel path sia come scripts.free_first
    import free_chain
except ModuleNotFoundError:  # pragma: no cover
    from scripts import free_chain

BUDGET_PREDEFINITO: float = 120.0
PAUSA_PREDEFINITA: float = 60.0

class LimiteRaggiunto(RuntimeError):
    """Sollevata dal chiamante quando il provider risponde 429. Porta i secondi di Retry-After."""
    def __init__(self, messaggio: str, attendi: float = PAUSA_PREDEFINITA) -> None:
        super().__init__(messaggio)
        self.attendi = attendi

@dataclass
class Tentativo:
    modello: str
    gratuito: bool
    secondi: float
    accettato: bool
    motivo: str

@dataclass
class Esito:
    testo: str
    modello: str
    gratuito: bool
    accettato: bool
    secondi: float
    tentativi: List[Tentativo]

class Pausa:
    """Registro dei modelli temporaneamente esclusi perche hanno esaurito la quota."""
    def __init__(self) -> None:
        self._espiri: Dict[str, float] = {}

    def attiva(self, modello: str, secondi: float, adesso: float) -> None:
        self._espiri[modello] = adesso + secondi

    def attivo(self, modello: str, adesso: float) -> bool:
        scadenza = self._espiri.get(modello)
        return scadenza is not None and scadenza > adesso

    def filtra(self, modelli: Sequence[str], adesso: float) -> List[str]:
        return [m for m in modelli if not self.attivo(m, adesso)]

def budget_residuo(iniziato: float, budget: float, adesso: float) -> float:
    """Secondi ancora disponibili per la cascata gratuita. Mai negativo."""
    rimanente = budget - (adesso - iniziato)
    if rimanente < 0.0:
        return 0.0
    return rimanente

def esegui(
    chiama_gruppo: Callable[[Sequence[str]], Tuple[str, str]],
    chiama_pagato: Callable[[], Tuple[str, str]],
    giudizio: Callable[[str], List[str]],
    modelli: Optional[Sequence[str]] = None,
    budget_secondi: float = BUDGET_PREDEFINITO,
    orologio: Callable[[], float] = time.monotonic,
    pausa: Optional[Pausa] = None,
) -> Esito:
    """Prova i gruppi gratuiti in ordine di velocita, accetta il primo testo che supera il giudizio, e ricade sul pagato quando nessuno passa o il budget e esaurito."""
    if pausa is None:
        pausa = Pausa()

    # Se il budget è zero o negativo, saltiamo direttamente al modello a pagamento
    if budget_secondi <= 0.0:
        inizio_pagato = orologio()
        try:
            testo_pagato, modello_pagato = chiama_pagato()
            elapsed_pagato = orologio() - inizio_pagato
            difetti_pagato = giudizio(testo_pagato)
            accettato_pagato = not difetti_pagato
            motivo_pagato = "" if accettato_pagato else ("giudizio: " + ", ".join(difetti_pagato))
            tentativi = [
                Tentativo(
                    modello=modello_pagato,
                    gratuito=False,
                    secondi=elapsed_pagato,
                    accettato=accettato_pagato,
                    motivo=motivo_pagato,
                )
            ]
            totale = orologio() - inizio_pagato
            return Esito(
                testo=testo_pagato,
                modello=modello_pagato,
                gratuito=False,
                accettato=accettato_pagato,
                secondi=totale,
                tentativi=tentativi,
            )
        except Exception as ecc_pagato:
            elapsed_pagato = orologio() - inizio_pagato
            tentativi = [
                Tentativo(
                    modello="",
                    gratuito=False,
                    secondi=elapsed_pagato,
                    accettato=False,
                    motivo=f"eccezione: {ecc_pagato}",
                )
            ]
            messaggio = "\n".join(
                ["Nessun modello ha risposto.", f"- pagato: {tentativi[0].motivo}"]
            )
            raise RuntimeError(messaggio) from None

    # Determina la lista di modelli gratuiti di base
    if modelli is None:
        base_modelli = list(free_chain.free_models(escludi_lenti=True))
    else:
        base_modelli = list(modelli)

    adesso0 = orologio()
    disponibili = pausa.filtra(base_modelli, adesso0)

    # Se nessun modello disponibile, saltiamo direttamente al modello a pagamento
    gruppi: List[List[str]] = []
    if disponibili:
        gruppi = free_chain.gruppi(disponibili, dimensione=free_chain.GRUPPO_MAX)

    iniziato = orologio()
    tentativi: List[Tentativo] = []
    testo_accettato: Optional[str] = None
    modello_accettato: Optional[str] = None
    gratuito_accettato: bool = False
    tempo_trascorso_accettato: float = 0.0
    previous_elapsed: Optional[float] = None  # elapsed dell'ultimo tentativo gratuito

    # Funzione interna per aggiungere tentativo di budget esaurito
    def _aggiungi_tentativo_budget(gruppo_corrente: List[str]) -> None:
        modello_riferimento: Optional[str] = None
        if gruppo_corrente:
            modello_riferimento = gruppo_corrente[0]
        elif disponibili:
            modello_riferimento = disponibili[0]
        if modello_riferimento is not None:
            tentativi.append(
                Tentativo(
                    modello=modello_riferimento,
                    gratuito=True,
                    secondi=0.0,
                    accettato=False,
                    motivo="budget esaurito: nessun altro gratuito",
                )
            )

    # Iteriamo sui gruppi gratuiti
    for gruppo in gruppi:
        # Controlla budget prima di provare il gruppo
        adesso_check = orologio()
        rimanente = budget_residuo(iniziato, budget_secondi, adesso_check)
        # Se il budget è esaurito o non possiamo permetterci almeno il tempo dell'ultimo tentativo gratuito
        if rimanente <= 0.0 or (previous_elapsed is not None and rimanente < previous_elapsed):
            _aggiungi_tentativo_budget(gruppo)
            break

        inizio_tentativo = orologio()
        try:
            testo, modello_usato = chiama_gruppo(gruppo)
        except Exception as ecc:  # qualsiasi eccezione dal modello gratuito
            elapsed = orologio() - inizio_tentativo
            # Gestione pausa solo per LimiteRaggiunto
            if isinstance(ecc, LimiteRaggiunto):
                attendi = ecc.attendi
                adesso_eccezione = orologio()
                for m in gruppo:
                    pausa.attiva(m, attendi, adesso_eccezione)
            motivo = f"eccezione: {ecc}"
            tentativi.append(
                Tentativo(
                    modello=gruppo[0],
                    gratuito=True,
                    secondi=elapsed,
                    accettato=False,
                    motivo=motivo,
                )
            )
            previous_elapsed = elapsed
            continue  # passa al gruppo successivo
        else:
            elapsed = orologio() - inizio_tentativo
            difetti = giudizio(testo)
            if not difetti:  # accettato
                tentativi.append(
                    Tentativo(
                        modello=modello_usato,
                        gratuito=True,
                        secondi=elapsed,
                        accettato=True,
                        motivo="",
                    )
                )
                testo_accettato = testo
                modello_accettato = modello_usato
                gratuito_accettato = True
                tempo_trascorso_accettato = elapsed
                previous_elapsed = elapsed
                break
            else:  # rifiutato dal giudizio
                motivo = "giudizio: " + ", ".join(difetti)
                tentativi.append(
                    Tentativo(
                        modello=modello_usato,
                        gratuito=True,
                        secondi=elapsed,
                        accettato=False,
                        motivo=motivo,
                    )
                )
                previous_elapsed = elapsed
                # continua con il gruppo successivo

    # Se abbiamo trovato un risultato gratuito accettato, restituiamo subito
    if testo_accettato is not None:
        totale = orologio() - iniziato
        return Esito(
            testo=testo_accettato,
            modello=modello_accettato,  # type: ignore[arg-type]
            gratuito=True,
            accettato=True,
            secondi=totale,
            tentativi=tentativi,
        )

    # Nessun gratuito accettato: proviamo il modello a pagamento
    inizio_pagato = orologio()
    try:
        testo_pagato, modello_pagato = chiama_pagato()
        elapsed_pagato = orologio() - inizio_pagato
        difetti_pagato = giudizio(testo_pagato)
        accettato_pagato = not difetti_pagato
        motivo_pagato = "" if accettato_pagato else ("giudizio: " + ", ".join(difetti_pagato))
        tentativi.append(
            Tentativo(
                modello=modello_pagato,
                gratuito=False,
                secondi=elapsed_pagato,
                accettato=accettato_pagato,
                motivo=motivo_pagato,
            )
        )
        totale = orologio() - iniziato
        return Esito(
            testo=testo_pagato,
            modello=modello_pagato,
            gratuito=False,
            accettato=accettato_pagato,
            secondi=totale,
            tentativi=tentativi,
        )
    except Exception as ecc_pagato:
        elapsed_pagato = orologio() - inizio_pagato
        # Registriamo il tentativo del pagamento fallito
        tentativi.append(
            Tentativo(
                modello="",  # modello sconosciuto in caso di eccezione
                gratuito=False,
                secondi=elapsed_pagato,
                accettato=False,
                motivo=f"eccezione: {ecc_pagato}",
            )
        )
        # Costruiamo il messaggio di errore come richiesto
        righe = ["Nessun modello ha risposto."]
        for t in tentativi:
            if t.gratuito:
                righe.append(f"- {t.modello}: {t.motivo}")
            else:
                # tentativo relativo al pagamento
                righe.append(f"- pagato: {t.motivo}")
        messaggio = "\n".join(righe)
        raise RuntimeError(messaggio) from None
