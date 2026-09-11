# ADR-0032 — Nessun hook di memoria o DLL nel client, ora

**Status:** Accepted
**Date:** 2026-09-11
**Contratto:** C-105 (R-203)

## Fatti verificati

1. Il canale di osservazione esistente e' gia' duplice e MERGED: cattura di
   rete via WireProtocol/GameTrafficCaptureEngine (C-103/C-104), e lettura di
   memoria del processo client dall'esterno via DllImport
   (`Win32ProcessAdapter`), senza iniettare codice nel processo.
2. L'input verso il client passa gia' da probe reali verificati (R-004):
   mouse via API standard, tastiera via hook a basso livello osservato da
   fuori, mai iniettato dentro il processo del gioco.
3. Nessun comportamento concreto e documentato richiede oggi dati che il
   canale di rete o la lettura esterna di memoria non possano fornire.
4. Un hook di memoria o una DLL injection nel processo del client
   aumenterebbe rilevabilita', rischio di crash del client e superficie di
   rischio, senza un caso d'uso nominato che lo giustifichi.
5. Lo stile di decisione del progetto (vedi
   [ADR-0031](ADR-0031-no-explicit-fsm.md)) e' non costruire un'autorita' o
   una tecnica invasiva prima che un caso concreto la richieda.

## Decisione

Non si introduce alcun hook di memoria o DLL injection nel client ora. Il
canale di osservazione resta quello esistente: cattura di rete e lettura
esterna di memoria via DllImport. **C-105 si chiude come scelta datata, non
come debito.**

## Quando riaprire

Solo con un caso nominato: un dato necessario al comportamento dell'agente
che il canale di rete e la lettura esterna di memoria non possono fornire in
alcun modo, documentato con l'evidenza specifica del gap.

## Conseguenze

- C-105 esce dai contratti architetturali aperti.
- Nessuna modifica al codice esistente.
- Rischio accettato: se in futuro serve un dato non osservabile
  dall'esterno, il costo di introdurre l'hook si paga allora, con un caso
  concreto invece che in anticipo.
