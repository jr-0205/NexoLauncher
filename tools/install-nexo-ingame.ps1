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
$gradleSha256 = '7a00d51fb93147819aab76024feece20b6b84e420694101f276be952e08bef03'
$gradleHome = Join-Path $cacheRoot ("gradle-{0}" -f $gradleVersion)
$gradleZip = Join-Path $cacheRoot ("gradle-{0}-bin.zip" -f $gradleVersion)
$gradleExe = Join-Path $gradleHome 'bin\gradle.bat'

function Stage([string]$message) {
    Write-Output ("NEXO_STAGE|{0}" -f $message)
}

function Fail([string]$message) {
    throw ("NEXO In-Game: {0}" -f $message)
}

function Quote-NativeArgument([string]$value) {
    if ($null -eq $value) { return '""' }
    return '"' + $value.Replace('"', '\"') + '"'
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

function Download-WithCurl(
    [string]$Uri,
    [string]$Destination,
    [string]$Label,
    [int]$MaximumSeconds = 420
) {
    $curl = Get-Command 'curl.exe' -ErrorAction SilentlyContinue
    if (-not $curl) {
        Fail 'curl.exe is required for reliable downloads on Windows.'
    }

    $directory = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Force
    }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $curl.Source
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Arguments = [string]::Join(' ', @(
        '-L',
        '--fail',
        '--silent',
        '--show-error',
        '--connect-timeout', '20',
        '--max-time', [string]$MaximumSeconds,
        '--retry', '3',
        '--retry-delay', '2',
        '--output', (Quote-NativeArgument $Destination),
        (Quote-NativeArgument $Uri)
    ))

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            Fail ("Could not start download for {0}." -f $Label)
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $lastReportedMiB = -1

        while (-not $process.HasExited) {
            $size = 0L
            if (Test-Path -LiteralPath $Destination) {
                try { $size = (Get-Item -LiteralPath $Destination).Length } catch { }
            }

            $mib = [math]::Floor($size / 1MB)
            if ($mib -ne $lastReportedMiB) {
                Stage ("{0}... {1:N0} MB" -f $Label, ($size / 1MB))
                $lastReportedMiB = $mib
            }
            Start-Sleep -Milliseconds 1000
        }

        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            if (Test-Path -LiteralPath $Destination) {
                Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
            }
            $details = (($stdout + [Environment]::NewLine + $stderr).Trim())
            Fail ("Download failed for {0} (curl code {1}). {2}" -f $Label, $process.ExitCode, $details)
        }
    }
    finally {
        $process.Dispose()
    }
}

function Test-FileHash([string]$Path, [string]$Algorithm, [string]$ExpectedHash) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm $Algorithm).Hash
    return [string]::Equals($actual, $ExpectedHash, [StringComparison]::OrdinalIgnoreCase)
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
    $uri = ("https://services.gradle.org/distributions/gradle-{0}-bin.zip" -f $gradleVersion)

    if (-not (Test-FileHash $gradleZip 'SHA256' $gradleSha256)) {
        if (Test-Path -LiteralPath $gradleZip) {
            Stage 'Discarding incomplete Gradle download...'
            Remove-Item -LiteralPath $gradleZip -Force
        }
        Download-WithCurl $uri $gradleZip ("Downloading Gradle {0}" -f $gradleVersion) 420
    }

    Stage ("Verifying Gradle {0} SHA-256..." -f $gradleVersion)
    if (-not (Test-FileHash $gradleZip 'SHA256' $gradleSha256)) {
        Remove-Item -LiteralPath $gradleZip -Force -ErrorAction SilentlyContinue
        Fail 'Gradle SHA-256 verification failed. The cached ZIP was deleted.'
    }

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
    Download-WithCurl ([string]$file.url) $apiDestination 'Downloading Fabric API' 180

    if ($file.hashes -and $file.hashes.sha512) {
        Stage 'Verifying Fabric API SHA-512...'
        if (-not (Test-FileHash $apiDestination 'SHA512' ([string]$file.hashes.sha512))) {
            Remove-Item -LiteralPath $apiDestination -Force -ErrorAction SilentlyContinue
            Fail 'Fabric API SHA-512 verification failed.'
        }
    }
    Write-Output ("Fabric API installed: {0}" -f $apiDestination)
}

Stage 'Finalizing installation...'
Write-Output 'OK: NEXO In-Game is installed in the selected instance.'
Write-Output 'Close Minecraft completely, launch it again from NEXO, and press RIGHT SHIFT in game.'
