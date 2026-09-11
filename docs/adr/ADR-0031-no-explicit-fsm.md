# ADR-0031 — Nessuna macchina a stati esplicita

**Status:** Accepted — deciso dall'orchestratore il 2026-09-11
**Date:** 2026-09-11

## Context

La roadmap pone la domanda:

> Una FSM esplicita sostituisce Planner e Orchestrator o li affianca.

e il contratto **C-304** «Macchina a stati finiti esplicita» e' bloccato con la
motivazione *«zero sorgenti con FSM; duplicherebbe l'autorita' di Planner e
Orchestrator»*.

### La domanda e' mal posta, e si vede da ADR-0028

[ADR-0028](ADR-0028-htn-goap-layer-attach-or-remove.md), accettato il 2026-09-07
con l'opzione C, ha misurato che lo strato HTN/GOAP **esiste, e' provato e non e'
attaccato a niente**: `ModuleReachability` dichiara `NosAi.Core.Planning`,
`NosAi.Core.Planning.Goap` e `NosAi.Core.Safety` come `Unreferenced`, e lo scrive
come scelta datata — *«UNREACHED ON PURPOSE, decided 2026-09-07 (ADR-0028, option
C)»*.

Quindi non c'e' un'autorita' di Planner e Orchestrator da duplicare: quei tipi non
sono nel percorso di esecuzione. Il percorso vivo e', testualmente dal registro,
**StrategyPlanner piu' il ciclo di Gate3**, e *«neither searches -- they
evaluate»*.

Aggiungere una FSM oggi significherebbe introdurre una **terza** autorita' sulla
stessa decisione, accanto a una che gira e a una che esiste ma non e' collegata.

E ADR-0028 ha gia' identificato la ragione per cui attaccare strutture in
anticipo e' dannoso, non solo inutile: la parte difficile e' la riduzione dei
fatti del World Model a predicati, e *«inventare quella riduzione senza un
obiettivo reale da pianificare deciderebbe la questione piu' difficile con meno
evidenza»*. Lo stesso vale identico per gli stati di una FSM.

## Decision

**Non si costruisce una macchina a stati esplicita.** **C-304 si chiude come
scelta, non come debito.**

La ragione non e' prudenza: e' che una FSM inventata prima di un comportamento
che la richieda fisserebbe una partizione degli stati scelta a tavolino, e quella
partizione e' esattamente la parte che decide se la macchina dice il vero.

## Quando riaprire

Con un caso nominato, e solo allora. Il criterio e' lo stesso di ADR-0028:
esiste un comportamento che il ciclo attuale non sa produrre, e il motivo per cui
non lo sa produrre e' l'assenza di stati espliciti — non l'assenza di una
pianificazione a piu' passi, che e' la domanda di ADR-0028 e ha una risposta
diversa.

Un caso plausibile: un **interblocco di sicurezza** che deve essere vero per
costruzione e non per valutazione, per esempio «mai attaccare mentre la finestra
di scambio e' aperta». Quello non e' una FSM generale: e' una guardia stretta, e
va costruita come guardia.

## Consequences

- **C-304** esce dai contratti aperti. Il Gate 3 passa da 55% a chiuso su questo
  punto senza scrivere una riga: e' il modo piu' rapido di avvicinarsi al 100%,
  perche' rimuove lavoro che non serviva.
- `ModuleReachability` non cambia: ADR-0028 ha gia' registrato la scelta e la sua
  data, e quella registrazione era la condizione dell'opzione C. Resta valida.
- Se domani si attacca HTN/GOAP secondo ADR-0028 opzione A, questo ADR **non** si
  riapre da solo: pianificare a piu' passi e avere stati espliciti sono due
  bisogni diversi, e confonderli e' ciò che ha prodotto la domanda mal posta.
- **Rischio accettato**: senza stati espliciti, un comportamento che dipende dalla
  storia recente va espresso nel ciclo di Gate3, dove e' meno leggibile. Se
  quell'illeggibilita' diventasse la causa di un difetto misurato, e' il caso
  nominato che riapre questo ADR.
