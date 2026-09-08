@echo off
setlocal
rem Ispettore del traffico: una pagina sola con tutti i worker, aggiornata dal
rem server via SSE. Non serve amministratore: legge Ollama in locale, il tunnel
rem Colab pubblicato su ntfy e il registro delle deleghe in data/traffic.
cd /d "%~dp0"

set "PORT=%NOSAI_INSPECTOR_PORT%"
if "%PORT%"=="" set "PORT=8787"

echo Avvio dell'ispettore su http://localhost:%PORT%
echo Chat dei worker:        http://localhost:%PORT%/chat
start "" "http://localhost:%PORT%"
python "tools\traffic_inspector\server.py"
endlocal
