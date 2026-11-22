param(
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [string]$BepInExCore = ""
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path $GameRoot 'REPO_Data\Managed'
if (!(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) {
    throw "R.E.P.O. Managed assemblies not found at $managed"
}
if (!$BepInExCore) { $BepInExCore = Join-Path $GameRoot 'BepInEx\core' }
foreach ($name in @('BepInEx.dll', '0Harmony.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $BepInExCore $name))) { throw "Missing $name in $BepInExCore" }
}
& dotnet build (Join-Path $repoRoot 'tools\CloudHost.Pc\CloudHost.Pc.csproj') -c Release --nologo "-p:GameManaged=$managed" "-p:BepInExCore=$BepInExCore"
if ($LASTEXITCODE -ne 0) { throw 'Cloud host build failed' }
Write-Output 'Build complete. Copy this DLL into the PC BepInEx/plugins folder next to LocalMultiplayer:'
Write-Output (Join-Path $repoRoot 'tools\CloudHost.Pc\bin\Release\netstandard2.1\RepoQuest.CloudHost.dll')
