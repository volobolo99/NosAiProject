# AP-05 / A2+A4 — DeepSeek: la lista abilità del personaggio

**Stato:** parte 1 eseguita il 2026-09-07 — esito negativo verificato; parte 2 bloccata su una cattura che non esiste ancora
**Ownership:** `NosAi.Runtime` (osservazione + cablaggio). Non toccare
`NosAi.Core`: i contratti `Skill`/`Cooldown`/`Player` sono di Claude (A1).

## Il fatto, verificato

Nessun canale di osservazione popola `Player.Skills`. Fino al 2026-09-07 era
`EquatableArray<Skill>.Empty` in tutti e quattro i siti di costruzione di
produzione, il che significa che ogni snapshot mai prodotto da questo runtime
*affermava* che il personaggio non ha abilità.

**Quella metà è stata corretta** (`ADR-0027`, commit `c28dd74`): il campo è ora
`WorldFact<EquatableArray<Skill>>` e i quattro siti dichiarano `Unknown` con una
motivazione nominata — `skill_list_never_read:no_observation_channel` nel
proiettore, `skill_list_not_read_by_this_observer` in `LiveCombatObserver`. Il
modello non mente più su ciò che non sa.

Resta l'altra metà, che è questo compito: **nessuno legge ancora la lista**. La
differenza è che un rifiuto ora dice `skill_list_not_observed` perché lo ha
letto da un fatto, non perché ha dedotto da un array vuoto.

Questo ha già prodotto due difetti reali, entrambi corretti aggirando il
sintomo:

1. `CombatPlanner.CheckSkillReady` rifiutava con `skill_not_found` anche quando
   la lista non era mai stata letta — cioè sempre, su client reale (`5e9bd93`).
2. `CombatPlanner.GenerateCandidates` non può produrre alcun candidato
   `UseSkill` senza skill, il che rendeva `--combat-report` strutturalmente
   incapace di predire `--engage` (`a39197e`).

## Cosa il wire dice già

`sr` è decodificato (`NosTaleWorldProtocolDecoder.cs:120` →
`DecodeSkillReady`) in `SkillReady(int Slot, DateTime ObservedAtUtc,
DataSourceKind Source)` (`GameTrafficObserver.cs:172`) e arriva al World Model
come `Cooldown` (`GameplayObservationProjector.cs:98-101`), dove `SkillId` è **il
numero di slot reso stringa** — una convenzione di chiave, non un identificativo
di abilità.

`docs/PROTOCOLLO_NOSTALE.md:208` classifica `sr` come **probable**: *"Skill ready
/ cooldown ended, by skill slot"*. `su` (riga 105) porta un vnum di skill al
campo 5, classificato **probable**.

**Nessun pacchetto attualmente decodificato enumera la lista abilità.**

## Il compito

### Parte 1 — ESEGUITA. Esito: il pacchetto non c'è in nessuna cattura

Eseguito il 2026-09-07 con `--world-replay` su **tutte e cinque** le catture
reali presenti su questa macchina (`Desktop/nos/NosAiProject/data/`). Il replay
censisce ogni opcode che attraversa il filo, letto o no, quindi un pacchetto
sconosciuto comparirebbe comunque nell'elenco.

| Cattura | Messaggi inbound | Opcode distinti | `ski` |
|---|---|---|---|
| `nostale_combat.noscap` | 8 211 | 21 | **assente** |
| `certificazione.noscap` | 11 528 | 16 | **assente** |
| `equip_test.noscap` | 3 584 | 17 | **assente** |
| `nostale_live.noscap` | 1 913 | 13 | **assente** |
| `nostale_01.noscap` | 2 490 | 2 | **assente** |

**27 726 messaggi, zero `ski`.** La spiegazione sta nel modo in cui le catture
nascono: WinDivert si aggancia a un client **già in gioco**, e `ski` è inviato
una sola volta al caricamento del personaggio. Lo stesso effetto è già
documentato in forma più debole per `in` — 25 occorrenze contro 7 685 `mv` sulla
stessa cattura, perché le entità già in vista non vengono ri-annunciate.

La fonte esterna (OpenNos, `Character.GenerateSki()`) descrive il pacchetto come
`ski {skibase}{generatedSkills}`: il vnum della prima abilità ripetuto due
volte, poi l'elenco dei vnum. **Resta una pista non riscontrata.** La regola
sulle fonti esterne di `CLAUDE.md` chiede di confrontare almeno un valore
decodificato con uno realmente osservato, e qui non esiste alcun valore
osservato con cui confrontarlo: scrivere il decoder ora significherebbe
consegnare un parser mai eseguito su un byte vero.

**Cosa serve, ed è l'unica cosa che serve.** Una cattura che cominci **prima**
del login: avviare `--record-wire` a client chiuso, poi accedere ed entrare in
gioco. Quel singolo file conterrebbe `ski`, e con esso i pacchetti di
caricamento personaggio che `equip_test.noscap` mostra solo in parte (`sc`,
`equip`, `inv`, `lev`). Nessun altro esperimento è necessario: il censimento qui
sopra ha già escluso che il pacchetto sia nascosto nelle registrazioni esistenti.

### Parte 1 — il metodo, conservato per riferimento

Cercare, nelle catture reali già in repository e con
`WinDivertProbe.exe --world <file.noscap>`, un pacchetto che elenchi le abilità
del personaggio. In OpenNos e nella documentazione di comunità il candidato è
`ski`, ma **questa frase è una pista, non un fatto**: si applica la regola
«External reference data» di `CLAUDE.md`. Prima di decodificare qualunque campo
su un percorso non puramente diagnostico, riscontrare almeno un valore
decodificato con uno realmente osservato (un numero visibile in gioco, una
lettura del client vivo), e citare la fonte — nome e URL — nel commento del
codice e nel messaggio di commit.

Se il pacchetto non compare nelle catture disponibili, **l'esito corretto è
dirlo**, con il comando eseguito e il risultato numerico, e fermarsi: una lista
inventata da `sr` sarebbe peggio di nessuna lista. Uno slot che è tornato
pronto dice che *quello slot esiste*, non quale abilità contiene né se è
utilizzabile ora.

### Parte 2 — BLOCCATA finché non esiste una cattura da login (A4)

Aggiungere il campo osservato a `GameplayObservation` con lo stesso trattamento
additivo che gli altri già hanno, popolarlo nel provider, e proiettarlo in
`GameplayObservationProjector`. `LiveCombatObserver.BuildPlayer` (`:149`) deve
riceverlo per la stessa via, altrimenti `--combat-report` e `--engage`
continuano a vedere una lista vuota anche quando il resto del runtime non la
vede più.

`Skill(SkillId Id, WorldFact<string> Name, WorldFact<int> Level, WorldFact<bool>
IsUsable)`: `Name` resta `Unknown` finché non esiste un catalogo dei nomi;
`Level` e `IsUsable` restano `Unknown` a meno che il pacchetto non li dichiari
davvero. **Non dedurre `IsUsable = true` dalla presenza nella lista**:
`CombatPlanner.IsSkillReady` rifiuta un'usabilità Unknown, ed è l'esito
corretto, non un ostacolo da rimuovere.

## Vincolo di contratto

`Player.Skills` è `WorldFact<EquatableArray<Skill>>` da `ADR-0027` (Accepted,
applicato in `c28dd74`). Chi esegue la parte 2 deve quindi produrre un **fatto**,
non una lista: `Live(...)` quando il pacchetto è arrivato, `Unknown(motivo)`
quando non è arrivato — e mai una lista vuota osservata per dire «non l'ho
letta», che è precisamente l'errore che quell'ADR ha chiuso.

Corollario da non perdere: una lista `ski` vuota è oggi **esprimibile e
distinta**. Un personaggio appena creato che non ha ancora imparato alcuna
abilità produce un `Live` su una lista vuota, e il rifiuto corretto per lui è
`skill_not_found`, non `skill_list_not_observed`.

## Definizione di fatto

- Esito della parte 1 riportato con evidenza numerica, riuscito o no.
- Se cablato: build 0 errori / 0 avvisi, entrambe le suite verdi, e un test che
  fissa il caso «pacchetto assente ⇒ lista non osservata», non solo il caso
  felice.
- Nessuna affermazione nei commenti o nei documenti che non sia stata verificata
  nel codice reale (`DEEPSEEK_TASKS.md`, REGOLA ASSOLUTA #3).
