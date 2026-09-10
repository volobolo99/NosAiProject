# Indice delle funzioni NosAiProject
Snapshot: 3e4a8c42d75a73ccf5c6b739705e6293b059827e.
Manifest: [FUNCTION_INDEX.json](FUNCTION_INDEX.json).
Audit: [DOCUMENTATION_ALIGNMENT_AUDIT.md](DOCUMENTATION_ALIGNMENT_AUDIT.md).

## Ricerca minima
1. Cercare il file nella sezione coverage del manifest.
2. Aprire uno shard della sua radice (src, nosai, tests, scripts, tools, third_party).
3. Cercare name/scope; signature mostra la dichiarazione sintattica.
4. Aprire path e line alla source_revision indicata. Le righe non valgono per altri commit.

12.599 voci, 1.098 file, 29 shard. Inclusi test e codice esterno separati da category.
Quattro file richiedono revisione parser: elencati in coverage e nell'audit.
Non usare questo indice come prova di funzionamento o come call graph.

## Rigenerazione
Installare in ambiente di tooling dedicato:
tree-sitter==0.25.2, tree-sitter-c-sharp==0.23.5,
tree-sitter-python==0.25.0, tree-sitter-javascript==0.25.0,
tree-sitter-cpp==0.23.4, tree-sitter-bash==0.25.1,
tree-sitter-powershell==0.26.4.
La versione 0.26.0 di tree-sitter ha prodotto un crash durante questa scansione;
0.25.2 ha completato la scansione.

python scripts/build_function_index.py CHECKOUT COMMIT OUTPUT_DIRECTORY

Il generatore legge i file Git tracciati e produce un JSON monolitico con copertura
e funzioni. Per consultazione su GitHub, suddividere functions per radice e blocchi
da 500 voci, mantenendo source_revision e coverage nel manifest.
Il checkout deve essere pulito e corrispondere al COMMIT fornito; la revisione
non viene risolta o verificata automaticamente dal generatore.
Estensioni: py, cs, js, html (script inline), cpp, h, sh, ps1.
Aggiungere parser e test di copertura quando entrano altri linguaggi.
