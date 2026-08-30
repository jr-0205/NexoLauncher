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
$gradleVersion = '8.12'
$gradleHome = Join-Path $cacheRoot ("gradle-{0}" -f $gradleVersion)
$gradleZip = Join-Path $cacheRoot ("gradle-{0}-bin.zip" -f $gradleVersion)
$gradleExe = Join-Path $gradleHome 'bin\gradle.bat'

function Stage([string]$message) {
    Write-Output ("NEXO_STAGE|{0}" -f $message)
}

function Fail([string]$message) {
    throw ("NEXO In-Game: {0}" -f $message)
}

function Invoke-NativeCapture([string]$FileName, [string[]]$Arguments) {
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FileName
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Arguments = [string]::Join(' ', $Arguments)

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            return [pscustomobject]@{ ExitCode = -1; Output = ''; Error = ("Could not start {0}" -f $FileName) }
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

Stage 'Locating Fabric 1.21.1 instance...'

if (-not (Test-Path $projectRoot)) {
    Fail ("Fabric 1.21.1 project was not found at {0}" -f $projectRoot)
}
if (-not (Test-Path $instancesRoot)) {
    Fail ("Instances directory was not found at {0}" -f $instancesRoot)
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
        Write-Warning ("Skipped unreadable instance.json: {0}" -f $manifestPath)
    }
}

if ($candidates.Count -eq 0) {
    if ($InstanceId) { Fail ("No Fabric 1.21.1 instance exists with ID '{0}'" -f $InstanceId) }
    Fail 'No Fabric 1.21.1 instance was found.'
}

$target = $candidates | Sort-Object LastWrite -Descending | Select-Object -First 1
Write-Output ("Instance: {0} [{1}]" -f $target.Name, $target.Id)
Write-Output ("Game dir: {0}" -f $target.Game)

Stage 'Validating Java 21...'

$javaExecutable = 'java'
if (-not [string]::IsNullOrWhiteSpace($JavaPath)) {
    $javaExecutable = [System.IO.Path]::GetFullPath($JavaPath)
    if (-not (Test-Path -LiteralPath $javaExecutable)) { Fail ("Java does not exist at {0}" -f $javaExecutable) }
    $javaBin = Split-Path -Parent $javaExecutable
    $env:JAVA_HOME = Split-Path -Parent $javaBin
    $env:PATH = ("{0};{1}" -f $javaBin, $env:PATH)
}

$javaProbe = Invoke-NativeCapture $javaExecutable @('-version')
$javaVersion = (($javaProbe.Output + [Environment]::NewLine + $javaProbe.Error).Trim())
if ($javaProbe.ExitCode -ne 0) {
    Fail ("Java is not available. Java 21 is required.`n{0}" -f $javaVersion)
}
if ($javaVersion -notmatch 'version\s+"21(?:\.|\")') {
    Fail ("Java 21 is required. Detected runtime:`n{0}" -f $javaVersion)
}
Write-Output ("Java 21 validated: {0}" -f $javaExecutable)

New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
if (-not (Test-Path $gradleExe)) {
    Stage ("Downloading Gradle {0}..." -f $gradleVersion)
    $uri = ("https://services.gradle.org/distributions/gradle-{0}-bin.zip" -f $gradleVersion)
    Invoke-WebRequest -UseBasicParsing -TimeoutSec 180 -Uri $uri -OutFile $gradleZip

    Stage ("Preparing Gradle {0}..." -f $gradleVersion)
    if (Test-Path $gradleHome) { Remove-Item -Recurse -Force $gradleHome }
    Expand-Archive -LiteralPath $gradleZip -DestinationPath $cacheRoot -Force
}
if (-not (Test-Path $gradleExe)) { Fail ("Gradle was not prepared at {0}" -f $gradleExe) }

Stage 'Compiling NEXO In-Game...'
$env:GRADLE_OPTS = '-Dorg.gradle.internal.http.connectionTimeout=60000 -Dorg.gradle.internal.http.socketTimeout=120000'
& $gradleExe -p $projectRoot clean build --no-daemon --stacktrace --console=plain
$gradleExitCode = $LASTEXITCODE
if ($gradleExitCode -ne 0) { Fail ("Gradle exited with code {0}" -f $gradleExitCode) }

Stage 'Preparing NEXO In-Game JAR...'
$jar = Get-ChildItem -Path (Join-Path $projectRoot 'build\libs') -Filter '*.jar' |
    Where-Object { $_.Name -notmatch 'sources|dev|javadoc' } |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if (-not $jar) { Fail 'Build finished without producing an installable JAR.' }

$mods = Join-Path $target.Game 'mods'
New-Item -ItemType Directory -Force -Path $mods | Out-Null
Get-ChildItem -Path $mods -Filter 'nexo-ingame*.jar' -ErrorAction SilentlyContinue | Remove-Item -Force
$installedJar = Join-Path $mods 'nexo-ingame-fabric-1.21.1.jar'
Copy-Item -LiteralPath $jar.FullName -Destination $installedJar -Force
Write-Output ("NEXO In-Game installed: {0}" -f $installedJar)

$fabricApi = Get-ChildItem -Path $mods -Filter 'fabric-api*.jar' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $fabricApi) {
    Stage 'Resolving compatible Fabric API...'
    $gameVersions = [Uri]::EscapeDataString('["1.21.1"]')
    $loaders = [Uri]::EscapeDataString('["fabric"]')
    $versionsUri = ("https://api.modrinth.com/v2/project/fabric-api/version?game_versions={0}&loaders={1}" -f $gameVersions, $loaders)
    $headers = @{ 'User-Agent' = 'NexoLauncher/0.5.2 (github.com/jr-0205/NexoLauncher)' }
    $versions = Invoke-RestMethod -TimeoutSec 60 -Headers $headers -Uri $versionsUri -Method Get
    $selected = $versions |
        Where-Object { $_.version_type -eq 'release' } |
        Sort-Object { [DateTimeOffset]$_.date_published } -Descending |
        Select-Object -First 1
    if (-not $selected) {
        $selected = $versions | Sort-Object { [DateTimeOffset]$_.date_published } -Descending | Select-Object -First 1
    }
    if (-not $selected) { Fail 'Modrinth returned no compatible Fabric API version for Minecraft 1.21.1.' }

    $file = $selected.files | Where-Object { $_.primary -eq $true } | Select-Object -First 1
    if (-not $file) { $file = $selected.files | Select-Object -First 1 }
    if (-not $file) { Fail 'The selected Fabric API version contains no downloadable files.' }

    Stage 'Downloading Fabric API...'
    $apiDestination = Join-Path $mods ([string]$file.filename)
    Invoke-WebRequest -UseBasicParsing -TimeoutSec 120 -Headers $headers -Uri ([string]$file.url) -OutFile $apiDestination
    Write-Output ("Fabric API installed: {0}" -f $apiDestination)
}

Stage 'Finalizing installation...'
Write-Output 'OK: NEXO In-Game is installed in the selected instance.'
Write-Output 'Close Minecraft completely, launch it again from NEXO, and press RIGHT SHIFT in game.'
