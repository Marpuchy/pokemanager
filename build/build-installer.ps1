<#
.SYNOPSIS
    Builds the Windows installer of Pokemanager (Setup.exe) with everything bundled.

.DESCRIPTION
    1. Publishes the application self-contained for win-x64 (no .NET install needed on the target PC).
    2. Compiles the UPR ZX launcher (PokemanagerUpr.java) so the bundled Java runtime needs no compiler.
    3. Builds a trimmed Java runtime with jlink (only the modules UPR ZX and the launcher use) into tools/java.
    4. Bundles the battle simulator: node.exe into tools/node and the pruned Pokemon Showdown package into battle/,
       then plays a random battle with them.
    5. Packs everything with Velopack: a per-user Setup.exe with Start menu and desktop shortcuts and an uninstaller.

    Requirements on the build machine: .NET 10 SDK, a JDK 17 or later (JAVA_HOME, or Java on the PATH) and Node.js 20+
    with npm (its node.exe is the one bundled).

.EXAMPLE
    ./build/build-installer.ps1 -Version 1.0.0
#>
param(
    [string] $Version = "1.0.0",
    [string] $Runtime = "win-x64",
    [string] $JdkHome = $env:JAVA_HOME
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$publish = Join-Path $artifacts "publish"
$installer = Join-Path $artifacts "installer"

function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

function Find-JdkTool($name) {
    if ($JdkHome -and (Test-Path (Join-Path $JdkHome "bin/$name.exe"))) { return (Join-Path $JdkHome "bin/$name.exe") }
    $javac = Get-Command javac -ErrorAction SilentlyContinue
    if ($javac) {
        # The PATH may point to a shim: ask Java where its home is.
        $home = (& java -XshowSettings:properties -version 2>&1 | Select-String "java.home" | ForEach-Object { ($_ -split "=", 2)[1].Trim() })
        if ($home -and (Test-Path (Join-Path $home "bin/$name.exe"))) { return (Join-Path $home "bin/$name.exe") }
    }
    throw "No JDK found (needed: $name). Install a JDK 17+ and set JAVA_HOME."
}

if (Test-Path $artifacts) { Remove-Item -Recurse -Force $artifacts }
New-Item -ItemType Directory -Force $publish, $installer | Out-Null

Step "Publishing Pokemanager $Version ($Runtime, self-contained)"
dotnet publish (Join-Path $root "src/Pokemanager.App/Pokemanager.App.csproj") `
    -c Release -r $Runtime --self-contained true `
    -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$jar = Join-Path $publish "tools/upr/PokeRandoZX.jar"
$uprDir = Join-Path $publish "upr"

Step "Compiling the UPR ZX launcher"
& (Find-JdkTool "javac") --release 17 -encoding UTF-8 -cp $jar -d $uprDir (Join-Path $root "src/Pokemanager.Randomizer/upr/PokemanagerUpr.java")
if ($LASTEXITCODE -ne 0) { throw "javac failed" }

Step "Building the bundled Java runtime (jlink)"
$modules = (& (Find-JdkTool "jdeps") --multi-release 17 --ignore-missing-deps --print-module-deps $jar $uprDir).Trim()
if ($LASTEXITCODE -ne 0 -or -not $modules) { throw "jdeps failed" }
# UPR reads its resources and prints with charsets and zip file systems that jdeps cannot see through reflection.
$modules = ($modules + ",jdk.charsets,jdk.zipfs,jdk.localedata") -replace "\s", ""
Write-Host "    modules: $modules"
& (Find-JdkTool "jlink") --add-modules $modules --strip-debug --no-man-pages --no-header-files --compress zip-6 `
    --output (Join-Path $publish "tools/java")
if ($LASTEXITCODE -ne 0) { throw "jlink failed" }

Step "Checking the bundled runtime runs the launcher"
$javaExe = Join-Path $publish "tools/java/bin/java.exe"
$describe = & $javaExe -cp "$jar;$uprDir" PokemanagerUpr describe-settings - 2>&1
if ($LASTEXITCODE -ne 0 -or -not ($describe -match '"options"')) { throw "The bundled Java could not run the UPR launcher:`n$describe" }

Step "Bundling the battle simulator (Node.js + Pokemon Showdown)"
# The app looks for tools/node/node.exe and battle/pokemanager-battle.js next to itself (ShowdownTools.Locate).
$battleSource = Join-Path $root "battle"
Push-Location $battleSource
# npm prints deprecation warnings on stderr, which Windows PowerShell 5.1 turns into errors under "Stop": judge by exit code.
$ErrorActionPreference = "Continue"
try {
    npm ci --no-audit --no-fund 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed in battle/" }
}
finally {
    $ErrorActionPreference = "Stop"
    Pop-Location
}
$battle = Join-Path $publish "battle"
$showdownSource = Join-Path $battleSource "node_modules/pokemon-showdown"
$showdown = Join-Path $battle "node_modules/pokemon-showdown"
New-Item -ItemType Directory -Force (Join-Path $showdown "dist") | Out-Null
Copy-Item (Join-Path $battleSource "pokemanager-battle.js"), (Join-Path $battleSource "package.json") $battle
Copy-Item (Join-Path $showdownSource "package.json"), (Join-Path $showdownSource "LICENSE") $showdown
# Only the compiled simulator is used (not the server, tools or translations: ~104 MB -> ~95 MB with the data).
foreach ($part in "sim", "data", "lib", "config") {
    Copy-Item -Recurse (Join-Path $showdownSource "dist/$part") (Join-Path $showdown "dist")
}
# Runtime dependencies of the simulator alone (found by running a battle on the pruned copy).
foreach ($dependency in "ts-chacha20") {
    Copy-Item -Recurse (Join-Path $battleSource "node_modules/$dependency") (Join-Path $battle "node_modules")
}
$node = (Get-Command node -ErrorAction SilentlyContinue).Source
if (-not $node) { throw "Node.js not found on the PATH (needed to bundle the battle simulator)" }
New-Item -ItemType Directory -Force (Join-Path $publish "tools/node") | Out-Null
Copy-Item $node (Join-Path $publish "tools/node/node.exe")

Step "Checking the bundled simulator plays a battle"
$bundledNode = Join-Path $publish "tools/node/node.exe"
$check = & $bundledNode (Join-Path $PSScriptRoot "check-battle.js") $battle 2>&1
if ($LASTEXITCODE -ne 0 -or -not ($check -match "winner")) { throw "The bundled simulator could not play a battle:`n$check" }
Write-Host "    $($check -join ' ')"

Step "Packing the installer (Velopack)"
Push-Location $root
try {
    dotnet tool restore
    dotnet tool run vpk -- pack `
        --packId PokemanagerApp `
        --packVersion $Version `
        --packTitle "Pokemanager" `
        --packAuthors "Marpuchy" `
        --packDir $publish `
        --mainExe "Pokemanager.App.exe" `
        --icon (Join-Path $root "src/Pokemanager.App/Assets/pokemanager.ico") `
        --shortcuts "Desktop,StartMenuRoot" `
        --outputDir $installer
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }
}
finally {
    Pop-Location
}

$setup = Get-ChildItem $installer -Filter "*Setup.exe" | Select-Object -First 1
Step "Done: $($setup.FullName) ($([math]::Round($setup.Length / 1MB, 1)) MB)"
