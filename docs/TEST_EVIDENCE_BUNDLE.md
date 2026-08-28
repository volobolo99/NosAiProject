# NosAi Test Evidence Bundle

Ogni test ufficiale deve produrre un bundle portabile sotto `.nosai/test-center/runs/<RUN-ID>/`.

## Contratto minimo

- `manifest.json`: run ID, schema, versione progetto, commit, timestamp, piattaforma, device e stato finale.
- `environment.json`: OS, runtime, hardware e configurazione rilevante.
- `tests.json`: risultato di ogni test con durata, messaggio e dettagli.
- `metrics.json`: performance e metriche osservate.
- `events.jsonl`: eventi runtime cronologici.
- `ai_decisions.jsonl`: decisioni/proposte, confidence, rationale e outcome quando disponibili.
- `errors.jsonl`: errori, eccezioni e contesto diagnostico.
- `artifacts.json`: artifact raccolti con dimensione e SHA-256.
- `artifacts/`: screenshot, log, report e altri file utili alla diagnosi.

## Stati

Sono ammessi solo `NOT_RUN`, `RUNNING`, `PASS`, `FAIL`, `PARTIAL`.

Un test `NOT_RUN`, `PARTIAL` o `FAIL` impedisce il PASS del run.

## Gate fisici

Per una fase che richiede entrambe le piattaforme, `finalize()` deve ricevere almeno:

```json
{"pc": "PASS", "smartphone": "PASS"}
```

Qualsiasi altro valore blocca il run.

## Regola di miglioramento

Il Test Center deve poter confrontare il bundle corrente con una baseline precedente. Un PASS funzionale non implica automaticamente un miglioramento: regressioni di latenza, stabilità, accuratezza, risorse o comportamento AI devono essere evidenziate separatamente.

## Privacy e sicurezza

I collector non devono inserire segreti, token, password, cookie, chiavi private o dati personali non necessari. I log devono essere redatti prima della condivisione.

## Uso operativo

Il bundle viene generato automaticamente dal runner di test. Non è valido dichiarare una fase PASS sulla base di un messaggio umano senza bundle di evidenza associato.
