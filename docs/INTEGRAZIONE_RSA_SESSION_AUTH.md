# NosAi — RSA SESSION_AUTH e framing PC↔Phone

## Stato
Versione progetto: **1.0 Beta**. Questa integrazione aggiunge le primitive software del contratto RSA e del framing binario senza dichiarare validato il collegamento fisico PC↔Phone.

## RSA SESSION_AUTH
- challenge: 32 byte casuali;
- firma smartphone: RSA-2048 + SHA-256 con PKCS#1 v1.5;
- verifica della chiave pubblica RSA-2048;
- digest SHA-256 della challenge per audit;
- una challenge deve essere trattata come monouso dal livello di sessione.

## Framing
Il modulo `nosai/network/wire_protocol.py` implementa header binario da 12 byte:
`MAGIC(4) | VERSION(1) | TYPE(1) | PAYLOAD_LEN(2) | SEQ(4)`.

`SequenceGuard` accetta esclusivamente la sequenza attesa e consente al livello superiore di applicare il requisito fail-closed in caso di gap, duplicato o regressione.

## Limiti attuali
Non sono ancora integrati in questa modifica il trasporto TCP 6100, AES-GCM-256, heartbeat temporizzato, timeout fail-closed da 2000 ms, APK `GuardAi.apk` e test su smartphone fisico. Questi componenti devono essere implementati e testati prima di dichiarare il canale PC↔Phone operativo.

## Storage
Le chiavi pubbliche e gli artefatti di configurazione devono essere conservati nel volume dedicato `NOSAI-SSD`; non devono essere inseriti segreti privati nel repository.
