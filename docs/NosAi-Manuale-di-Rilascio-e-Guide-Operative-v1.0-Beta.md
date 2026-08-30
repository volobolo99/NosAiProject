# NosAi — Manuale di Rilascio e Guide Operative (v1.0 Beta)

**Creatore:** Volodymyr Ryzhuk  
**Versione:** 1.0 Beta (Bloccata)  
**Piattaforma:** C# 12 / .NET 8 su Windows (Riferimento: Acer Nitro V 16 AI + Crucial X6 SSD)

---

## 1. Panoramica dell'Ecosistema

Il runtime di **NosAi** è stato implementato nativamente in **C# / .NET 8** per garantire prestazioni di classe mondiale, azzeramento della pressione sul Garbage Collector tramite buffer riutilizzabili (`ArrayPool`, `Memory`, `Span`), comunicazione sicura Zero-Trust e un ciclo decisionale deterministico a ciclo chiuso.

---

## 2. Struttura dei Sottosistemi Principali nel Repository

1. **`NosAi.Runtime.Gate1` — Connettività Base & Dashboard:**
   - Adapter client di gioco NosTale, telemetria hardware e API operatore locale su `http://127.0.0.1:8766/`
     (l'interfaccia operatore Python resta su `http://127.0.0.1:8765/` e legge questa API).
2. **`NosAi.Runtime.Gate2` — World Model, Bounded Bus & WAL SQLite:**
   - Modello di stato immutabile, EventBus a capienza limitata, `VRAMContextSlimmer`, persistenza batch asincrona e compressione Delta-Encoding (>70% risparmio banda).
3. **`NosAi.Runtime.Gate3` — Pipeline di Sicurezza & Ciclo Chiuso:**
   - Simulazione, Tactical Ranking (MAUT), Guard AI, Trust Boundary, Safety Gate con token HMAC monouso, Executor, Verifier e RecoveryController adattivo.
4. **`NosAi.Runtime.Gate4` — Progression Engine V2 & Knowledge Base:**
   - Risolutore DAG per missioni e SP (SP1..SP8+), aggiornamenti bayesiani Beta-Binomiali, selezione UCB1 ed evidenza strategica con ciclo di vita *Mastery*.
5. **`NosAi.Runtime.Gate5` & `Gate6` — Master Host & Eye AI View:**
   - Orchestratore globale di sistema, router dei provider locali (llama.cpp) con policy *Local-First*, discovery del volume `NOSAI-SSD` e Centro di Controllo Dashboard a 3 strati (*Osservato*, *Stimato*, *Decisionale*).
6. **`NosAi.Perception` — Visione ad Alte Prestazioni:**
   - Acquisizione DXGI Triple Buffer, segmentazione ROI, OCR con cache a glifi Hashing e tracciamento temporale con Filtro di Kalman 2D.
7. **`NosAi.Security` — Trasporto LAN & Onboarding:**
   - Protocollo Noise (`Noise_IK_25519`), chiavi effimere X25519, derivazione HKDF-SHA256, cifratura ChaCha20-Poly1305 / AES-GCM-256, anti-replay sliding window e provisioning ADB.
8. **`NosAi.Adapter` — Shared Memory & Controlled Interop:**
   - Blocco binario `PlayerStatusBlock` (64 byte, `Pack = 1`), `MemoryMappedFile` IPC, validatore confini client e dispatch CapBAC protetto da SafetyToken.
9. **`NosAi.Mobile` — Guard AI Smartphone Node:**
   - Autenticazione RSA-2048, heartbeat a 500 ms con timeout fail-closed a 2000 ms, ricostruzione delta-state e comandi operatore prioritari (Emergency STOP).
10. **`NosAi.Tactics` — Combattimento e Coordinamento Multi-Entità:**
    - Aggro Table Tracker, combo coordinate (Player + Tank Partner + Pet), kiting spaziale e protezione vitale autonoma.
11. **`NosAi.Navigation` — Pathfinding A\* & Routing Portali:**
    - Griglia di collisione 2D, euristica ottile, hazard cost overlay per le minacce, smoothing dei cammini (Raycasting LoS) e grafo multi-mappa (Dijkstra).
12. **`NosAi.Economy` — Inventario e Simulatore Upgrade:**
    - Gestione tab zaino, simulazione probabilistica upgrade con blocco fail-closed per rischio distruzione, arbitraggio Bazar vs NPC e solutore ricette crafting.
13. **`NosAi.Raids` — Raid Celestiali, Dodekatheon e Humanizer:**
    - Gestione fasi boss, stabilità Stagger Gauge, schivata AoE, risoluzione TimeSpace e generatore di movimenti mouse basato su curve di Bézier cubiche.
14. **`NosAi.Storage` & `Hardware` — Infrastruttura e Autoscale:**
    - Gestione volume `NOSAI-SSD`, policy SQLite WAL centralizzata, migrazioni schema, backup con sigillo SHA-256 e autoscale delle prestazioni con trigger termico a 80 °C.

---

## 3. Guida all'Avvio e all'Esecuzione dei Test

Per compilare, avviare ed eseguire l'intera suite di test di certificazione di tutti i moduli in .NET 8:

```bash
# Esecuzione della suite completa di test di rilascio con esito certificato
dotnet run --project NosAi.Host.csproj -- --test
```

Per avviare il Master Runtime Host in background e aprire il Centro di Controllo locale:
```bash
dotnet run --project NosAi.Host.csproj
```
*(Successivamente, aprire il browser all'indirizzo `http://127.0.0.1:8767/` per accedere alla dashboard interattiva Eye AI View del Master Host; l'host stampa all'avvio la porta effettivamente aperta).* 

---
*Documento ufficiale redatto in conformità alle specifiche di progetto di NosAi 1.0 Beta.*
