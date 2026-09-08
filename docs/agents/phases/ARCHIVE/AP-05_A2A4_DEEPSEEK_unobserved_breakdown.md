# AP-05 / A2+A4 — DeepSeek — 481 pacchetti su 3584 non producono nulla, e nessuno sa perché

**Da prendere DOPO `progression_from_lev` e `worn_equipment_from_wire`**: tocca
`WorldChannelReplay.cs` e `WorldReplayCommand.cs`, che quei due possiedono.
Quando sono consegnati e pushati, questo è pronto.

## Prima di tutto

1. `docs/agents/DEEPSEEK_TASKS.md` — le tre regole assolute.
2. `CLAUDE.md` § *Architecture invariants* — *Unknown is not zero, false or empty*.

**REGOLA #3**: i numeri sotto sono l'uscita reale di `--world-replay`, eseguito
il 2026-09-07 su tutte e cinque le registrazioni. Ricontrollali: se sono
cambiati (i due task precedenti li spostano di proposito), i tuoi vincono.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`. `git pull`, **`git push` tu.**

---

## Il fatto

`--world-replay` stampa, per ogni registrazione, `messaggi inbound: N (senza
osservazione: M)`. Misurato:

| Registrazione | inbound | senza osservazione | % |
|---|---:|---:|---:|
| `nostale_01` (inattiva) | 2 490 | **0** | 0 % |
| `nostale_live` | 1 913 | 29 | 1,5 % |
| `certificazione` | 11 528 | 176 | 1,5 % |
| `nostale_combat` | 8 211 | 174 | 2,1 % |
| `equip_test` | 3 584 | **481** | **13,4 %** |

Un pacchetto «senza osservazione» è un pacchetto che il decoder ha visto e da
cui non è uscito nulla. Su `equip_test` sono uno su sette, sei volte la quota di
ogni altra cattura — e **nessuno sa perché**. Il numero c'è da sempre e non è
mai stato spiegato.

Le ipotesi sono almeno tre, e producono lo stesso numero:

1. l'opcode non è fra quelli che il decoder legge;
2. l'opcode è letto, e quella riga è stata **rifiutata** (campo malformato,
   valore fuori limite) — un rifiuto è un fatto, e qui sparisce;
3. l'opcode è letto, la riga è valida, e il risultato è legittimamente vuoto —
   per esempio un `mv` di un'entità mai introdotta da un `in`, che
   `docs/PROTOCOLLO_NOSTALE.md` documenta come scartato di proposito.

Sono tre cose diverse: la prima è un buco di copertura, la seconda un difetto di
decodifica o un dato strano sul filo, la terza il comportamento corretto.
Collassarle in un numero solo è esattamente ciò che questo progetto rifiuta.

---

## Cosa fare

Nel censimento di `--world-replay`, sostituisci il numero con la sua
scomposizione. Per ogni pacchetto che non produce osservazione, classifica il
motivo in una delle tre categorie sopra, e stampa il conteggio per categoria e,
dentro la prima e la seconda, **per opcode**.

```
  messaggi inbound       : 3584
  senza osservazione     : 481
    opcode non letto     : ...   (eq 6, equip 6, sc 6, ...)
    riga rifiutata       : ...   (ivn 2, ...)
    vuoto per progetto   : ...   (mv 468: entita' mai introdotta)
```

Le categorie sopra sono un esempio della forma, non i numeri attesi: **i numeri
sono il risultato del task**, e inventarli qui li trasformerebbe in
un'aspettativa. Riportali tu.

**La terza categoria va giustificata, non assunta.** Se classifichi un pacchetto
come «vuoto per progetto», il codice deve poterlo dire per una ragione precisa
(l'entità non è mai stata introdotta, il campo è opzionale e assente), non
perché non rientra nelle prime due. Se una quarta categoria emerge — pacchetti
che non sai spiegare — **falla esistere e contala**: un «non so» misurato vale
più di una categoria di comodo che li assorbe.

---

## OWN

- `tests/NosAi.Runtime.Tests/UnobservedBreakdownTests.cs` (nuovo)

## MODIFY

- `src/NosAi.Runtime/LiveIntegration/Capture/WorldChannelReplay.cs`
- `src/NosAi.Runtime/Observability/WorldReplayCommand.cs`

Nient'altro. In particolare **non** il decoder: se per classificare un rifiuto ti
servisse che il decoder dica perché ha rifiutato, quella è una modifica al
contratto di osservazione — **fermati e riferisci**, non allargare il task.

---

## Test

Con `InMemoryPacketSource`, pacchetti costruiti a mano:

1. Un opcode sconosciuto finisce in «non letto», e sotto il proprio nome.
2. Un opcode letto ma con una riga malformata finisce in «rifiutata».
3. Un `mv` di un'entità mai introdotta finisce in «vuoto per progetto», e lo
   stesso `mv` dopo un `in` **non** finisce lì.
4. La somma delle categorie è esattamente il totale «senza osservazione»: nessun
   pacchetto perso, nessuno contato due volte. Questo test è il più importante.

Contro le registrazioni reali, con `RecordedCaptureFactAttribute` (mai un
`if (…) return`):

5. `equip_test.noscap`: la scomposizione somma a quello che il censimento
   riporta come totale.
6. `nostale_01.noscap`: zero senza osservazione, quindi tutte le categorie a
   zero. È il controllo che la scomposizione non inventa righe.

## Definition of done

- Build 0/0; suite verdi, numeri riportati.
- `--world-replay` eseguito **su tutte e cinque** le registrazioni, le cinque
  scomposizioni incollate nel report.
- Una riga di conclusione: cosa spiega davvero i 481 di `equip_test`.
- Livello: **Integrated**.
