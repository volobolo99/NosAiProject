# Test rimandati (operatore)

Elenco unico dei test **reali** che l'operatore ha rimandato.
Non bloccano lo sviluppo. Un test qui non è `Verified`.
Chiudere una riga richiede evidenza (log, checklist, o nota in `docs/GATE1_CHECKLIST.md`).

Gli agenti ricordano le voci **aperte** a ogni resoconto di fine lavoro.

| ID | Cosa | Cosa fare | Aperto |
|---|---|---|---|
| T-01 | Wire v3 sul telefono (ADR-0009) | Reinstallare l'APK (`Abbina telefono`), poi sessione USB e sessione Wi-Fi. Un APK v2 viene rifiutato all'header. | sì |
| T-02 | Android Keystore sul dispositivo (ADR-0010) | Dopo T-01, verificare che l'app dichiari custodia Keystore (non file) e che l'abbinamento regga. | sì |
| T-03 | Barra HP/MP su NosTale reale | Client visibile → Control Panel → Percezione → probe DXGI. Controllare `data/perception/crops/`. `DERIVED` solo se la ROI centra la HUD; altrimenti resta UNKNOWN. | sì |
| T-04 | Prima cattura di traffico reale | Installare WinDivert in `tools/windivert/`, poi catturare una sessione su `.noscap` e misurarla con `WinDivertProbe.exe --analyze <file>`. Oggi **nessun byte reale è mai stato catturato**: tutto il livello è provato su sintetico e su file registrati. | sì |
| T-05 | Derivare `ProtocolMap.PlayerVitals` | Dalle catture di T-04, correlare i byte con HP/MP letti sullo schermo del client usando `TrafficRecorder.FindOffsetsMatching`. È la sola cosa che manca perché il gameplay smetta di essere `UNKNOWN`. Più catture con valori diversi restringono i candidati: un solo riscontro non basta. | sì |
| T-06 | Gate 1 — handshake Noise su nodo mobile reale (`docs/ROADMAP_ESECUTIVA.md` S:2.5) | Avviare `NosAi.Host --gate 1 --attach <process> --module-sha256 <hex> --listen` (default porta 17480). Da un telefono, un iniziatore `Noise_XX_25519_ChaChaPoly_SHA256` + `NosFrameHeader` (non l'APK Guard attuale, che parla `WireHeader`) completa 100 handshake; p99 < 25 ms. Il protocollo è già verde in-process e su TCP loopback (`TransportLoopTests`); manca il canale fisico PC↔telefono. | sì |
| T-07 | Gate 1 — validazione fisica human-in-the-loop (`docs/ROADMAP_ESECUTIVA.md` S:2.5, DoD punto 8) | Con il processo target realmente in esecuzione e l'host in `--listen`: sulla console il conteggio `frames=` cresce, sul telefono lo stato `Transport`, poi staccare la rete del dispositivo e confermare `status=disconnected` e journal SQLite integro. Firma su `docs/CERTIFICAZIONI/gate1.md`. | sì |

## Chiusi

Nessuno.
