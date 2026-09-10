# Risoluzione delle firme contrattuali

La documentazione ora indica esplicitamente quali contratti non sono pronti per infilling.
Non sostituire una firma non definita con un metodo scelto arbitrariamente.

| CID | Primo percorso di ricerca | Consegna richiesta |
|---|---|---|
| C-103 | src/NosAi.Runtime/LiveIntegration/Capture/GameTrafficCaptureEngine.cs | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |
| C-104 | src/NosAi.Protocol/WireProtocol.cs | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |
| C-105 | da definire | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |
| C-106 | src/NosAi.Runtime/LiveIntegration/Capture/CaptureFile.cs | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |
| C-201 | da confermare fra i 16 sorgenti che citano dispatch | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |
| C-202 | src/NosAi.Core/WorldModel/ | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |
| C-203 | src/NosAi.Runtime/WorldModel/Fusion/WorldModelFusionLoop.cs | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |
| C-204 | da definire | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |
| C-301 | da confermare fra i 4 sorgenti che citano HTN | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |
| C-302 | da confermare fra i 5 sorgenti che citano GOAP | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |
| C-303 | da confermare fra i 30 sorgenti che citano Orchestrator | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |
| C-304 | da definire | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |
| C-305 | da confermare fra i 44 sorgenti che citano recovery o reconnect | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |
| C-401 | src/NosAi.Protocol/SessionCipher.cs | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |
| C-402 | da definire | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |
| C-403 | src/NosAi.Security/ | Simbolo canonico, firma completa, tipi/unità/errori, test e revisione del contratto |

Usare docs/FUNCTION_INDEX.md per localizzare candidati. C-105, C-204, C-304 e C-402
richiedono prima scelta architetturale/perimetro; gli altri richiedono confronto della
superficie implementata con scopo e consumatori. C-003/004/005 hanno firme proposte
ma non sono certificati come implementati. C-404 riguarda una procedura.

Criterio di chiusura: firma confrontata con sorgente e consumatori, contratto versionato,
test eseguiti e prova registrata. Il coordinatore risolve prima questi task se il
lavoro richiesto dipende da loro; nessuna promozione automatica da MERGED a VERIFIED.
