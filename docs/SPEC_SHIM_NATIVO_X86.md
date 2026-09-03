# Shim nativo x86 — specifica non approvata

> **NON IMPLEMENTARE.** Nessun agente deve scrivere codice a partire da questo
> documento. Serve un ADR dedicato e accettato prima che una sola riga esista.
> Vedi §6 per che cosa quell'ADR dovrebbe dimostrare.

**Stato:** opzione futura, registrata per non essere riscoperta da zero.
**Data:** 2 settembre 2026

## 1. Perché esiste questo documento

Durante l'analisi di un bot NosTale di terze parti è emersa una possibilità che il
progetto oggi non usa: **chiamare direttamente le funzioni del client** — movimento,
attacco, raccolta — invece di guidarlo tramite input.

La possibilità è reale e il documento la registra. Non è adottata.

## 2. Il vincolo tecnico, e perché non riguarda la lettura

Le funzioni del client usano la convenzione a registri Borland/Delphi: **`this` in
`EAX`, primo argomento in `EDX`, secondo in `ECX`**, più flag sullo stack.

Nessuna `CallingConvention` del CLR la esprime. `Marshal.GetDelegateForFunctionPointer`
non ha modo di mettere un valore in `EAX` prima della chiamata. Servirebbe quindi
codice nativo x86, dentro il processo del client, con la chiamata scritta in assembly.

**Questo vincolo riguarda solo la chiamata.** La lettura non ne è toccata: un processo
.NET 8 a 64 bit legge la memoria di un client a 32 bit con `ReadProcessMemory` senza
alcun problema, ed è quello che `ProcessMemoryReader` già fa. Chi legge questo documento
cercando una ragione per iniettare qualcosa allo scopo di *osservare* non la troverà:
non c'è.

## 3. Che cosa comporterebbe

| Aspetto | Conseguenza |
|---|---|
| Architettura | DLL x86 iniettata nel client + IPC verso il runtime .NET x64 out-of-process. Non si inietta .NET 8 in un processo a 32 bit. |
| Toolchain | MSVC con toolset x86 e Windows SDK, oltre al .NET SDK. Oggi il repository si compila con `dotnet build` e basta. |
| Superficie di rischio | Aprire un handle al client è già osservabile; iniettare una DLL ed eseguire codice suo è un'altra categoria. |
| Autorità | Una funzione del client chiamata direttamente **bypassa** il percorso che ADR-0019/0020 hanno definito per l'attuazione. Non è un'ottimizzazione del canale: è un secondo canale. |

L'ultima riga è la ragione principale per cui questa specifica resta chiusa. Il progetto
ha un solo percorso all'atto, e ne ha uno solo di proposito.

## 4. Disegno, se mai venisse adottato

Registrato al livello di dettaglio sufficiente a non ricominciare.

**Progetti:** `NosAi.Native.Shim` (DLL C++17, x86, `/MT`, niente `/clr`, niente GUI),
`NosAi.Native.Injector` (x86, `CreateRemoteThread` + `LoadLibraryW`),
`NosAi.Shim.Client` (.NET 8, x64).

**Trasporto, due canali:**

- *Snapshot ad alta frequenza* — memoria condivisa (`CreateFileMapping`), struttura a
  dimensione fissa, `#pragma pack(1)`, nessun puntatore al suo interno, protetta da
  **seqlock**: il writer porta `sequence` a dispari prima di scrivere e a pari dopo; il
  reader legge `sequence`, legge il corpo, rilegge `sequence` e riprova se dispari o
  cambiata. Nessun lock fra i due processi, nessuna lettura strappata.
- *Controllo* — named pipe con framing a lunghezza prefissa: handshake, versione del
  layout, fingerprint del client, heartbeat.

**Sicurezza della lettura in-process:** dentro il client una catena rotta non produce
`UNKNOWN`, fa crashare il gioco. Ogni dereferenza va protetta con validazione
dell'indirizzo (cache di `VirtualQuery` per regione) e `__try/__except`. È lavoro che
out-of-process non serve fare, ed è un costo da mettere nel conto.

**Loop di polling:** thread dedicato, periodo configurabile, nessuna allocazione,
nessun I/O, `DisableThreadLibraryCalls` in `DllMain`, e in `DllMain` nient'altro che la
creazione del thread.

**Contratto verso .NET:** un solo `bool TryGetSnapshot(out WorldSnapshot)` che ritorna
`false` se lo snapshot è invalido, se il seqlock non converge, o se il timestamp è più
vecchio della soglia di staleness. Nessuna variante che restituisca dati stantii: il
fail-closed dev'essere una proprietà del tipo, non della disciplina di chi chiama.

**Firme delle funzioni del client** (dalla fonte terza, build ignota, da ritrovare per
pattern e non per RVA): movimento, attacco, attacco in corsa, raccolta, riposo, e le
varianti per pet e partner. Il parametro di posizione è impacchettato `Y * 65536 + X`.

## 5. Che cosa NON entrerebbe comunque

L'evasione dei sistemi di rilevamento resta esclusa da ADR-0014 e questo documento non
la riapre. Rendere il runtime difficile da notare è un'attività diversa dal leggere dati
o dal guidare un client, ed è l'unica tecnica che quel record ha lasciato fuori per nome.

## 6. Che cosa dovrebbe dimostrare l'ADR che lo autorizzasse

ADR-0013 è superato, quindi l'iniezione non è più vietata in quanto tale. Ma la parte
del suo ragionamento che ADR-0014 ha esplicitamente conservato è ancora in vigore, e va
soddisfatta:

1. **Correttezza.** Che cosa distingue una chiamata riuscita da una chiamata a un
   indirizzo sbagliato, dentro il processo, prima che il client crashi.
2. **Safety.** Come un secondo canale di attuazione resta subordinato al Safety Gate
   senza duplicare l'autorità che ADR-0020 ha concentrato in un punto solo.
3. **Necessità.** Quale problema reale il canale di input non risolve. Se la risposta è
   "è più veloce", non basta: la velocità non era il vincolo.

Finché quelle tre non hanno una risposta scritta e accettata, questo file resta un
documento e nient'altro.
