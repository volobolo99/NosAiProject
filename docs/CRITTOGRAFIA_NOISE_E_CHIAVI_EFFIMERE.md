# NosAi — Crittografia Noise e chiavi effimere

## Origine

Questo documento importa nel progetto i requisiti architetturali contenuti nelle specifiche allegate **v1.8** e **v1.9**.

La specifica v1.8 propone un canale Zero-Trust basato sul Noise Protocol Framework, con pattern `Noise_KK_25519_ChaChaPoly_SHA256`, chiavi statiche X25519 e prologo `NOS_AI_PROTOCOL_V1`. La specifica v1.9 estende il modello con chiavi client effimere e pattern `Noise_IK_25519_ChaChaPoly_SHA256`, oltre a una suite di stress test asincrona da 1000 macro. fileciteturn88file0L2-L4 fileciteturn88file1L2-L4

## Implementazione presente

`nosai/security/ephemeral_session.py` implementa il nucleo riutilizzabile della variante effimera:

1. generazione X25519;
2. scambio Diffie-Hellman;
3. derivazione della chiave di sessione tramite HKDF-SHA256;
4. cifratura autenticata ChaCha20-Poly1305;
5. nonce monotono per la cifratura della sessione;
6. dati associati autenticati;
7. nuova chiave effimera per ogni nuova sessione.

Il modulo effimero (`EphemeralSession`) resta una costruzione a sé, mantenuta per parità di formato con la controparte Python.

Accanto ad esso, `src/NosAi.Runtime/Security/NoiseProtocol.cs` implementa il pattern **`Noise_IK_25519_ChaChaPoly_SHA256`** con la macchina a stati del framework: `CipherState`, `SymmetricState` (HKDF a catena HMAC, `MixKey`/`MixHash`/`EncryptAndHash`/`Split`) e `HandshakeState` con i token `e, es, s, ss` / `e, ee, se`. Il nonce è little-endian come impone Noise, a differenza del big-endian del modulo effimero: **i due formati non vanno mescolati**.

Il trasporto post-handshake porta il numero di sequenza esplicito sul filo, autenticato come dato associato, e lo verifica con una finestra scorrevole anti-replay in stile RFC 6479 (64 messaggi).

Resta fuori dall'attuale livello di verifica il **test di interoperabilità contro un'altra implementazione Noise**: finché non esiste, questa resta una implementazione conforme alla specifica ma verificata solo contro sé stessa.

## Chiavi statiche

La specifica v1.8 prevede coppie statiche X25519 per Core e client e lo scambio delle chiavi pubbliche. fileciteturn88file0L2-L3

Il progetto espone una funzione per generare chiavi raw, ma le chiavi private **non devono mai essere committate nel repository**. Devono essere gestite tramite un archivio locale dei segreti o un gestore di segreti.

## Chiavi effimere

La specifica v1.9 introduce il modello con server statico e client effimero per aumentare flessibilità e Forward Secrecy. fileciteturn88file1L4-L5

Nel progetto la chiave effimera è confinata all'oggetto di sessione e il riferimento può essere rimosso dopo l'uso. La rimozione del riferimento Python non è una garanzia di zeroizzazione fisica della RAM e non viene descritta come tale.

## Test

`tests/test_ephemeral_session.py` verifica:

- accordo della chiave tra le due estremità;
- autenticazione dei dati associati;
- rilevamento della manomissione;
- generazione di chiavi diverse tra sessioni.

`tests/stress_test_cifratura.py` importa l'idea dello stress test asincrono da 1000 macro descritta nella specifica v1.9. Il risultato è una misura sperimentale: non costituisce una garanzia di 1000 macro/s. La specifica originale misura throughput e latenza sul ciclo di 1000 macro. fileciteturn88file1L2-L4

## Protocollo di rete futuro

L'integrazione del vero handshake Noise IK/KK nel trasporto NosAi deve includere:

- framing con limiti di dimensione;
- timeout;
- autenticazione dell'identità prevista dal pattern;
- gestione esplicita dello stato handshake;
- chiusura sicura in caso di errore;
- protezione replay coerente con il protocollo;
- gestione lifecycle delle chiavi;
- test di interoperabilità;
- telemetria senza registrare segreti.

## Matrice della specifica importata

| Elemento | Specifica | Stato NosAi |
|---|---|---|
| X25519 | v1.8/v1.9 | implementato nel modulo effimero |
| ChaCha20-Poly1305 | v1.8/v1.9 | implementato |
| HKDF-SHA256 | estensione implementativa | implementato |
| Chiavi statiche | v1.8 | generatore disponibile; gestione segreti da completare |
| Noise KK completo | v1.8 | pianificato |
| Noise IK completo | v1.9 | implementato (`NoiseHandshakeState`), verificato solo localmente |
| Anti-replay sliding window | v1.9 | implementato (`ReplayWindow`, 64 messaggi) |
| Interoperabilità con altre implementazioni Noise | — | non verificata |
| Chiavi client effimere | v1.9 | implementato come sessione crittografica |
| Stress test 1000 macro | v1.9 | importato come benchmark |
| Forward Secrecy end-to-end Noise IK | v1.9 | fornita dalle chiavi effimere dell'handshake IK; da validare contro una controparte esterna |

## Regola di integrazione

Le specifiche allegate sono state importate nel progetto come requisiti e implementazioni dove tecnicamente verificabili. Non vengono trasformate in garanzie di produzione finché mancano test di interoperabilità e validazione end-to-end.
