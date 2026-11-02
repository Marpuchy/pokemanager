#Requires -Version 5.1
<#
.SYNOPSIS
    Compila y abre Pokemanager tal como esta el arbol de trabajo ahora mismo.
.DESCRIPTION
    Pensado para probar cambios sin commit. Hace cuatro cosas, en este orden:
      1. Comprueba que hay SDK de .NET 10.
      2. Ensena que cambios sin commit se van a ejecutar.
      3. Compila. Si falla, para y deja los errores a la vista.
      4. Pasa los tests y abre la aplicacion.
.PARAMETER Dump
    Carpeta de un dump importado (la que tiene pokemanager-game.json). Activa los tests
    que necesitan datos reales. Se recuerda para la proxima vez.
.PARAMETER Roms
    Carpeta con ROMs descifradas (.3ds/.cci/.cxi), o rutas de ROM separadas por ';'.
    Una carpeta se expande a la lista que el test espera. Activa los tests de importacion. Se recuerda.
.PARAMETER Release
    Compila en Release en vez de Debug (como el CI).
.PARAMETER SkipTests
    Compila y abre sin pasar los tests.
.PARAMETER NoLaunch
    Compila y pasa los tests, pero no abre la aplicacion.
.EXAMPLE
    .\build\run-current.ps1 -Dump "C:\Users\Marc\Documents\Pokemanager\Games\UltraSun"
#>
[CmdletBinding()]
param(
    [string]$Dump,
    [string]$Roms,
    [switch]$Release,
    [switch]$SkipTests,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$root         = Split-Path -Parent $PSScriptRoot
$config       = if ($Release) { 'Release' } else { 'Debug' }
$settingsFile = Join-Path $PSScriptRoot 'run-current.local.json'
$appProject   = Join-Path $root 'src\Pokemanager.App\Pokemanager.App.csproj'
$appExe       = Join-Path $root "src\Pokemanager.App\bin\$config\net10.0\Pokemanager.App.exe"
$solution     = Join-Path $root 'Pokemanager.slnx'

function Step([string]$t) { Write-Host ''; Write-Host "-- $t" -ForegroundColor Cyan }
function Ok  ([string]$t) { Write-Host "   $t" -ForegroundColor Green }
function Warn([string]$t) { Write-Host "   $t" -ForegroundColor Yellow }
function Bad ([string]$t) { Write-Host "   $t" -ForegroundColor Red }

function Stop-Here([string]$why) {
    Write-Host ''
    Bad $why
    Write-Host ''
    if ($Host.Name -eq 'ConsoleHost' -and -not $env:CI) {
        Read-Host 'Pulsa Enter para cerrar'
    }
    exit 1
}

Write-Host ''
Write-Host "  Pokemanager - estado actual del arbol de trabajo  ($config)" -ForegroundColor White
Write-Host "  $root" -ForegroundColor DarkGray

# ---------------------------------------------------------------- 1. SDK

Step '.NET SDK'

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Stop-Here 'No se encuentra "dotnet". Instala el SDK de .NET 10: https://dotnet.microsoft.com/download/dotnet/10.0'
}

$sdks = & dotnet --list-sdks 2>&1
$net10 = $sdks | Where-Object { $_ -match '^10\.' }
if (-not $net10) {
    Write-Host ''
    Write-Host '   SDKs instalados:' -ForegroundColor DarkGray
    $sdks | ForEach-Object { Write-Host "     $_" -ForegroundColor DarkGray }
    Stop-Here 'Hace falta el SDK de .NET 10 (los proyectos van a net10.0). Descargalo en https://dotnet.microsoft.com/download/dotnet/10.0'
}
Ok ($net10 | Select-Object -First 1)

# ------------------------------------------------- 2. cambios sin commit

Step 'Cambios sin commit que se van a ejecutar'

$git = Get-Command git -ErrorAction SilentlyContinue
if ($git) {
    Push-Location $root
    try {
        $branch  = (& git rev-parse --abbrev-ref HEAD 2>$null)
        $changes = @(& git status --short 2>$null | Where-Object { $_ })
        Write-Host "   rama: $branch" -ForegroundColor DarkGray
        if ($changes.Count -eq 0) {
            Warn 'Ninguno: esto es exactamente lo que hay en el commit actual.'
        } else {
            $changes | ForEach-Object { Write-Host "     $_" -ForegroundColor Yellow }
            Ok "$($changes.Count) archivo(s) modificados respecto al ultimo commit."
        }
    } finally { Pop-Location }
} else {
    Warn 'git no esta en el PATH; me salto esta comprobacion.'
}

# --------------------------------------------------------------- 3. build

Step "Compilando ($config)"

& dotnet build $solution -c $config --nologo
if ($LASTEXITCODE -ne 0) {
    Stop-Here 'La compilacion ha fallado. Los errores estan justo arriba, con archivo y linea.'
}
Ok 'Compila.'

# --------------------------------------------------------------- 4. tests

$testsFailed = $false

if ($SkipTests) {
    Step 'Tests'
    Warn 'Saltados (-SkipTests).'
} else {
    # Rutas de datos reales: parametro > recordado antes.
    $saved = @{}
    if (Test-Path $settingsFile) {
        try {
            (Get-Content $settingsFile -Raw | ConvertFrom-Json).PSObject.Properties |
                ForEach-Object { $saved[$_.Name] = $_.Value }
        } catch { Warn "No pude leer $settingsFile; lo ignoro." }
    }
    if (-not $Dump -and $saved.ContainsKey('Dump')) { $Dump = $saved['Dump'] }
    if (-not $Roms -and $saved.ContainsKey('Roms')) { $Roms = $saved['Roms'] }

    Step 'Datos reales para los tests'

    if ($Dump) {
        if (Test-Path $Dump) { $env:POKEMANAGER_DUMP = $Dump; Ok "dump: $Dump" }
        else { Warn "La carpeta del dump no existe, la ignoro: $Dump"; $Dump = $null }
    } else {
        Warn 'Sin dump (-Dump). Los tests que verifican contra el juego real se saltaran.'
    }

    if ($Roms) {
        if (-not (Test-Path $Roms)) {
            Warn "La ruta de ROMs no existe, la ignoro: $Roms"
            $Roms = $null
        }
        else {
            # El test quiere rutas de ROM separadas por ';'. Si se da una carpeta, se expande aqui.
            if (Test-Path $Roms -PathType Container) {
                $found = @(Get-ChildItem -LiteralPath $Roms -File -Include *.3ds, *.cci, *.cxi -ErrorAction SilentlyContinue |
                           Sort-Object Name | Select-Object -ExpandProperty FullName)
                if ($found.Count -eq 0) {
                    # -Include solo filtra con -Recurse o un comodin en la ruta: segundo intento.
                    $found = @(Get-ChildItem -LiteralPath $Roms -File -ErrorAction SilentlyContinue |
                               Where-Object { $_.Extension -in '.3ds', '.cci', '.cxi' } |
                               Sort-Object Name | Select-Object -ExpandProperty FullName)
                }
                if ($found.Count -eq 0) {
                    Warn "No hay ROMs (.3ds/.cci/.cxi) en $Roms, la ignoro."
                    $Roms = $null
                } else {
                    $env:POKEMANAGER_TEST_ROMS = ($found -join ';')
                    Ok "roms: $($found.Count) encontradas en $Roms"
                    $found | ForEach-Object { Write-Host "     $(Split-Path -Leaf $_)" -ForegroundColor DarkGray }
                }
            }
            else {
                $env:POKEMANAGER_TEST_ROMS = $Roms
                Ok "roms: $Roms"
            }
        }
    } else {
        Warn 'Sin ROMs (-Roms). Los tests de importacion se saltaran.'
    }

    if ($Dump -or $Roms) {
        try {
            $keep = @{}
            if ($Dump) { $keep['Dump'] = $Dump }
            if ($Roms) { $keep['Roms'] = $Roms }
            $keep | ConvertTo-Json | Set-Content $settingsFile -Encoding UTF8
            Write-Host "   (recordado en $settingsFile)" -ForegroundColor DarkGray
        } catch { Warn 'No pude guardar las rutas para la proxima vez.' }
    }

    Step 'Tests'
    & dotnet test --solution $solution -c $config --no-build
    if ($LASTEXITCODE -ne 0) {
        $testsFailed = $true
        Bad 'Hay tests que fallan. El detalle esta arriba.'
    } else {
        Ok 'Todos los tests pasan.'
    }
}

# -------------------------------------------------------------- 5. lanzar

Step 'Aplicacion'

if ($NoLaunch) {
    Warn 'No la abro (-NoLaunch).'
} elseif (-not (Test-Path $appExe)) {
    Bad "Compilo pero no encuentro el ejecutable: $appExe"
    Warn 'Lanzo con "dotnet run" en su lugar.'
    & dotnet run --project $appProject -c $config --no-build
} else {
    Ok "Abriendo $appExe"
    Start-Process -FilePath $appExe -WorkingDirectory (Split-Path -Parent $appExe)
}

# ------------------------------------------------------------- 6. resumen

Write-Host ''
Write-Host '  Resumen' -ForegroundColor White
Write-Host "    compilacion .. OK ($config)" -ForegroundColor Green
if ($SkipTests) {
    Write-Host '    tests ........ saltados' -ForegroundColor Yellow
} elseif ($testsFailed) {
    Write-Host '    tests ........ FALLAN  <-- mirar antes de hacer commit' -ForegroundColor Red
} else {
    Write-Host '    tests ........ OK' -ForegroundColor Green
}
if (-not $SkipTests -and -not $Dump) {
    Write-Host '    datos reales . sin verificar (pasa -Dump para cerrar esto)' -ForegroundColor Yellow
}
Write-Host ''

if ($Host.Name -eq 'ConsoleHost' -and -not $env:CI) {
    Read-Host 'Pulsa Enter para cerrar esta ventana'
}
