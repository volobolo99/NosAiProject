# Contratti di modulo

## Cosa contiene un contratto
Un contratto è un documento JSON compatto che descrive i dettagli di un modulo. Contiene informazioni come il file bersaglio, le firme delle funzioni e delle struct, allineamento byte e dimensioni dei buffer, precondizioni, postcondizioni e vincoli di memoria.

## Ciclo di vita
I possibili stati di un contratto sono:
- **DRAFT**: Contratto in fase di bozza.
- **SKELETON_OK**: Contratto con lo scheletro del modulo correttamente generato.
- **INFILLED**: Contratto con tutte le funzioni e struct riempite.
- **PREFLIGHT_OK**: Contratto passato la verifica di preflight.
- **VERIFIED**: Contratto verificato senza errori.
- **ASAN_VERIFIED**: Contratto verificato con AddressSanitizer.
- **TEST_VERIFIED**: Contratto verificato con i test.
- **MERGED**: Contratto integrato nel codice principale.
- **BLOCKED**: Contratto bloccato per motivi specifici.
- **DROPPED**: Contratto scartato.

## Dove sta lo stato
Lo stato di ogni contratto viene mantenuto nel file `contracts/ledger.json` e viene aggiornato utilizzando lo strumento `update_contract_state`.
