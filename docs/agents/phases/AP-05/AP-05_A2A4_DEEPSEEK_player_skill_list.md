# AP-05 / A2+A4 — DeepSeek: la lista abilità del personaggio

**Stato:** da assegnare
**Ownership:** `NosAi.Runtime` (osservazione + cablaggio). Non toccare
`NosAi.Core`: i contratti `Skill`/`Cooldown`/`Player` sono di Claude (A1).

## Il fatto, verificato

`Player.Skills` è `EquatableArray<Skill>.Empty` in **tutti e quattro** i siti di
costruzione di produzione — verificato con
`grep -rn "EquatableArray<Skill>" src/`:

- `src/NosAi.Core/WorldModel/WorldModelSnapshot.cs:49`
- `src/NosAi.Runtime/WorldModel/Fusion/GameplayObservationProjector.cs:127`
- `src/NosAi.Runtime/Tactical/AutoplayCommand.cs:570`
- `src/NosAi.Runtime/Tactical/LiveCombatObserver.cs:149`

Nessun canale di osservazione la popola. Ogni snapshot che questo runtime abbia
mai prodotto afferma quindi che il personaggio **non ha abilità**.

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

### Parte 1 — stabilire se il wire la dice affatto (A2)

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

### Parte 2 — cablarla, solo se la parte 1 è riuscita (A4)

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

`Player.Skills` è oggi `EquatableArray<Skill>`, che non distingue «vuoto» da
«non osservato». `docs/adr/ADR-0027` propone di cambiarlo e la decisione è
dell'utente. **Non anticiparla**: cablare la lista dentro la forma attuale, e se
la parte 1 riesce, dirlo nel resoconto — la lista osservata è precisamente il
canale che rende la distinzione osservabile e quindi urgente.

## Definizione di fatto

- Esito della parte 1 riportato con evidenza numerica, riuscito o no.
- Se cablato: build 0 errori / 0 avvisi, entrambe le suite verdi, e un test che
  fissa il caso «pacchetto assente ⇒ lista non osservata», non solo il caso
  felice.
- Nessuna affermazione nei commenti o nei documenti che non sia stata verificata
  nel codice reale (`DEEPSEEK_TASKS.md`, REGOLA ASSOLUTA #3).
