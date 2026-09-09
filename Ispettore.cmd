@echo off
setlocal
rem Console degli agenti: una pagina sola con i worker e le sessioni Claude,
rem cosa stanno facendo, i token spesi e una chat per comandarli. Aggiornata dal
rem server via SSE. Non serve amministratore: legge Ollama in locale, i transcript
rem delle sessioni e il registro delle deleghe in data/traffic.
cd /d "%~dp0"

set "PORT=%NOSAI_INSPECTOR_PORT%"
if "%PORT%"=="" set "PORT=8787"

echo Console degli agenti:  http://localhost:%PORT%
echo Flusso e stato worker: http://localhost:%PORT%/flusso
start "" "http://localhost:%PORT%"
python "tools\traffic_inspector\server.py"
endlocal
