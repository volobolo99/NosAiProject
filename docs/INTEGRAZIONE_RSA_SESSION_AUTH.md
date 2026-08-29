# NosAi — RSA SESSION_AUTH e framing PC↔Phone

## Stato
Versione progetto: **1.0 Beta**. Le primitive RSA e il framing binario sono ora presenti nel repository; il collegamento fisico PC↔Phone resta non validato finché non passano i test reali PC e smartphone.

## RSA SESSION_AUTH
- challenge: 32 byte casuali;
- firma smartphone: RSA-2048 + SHA-256 con PKCS#1 v1.5;
- verifica della chiave pubblica RSA-2048;
- digest SHA-256 della challenge per audit/provenienza;
- challenge consumata come nonce monouso, anche in caso di verifica fallita;
- nessuna chiave privata viene caricata dal runtime PC.

Implementazione: `nosai/network/crypto_auth.py` (`NosAiCryptoAuthManager`).

## Framing
Il modulo `nosai/network/wire_protocol.py` implementa header binario da 12 byte:
`MAGIC(4) | VERSION(1) | TYPE(1) | PAYLOAD_LEN(2) | SEQ(4)`.

`SequenceGuard` accetta esclusivamente la sequenza attesa e consente al livello superiore di applicare il requisito fail-closed in caso di gap, duplicato o regressione.

## Onboarding
`nosai/phone/onboarding_engine.py` implementa il provisioning ADB isolato dal volume dedicato, la verifica di un device autorizzato, l'installazione condizionata dell'APK locale `GuardAi.apk`, il forwarding TCP sulla porta 6100 e la costruzione del primo `SESSION_HELLO` con sequenza 1.

Il provisioning non scarica componenti dall'esterno.

## Limiti attuali
Non sono ancora dichiarati operativi in produzione: trasporto TCP completo con macchina a stati, AES-GCM-256 del payload, heartbeat temporizzato, timeout fail-closed da 2000 ms, APK `GuardAi.apk` reale e test di interoperabilità su smartphone fisico.

## Storage
Le chiavi pubbliche e gli artefatti di configurazione devono essere conservati nel volume dedicato `NOSAI-SSD`; non devono essere inseriti segreti privati nel repository.
