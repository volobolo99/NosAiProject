# RAPPORTO_S1 — gli opcode che il filo porta e nessuno legge

## 1. File creati e modificati

- `src/NosAi.Runtime/Perception/Network/NosTaleWorldProtocolDecoder.cs` (modificato)
- `src/NosAi.Runtime/Perception/Network/GameTrafficObserver.cs` (modificato)
- `tests/NosAi.Runtime.Tests/IconItemVnumTests.cs` (nuovo)
- `tests/NosAi.Runtime.Tests/IconItemVnumRecordedCaptureTests.cs` (nuovo)

## 2. Build e test

- `dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj` → 0 errori, 0 warning.
- `dotnet build tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj` → 0 errori, 0 warning.
- `dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj --no-build` →
  **Superati: 2639, Non superati: 2, Ignorati: 9, Totale: 2650.**
  I 2 non superati non sono miei (vedi § 6).
- `dotnet test ... --filter FullyQualifiedName~IconItemVnum` → **Superati: 12, Non superati: 0.**

## 3. Le misure (18 opcode, 8 catture)

Censimento `--wire-inspect <file> --fields` su tutte le catture di `data/`. Un
campo costante su tutte le occorrenze non è decodificabile per misura.

| opcode | occorrenze (cattura) | campi | forma | esito |
|---|---|---|---|---|
| `icon` | 1 messaggi, 1 certificazione, 2 nostale_combat | 4 | `1 <id> 1 <vnum>` | **decodificato** — vnum 8 e 2006, = `drop` che precede |
| `cancel` | 10 messaggi, 1 nostale_combat, 1 nostale_live | 3 | `0 0 -1` / `1 1092257 -1` / `0 4 -1` | non regge: un solo match drop-id, contraddetto altrove |
| `msgi` | 2 messaggi, 1 equip_test, 5 nostale_combat | 7 | `0 <msgId> 0 0 0 0 0` | shift confermato ma serve catalogo testi |
| `sayi` | 4 messaggi, 3 certificazione, 13 nostale_combat | 9 | `1 <ownId> ? <msgId> ? <arg> ? 0 0` | campo 6 = argomento; testo serve catalogo |
| `say` | 17 messaggi | 4,6 | `2 <id> 0 <testo…>` | testo in chiaro, ma campo 2 non confermato |
| `eff` | 2+3+10+3+1+6+3 (7 catture) | 3 | `<tipo 1..3> <id> <effId>` | effId senza catalogo effetto |
| `guri` | 2 messaggi, 1 equip_test, 6 nostale_combat | 3,4 | due forme diverse | forme incoerenti |
| `script` | 11 messaggi | 2 | `1 <scriptId>` | id senza catalogo |
| `gidx` | 5 messaggi | 6 | `1 <id> <float> <nome> <flag> 0\|0\|0` | indice gruppo, non confermato |
| `qsti` | 6 messaggi | 1 | stringa puntata | info quest, senza catalogo |
| `qstlist` | 4 messaggi | 0,1 | stringa puntata | info quest |
| `sayi2` | 5 messaggi | 7 | campo 7 `n/5` | senza catalogo |
| `fd` | 2 messaggi | 4 | `0 1 <n> 1`, n=27,77 | 2 sole occorrenze |
| `rest` | 2 messaggi | 3 | `1 8292772 0` costante | 2 occorrenze costanti |
| `npc_req` | 1 messaggi | 3 | `2 3102 9714` | 1 occorrenza |
| `pidx` | 1 messaggi | 2 | `-1 1.8314067` | 1 occorrenza |
| `qr` | 1 messaggi | 13 | 12 campi a 0 | 1 occorrenza |
| `targetoff` | 1 messaggi | 4 | `57 149 1 1997` | 1 occorrenza |

Nota sul censimento: `data/messaggi.noscap` porta **32** opcode distinti, non 31.
`targetoff` (9 caratteri) non veniva intercettato dal filtro del precedente
conteggio perché l'header del censimento lo stampa senza spazio di padding.

### Verifiche `--timeline` (le tre piste, non assunte)

- **`icon`[4] = vnum — confermato in 3 catture.** messaggi: `drop 8 4867701` →
  `icon 1 3548294 1 8` → `get … 4867701`. nostale_combat: `drop 2006 1092257` →
  `icon 1 3443217 1 2006` → `get … 1092257` (e di nuovo 2006). certificazione:
  `drop 2006 781824` → `icon 1 3443217 1 2006` → `get … 781824`.
- **`cancel`[2] = drop id — non regge.** Un solo riscontro: nostale_combat
  `cancel 1 1092257 -1` con `drop 2006 1092257`. Ma nostale_live ha
  `cancel 0 4 -1` mentre il suo unico drop è `1182217` (≠ 4), e messaggi ha dieci
  `cancel 0 0 -1`. Il flag (campo 1 = 0/1) e il significato del campo 2 col
  flag 0 restano non confermati. Un'occorrenza sola non è una regola.
- **`msgi` = `sayi` senza i primi tre campi — confermato lo shift, non basta.**
  `msgi 0 975 2 2612 1 0 0` corrisponde a `sayi 1 3443217 11 975 2 2612 1 0 0`
  (msgi[2]=sayi[4]=id messaggio, msgi[4]=sayi[6]=argomento). Ma il campo 1 di
  `msgi` (0/1) non ha spiegazione, e l'id messaggio si risolve in testo solo col
  catalogo del client (fuori scope S1).

## 4. Cosa ho lasciato `Unknown` (non letto) e perché

Tutti i 17 opcode non decodificati producono `DecodedObservations.Empty`, mai un
valore zero. Motivo per gruppo:

- `cancel`, `eff`, `guri`, `fd`, `rest`, `npc_req`, `pidx`, `qr`, `targetoff`:
  costanti, una sola occorrenza, o forma incoerente — nessun riscontro indipendente.
- `say`, `sayi`, `msgi`, `sayi2`, `qsti`, `qstlist`, `script`, `gidx`: sistema
  messaggi/quest; l'id si risolve in testo solo col catalogo `NSlangData_IT.NOS`
  (S2/S4), fuori scope.
- `icon` campi 1 e 3: costanti `1` ma senza significato stabilito — non letti.

## 5. Dove mi sono fermato

Nessun blocco. `icon` è l'unico decodificabile con riscontro wire-to-wire.

## 6. Specifica vs codice

- Il remark del decoder diceva «Quindici opcode» ma lo switch ne aveva già
  sedici (`out` era entrato senza aggiornare il conto). Corretto a **Diciassette**
  aggiungendo `icon`, enumerandoli tutti.
- La suite completa ha **2 test non miei** in rosso, presenti prima e dopo la mia
  modifica (non una regressione): `RefusalReasonRegisterTests` (S3 ha aggiunto
  `MonsterCatalogue`/`MonsterReportCommand` con 5 motivi `…Reason` nuovi non
  ancora registrati: `monster_not_in_catalogue`, `monster_report_no_target`,
  `monster_undecodable`, `max_hp_bonus_not_isolable_from_live_total`,
  `max_mp_bonus_not_isolable_from_live_total` — file `MonsterCatalogue.cs` non
  committati in parallelo) e `GuardAiClientTests` (test di stress heartbeat,
  `receive_failed: IOException` flaky sotto carico parallelo).
