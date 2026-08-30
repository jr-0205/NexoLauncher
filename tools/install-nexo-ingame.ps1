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

function Stage([string]$message) {
    Write-Output "NEXO_STAGE|$message"
}

function Fail([string]$message) {
    throw "NEXO In-Game: $message"
}

function Invoke-NativeCapture([string]$FileName, [string[]]$Arguments) {
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FileName
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    # Compatible con Windows PowerShell 5.1 (.NET Framework).
    $startInfo.Arguments = [string]::Join(' ', $Arguments)

    $process = New-Object System.Diagnostics.Process
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

Stage 'Localizando instancia Fabric 1.21.1…'

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
Write-Output "Instancia: $($target.Name) [$($target.Id)]"
Write-Output "Game dir: $($target.Game)"

Stage 'Validando Java 21…'

$javaExecutable = 'java'
if (-not [string]::IsNullOrWhiteSpace($JavaPath)) {
    $javaExecutable = [System.IO.Path]::GetFullPath($JavaPath)
    if (-not (Test-Path -LiteralPath $javaExecutable)) { Fail "Java no existe en $javaExecutable" }
    $javaBin = Split-Path -Parent $javaExecutable
    $env:JAVA_HOME = Split-Path -Parent $javaBin
    $env:PATH = "$javaBin;$env:PATH"
}

# java -version escribe deliberadamente por stderr; evaluamos el código real.
$javaProbe = Invoke-NativeCapture $javaExecutable @('-version')
$javaVersion = (($javaProbe.Output + [Environment]::NewLine + $javaProbe.Error).Trim())
if ($javaProbe.ExitCode -ne 0) {
    Fail "Java no está disponible. NEXO In-Game 1.21.1 necesita Java 21 para compilar.`n$javaVersion"
}
if ($javaVersion -notmatch 'version\s+"21(?:\.|\")') {
    Fail "se necesita Java 21 para compilar NEXO In-Game. Runtime detectado:`n$javaVersion"
}
Write-Output "Java 21 validado: $javaExecutable"

New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
if (-not (Test-Path $gradleExe)) {
    Stage "Descargando Gradle $gradleVersion…"
    $uri = "https://services.gradle.org/distributions/gradle-$gradleVersion-bin.zip"
    Invoke-WebRequest -UseBasicParsing -TimeoutSec 180 -Uri $uri -OutFile $gradleZip

    Stage "Preparando Gradle $gradleVersion…"
    if (Test-Path $gradleHome) { Remove-Item -Recurse -Force $gradleHome }
    Expand-Archive -LiteralPath $gradleZip -DestinationPath $cacheRoot -Force
}
if (-not (Test-Path $gradleExe)) { Fail "Gradle no quedó disponible en $gradleExe" }

Stage 'Compilando NEXO In-Game…'
$env:GRADLE_OPTS = '-Dorg.gradle.internal.http.connectionTimeout=60000 -Dorg.gradle.internal.http.socketTimeout=120000'
& $gradleExe -p $projectRoot clean build --no-daemon --stacktrace --console=plain
if ($LASTEXITCODE -ne 0) { Fail "Gradle terminó con código $LASTEXITCODE" }

Stage 'Preparando JAR de NEXO In-Game…'
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
Write-Output "NEXO In-Game instalado: $installedJar"

$fabricApi = Get-ChildItem -Path $mods -Filter 'fabric-api*.jar' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $fabricApi) {
    Stage 'Resolviendo Fabric API compatible…'
    $gameVersions = [Uri]::EscapeDataString('["1.21.1"]')
    $loaders = [Uri]::EscapeDataString('["fabric"]')
    $versionsUri = "https://api.modrinth.com/v2/project/fabric-api/version?game_versions=$gameVersions&loaders=$loaders"
    $headers = @{ 'User-Agent' = 'NexoLauncher/0.5.2 (github.com/jr-0205/NexoLauncher)' }
    $versions = Invoke-RestMethod -TimeoutSec 60 -Headers $headers -Uri $versionsUri -Method Get
    $selected = $versions |
        Where-Object { $_.version_type -eq 'release' } |
        Sort-Object { [DateTimeOffset]$_.date_published } -Descending |
        Select-Object -First 1
    if (-not $selected) { $selected = $versions | Sort-Object { [DateTimeOffset]$_.date_published } -Descending | Select-Object -First 1 }
    if (-not $selected) { Fail 'Modrinth no devolvió una versión de Fabric API compatible con Minecraft 1.21.1' }
    $file = $selected.files | Where-Object { $_.primary -eq $true } | Select-Object -First 1
    if (-not $file) { $file = $selected.files | Select-Object -First 1 }
    if (-not $file) { Fail 'la versión de Fabric API no contiene archivos descargables' }

    Stage 'Descargando Fabric API…'
    $apiDestination = Join-Path $mods ([string]$file.filename)
    Invoke-WebRequest -UseBasicParsing -TimeoutSec 120 -Headers $headers -Uri ([string]$file.url) -OutFile $apiDestination
    Write-Output "Fabric API instalado: $apiDestination"
}

Stage 'Finalizando instalación…'
Write-Output 'OK: NEXO In-Game está dentro de la instancia.'
Write-Output 'Cierra Minecraft por completo, vuelve a iniciarlo desde NEXO y pulsa SHIFT DERECHO dentro del juego.'
