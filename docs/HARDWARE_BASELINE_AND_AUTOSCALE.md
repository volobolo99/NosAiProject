# NosAi — Profilo hardware di riferimento e configurazione automatica al primo avvio

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

## 1. Scopo

Questo documento definisce il profilo hardware di riferimento fornito dal proprietario del progetto per sviluppo, profilazione e regressione di NosAi, oltre alla politica di configurazione automatica al primo avvio per Play AI su PC e Guard AI su smartphone.

Il profilo PC è il riferimento autorevole di sviluppo. È una base per l'ottimizzazione e non un requisito rigido di distribuzione. Le impostazioni devono essere sempre determinate dalle capacità effettive del dispositivo.

## 2. Riferimento PC / Play AI

| Componente | Specifica di riferimento |
|---|---|
| Dispositivo | Acer Nitro V 16 AI |
| CPU | AMD Ryzen 7 260, fino a 5,1 GHz turbo, 16 MB di cache |
| GPU | NVIDIA GeForce RTX 5060, 8 GB GDDR7, 95 W TDP, 1785 MHz Boost Clock |
| RAM | 16 GB DDR5, 2 × 8 GB, 5600 MT/s, espandibile a 32 GB |
| Schermo | 16 pollici WUXGA IPS, 1920 × 1200 px, 180 Hz, 300 nit, 9 ms, opaco, NTSC 45% |
| Archiviazione | SSD PCIe NVMe M.2 80 mm da 1024 GB, PCIe Mainstream Performance (NVMe), secondo alloggiamento SSD libero |
| Rete | Intel Wi-Fi 6E, Bluetooth 5.3, LAN |
| Raffreddamento | Doppia ventola |
| Audio | Realtek ALC245-CG (HDA)_G4, DTS:X Ultra, Acer TrueHarmony |
| Alimentatore | 135 W |
| Sistema operativo | Windows 11 Home |
| Software OEM | Microsoft 365 Trial, NitroSense |
| Tastiera | Retroilluminata; Fn+F11 disattiva la retroilluminazione; Fn+F12 la attiva |

Questi valori costituiscono il **profilo PC di riferimento fornito dal proprietario** e devono essere utilizzati per la taratura di Play AI e per le regressioni di base finché il proprietario non li modifica esplicitamente.

## 3. Riferimento smartphone / Guard AI

| Componente | Riferimento |
|---|---|
| SoC | Snapdragon 865 5G |
| Ricarica | 65 W SuperDART |
| Schermo | Super AMOLED a schermo intero, 90 Hz |
| Fotocamera principale | Quadrupla fotocamera da 64 MP |
| Fotocamera anteriore | Doppia fotocamera da 32 MP integrata nello schermo |

Questi valori rimangono il profilo di riferimento del progetto. Non devono essere trattati automaticamente come specifiche esatte di ogni dispositivo utilizzato.

## 4. Modello delle capacità hardware

Il runtime deve normalizzare l'hardware in dati sulle capacità invece di fissare il funzionamento a un modello specifico. Il profilo normalizzato dovrebbe includere:

- modello CPU, architettura, struttura di core/thread e informazioni sulle prestazioni;
- RAM totale e disponibile;
- modello GPU, VRAM e capacità grafiche;
- risoluzione e frequenza di aggiornamento dello schermo;
- tipo, capacità e spazio libero dell'archiviazione;
- versione del sistema operativo e del runtime;
- stato e limiti termici quando disponibili;
- alimentazione/batteria quando disponibili;
- capacità di rete;
- capacità di accelerazione/inferenza quando esposte dalla piattaforma.

Le informazioni specifiche del produttore, come NitroSense, sono telemetria o integrazione opzionale e non una dipendenza obbligatoria del runtime.

## 5. Configurazione automatica al primo avvio

Al primo avvio di ogni dispositivo, il runtime esegue una scansione delle capacità hardware e genera un profilo di impostazioni ottimizzato.

### Play AI su PC

Il profilo deve poter rappresentare la configurazione RTX 5060 con 8 GB GDDR7 e la base 16 GB/5600 MT/s DDR5. Quando le API della piattaforma lo consentono, devono essere raccolti anche utilizzo GPU, uso VRAM, temperatura, potenza e stato delle frequenze.

### Guard AI su smartphone

Quando disponibili, devono essere raccolti:

- modello SoC/dispositivo;
- struttura dei core CPU e informazioni sulle prestazioni;
- RAM e pressione della memoria;
- risoluzione e frequenza di aggiornamento dello schermo;
- stato batteria/temperatura/alimentazione;
- capacità Android/runtime;
- capacità della fotocamera soltanto quando una funzione di Guard AI la richiede effettivamente.

## 6. Regole della configurazione automatica

La configurazione automatica deve ottimizzare nell'ordine **stabilità, reattività, efficienza delle risorse**. Non deve mai presupporre la presenza dell'hardware di riferimento.

Il profilo generato deve contenere almeno:

- livello di calcolo;
- livello di memoria;
- livello grafico per PC;
- livello dello schermo;
- budget di inferenza/aggiornamento;
- budget di campionamento della percezione;
- frequenza della telemetria;
- budget di concorrenza/lavoratori;
- politica energetica/termica;
- limiti di sicurezza di ripiego.

Per Play AI, la configurazione 16 GB RAM / RTX 5060 8 GB è un obiettivo di taratura, mentre l'eventuale espansione a 32 GB deve essere rilevata come capacità superiore e non presunta.

La configurazione automatica deve essere deterministica a parità di profilo hardware normalizzato e versione della politica.

## 7. Persistenza e identità del dispositivo

La configurazione automatica viene eseguita automaticamente **solo al primo avvio di un dispositivo**.

Dopo una calibrazione riuscita devono essere salvati:

- impronta hardware normalizzata;
- capacità rilevate;
- impostazioni generate;
- versione della politica di configurazione automatica;
- data e ora;
- versione dello schema/configurazione.

Negli avvii successivi il profilo salvato deve essere utilizzato se l'impronta hardware corrisponde ancora.

Se l'impronta cambia in modo significativo, il vecchio profilo deve essere invalidato e la configurazione automatica deve essere eseguita nuovamente.

L'impronta deve usare caratteristiche hardware e non dati personali identificativi.

## 8. Modifica manuale

La configurazione automatica è il comportamento predefinito. L'utente o operatore può modificare le impostazioni di prestazione non critiche per la sicurezza; i limiti di sicurezza e l'autorizzazione di Guard AI non possono essere superati tramite la configurazione delle prestazioni.

## 9. Standard di sviluppo

Il profilo PC della sezione 2 è lo standard corrente di sviluppo e regressione di Play AI. Il profilo smartphone della sezione 3 è lo standard corrente di riferimento di Guard AI.

**La versione del progetto rimane 1.0 Beta finché il proprietario non la modifica esplicitamente.**
