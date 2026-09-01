# Test rimandati (operatore)

Elenco unico dei test **reali** che l'operatore ha rimandato.
Non bloccano lo sviluppo. Un test qui non è `Verified`.
Chiudere una riga richiede evidenza (log, checklist, o nota in `docs/GATE1_CHECKLIST.md`).

Gli agenti ricordano le voci **aperte** a ogni resoconto di fine lavoro.

| ID | Cosa | Cosa fare | Aperto |
|---|---|---|---|
| T-01 | Wire v4 sul telefono (ADR-0009) | Reinstallare l'APK (`Abbina telefono`), poi sessione USB e sessione Wi-Fi. Un APK più vecchio viene rifiutato all'header. | **no** |
| T-02 | Android Keystore sul dispositivo (ADR-0010) | Dopo T-01, verificare che l'app dichiari custodia Keystore (non file) e che l'abbinamento regga. | **no** |
| T-03 | Barra HP/MP su NosTale reale | Client visibile → Control Panel → Percezione → probe DXGI. Controllare `data/perception/crops/`. `DERIVED` solo se la ROI centra la HUD; altrimenti resta UNKNOWN. | sì |
| T-04 | Prima cattura di traffico reale | Installare WinDivert in `tools/windivert/`, poi catturare una sessione su `.noscap` e misurarla con `WinDivertProbe.exe --analyze <file>`. | **no** |
| T-05 | Derivare i vitals dal traffico | Il traffico non è binario a offset fissi: è testo dopo la decodifica, quindi `FindOffsetsMatching` non si applica. `NosTaleWorldDecoder` legge il canale world e `stat <hp> <maxHp> <mp> <maxMp>` porta i vitals, verificati contro la HUD. Catalogo completo in `docs/PROTOCOLLO_NOSTALE.md`. Resta da scrivere il provider che pubblica quei valori come `LIVE`. | sì (provider) |
| T-06 | Gate 1 — handshake Noise su nodo mobile reale (`docs/ROADMAP_ESECUTIVA.md` S:2.5) | Avviare `NosAi.Host --gate 1 --attach <process> --module-sha256 <hex> --listen` (default porta 17480). Da un telefono, un iniziatore `Noise_XX_25519_ChaChaPoly_SHA256` + `NosFrameHeader` (non l'APK Guard attuale, che parla `WireHeader`) completa 100 handshake; p99 < 25 ms. Il protocollo è già verde in-process e su TCP loopback (`TransportLoopTests`); manca il canale fisico PC↔telefono. | sì |
| T-07 | Gate 1 — validazione fisica human-in-the-loop (`docs/ROADMAP_ESECUTIVA.md` S:2.5, DoD punto 8) | Con il processo target realmente in esecuzione e l'host in `--listen`: sulla console il conteggio `frames=` cresce, sul telefono lo stato `Transport`, poi staccare la rete del dispositivo e confermare `status=disconnected` e journal SQLite integro. Firma su `docs/CERTIFICAZIONI/gate1.md`. | sì |

## Chiusi

- **T-01** — 1 set 2026. APK wire v4 reinstallato; sessione USB `c9d2f5f0c9d1` su socket loopback via `adb reverse`, poi tunnel rimossi e sessione Wi-Fi `a6bb4f040122` su `192.168.0.4:17471 <- 192.168.0.2:55514`. Un APK più vecchio era stato rifiutato con `invalid_header:unsupported_version` prima dell'aggiornamento, che è la clausola del rifiuto all'header.
- **T-02** — 1 set 2026. L'app dichiara `Chiave del dispositivo: Android Keystore` e l'abbinamento ha retto su entrambe le sessioni. Ha richiesto una correzione: `store.GetKey(...) is IPrivateKey` rispondeva falso su una chiave AndroidKeyStore, perché la classe non ha binding gestito e .NET Android restituisce un proxy generico; la custodia era silenziosamente degradata a file.
- **T-04** — 1 set 2026. 143 pacchetti dal gioco (41678 byte) in `data/nostale_01.noscap`, poi 1131 in `data/nostale_combat.noscap`. Ha richiesto una correzione: `FlagRecvOnly` valeva `0x0008`, che in WinDivert 2.x è `SEND_ONLY`; l'handle di cattura era aperto in sola scrittura e non poteva ricevere nulla. Confermato con una cattura di controllo su traffico generato apposta.
