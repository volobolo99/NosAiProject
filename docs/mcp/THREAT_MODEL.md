# MCP Threat Model

## Minacce considerate

- provider remoto non disponibile o che restituisce output corrotto;
- prompt injection da risorse online;
- accidental logging di API key/token/password;
- configurazione che abilita la rete senza consenso;
- modello Direttore che tenta di modificare il proprio giudice;
- apprendimento di una strategia non verificata;
- saturazione CPU/RAM/VRAM o latenza che blocca il loop live.

## Mitigazioni

- offline default e conferma operatore;
- redazione e cifratura locale dei segreti;
- allowlist di provider e tool;
- contratti versionati e test fail-closed;
- simulazione side-effect-free e shadow mode;
- audit append-only e rollback esterno;
- separazione netta tra MCP e autorità Safety del gameplay;
- code/budget bounded e fallback locale.


