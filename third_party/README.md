# NosAiProject — Third-Party Source Vault

Questa cartella è il punto unico del repository per il materiale open-source analizzato o eventualmente riutilizzabile proveniente da altri progetti.

## Obiettivo

Ridurre il costo cognitivo e i token spesi da Cursor/Claude: invece di cercare ogni volta repository esterni, gli agenti devono consultare prima questa cartella e i relativi manifest.

## Regola fondamentale

I file copiati qui **non diventano automaticamente parte del runtime NosAi**. Sono materiale di riferimento o candidati al riuso. Prima di integrare codice nel prodotto è obbligatorio verificare:

1. licenza e compatibilità;
2. commit/versione esatta della sorgente;
3. provenienza/autore;
4. modifiche necessarie per l'architettura NosAi;
5. compatibilità con `ADR-0021-unprivileged-observability-boundary.md`;
6. test, sicurezza e performance;
7. eventuali obblighi di attribuzione/licenza.

## Struttura

- `manifests/` — manifest machine-readable/agent-readable per ogni sorgente.
- `sources/` — copie locali dei file o estratti autorizzati che sono realmente utili.
- `adapters/` — note su come adattare una sorgente all'architettura NosAi senza accoppiare il core alla sorgente esterna.
- `licenses/` — testi delle licenze quando necessari.
- `provenance/` — repository, URL, commit/tag, autore, data di acquisizione e motivazione.

## Policy per Cursor e Claude

Prima di cercare materiale esterno:

1. leggere `third_party/README.md`;
2. cercare in `third_party/manifests/`;
3. cercare in `third_party/sources/`;
4. verificare `third_party/provenance/` e `third_party/licenses/`;
5. usare fonti esterne solo se il materiale locale non è sufficiente o deve essere aggiornato.

## Stato iniziale

I candidati già studiati per NosAi includono OpenNos, NosCore, ChickenAPI, SaltyEmu, NosGm e un'architettura RAG .NET. I dettagli vanno registrati nei manifest prima di copiare codice.

## Divieti

Non importare materiale che richieda:

- accesso GM/moderatore/admin;
- database server privilegiato;
- console server;
- API amministrative;
- credenziali segrete;
- stato di gioco nascosto non disponibile a un normale client.

Il materiale third-party deve servire a costruire un client/test environment riproducibile e non privilegiato.
