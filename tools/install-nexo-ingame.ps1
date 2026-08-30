param(
    [string]$InstanceId,
    [string]$JavaPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Join-Path $repoRoot 'ingame\fabric-1.21.1'
$instancesRoot = Join-Path $env:LOCALAPPDATA 'NexoLauncher\instances'
$cacheRoot = Join-Path $env:LOCALAPPDATA 'NexoLauncher\cache\devtools'
$gradleVersion = '8.10.2'
$gradleHome = Join-Path $cacheRoot "gradle-$gradleVersion"
$gradleZip = Join-Path $cacheRoot "gradle-$gradleVersion-bin.zip"
$gradleExe = Join-Path $gradleHome 'bin\gradle.bat'

function Fail([string]$message) {
    throw "NEXO In-Game: $message"
}

function Invoke-NativeCapture([string]$FileName, [string[]]$Arguments) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            return [pscustomobject]@{ ExitCode = -1; Output = ''; Error = "No se pudo iniciar $FileName" }
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $stdoutTask.GetAwaiter().GetResult()
            Error = $stderrTask.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

if (-not (Test-Path $projectRoot)) {
    Fail "no se encontró el proyecto Fabric 1.21.1 en $projectRoot"
}
if (-not (Test-Path $instancesRoot)) {
    Fail "no existen instancias en $instancesRoot"
}

$candidates = @()
Get-ChildItem -Path $instancesRoot -Directory | ForEach-Object {
    $manifestPath = Join-Path $_.FullName 'instance.json'
    if (-not (Test-Path $manifestPath)) { return }
    try {
        $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
        $loaderType = if ($manifest.loader -is [string]) { [string]$manifest.loader } else { [string]$manifest.loader.type }
        if ([string]$manifest.minecraftVersion -ne '1.21.1') { return }
        if ($loaderType.ToLowerInvariant() -ne 'fabric') { return }
        if ($InstanceId -and ([string]$manifest.id -ne $InstanceId) -and ($_.Name -ne $InstanceId)) { return }

        $gameRelative = if ([string]::IsNullOrWhiteSpace([string]$manifest.gameDirectory)) { 'game' } else { [string]$manifest.gameDirectory }
        $candidates += [pscustomobject]@{
            Id = [string]$manifest.id
            Name = [string]$manifest.name
            Manifest = $manifestPath
            LastWrite = (Get-Item $manifestPath).LastWriteTimeUtc
            Game = Join-Path $_.FullName $gameRelative
        }
    }
    catch {
        Write-Warning "Se omitió un instance.json no interpretable: $manifestPath"
    }
}

if ($candidates.Count -eq 0) {
    if ($InstanceId) { Fail "no existe una instancia Fabric 1.21.1 con ID '$InstanceId'" }
    Fail 'no se encontró ninguna instancia Fabric 1.21.1. Crea o selecciona una antes de instalar NEXO In-Game.'
}

$target = $candidates | Sort-Object LastWrite -Descending | Select-Object -First 1
Write-Host "Instancia: $($target.Name) [$($target.Id)]"
Write-Host "Game dir: $($target.Game)"

$javaExecutable = 'java'
if (-not [string]::IsNullOrWhiteSpace($JavaPath)) {
    $javaExecutable = [System.IO.Path]::GetFullPath($JavaPath)
    if (-not (Test-Path -LiteralPath $javaExecutable)) { Fail "Java no existe en $javaExecutable" }
    $javaBin = Split-Path -Parent $javaExecutable
    $env:JAVA_HOME = Split-Path -Parent $javaBin
    $env:PATH = "$javaBin;$env:PATH"
}

# `java -version` escribe deliberadamente su versión por stderr. Ejecutarlo con
# `2>&1` bajo `$ErrorActionPreference = 'Stop'` hace que Windows PowerShell lo
# convierta en NativeCommandError aunque Java termine con código 0. Capturamos
# stdout/stderr con Process para evaluar únicamente el código de salida real.
$javaProbe = Invoke-NativeCapture $javaExecutable @('-version')
$javaVersion = (($javaProbe.Output + [Environment]::NewLine + $javaProbe.Error).Trim())
if ($javaProbe.ExitCode -ne 0) {
    Fail "Java no está disponible. NEXO In-Game 1.21.1 necesita Java 21 para compilar.`n$javaVersion"
}
if ($javaVersion -notmatch 'version\s+"21(?:\.|\")') {
    Fail "se necesita Java 21 para compilar NEXO In-Game. Runtime detectado:`n$javaVersion"
}
Write-Host "Java 21 validado: $javaExecutable"

New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
if (-not (Test-Path $gradleExe)) {
    Write-Host "Descargando Gradle $gradleVersion..."
    $uri = "https://services.gradle.org/distributions/gradle-$gradleVersion-bin.zip"
    Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $gradleZip
    if (Test-Path $gradleHome) { Remove-Item -Recurse -Force $gradleHome }
    Expand-Archive -LiteralPath $gradleZip -DestinationPath $cacheRoot -Force
}
if (-not (Test-Path $gradleExe)) { Fail "Gradle no quedó disponible en $gradleExe" }

Write-Host 'Compilando NEXO In-Game Fabric 1.21.1...'
& $gradleExe -p $projectRoot clean build --no-daemon --stacktrace
if ($LASTEXITCODE -ne 0) { Fail "Gradle terminó con código $LASTEXITCODE" }

$jar = Get-ChildItem -Path (Join-Path $projectRoot 'build\libs') -Filter '*.jar' |
    Where-Object { $_.Name -notmatch 'sources|dev|javadoc' } |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if (-not $jar) { Fail 'la compilación terminó sin producir un JAR instalable' }

$mods = Join-Path $target.Game 'mods'
New-Item -ItemType Directory -Force -Path $mods | Out-Null
Get-ChildItem -Path $mods -Filter 'nexo-ingame*.jar' -ErrorAction SilentlyContinue | Remove-Item -Force
$installedJar = Join-Path $mods 'nexo-ingame-fabric-1.21.1.jar'
Copy-Item -LiteralPath $jar.FullName -Destination $installedJar -Force
Write-Host "NEXO In-Game instalado: $installedJar"

$fabricApi = Get-ChildItem -Path $mods -Filter 'fabric-api*.jar' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $fabricApi) {
    Write-Host 'Fabric API no está presente; resolviendo una build compatible desde Modrinth...'
    $gameVersions = [Uri]::EscapeDataString('["1.21.1"]')
    $loaders = [Uri]::EscapeDataString('["fabric"]')
    $versionsUri = "https://api.modrinth.com/v2/project/fabric-api/version?game_versions=$gameVersions&loaders=$loaders"
    $headers = @{ 'User-Agent' = 'NexoLauncher/0.5.2 (github.com/jr-0205/NexoLauncher)' }
    $versions = Invoke-RestMethod -Headers $headers -Uri $versionsUri -Method Get
    $selected = $versions |
        Where-Object { $_.version_type -eq 'release' } |
        Sort-Object { [DateTimeOffset]$_.date_published } -Descending |
        Select-Object -First 1
    if (-not $selected) { $selected = $versions | Sort-Object { [DateTimeOffset]$_.date_published } -Descending | Select-Object -First 1 }
    if (-not $selected) { Fail 'Modrinth no devolvió una versión de Fabric API compatible con Minecraft 1.21.1' }
    $file = $selected.files | Where-Object { $_.primary -eq $true } | Select-Object -First 1
    if (-not $file) { $file = $selected.files | Select-Object -First 1 }
    if (-not $file) { Fail 'la versión de Fabric API no contiene archivos descargables' }
    $apiDestination = Join-Path $mods ([string]$file.filename)
    Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri ([string]$file.url) -OutFile $apiDestination
    Write-Host "Fabric API instalado: $apiDestination"
}

Write-Host ''
Write-Host 'OK: NEXO In-Game está dentro de la instancia.' -ForegroundColor Green
Write-Host 'Cierra Minecraft por completo, vuelve a iniciarlo desde NEXO y pulsa SHIFT DERECHO dentro del juego.' -ForegroundColor Green
