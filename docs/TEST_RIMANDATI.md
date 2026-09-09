# Test rimandati (operatore)

Elenco unico dei test **reali** che l'operatore ha rimandato.
Non bloccano lo sviluppo. Un test qui non è `Verified`.
Chiudere una riga richiede evidenza (log, checklist, o nota in `docs/GATE1_CHECKLIST.md`).

Gli agenti ricordano le voci **aperte** a ogni resoconto di fine lavoro.

## Una sola partita chiude più voci insieme (verificato il 2026-09-08)

`--await-client-capture` — il pulsante **Avvia cattura pre-login** nella
sezione *Certificazione* del Control Panel — non registra soltanto gli opcode
che gli servono per fermarsi. Il tee della sessione filtra **per endpoint e
non per opcode** (`PreLoginCaptureSession.PreLoginTeeSource`), quindi il file
`.noscap` prodotto contiene **tutto** il traffico di gioco di quella sessione.

Ne segue che una sola sessione giocata normalmente produce l'evidenza per
chiudere insieme:

- **T-05** — gameplay osservato dal vivo, che è il criterio di chiusura del
  comando stesso (`stat`, `in`, e uno fra `ivn`/`equip`);
- **T-12, seconda metà** — quale `InventoryKind` produce un equip reale:
  serve una cattura che contenga `equip` e `ivn` mentre si indossa qualcosa;
- **la scala di `cond.speed`** — ignota oggi (vedi
  `docs/PROTOCOLLO_NOSTALE.md`, `cond`): camminare fra due punti dà `cond`
  per la velocità dichiarata e `mv` per posizioni e istanti, e il rapporto
  dà l'unità;
- **gli opcode delle missioni** — mai osservati perché ogni cattura esistente
  comincia a client già in gioco: aprire il diario missioni durante la
  registrazione li mette nel file;
- **gli opcode di `Stop`, `Interact` e `UseItem`** — delle sette azioni di
  `CharacterActionKind` (`CharacterControlContracts.cs:5-11`), quattro sono
  verificate all'`Execute → Verify` del flusso canonico: `Move` da
  `MovementVerificationProjector`, chiamato da `ScoutCommand.cs`;
  `BasicAttack`/`UseSkill` da `CombatVerificationProjector`, chiamato da
  `EngageCommand.cs` e `RecoverCommand.cs`; `Pickup` dal decoder che produce
  `ItemPickup` (`NosTaleWorldProtocolDecoder.cs:131`), proiettato su
  `WorldModelSnapshot.LastDropClaim`. Per `Stop`, `Interact` e `UseItem`
  nessun opcode noto le conferma: il decoder non ha un case per loro fra
  `NosTaleWorldProtocolDecoder.cs:116-136` e `docs/PROTOCOLLO_NOSTALE.md` non
  ne elenca alcuno — l'assenza nel documento prova solo che nessuno le ha
  ancora osservate, non che il server non le mandi. Fermare il personaggio,
  usare un oggetto dall'inventario e interagire con un NPC durante la stessa
  registrazione mette a referto la risposta del server, se distinguibile: se
  non risponde nulla, l'assenza diventa un fatto misurato invece che una
  lacuna del campione.

Perché rendano tutto questo, la sessione va giocata facendo quelle cose:
indossare e togliere un pezzo, camminare in linea retta fra due punti
riconoscibili, aprire il diario delle missioni, fermare il personaggio,
usare un oggetto dall'inventario, interagire con un NPC. Restano gesti di
gioco normale, non una procedura di collaudo.

| ID | Cosa | Cosa fare | Aperto |
|---|---|---|---|
| T-01 | Wire v4 sul telefono (ADR-0009) | Reinstallare l'APK (`Abbina telefono`), poi sessione USB e sessione Wi-Fi. Un APK più vecchio viene rifiutato all'header. | **no** |
| T-02 | Android Keystore sul dispositivo (ADR-0010) | Dopo T-01, verificare che l'app dichiari custodia Keystore (non file) e che l'abbinamento regga. | **no** |
| T-03 | Barra HP/MP su NosTale reale | La ROI è confermata sulla HUD: i ritagli del 1 set (`data/perception/crops/`, copiati come fixture in `tests/NosAi.Runtime.Tests/Fixtures/`) mostrano `7305/7305` e `1420/1420`, gli stessi numeri del canale mondo. Il lettore di barra rifiutava quei ritagli (`noisy_bar_profile`) perché il client scrive i numeri **sopra** la barra: il modello a una sola transizione non sopravvive al testo. `HudBarFillReader` ora misura il bordo destro del riempimento e ignora i buchi interni; i due ritagli reali leggono pieno. L'OCR numerico è addestrabile dal wire (ADR-0017): `HudGlyphAtlas` + `HudGlyphTraining`. **Resta da fare sul client vivo:** eseguire `NosAi.Runtime.dll --hud-probe` con la barra **parzialmente scarica** e confrontare il rapporto letto con i numeri sulla HUD — entrambe le fixture sono a barra piena, quindi il bordo è stato esercitato solo su barre sintetiche, e la scanalatura vuota di NosTale non è mai stata misurata. Poi addestrare l'atlante dei glifi con l'osservazione di rete attiva. | sì (barra parziale + addestramento) |
| T-04 | Prima cattura di traffico reale | Installare WinDivert in `tools/windivert/`, poi catturare una sessione su `.noscap` e misurarla con `WinDivertProbe.exe --analyze <file>`. | **no** |
| T-05 | Derivare i vitals dal traffico | Il traffico non è binario a offset fissi: è testo dopo la decodifica. `NosTaleWorldDecoder` + `NosTaleWorldFramer` + `NosTaleWorldProtocolDecoder` pubblicano `stat` come `LIVE` su byte live e `CACHED` su registrazione. Riscontro offline ripetibile: `WinDivertProbe.exe --world <file.noscap>` — sulla cattura di combattimento riporta 62 letture, HP 7218..7305 su max 7305, MP 1362..1420, gli stessi numeri di `docs/PROTOCOLLO_NOSTALE.md`. Il provider si attacca al runtime con `--observe-game <host:porta>`, `NOSAI_OBSERVE_GAME`, o l'impostazione Control Panel "Endpoint osservazione gioco". Senza driver l'host parte comunque e il gameplay resta UNKNOWN col motivo nominato. Resta da confermare `LIVE` su una sessione client in corso, non solo sul recording. | sì (cattura live) |
| T-06 | Gate 1 — handshake Noise su nodo mobile reale (`docs/ROADMAP_ESECUTIVA.md` S:2.5) | Avviare `NosAi.Host --gate 1 --attach <process> --module-sha256 <hex> --listen` (default porta 17480). Da un telefono, un iniziatore `Noise_XX_25519_ChaChaPoly_SHA256` + `NosFrameHeader` (non l'APK Guard attuale, che parla `WireHeader`) completa 100 handshake; p99 < 25 ms. Il protocollo è già verde in-process e su TCP loopback (`TransportLoopTests`), ma il p99 su loopback è una misura a orologio da parete e gira solo nella passata isolata (`NOSAI_QUIESCED_MACHINE=1` con `--filter "Category=PerfBudget"`): dentro la suite completa, e sul runner condiviso della CI, misurava la contesa fra processi e non il transport — 102,842 ms letti su GitHub Actions contro un budget di 25 ms (`docs/CERTIFICAZIONI/gate1.md` §5). Manca il canale fisico PC↔telefono. | sì |
| T-07 | Gate 1 — validazione fisica human-in-the-loop (`docs/ROADMAP_ESECUTIVA.md` S:2.5, DoD punto 8) | Con il processo target realmente in esecuzione e l'host in `--listen`: sulla console il conteggio `frames=` cresce, sul telefono lo stato `Transport`, poi staccare la rete del dispositivo e confermare `status=disconnected` e journal SQLite integro. Firma su `docs/CERTIFICAZIONI/gate1.md`. | sì |
| T-08 | Gate 3 — decisione sul client vivo | Il ciclo di decisione ora esiste come processo: `NosAi.Runtime.dll --decide` lo avvia dentro il Gate 1 e pianifica sullo snapshot canonico. Riscontro offline ripetibile: `NosAi.Runtime.dll --decide-replay data/nostale_combat.noscap` produce decisioni su HP 7218/7305 e 7305/7305 letti dalla cattura reale, entrambe `NoCandidate` (personaggio sano, bersaglio sconosciuto). **Resta da fare:** avviare con `--observe-game` su una sessione in corso e vedere il ciclo passare da `NoWorldState` a una decisione su letture `LIVE`. Nulla può agire: la politica di sicurezza tiene l'input disabilitato e l'effector è `DisabledActionEffector`, quindi l'esito atteso a valle del Safety Gate è `ExecutionDisabled`. | sì |
| T-09 | Calibrazione della ROI del riquadro bersaglio (ADR-0018) | `HasTarget` è ora stabilito dallo schermo, ma le frazioni `TargetHpBar` di `RoiSegmenter` (`0.40, 0.06, 0.20, 0.02`) non sono mai state verificate su un client reale: solo `PlayerHpBar` lo è, con T-03. Un lettore puntato male non fallisce — misura i pixel sbagliati e riporta `Absent`, cioè un *nessun bersaglio* sicuro di sé a ogni frame. Finché la calibrazione non esiste il composer rifiuta con `target_roi_not_calibrated` e `HasTarget` resta UNKNOWN. **Da fare sul client vivo:** selezionare un bersaglio, eseguire `NosAi.Runtime.dll --hud-probe`, guardare `data/perception/crops/target_latest.bmp`; quando il ritaglio è il riquadro bersaglio, registrarlo con `--hud-probe --calibrate-target <x> <y> <w> <h>` (frazioni dell'area client). Poi verificare i due esiti sul vivo: riquadro presente → `Present`, bersaglio deselezionato → `Absent`. Il file sta in `data/perception/` ed è **versionato dal 2026-09-07** (`data/perception/target-roi.calibration`, commit `431ff1a`); resta specifico di una macchina e di una risoluzione, quindi altrove va rifatto. | sì |
| T-10 | Calibrazione della proiezione schermo (F2-3) | **Riscritta il 1 set 2026: il modello era sbagliato, non impreciso.** La versione precedente adattava `schermo = A*coordinataMappa + C`, e una trasformazione del genere non esiste: la telecamera segue il personaggio, quindi la stessa casella e' disegnata a un pixel diverso ogni volta che lui si muove. Misurato sul client vero, camminando dodici caselle il pixel del personaggio si e' spostato di sette; tre campioni fissano sei incognite esattamente, quindi il residuo usciva 0.00 su una trasformazione che non descriveva nulla. Ora si adatta `schermo = A*scostamento + ancora`, dove lo scostamento e' in caselle dal personaggio: e' la sola quantita' misurabile, e l'ancora acquista un significato verificabile (e' il pixel dove il personaggio e' disegnato, e un fit che lo mette fuori dalla finestra viene rifiutato). Il file porta una versione, quindi una calibrazione del modello assoluto viene rifiutata invece di essere reinterpretata. **Da fare sul client vivo:** console **elevata**, personaggio in zona aperta, finestra del gioco in primo piano, poi `NosAi.Runtime.dll --screen-autocalibrate --arm-input`: il runtime sceglie sei pixel su un anello, li clicca attraverso il Safety Gate e legge da `WalkTarget` quale casella il client ha risolto per ognuno. Tre servono al fit, gli altri lo verificano; un campione contraddetto viene scartato e si rifa' il fit; una scala che non e' una casella di mappa e' rifiutata. Nessuno deve mirare e nessuno deve conoscere una coordinata. Senza `--arm-input` gira a secco, perche' ogni campione fa camminare il personaggio. Verificare infine che un `MoveToPosition` clicchi dove previsto. Il file sta in `data/perception/` ed è **versionato dal 2026-09-07** (commit `431ff1a`). **Finestra e schermo intero non sono piu' un problema:** un ridimensionamento e' rifiutato per nome invece di puntare al posto sbagliato, e basta rieseguire il comando per ricalibrare. **Aggiornamento 1 set 2026, sera: eseguito sul client vivo cinque volte, e nessuna delle cinque ha prodotto una calibrazione utilizzabile.** Il modello `schermo = A*scostamento + ancora` regge - i pixel bassi danno letture pulite e monotone - ma la procedura aveva tre difetti che solo il client vero ha mostrato. **Primo:** il fit passava esattamente per i primi tre campioni e misurava gli altri contro di esso, per cui sei letture d'accordo entro una casella risultavano in disaccordo di 218 px; ora e' ai minimi quadrati su tutti i campioni. **Secondo:** la soglia del residuo era 6 px, unita' che l'auto-calibrazione non misura affatto - legge un indice di casella, e una casella e' circa 32x15 px; ora il residuo e' giudicato in caselle (max 1.5 = mezza casella di quantizzazione del clic piu' mezza della posizione sub-casella del personaggio). **Terzo, il piu' grave:** con soli tre campioni sotto, scartare i campioni finche' il fit passa e' adattare il rumore; una corsa ha scartato uno su cinque, fittato quattro e **scritto** una trasformata ruotata di trenta gradi. Ora il minimo e' sei campioni fittati e si cerca il *piu' grande* insieme concorde, mai il piu' piccolo. **Quarto:** un residuo piccolo non e' una risposta determinata - due corse a minuti di distanza, stessa finestra, hanno dato caselle da 37 px e 56 px passando entrambe il residuo; ora si rifiuta quando l'errore standard della scala supera il 5% (mezza casella a dieci caselle, che e' la distanza di questi clic). **Cosa resta, e non e' codice:** l'ultima corsa ha camminato 11 clic su 12 e ha dato 3% in orizzontale e 11% in verticale, perche' le letture verso l'alto non sono monotone (+213 px -> -13 caselle, +171 px -> -3) mentre quelle verso il basso lo sono. C'e' un ostacolo a nord del personaggio. **Serve rieseguire con il personaggio in una zona davvero aperta, con almeno quindici caselle libere in ogni direzione, nord compreso.** **CHIUSO il 2026-09-07 da T-15**, e non ricampionando: il modello era ancora sbagliato. La mappa non e' affine ma **prospettica** -- la casella vale piu' pixel in basso che in alto, misurato su due sessioni indipendenti -- e con i due termini in piu' i dodici campioni del 3 settembre passano da 2,43 a 1,23 caselle di residuo. La calibrazione esiste e il runtime la dichiara *usable*: `data/perception/screen-projection.calibration`, passo 38,14 x 16,83 px per casella, 19 campioni su 20, ed e' **versionata**. Il racconto qui sopra resta perche' i quattro difetti che elenca erano veri e sono stati corretti; quello che non era vero e' la diagnosi finale. | **no** |
| T-11 | Posizione propria dalla memoria del client (F1-10) | **Chiusa il 1 set 2026.** `NosTaleClientLayout` trova l'oggetto del personaggio con una firma di codice (3 ms), non con un offset ricordato: niente si conserva fra un avvio e l'altro, quindi ASLR e riavvii non sono piu' una fonte di errore. Confermata da una sorgente indipendente: l'id che il client tiene a `+0x24` vale **3443217**, lo stesso che il server aveva mandato su `cond`. Verificato che segua il personaggio con dieci letture in 20 secondi durante il movimento. Serve una console elevata, perche' il manifest del client dichiara `requireAdministrator`. Prova completa in `docs/GATE1_CHECKLIST.md`. | **no** |
| T-12 | Calibrazione screen-space del pannello equipaggiamento (AP-07) | **Calibrazione eseguita e scritta il 2026-09-06** su client vivo (finestra `TNosTaleMainF`, area client 1024x768): l'operatore ha confermato sul proprio pannello "Info Personaggio" la griglia 3×6 e la mappatura dei 18 `EquipmentSlot` (ordine per riga: Mask/Hat/Fairy, Weapon/SpecialistCard/SecondaryWeapon, Gloves/Armor/Boots, Ring/Necklace/Bracelet, CostumeWings/Amulet/WeaponSkin, CostumeHat/CostumeSuit/MiniPet — 2 icone incrociate con l'evidenza visiva della schermata inviata: pugnali per `Weapon`, ritratto per `SpecialistCard`). `--calibrate-inventory-panel` con tutti e 18 i token ha scritto `data/perception/inventory-panel-roi.calibration` (confermato dall'output, nessun `[REFUSED]`). **Seconda metà chiusa il 2026-09-08**, e la domanda aveva un presupposto sbagliato: **non esiste un `InventoryKind` che significhi «indossato»**. Registrazione `data/equip_test_20260908_131656.noscap` (30 s dal pannello, l'operatore equipaggia un oggetto, ne toglie due, ne rimette uno). Sui quattro `ivn` della cattura il tipo è **sempre 0**, sia quando l'oggetto esce dalla borsa sia quando vi rientra; `Wear=8` non compare mai. Lo stato indossato viaggia nel pacchetto **`equip`**, dove ogni pezzo è `slot.vnum.…`: `#1078` porta `4.715.0.2.0.0.0` mentre `ivn 0 18.0.0.0.0.0.0` svuota lo slot 18; a `#1820` `ivn 0 18.715.0.2.0.0.0` riporta l'oggetto in borsa e `#1821` non ha più il pezzo `4.`; `#2547`/`#2548` ripetono la coppia per il vnum 902 nello slot 12, e `#3261`/`#3262` la invertono al rientro. Riproducibile: `NosAi.Runtime.dll --wire-inspect data/equip_test_20260908_131656.noscap --timeline equip,ivn`. **Prossimo passo, non un blocco:** `GameplayObservationProjector` lascia `Player.Equipment` vuoto in attesa di un kind confermato — il canale da leggere è `equip`, non `ivn`. Il file di calibrazione sta in `data/perception/` ed è **versionato dal 2026-09-07** (`data/perception/inventory-panel-roi.calibration`, commit `431ff1a`); resta specifico di macchina/risoluzione. | **no** |
| T-13 | AP-05 — la catena mob → World Model → planner → attuazione, su client vivo | È il passo che porta l'intera catena AP-05 da `Present` a `Verified`: nessun operatore ha ancora visto un `Mob` reale nello snapshot fuso, e quindi nulla di ciò che ci sta sopra è stato osservato funzionare. **Serve:** client NosTale in esecuzione, WinDivert installato in `tools/windivert/`, un terminale **come Amministratore**, e un volume etichettato `NOSAI-SSD` collegato — **soddisfatto dal 2026-09-07**, con il catalogo che conta 2705 mostri: senza di esso il catalogo non si apre (`reference catalog: nosai_ssd_not_found`) e il passo (1) riporta `no_monster_observed` per una ragione che non riguarda il gioco. Controllarlo prima di cominciare: la prima riga di `--world-replay` lo dice. Vedi `EXTERNAL_SSD_DEPLOYMENT.md` § 1-bis — senza la cattura pacchetti non esistono entità, e da `0fc7f55` `--engage` **rifiuta** invece di premere un tasto (`engage_target_not_verifiable`). **(0)** `... --keybinds-check` e annotare quali intent `skill.<id>` esistono e sono `Confirmed`: l'`<skillId>` che `--engage` accetta è quello, non il vnum della skill né lo slot del pacchetto `sr` (`EngageCommand.cs:232` costruisce l'intent come `skill.` + l'argomento). Senza un keybind confermato il passo (4) rifiuta con `keybind_not_confirmed` e non prova nulla. **(1)** Con il personaggio fermo accanto a dei mostri: `dotnet src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll --combat-report`. Atteso: una riga `mobs: N observed, ...` con `N > 0`. Se `N = 0`, il `[WARN]` dice già quale dei mondi possibili è — `no_monster_observed` significa che il catalogo non ha stabilito alcun vnum (feed assente, o solo vnum mai nominati da un pacchetto `in`), che è diverso da `no_mob_known_hostile`. **(2)** Farsi colpire da un mostro e rieseguire: l'ostilità si stabilisce solo dal fatto osservato di un colpo subito, quindi `known hostile` deve salire e comparire una riga `engage: target=mob-... would_act=True`. **(3)** Confrontare le due metà del report: le righe `engage:` coprono la portata skill (6.0), le righe `candidate:` la portata dell'attacco base (2.0). Un mob a distanza intermedia deve comparire nelle prime e non nelle seconde — è esattamente la divergenza che `a39197e` ha chiuso, e vederla dal vivo la conferma. **(4)** Prendere un id da una riga `engage: ... would_act=True` e lanciare `... --engage mob-<id> <skillId> --watch 3`. Atteso: il rifiuto **non** compare, la pressione avviene, e ogni round ristampa l'evidenza. Poi allontanarsi oltre i 6.0 mentre `--watch` gira: i round successivi devono dire `engage_target_refused:target_out_of_range` **senza** terminare l'invocazione, e riprendere se si rientra. **(5)** Da registrare:** l'output completo dei due comandi, e se le violazioni stampate da `--combat-report` coincidono con quelle che `--engage` ha rifiutato. **Nota:** senza permessi da Amministratore ogni passo rifiuta con motivazione nominata e non è evidenza di nulla; un rifiuto per mancanza di privilegi non chiude questa riga. | sì |
| T-14 | Una cattura di rete che cominci **prima** del login | Tutte e cinque le catture esistenti sono state prese a client gia' in gioco, quindi non contengono i pacchetti di caricamento personaggio. Censite il 2026-09-07 con `--world-replay`: 27 726 messaggi inbound complessivi, **zero `ski`** (la lista abilita'), e `sc`/`equip`/`inv` solo parzialmente in `equip_test.noscap`. **Cosa fare:** avviare `NosAi.Runtime.dll --record-wire <file.noscap>` a client **chiuso**, poi aprire il client, accedere ed entrare in gioco; lasciare correre qualche secondo e fermare. Un solo file basta. **Perche' serve:** e' l'unica cosa che sblocca `docs/agents/phases/AP-05/AP-05_A2A4_DEEPSEEK_player_skill_list.md` parte 2, cioe' il canale di osservazione a monte dei due difetti gia' corretti in `5e9bd93` e `a39197e`. Senza, un decoder per `ski` sarebbe un parser mai eseguito su un byte reale, che la regola sulle fonti esterne di `CLAUDE.md` vieta. Nessun altro esperimento e' necessario: il censimento ha gia' escluso che il pacchetto sia nascosto nelle registrazioni esistenti. | si' |

## Chiusi

- **T-01** — 1 set 2026. APK wire v4 reinstallato; sessione USB `c9d2f5f0c9d1` su socket loopback via `adb reverse`, poi tunnel rimossi e sessione Wi-Fi `a6bb4f040122` su `192.168.0.4:17471 <- 192.168.0.2:55514`. Un APK più vecchio era stato rifiutato con `invalid_header:unsupported_version` prima dell'aggiornamento, che è la clausola del rifiuto all'header.
- **T-02** — 1 set 2026. L'app dichiara `Chiave del dispositivo: Android Keystore` e l'abbinamento ha retto su entrambe le sessioni. Ha richiesto una correzione: `store.GetKey(...) is IPrivateKey` rispondeva falso su una chiave AndroidKeyStore, perché la classe non ha binding gestito e .NET Android restituisce un proxy generico; la custodia era silenziosamente degradata a file.
- **T-12** — 8 set 2026. Chiuso dall'evidenza sopra: `ivn` non cambia mai tipo, l'equipaggiato sta in `equip`. Ha richiesto due correzioni al pannello, perché la card mostrava zero righe su una registrazione che le aveva: `ToolRunner.RunAsync` tornava senza attendere la consegna delle ultime righe (`WaitForExitAsync` non aspetta gli handler asincroni), e la card filtrava `kind=` da `--world-replay`, cioè la metà del filo che non contiene la risposta. Ora chiede `--wire-inspect --timeline equip,ivn` e legge dall'output del comando.
- **T-04** — 1 set 2026. 143 pacchetti dal gioco (41678 byte) in `data/nostale_01.noscap`, poi 1131 in `data/nostale_combat.noscap`. Ha richiesto una correzione: `FlagRecvOnly` valeva `0x0008`, che in WinDivert 2.x è `SEND_ONLY`; l'handle di cattura era aperto in sola scrittura e non poteva ricevere nulla. Confermato con una cattura di controllo su traffico generato apposta.

---

## T-15 — proiezione schermo → mappa — **CHIUSO il 2026-09-07**

**La mappa non era affine, ed era quello il problema.** Il test era stato aperto
perché i dodici campioni allora in archivio lasciavano 2,43 caselle di residuo
contro una soglia di 1,5, e sottoinsiemi contigui si adattavano a trasformazioni
diverse. La lettura di allora — «il file è una miscela di due sessioni» — era
sbagliata: era una sola geometria, descritta con il modello sbagliato.

### La misura che l'ha deciso

Dividendo ciascuna sessione fra i clic nella metà alta e quelli nella metà bassa
della finestra, il passo della casella cambia nella stessa direzione in tutte e
due:

| sessione | passo in alto | passo in basso |
|---|---|---|
| 2026-09-03, 12 clic | 30,7 × 13,0 | 36,6 × 22,8 |
| 2026-09-07, 11 clic | 36,9 × 14,0 | 42,9 × 17,3 |

La casella è più grande in basso e più piccola in alto: è una telecamera
inclinata, e nessuna mappa affine può rappresentarla. Con i due termini
prospettici i dodici campioni del 3 settembre passano da 2,43 a **1,23 caselle**
— sotto soglia, con gli stessi campioni che venivano rifiutati. I due termini che
le due sessioni misurano indipendentemente concordano: `H` vale −0,0181 e
−0,0153, `G` è circa zero in entrambe.

### Cosa è stato corretto lungo la strada

- **`--screen-watch` chiedeva cinque campioni.** Cinque clic non determinano un
  fit: la sessione delle 21:14 è stata rifiutata con `scale_not_determined:72x43pct`,
  con una dispersione di 150 contro le 893 000 dell'anello che aveva calibrato.
- **La posizione del personaggio si legge in ritardo.** Una sessione ha prodotto
  sette clic su nove che dichiaravano ~13 caselle con il cursore entro 40 px dal
  personaggio: il delta aveva inglobato la camminata precedente. Il consigliere
  rifiuta ora un campione i cui pixel dal centro finestra, divisi per le caselle,
  scendono sotto 8 — soglia scelta misurando: le sessioni buone stanno fra 11 e
  41, quella cattiva dava 1, 1, 2, 3, 3, 4, 7.
- **L'incertezza era calcolata con la matrice del fit affine** applicata a un fit
  a otto parametri. Sui campioni veri la differenza era fra 13×35% e 5×8%.
- **Il DPI scritto nel file era 0**, quindi la calibrazione apparteneva a un
  regime che nessun campione aveva.

### Il risultato

Venti clic dell'operatore, uno scartato come fuori bersaglio, diciannove usati:

```
Passo 38,14 × 16,83 px per casella
Personaggio disegnato a (516, 445) di 1024×768 a 120 DPI
Residuo peggiore 52 px su 19 campioni
```

`data/perception/screen-projection.calibration`, versione 5. Il runtime la
rilegge e la dichiara **usable**.

### Quello che resta

La calibrazione scritta ha i termini prospettici a zero: tolto il campione fuori
bersaglio, l'affine batteva la prospettica sul residuo, e la regola adotta la
prospettiva solo se paga. Vale dentro la regione campionata; una sessione con
clic più estremi in verticale probabilmente farebbe vincere la prospettica.
Non è un blocco: è la prossima misura utile, non un difetto.

---

## T-16 — collegare un id sul filo al testo che l'operatore legge sullo schermo

**Aperto il 2026-09-07.** Il catalogo contiene ora **50 704** voci di testo del
client, fra cui 3639 missioni, 22 358 battute di NPC e 374 nomi di mappa
(`--reference-info` le conta). Il filo porta id in `sayi` e `msgi`. Nessuno ha
stabilito la corrispondenza fra i due, e **non si stabilisce dai dati che
abbiamo**.

### Cosa è già stato provato, e perché non basta

La riga reale, da `data/nostale_combat.noscap`:

```
sayi 1 3443217 12 975 2 2006 1 0 0
```

Il campo 6 è `2006`, che in quella stessa cattura è il vnum dell'oggetto caduto
e raccolto (`drop 2006 …`, `ivn 2 34.2006.1.0`): il pacchetto porta quindi
almeno un **parametro** riconoscibile, e la lettura «messaggio localizzato con
argomenti» regge. Ma il candidato naturale per l'id — il campo 4, `975` —
cercato in tutte e cinque le tabelle di solo testo, con e senza l'aggiustamento
di ±1, dà testi che non c'entrano nulla («Torneremo presto! Non ci arrenderemo
mai!», una missione di Hazel al Campo Akamur).

Quindi: o l'id non è quel campo, o la numerazione delle chiavi ha un'altra
regola, o la tabella giusta è un'altra. **Tre ipotesi che i dati registrati non
sanno distinguere**, perché nessuna delle cinque catture ha accanto ciò che
l'operatore vedeva sullo schermo in quel momento.

### Metà stabilita il 2026-09-07 — l'argomento

`data/messaggi.noscap` (2657 pacchetti, registrati apposta) contiene l'evento
intero: `drop 8 …`, `get … 4867701 0`, `sayi 1 3548294 12 975 2 8 1 0 0`,
`ivn 0 0.8.…`. Il campo 4 è **l'id del messaggio**, il campo 6 il suo
**argomento**, e l'argomento è il vnum dell'oggetto raccolto — confermato su due
catture indipendenti con due vnum diversi (`8` e `2006`). Il vnum risolve nel
catalogo: «Fionda in legno». Vedi `docs/PROTOCOLLO_NOSTALE.md` § `sayi`.

### Quello che resta, e dove non è

**L'id del messaggio non indicizza il catalogo.** Cercato `zts975?e` — con il
carattere marcatore, la regola che risolve i nomi dei mostri — in tutte e dodici
le tabelle di `NSlangData_IT.NOS`, per 975, 654, 697 e 2110: solo voci
scollegate. Il testo dei messaggi di sistema non è in quell'archivio. Il
candidato non ancora aperto è **`NScliData_IT.NOS`**, che `ReferenceImporter`
non legge.

### L'archivio è stato aperto, e la risposta è no

Il 2026-09-08 `NScliData_IT.NOS` è stato aperto (Q-138). Contiene **una sola
voce**, `conststring.dat`, che decodifica in **7869 righe** di testo italiano —
ed è il posto giusto: contiene `«è raccolto.»` e `«Hai raccolto [%s]:»`.

**Ma l'id `sayi` non lo indicizza.** Verificato due volte:

- **chiave diretta**: 975, 654 e 2110 non esistono nella tabella; 697 dà
  «Lacrima», che non c'entra;
- **scarto costante**: fissando lo scarto sulla coppia che torna (975 → 10666,
  «Hai raccolto [%s]»), gli altri tre id cadono su «Sono passate %d ore»,
  «Stessa età» e «Questo giocatore è già sposato».

La colonna chiave, che il lettore non decodificava, ora si legge: **7565 chiavi
numeriche distinte**. Il difetto era in `NosDataTable.Parse`, che salta sempre il
byte dopo il terminatore trattandolo come lunghezza, mentre per le chiavi da 100
in su quel byte è il marcatore di numero impacchettato.

### Importato il 2026-09-08

`conststring` è ora nel catalogo con le sue chiavi numeriche: **7445 voci**, e il
totale del testo passa da 50 704 a **58 149**. Le chiavi risolvono —
`10666` → «Hai raccolto [%s]:», `3099` → «è raccolto.», `1` → «OK» — e gli id del
filo continuano a non esserci, il che è esattamente quello che il catalogo deve
dire finché il collegamento non è stabilito.

Il difetto del lettore che teneva le chiavi illeggibili è corretto:
`NosDataTable.Parse` distingue ora il marcatore di numero impacchettato dalla
lunghezza dichiarata, e ogni tabella con chiavi da 100 in su ne beneficia.

### Quello che resta

Il testo c'è, indicizzabile, e l'id del filo non è il suo indice. Serve ancora
una coppia osservata (testo a schermo ↔ riga della cattura) per stabilire la
regola, e il pannello ora la raccoglie: **Rete → «Registra il filo, e annota cosa
hai visto»** scrive cattura e nota con lo stesso nome.

### Cosa serve, ed è un solo gesto

**Dal 2026-09-08 non c'è niente da digitare.** Pannello di controllo → **Rete** →
«Registra il filo, e annota cosa hai visto»:

1. scrivi nella casella cosa comparirà a schermo, premi **Registra**;
2. in gioco, provoca un messaggio inequivocabile — raccogliere un oggetto è il
   più semplice — e **scrivi il testo esatto** mentre è ancora a schermo;
3. la registrazione si ferma da sola.

Appena finisce, il pannello mostra sotto, nello stesso riquadro:

- **gli id che il filo ha detto**, con l'argomento e il nome che il catalogo gli
  dà (il nome solo quando il campo 5 dichiara che l'argomento è un oggetto: con
  qualunque altro valore il numero non è un vnum, e cercarlo darebbe un nome
  plausibile e falso);
- **le righe del catalogo dei messaggi che contengono le parole della nota**, con
  la loro chiave.

Le due metà si vedono così una accanto all'altra nel momento in cui esistono
entrambe, che è l'unico in cui la seconda esiste. **Il pannello non le collega**:
mostra e basta. Con una coppia la regola si propone, con tre si conferma — e
finché non è confermata nessuna riga di codice deve dedurla.

Finché non è fatto, il testo resta nel catalogo e **nessuno può dire quale riga
vale adesso** — ed è per questo che `DialogWindowStateComposer` continua a
trattare lo schermo come sola fonte sulla presenza di un pannello.

---

## T-17 — il costo MP di un'abilità: il catalogo lo dichiara, il filo deve confermarlo

**Aperto il 2026-09-08.** Più documenti danno AP-05 bloccato per «nessun dato
reale di danno/costo skill». **Il dato c'è**: `Skill.dat` è importato dal
2026-09-07 (1958 abilità) e ogni riga porta un campo `COST`. Dal 2026-09-08 anche
l'altro lato esiste — il campo 5 di `su` è il vnum dell'abilità — quindi i due si
possono confrontare.

### Cosa è già stabilito

Le sette abilità che le registrazioni contengono, con il primo valore di `COST`:

| vnum | nome | posizione nella classe | `COST[0]` |
|---:|---|---:|---:|
| 200 | Ritmo | 0 | **0** |
| 220 | Colpo di base | 0 | **0** |
| 222 | Colpo furioso | 2 | 5 |
| 223 | Colpo preciso | 3 | 7 |
| 224 | Energia della spada | 4 | 8 |
| 226 | Terremoto | 6 | 15 |
| 228 | Attacco Doppio | 8 | 7 |

I valori si dividono esattamente come la posizione nella classe suggerisce: zero
ai due attacchi base, un costo agli altri cinque. E il filo concorda sui due a
zero — in `certificazione` (36 usi di 220) e `messaggi` (34 usi di 200) l'MP ha
**un solo valore distinto** in tutta la registrazione, uguale al massimo.
Settanta usi senza un punto speso.

### Cosa manca, e perché le catture attuali non bastano

Che `COST[0]` sia l'MP *in generale* resta non confermato. Dove l'MP si muove,
`stat` arriva troppo di rado: le transizioni osservate valgono 43, 25, 22 e 30
punti su finestre di 90-380 pacchetti, e nel mezzo la rigenerazione **risale a
scatti di +24**. Ogni calo osservato è quindi costo *meno* rigenerazione, e
nessuno dei due si legge da solo.

C'è un accostamento: la raffica di 226 — un lancio e undici colpi, ed è l'unica
delle sette con raggio d'area — cade fra due letture di MP che distano esattamente
i **15** punti che il catalogo dichiara. Una coincidenza su una finestra sporca
non è una misura.

### La registrazione che chiude la domanda

Serve **una condizione pulita**, non una sessione di gioco:

1. Pannello → **Rete** → «Registra il filo, e annota cosa hai visto».
2. In gioco, **fermo**, con l'MP al massimo. Aspetta qualche secondo senza fare
   niente: serve a vedere qual è il valore di partenza e che non stia risalendo.
3. Usa **una sola volta** un'abilità a costo dichiarato — la più cara è meglio,
   perché il calo si distingue dalla rigenerazione. Nella nota scrivi **quale**.
4. Resta fermo altri dieci secondi, poi ferma la registrazione.

**Da leggere:** i pacchetti `stat` prima e dopo, e il `su`/`ct` in mezzo. Se il
calo è esattamente il `COST[0]` di quell'abilità, il campo è confermato; ripetuto
con una seconda abilità di costo diverso, è una regola.

**Perché conta:** con il costo confermato, la simulazione di combattimento smette
di essere rimandata «per assenza di dati» — `CombatPlanner` potrebbe finalmente
sapere quanto costa ciò che sta valutando, invece di trattare ogni abilità come
gratuita.

**Quello che resta comunque aperto** anche dopo: il **danno**. `su` porta un
numero di danno per colpo, ma il catalogo non è stato incrociato con quello, e il
danno dipende da statistiche del personaggio che nessun file da solo determina.
