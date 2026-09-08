# RAPPORTO_S3 — il catalogo dei mostri contro il filo

## 1. File creati e modificati

- `src/NosAi.Runtime/GameData/MonsterCatalogue.cs` (nuovo)
- `src/NosAi.Runtime/Observability/MonsterReportCommand.cs` (nuovo)
- `src/NosAi.Runtime/Program.cs` (modificato: solo la registrazione di `--monster-report`)
- `tests/NosAi.Runtime.Tests/MonsterCatalogueRefusalTests.cs` (nuovo)
- `tests/NosAi.Runtime.Tests/MonsterReportCommandTests.cs` (nuovo)

`MonsterReferenceDecoder.cs` (già mio) non è stato modificato: era completo e il
catalogo ci costruisce sopra.

## 2. Build e test

- `dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj` → 0 errori, 0 warning.
- `dotnet build tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj` → 0 errori, 0 warning.
- `dotnet test ... --filter "FullyQualifiedName~MonsterCatalogue|FullyQualifiedName~MonsterReportCommand"`
  → **Superati: 22, Non superati: 0.**
- `dotnet test ... --filter "FullyQualifiedName~RefusalReasonRegister"`
  → **Superati: 24, Non superati: 0** (i miei 5 motivi `…Reason` sono coperti).
- Suite completa `NosAi.Runtime.Tests` → **Superati: 2669, Non superati: 1,
  Ignorati: 9, Totale: 2679.** L'unico non superato è `Gate1Tests.Gate1SuitePasses`,
  che passa in isolamento (`--filter Gate1SuitePasses` → 1/1): è il flaky sotto
  carico parallelo già noto, non una mia regressione (nessun file condiviso).

Esecuzione reale: `--monster-report --vnum 45` sul catalogo del volume:

```
monster 45: Baccello di Pii morbido
  LEVEL level: 8 [CONFERMATO]
  HP/MP[0] max_hp_bonus: UNKNOWN [PROVVISORIO] (max_hp_bonus_not_isolable_from_live_total)
  HP/MP[1] max_mp_bonus: UNKNOWN [PROVVISORIO] (max_mp_bonus_not_isolable_from_live_total)
```

## 3. Le misure

Censimento `--wire-inspect data/nostale_combat.noscap --opcode st --fields`:

| campo | forma | lettura |
|---|---|---|
| 1 | 2 valori (2,3) | tipo entità |
| 2 | 16 distinti | id entità |
| 3 | 4 distinti (0,7,8,9) | **livello** — confermato 26/26 |
| 4 | costante 0 | — |
| 5 | 16 distinti (0..100) | hp% (ignorato: discorda dagli assoluti) |
| 6 | costante 100 | mp% |
| 7 | 18 distinti (0..336) | **vita corrente** |
| 8 | 4 distinti (4..60) | mana corrente |
| 9 | 4 distinti (7..336) | **vita massima** (costante per id) |
| 10 | 4 distinti (4..60) | mana massimo |
| 11 | costante 0 | — |

Censimento `--wire-inspect data/nostale_combat.noscap --opcode in --fields`:
n=25, campo 1 costante 3, campo 2 (vnum) = 4 distinti. Ordine di prima comparsa:
`[45, 36, 9, 96]` — è l'asserzione di `ObservedMonsters`.

Il vnum 45 (`st 3 3205 8 0 100 100 310 52 310 52 0`) ha vita massima **310**:
nessun campo del catalogo vale 310 per quel vnum, e non per caso — vedi § 4.

Sul `--timeline st,su` il campo 7 cala e il 9 resta costante per id, come già
letto dal decoder in `AbsoluteVitals`; il vitals di `su` (campi 16/17, con flag
campo 11=1) riporta la vita del **bersaglio** quando un mostro colpisce il
personaggio, non la vita del mostro — la vita del mostro sta solo in `st`.

## 4. Cosa ho lasciato `Unknown` e perché

- **`HP/MP[0]` (`MaxHpBonus`) e `HP/MP[1]` (`MaxMpBonus`)** → `Unknown`
  (`max_hp_bonus_not_isolable_from_live_total`, `max_mp_bonus_not_isolable_from_live_total`).
  Il catalogo porta un **bonus** sul totale, non il totale; il filo porta il
  totale (campo 9/10 di `st`). Il bonus non si isola dal totale osservato, quindi
  non ha riscontro indipendente e non viene dichiarato confermato. I numeri grezzi
  restano leggibili su `Raw.MaxHpBonus`/`Raw.MaxMpBonus`.
- Ogni altro campo di `MonsterReference` non è esposto come `ClassifiedValue`:
  non ha riscontro col filo, e un campo non misurabile non diventa un campo
  "provvisorio" solo per riempire il rapporto.
- **Nessuna classificazione «attaccabile / non attaccabile»** sul catalogo, come
  richiesto: `monster.dat` non distingue mostri da NPC/varchi/pet, e l'unico
  discriminante misurato resta `EntitySighting.Kind`.

## 5. Dove mi sono fermato

Nessun blocco. Il livello è l'unico campo del catalogo confermato dal filo, ed è
un risultato chiuso, non una voce aperta. I bonus HP/MP restano provvisori per
misura (non per assenza di codice).

## 6. Specifica vs codice

- La consegna elenca `MonsterReferenceDecoder.cs` fra i miei file: era già
  completo, non è stato toccato.
- La suite completa ha 1 test non mio in rosso (`Gate1SuitePasses`, flaky sotto
  carico parallelo, verde in isolamento) — non una regressione. I motivi
  `…Reason` di S4 (`item_not_in_catalogue`, `item_undecodable`,
  `item_fields_not_carried_by_wire`) hanno rotto temporaneamente
  `RefusalReasonRegisterTests` mentre S4 non aveva ancora i suoi test; ora sono
  coperti e il registro è verde.

## Test rimandati ancora aperti

T-03, T-05, T-06, T-07, T-08, T-09, T-12, T-13, T-14, T-16, T-17.
