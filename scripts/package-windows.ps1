param(
    [Parameter(Mandatory = $true)][string]$BaseBuilder,
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$DotNet = '',
    [string]$RepoXRDll = '',
    [string]$OutputDir = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$repoWork = [IO.Path]::GetFullPath((Join-Path $repo 'work'))
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be X.Y.Z' }
$stage = Join-Path $repoWork "windows-package\$Version"
if (!$OutputDir) { $OutputDir = Join-Path $repo "dist\cloud-coop-$Version" }
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
$BaseBuilder = (Resolve-Path -LiteralPath $BaseBuilder).Path
$GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
$managed = Join-Path $GameRoot 'REPO_Data\Managed'
if (!(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) { throw "Game DLLs not found in $managed" }
if (!$DotNet) {
    $localDotNet = Join-Path $repoWork 'dotnet\dotnet.exe'
    $DotNet = if (Test-Path -LiteralPath $localDotNet) { $localDotNet } else { (Get-Command dotnet).Source }
}
$sdk = & $DotNet --version
if ([int]($sdk.Split('.')[0]) -lt 10) { throw 'Use .NET SDK 10 or newer for the Avalonia source generators.' }
function Run-DotNet([string[]]$Arguments) {
    & $DotNet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($Arguments -join ' ')" }
}
function Remove-Stage([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($stage) + [IO.Path]::DirectorySeparatorChar
    if (!$full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe staging path: $full" }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}
function Write-Utf8([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding $false))
}
function Get-PinnedArchive($Pin, [string]$Name) {
    $target = Join-Path $stage "cache\$Name"
    if (!(Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $Pin.sha256) {
        Invoke-WebRequest -UseBasicParsing $Pin.url -OutFile $target -TimeoutSec 120
    }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $Pin.sha256) { throw "Hash mismatch: $Name" }
    return $target
}
New-Item -ItemType Directory -Force -Path $stage,(Join-Path $stage 'cache'),$OutputDir | Out-Null
$manifest = Get-Content -LiteralPath (Join-Path $repo 'builder\kit.json') -Raw | ConvertFrom-Json
$cliProject = Join-Path $repo 'builder\QuestBuilder.Cli\QuestBuilder.Cli.csproj'
Run-DotNet -Arguments @('build', $cliProject, '-c', 'Release', '--nologo', "-p:Version=$Version")
$cli = Join-Path $repo 'builder\QuestBuilder.Cli\bin\Release\net8.0\QuestBuilder.Cli.dll'
$kit = Join-Path $stage 'kit'
Remove-Stage $kit
Run-DotNet -Arguments @('--roll-forward', 'Major', $cli, 'extract-kit', '--builder', $BaseBuilder, '--out', $kit)
$original = Get-Content -LiteralPath (Join-Path $kit 'kit.json') -Raw | ConvertFrom-Json
if ($original.unity.version -ne $manifest.unity.version -or
    $original.unity.changeset -ne $manifest.unity.changeset -or
    $original.downloads.repoXR.version -ne $manifest.downloads.repoXR.version) {
    throw 'The base builder uses incompatible Unity/RepoXR versions. Use a builder with the same Unity and RepoXR versions as this checkout.'
}
$gameHash = (Get-FileHash -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
# Official 1.2.0 ships a stale testedGames list (0.5.0) despite targeting 0.5.1.
# Permit resource reuse only for the exact verified official EXE; the checkout
# must still recognize the supplied game and the final package must pass bake.
$official120 = $original.version -eq '1.2.0' -and
    (Get-FileHash -LiteralPath $BaseBuilder -Algorithm SHA256).Hash.ToLowerInvariant() -eq '9246431a15bee101baaf58dc456bf7f40fcfd7bdb9638870d3d9ab941c299ecd'
if ($gameHash -notin @($manifest.testedGames.assemblyCSharp) -or
    ($gameHash -notin @($original.testedGames.assemblyCSharp) -and !$official120)) {
    throw 'Unsupported game fingerprint or unverified base kit.'
}
$bepZip = Get-PinnedArchive $manifest.downloads.bepInEx "BepInEx-$($manifest.downloads.bepInEx.version).zip"
$xrZip = Get-PinnedArchive $manifest.downloads.repoXR "RepoXR-$($manifest.downloads.repoXR.version).zip"
$bepExtract = Join-Path $stage 'bepinex'
$xrExtract = Join-Path $stage 'repoxr-release'
Expand-Archive -LiteralPath $bepZip -DestinationPath $bepExtract -Force
Expand-Archive -LiteralPath $xrZip -DestinationPath $xrExtract -Force
$bepCore = Join-Path $bepExtract 'BepInEx\core'
$runtimeDeps = Join-Path $xrExtract 'BepInEx\plugins\RepoXR\RuntimeDeps'
if (!$RepoXRDll) { $RepoXRDll = Join-Path $repo 'work\repoxr-src\bin\Release\netstandard2.1\RepoXR.dll' }
if (!(Test-Path -LiteralPath $RepoXRDll)) {
    throw 'Rebuild patched RepoXR from current sources (including QuestStrings), or pass -RepoXRDll with that rebuilt DLL.'
}
$RepoXRDll = (Resolve-Path -LiteralPath $RepoXRDll).Path
Run-DotNet -Arguments @('build', (Join-Path $repo 'tools\SteamShim'), '-c', 'Release', '--nologo')
Run-DotNet -Arguments @('build', (Join-Path $repo 'tools\RepoXR.Standalone'), '-c', 'Release', '--nologo',
    "-p:GameManaged=$managed", "-p:RepoXRDll=$RepoXRDll", "-p:RepoXRRuntimeDeps=$runtimeDeps", "-p:BepInExCore=$bepCore")
Run-DotNet -Arguments @('build', (Join-Path $repo 'tools\CloudHost.Pc'), '-c', 'Release', '--nologo',
    "-p:GameManaged=$managed", "-p:BepInExCore=$bepCore", "-p:Version=$Version")
if ($RepoXRDll -ne (Join-Path $kit 'prebuilt\repoxr\RepoXR.dll')) {
    Copy-Item -LiteralPath $RepoXRDll -Destination (Join-Path $kit 'prebuilt\repoxr\RepoXR.dll') -Force
}
Copy-Item -LiteralPath (Join-Path $repo 'tools\RepoXR.Standalone\bin\Release\netstandard2.1\RepoXR.Standalone.dll') `
    -Destination (Join-Path $kit 'prebuilt\standalone\RepoXR.Standalone.dll') -Force
foreach ($name in @('Facepunch.Steamworks.Win64','SteamKit2','protobuf-net','protobuf-net.Core','System.Collections.Immutable')) {
    Copy-Item -LiteralPath (Join-Path $repo "tools\SteamShim\bin\Release\netstandard2.1\$name.dll") `
        -Destination (Join-Path $kit "prebuilt\steam\$name.dll") -Force
}
Get-ChildItem -LiteralPath (Join-Path $repo 'scripts') -Filter '*.py' | Where-Object {
    $_.Name -notin @('inventory.py','shader_disasm.py','patch_repoxr_shaders.py','publish_check.py','package-thunderstore.py','github_stats.py')
} | Copy-Item -Destination (Join-Path $kit 'scripts') -Force
Copy-Item -Path (Join-Path $repo 'scripts\lib\*.py') -Destination (Join-Path $kit 'scripts\lib') -Force
foreach ($folder in Get-ChildItem -LiteralPath (Join-Path $repo 'unity-tools') | Where-Object Name -ne 'RepoXRAssets') {
    Copy-Item -LiteralPath $folder.FullName -Destination (Join-Path $kit 'unity-tools') -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $repo 'builder\python\requirements.lock') -Destination (Join-Path $kit 'python\requirements.lock') -Force
$manifest.version = $Version
Write-Utf8 (Join-Path $kit 'kit.json') (($manifest | ConvertTo-Json -Depth 30) + "`n")
$kitCache = Join-Path $kit 'downloads'
New-Item -ItemType Directory -Force -Path $kitCache | Out-Null
Copy-Item -LiteralPath $bepZip,$xrZip -Destination $kitCache -Force
foreach ($package in $manifest.downloads.nuGet) {
    $name = "$($package.id).$($package.version).nupkg"
    $archive = Get-PinnedArchive $package $name
    Copy-Item -LiteralPath $archive -Destination $kitCache -Force
}
foreach ($marker in @('.content-hash','.zip-hash')) {
    $path = Join-Path $kit $marker
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}
$tools = Join-Path $stage 'tools-win-x64'
Remove-Stage $tools
Run-DotNet -Arguments @('publish', $cliProject, '-c', 'Release', '-r', 'win-x64', '--nologo',
    '-p:PublishSingleFile=false', '-p:DebugType=none', '-p:DebugSymbols=false', "-p:Version=$Version", '-o', $tools)
$kitZip = Join-Path $stage 'kit.zip'
$toolsZip = Join-Path $stage 'tools.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Write-PackageZip([string]$Source, [string]$Destination) {
    # Compress-Archive requests exclusive source handles, which can collide with a scanner after publish.
    # ZipFile shares reads and throws on failure instead of letting packaging continue without a ZIP.
    for ($zipAttempt = 0; $zipAttempt -lt 3; $zipAttempt++) {
        if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
        try {
            [IO.Compression.ZipFile]::CreateFromDirectory($Source, $Destination, [IO.Compression.CompressionLevel]::Optimal, $false)
            return
        } catch [IO.IOException] {
            if ($zipAttempt -eq 2) { throw }
            Start-Sleep -Seconds 1
        }
    }
}
Write-PackageZip $kit $kitZip
Write-PackageZip $tools $toolsZip
$published = Join-Path $stage 'published'
Remove-Stage $published
Run-DotNet -Arguments @('publish', (Join-Path $repo 'builder\QuestBuilder.App\QuestBuilder.App.csproj'), '-c', 'Release',
    '-r', 'win-x64', '--nologo', "-p:Version=$Version", "-p:KitZip=$kitZip", "-p:ToolsZip=$toolsZip",
    '-p:DebugType=none', '-p:DebugSymbols=false', '-o', $published)
Copy-Item -LiteralPath (Join-Path $published 'RepoQuestBuilder.exe') -Destination $OutputDir -Force
Copy-Item -LiteralPath (Join-Path $repo 'tools\CloudHost.Pc\bin\Release\netstandard2.1\RepoQuest.CloudHost.dll') -Destination $OutputDir -Force
foreach ($name in @('RepoQuestBuilder.exe', 'RepoQuest.CloudHost.dll')) {
    $actualVersion = (Get-Item -LiteralPath (Join-Path $OutputDir $name)).VersionInfo.ProductVersion
    if ($actualVersion -ne $Version) { throw "$name has product version $actualVersion; expected $Version" }
}
Copy-Item -LiteralPath (Join-Path $repo 'builder\release\THIRD-PARTY-NOTICES.txt') -Destination $OutputDir -Force
Write-Utf8 (Join-Path $OutputDir 'builder-startup.json') (([ordered]@{
    workspacePath = 'C:\RepoQuest'
} | ConvertTo-Json) + "`n")
Write-Utf8 (Join-Path $OutputDir 'README.txt') @"
Network play builder $Version

Run RepoQuestBuilder.exe and select Build APK.
Default workspace: C:\RepoQuest
The builder installs Unity and the Android modules as needed. Sign in to Unity Hub
and activate a license when prompted.

Copy RepoQuest.CloudHost.dll to BepInEx/plugins alongside LocalMultiplayer on PC.
On Quest, open Network play. To join, select Connect and enter the host's RQ1 code.
To host in Cloud mode, select Create game and share the invitation from the waiting lobby.
If a room website is configured, you can open it from the invitation or connection screen.
On PC, press F8, select Paste code, then select Join room.

Cloud settings let you configure the Photon App IDs, nickname, Cloud ID and optional
website. Restart the game after saving. Changing the Cloud ID can affect access to progress.
On PC, edit Photon App IDs in BepInEx/config/io.repoquest.cloudhost.cfg.

To check the package without building the APK:
RepoQuestBuilder.exe --check --report preflight.json
"@
Write-Output "Packaged builder: $(Join-Path $OutputDir 'RepoQuestBuilder.exe')"

# Validate the final single-file EXE and its embedded tools. Never export the game or start Unity here.
$report = Join-Path $OutputDir 'preflight.json'
$checkExe = Join-Path $OutputDir 'RepoQuestBuilder.exe'
$check = Start-Process -FilePath $checkExe -ArgumentList @('--check','--bake','--prepare-tools',
    '--game', ('"{0}"' -f $GameRoot), '--report', ('"{0}"' -f $report)) `
    -WindowStyle Hidden -Wait -PassThru `
    -RedirectStandardOutput (Join-Path $stage 'preflight.stdout.log') `
    -RedirectStandardError (Join-Path $stage 'preflight.stderr.log')
if ($check.ExitCode -ne 0) { throw "Packaged builder preflight failed. See $report" }
Write-Output "Preflight passed: $report"
