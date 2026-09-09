# Strategia dei modelli gratuiti

Il catalogo OpenRouter conta 431 modelli, di cui 21 a costo zero. Dieci modelli hanno superato entrambi i banchi del progetto e sono raccolti in `scripts/free_roster.json`. I modelli a pagamento del roster restano i predefiniti e non sono stati sostituiti: la catena gratuita è uno strumento aggiuntivo.

## Principi

La strategia seleziona modelli gratuiti già validati, mantenendo separato il percorso predefinito basato sui modelli a pagamento. L’impiego della catena gratuita non modifica il giudice di qualità né autorizza a considerare i modelli gratuiti superiori a quelli a pagamento.

## Le leve verificate

La prima leva è l’array `models` nel corpo della richiesta. Accetta al massimo tre elementi: con quattro modelli OpenRouter risponde 400, indicando che l’array deve contenere tre elementi o meno. I tre modelli di un gruppo formano un ordine di ripiego: viene interrogato un solo modello e gli altri subentrano soltanto se quello fallisce, non in parallelo o in batch. Il fallback si attiva sugli errori di esecuzione, compreso il 429. Il 2026-09-09, `google/gemma-4-31b-it:free` in errore 429 ha ceduto a `nex-agi/nex-n2.5-mini:free` in 698 millisecondi con una sola richiesta HTTP. Un identificativo inesistente, invece, non attiva il fallback e produce subito un errore 400.

La seconda leva è il parametro `provider.max_price`. Con `prompt` a 0 e `completion` a 0, qualunque modello a pagamento riceve una risposta 404 con il messaggio `No endpoints found that satisfy the max price`, mentre i gratuiti possono rispondere. In una lista mista, `max_price` pari a 0 salta i modelli a pagamento e fa rispondere quello gratuito.

La terza leva è il campo `model` della risposta: dichiara quale modello ha risposto effettivamente.

## La cascata a gruppi di tre

La cascata è implementata in `scripts/free_chain.py`. `free_models` legge il roster, `gruppi` lo divide in blocchi da tre, `corpo_richiesta` costruisce la richiesta con il lucchetto sul costo, `testo_utile` rifiuta le risposte vuote e `chiama_a_gruppi` prova i gruppi in ordine, fermandosi al primo utile.

Una risposta HTTP 200 con contenuto nullo non attiva il fallback di OpenRouter e deve essere intercettata dal chiamante. Il 2026-09-09 il provider Novita ha risposto così su `qwen3-coder-30b`, con token fatturati e nessun testo.

Nella prova finale del 2026-09-09, tre compiti diversi sono stati risolti con tre sole chiamate HTTP, comprese tra 824 e 1524 millisecondi, per una spesa totale di zero dollari. Il risultato descrive quella prova e non costituisce una garanzia per richieste future.

## Il lucchetto sul costo

Il lucchetto sul costo è il solo parametro `provider.max_price`: riguarda il prezzo, non la frequenza delle richieste. Con `prompt` e `completion` impostati a 0, esclude i modelli a pagamento e lascia passare quelli gratuiti. Non controlla quindi il numero di richieste al minuto o al giorno.

## Corsie di lavoro

La corsia interattiva usa soltanto i modelli con media misurata inferiore a 200 secondi. I modelli più lenti, come `nemotron-3.5-lightning` e `nemotron-3-ultra-550b`, restano destinati ai lavori in differita.

Il giudice della qualità non cambia: resta `validate_implementation` di `scripts/code_agent.py`, insieme ai test scritti prima della delega.

## Limiti e rischi noti

I limiti dei modelli gratuiti sono di volume e non di tempo: 20 richieste al minuto e 1000 al giorno per un account con almeno 10 crediti acquistati. La documentazione non dichiara alcuna scadenza. Questi valori impongono un tetto concreto all’uso e non vanno interpretati come disponibilità illimitata.

Quattro dei dieci modelli validati hanno un gemello a pagamento; due dei migliori sono stati pubblicati il 2026-09-08. La loro permanenza non è garantita.

Un modello gratuito incaricato di correggere un controllo ha disattivato il controllo stesso per farsi accettare. Dopo ogni delega si deve quindi leggere il diff.
