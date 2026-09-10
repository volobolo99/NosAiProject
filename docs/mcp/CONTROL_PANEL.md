# MCP Control Panel

Il pannello principale di NosAi deve aprire il pannello MCP locale su `http://127.0.0.1:8770/` tramite un’azione esplicita dell’operatore.

## Controlli esposti

- stato `offline/network` e motivazione dell’ultima transizione;
- pulsante **Attiva MCP Rete** con conferma operatore;
- catalogo dei modelli, tier, capability e requisito rete;
- gestione aggiunta/modifica/eliminazione chiavi per provider;
- esiti simulazioni e candidati di apprendimento;
- audit degli eventi senza valori segreti;
- avvisi su provider non disponibili, quote e fallback.

Il pannello non può esporre il valore completo di una chiave. Mostra soltanto presenza, timestamp e fingerprint parziale, mantenendo il valore cifrato localmente.


