@echo off
rem Compila y abre Pokemanager en el estado actual del arbol de trabajo.
rem Doble clic para ejecutarlo. Acepta los mismos parametros que build\run-current.ps1,
rem por ejemplo:  "Ejecutar Pokemanager.cmd" -Dump "C:\ruta\al\dump"
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\run-current.ps1" %*
