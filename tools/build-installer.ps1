[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$InnoCompiler
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$ui = Join-Path $repository "src\NexaLauncher.UI"
$desktopProject = Join-Path $repository "src\NexaLauncher.Desktop\NexaLauncher.Desktop.csproj"
$publishDirectory = Join-Path $repository "artifacts\publish\$Runtime"
$installerScript = Join-Path $repository "installer\NexoLauncher.iss"

Push-Location $repository
try {
    Write-Host "[1/4] Restaurando y compilando la interfaz..."
    Push-Location $ui
    try {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw "npm ci termino con codigo $LASTEXITCODE." }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "La interfaz termino con codigo $LASTEXITCODE." }
    }
    finally {
        Pop-Location
    }

    Write-Host "[2/4] Ejecutando pruebas..."
    dotnet test "NexoLauncher.slnx" --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Las pruebas terminaron con codigo $LASTEXITCODE." }

    Write-Host "[3/4] Publicando aplicacion autocontenida..."
    dotnet publish $desktopProject `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained true `
        --output $publishDirectory `
        -p:DebugType=None `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish termino con codigo $LASTEXITCODE." }

    if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
        $knownCompilers = @(
            (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
            (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
            (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
        )
        $InnoCompiler = $knownCompilers | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if ([string]::IsNullOrWhiteSpace($InnoCompiler) -or !(Test-Path -LiteralPath $InnoCompiler)) {
        throw "No se encontro Inno Setup 6. Instale Inno Setup o use -InnoCompiler <ruta-a-ISCC.exe>."
    }

    Write-Host "[4/4] Generando instalador..."
    & $InnoCompiler $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup termino con codigo $LASTEXITCODE." }

    $installer = Get-ChildItem (Join-Path $repository "artifacts\installer") -Filter "NEXA-Client-Setup-*.exe" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $installer) { throw "Inno Setup no genero el instalador esperado." }

    $hash = Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256
    Write-Host "Instalador: $($installer.FullName)"
    Write-Host "SHA-256:   $($hash.Hash)"
}
finally {
    Pop-Location
}
