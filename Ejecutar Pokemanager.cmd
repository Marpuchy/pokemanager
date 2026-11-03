@echo off
rem Abre Pokemanager. Solo compila la app (rapido, incremental) si el codigo ha cambiado; sin tests.
rem Para compilar y pasar los tests: build\run-current.ps1
cd /d "%~dp0"
dotnet build src\Pokemanager.App -c Debug -v q -nologo -clp:ErrorsOnly
if errorlevel 1 (
  echo.
  echo No se pudo compilar. Arriba estan los errores.
  pause
  exit /b 1
)
start "" "src\Pokemanager.App\bin\Debug\net10.0\Pokemanager.App.exe"
