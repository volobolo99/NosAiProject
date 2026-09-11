# ADR-0034 — Chiudere il signature_status UNRESOLVED di C-301 e C-302

**Status:** Accepted  
**Date:** 2026-09-11

## Context

La domanda su C-301 e C-302 è la stessa già risolta da [ADR-0028](ADR-0028-htn-goap-layer-attach-or-remove.md). ADR-0028 ha deciso di lasciare deliberatamente non collegato il codice in src/NosAi.Core/Planning/ e src/NosAi.Core/Safety/ finché AP-08 non avrà bisogno di pianificare a più passi.

## Decision

Il signature_status di C-301 e C-302 passerà da UNRESOLVED a RESOLVED, con i simboli esatti fissati così: C-301 "Pianificazione gerarchica HTN" corrisponde a `SequenceRoutine` e `SelectorRoutine` in `src/NosAi.Core/Planning/DeterministicRoutine.cs`, i due nodi del behaviour tree, insieme a `PlannerGoalStack` in `src/NosAi.Core/Planning/PlannerGoalStack.cs` per lo stack di obiettivi gerarchico; C-302 "Pianificazione GOAP" corrisponde a `DeterministicGoapPlanner` in `src/NosAi.Core/Planning/Goap/DeterministicGoapPlanner.cs`. I due contratti restano status MERGED e non diventano DROPPED come C-304, perché il loro codice esiste davvero, compila e ha test verdi, mentre C-304 descriveva una macchina a stati che non esisteva. Non si scrive nuovo codice in questo ADR.

## Quando riaprire

Un obiettivo reale che il ciclo di Gate 3 non sa raggiungere in un passo, e si riparte dall'opzione A di ADR-0028.

## Consequences

I due contratti restano status MERGED e il loro signature_status passa da UNRESOLVED a RESOLVED. C-303 'Orchestratore strategico' non è coperto da questo ADR perché è codice Python vivo e non appartiene al namespace C# src/NosAi.Core/Planning che ADR-0028 ha lasciato deliberatamente non collegato.
