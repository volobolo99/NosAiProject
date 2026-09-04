# Third-Party Sources Index

> Questo indice è il riferimento rapido per gli agenti di sviluppo. Nessuna sorgente è autorizzata al riuso solo perché compare qui: la licenza e il perimetro d'uso devono essere verificati prima dell'integrazione.

## 1. OpenNos

- Repository: `OpenNos/OpenNos`
- URL: https://github.com/OpenNos/OpenNos
- Stato: `REFERENCE`
- Licenza rilevata: GPL-2.0
- Utilizzo previsto: studio di protocollo, packet handling, architettura emulator/server e concetti di dominio.
- Vincolo: non portare codice GPL nel core NosAi senza una decisione esplicita di compatibilità/licensing.
- Boundary: solo laboratorio privato; nessun uso di API amministrative.

## 2. NosCore

- Repository: `NosCoreIO/NosCore`
- URL: https://github.com/NosCoreIO/NosCore
- Stato: `REFERENCE`
- Licenza rilevata: MIT
- Utilizzo previsto: architettura C#/.NET moderna, modularità, networking, persistence e servizi.
- Vincolo: riusare solo componenti tecnicamente compatibili dopo review; non assumere compatibilità con il client reale.

## 3. ChickenAPI

- Repository: `BlowaXD/ChickenAPI`
- URL: https://github.com/BlowaXD/ChickenAPI
- Stato: `REFERENCE`
- Utilizzo previsto: plugin system, event system, entity/domain separation.
- Vincolo: usare come riferimento architetturale; verificare sempre licenza e dipendenze prima di copiare codice.

## 4. SaltyEmu

- Repository: `BlowaXD/SaltyEmu`
- URL: https://github.com/BlowaXD/SaltyEmu
- Stato: `REFERENCE`
- Licenza rilevata: GPL-3.0
- Utilizzo previsto: modularità, event-driven architecture, testabilità, distribuzione.
- Vincolo: non importare codice GPL nel runtime NosAi senza review/licensing esplicita.

## 5. NosGm

- Repository: `KILL009/NosGm`
- URL: https://github.com/KILL009/NosGm
- Stato: `REFERENCE`
- Utilizzo previsto: resource exploration, packet catalog, parsing TimeSpace e studio della provenienza del codice.
- Vincolo: rispettare la provenienza dichiarata dal progetto e non introdurre nel runtime dati o comandi GM/admin.

## 6. LLM-RAG-Architecture

- Repository: `matt-bentley/LLM-RAG-Architecture`
- URL: https://github.com/matt-bentley/LLM-RAG-Architecture
- Stato: `REFERENCE`
- Licenza rilevata: MIT
- Utilizzo previsto: pattern RAG .NET, hybrid retrieval, reranking, vector store e testabilità.
- Vincolo: il RAG non può diventare una fonte di verità privilegiata per il gameplay; deve rispettare la provenance del WorldState.

## Decisione di integrazione

Il codice del prodotto deve rimanere indipendente dalle sorgenti third-party. Quando una tecnica è adottata, preferire un'implementazione NosAi-native e citare la sorgente nel relativo manifest/ADR. Copiare codice solo quando la licenza lo consente e quando il beneficio supera il costo di manutenzione/provenienza.
