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

## Chiusi

Nessuno.
