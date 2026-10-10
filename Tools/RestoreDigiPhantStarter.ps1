param(
    [string]$StarterPath = (Join-Path $PSScriptRoot '..\DigiPhantStarter')
)

$ErrorActionPreference = 'Stop'
$ExpectedStarterCommit = '4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$starter = (Resolve-Path $StarterPath).Path
$assets = Join-Path $project 'Assets'

function Invoke-GitChecked {
    param([string[]]$Arguments)
    & git @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed." }
}

foreach ($required in @('Elephant', 'Elephant.meta', 'Tracking\bridge.py', 'Tracking\test_bridge.py', 'Tracking\requirements.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $starter $required)))
    { throw "Starter is incomplete: missing $required." }
}
$head = (& git -C $starter rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not read the starter commit.' }
if ($head -ne $ExpectedStarterCommit)
{ throw "Starter commit must be $ExpectedStarterCommit." }
& git -C $starter diff --quiet HEAD -- Elephant Elephant.meta Tracking/bridge.py Tracking/test_bridge.py
if ($LASTEXITCODE -ne 0) { throw 'Starter shared assets or tracking sources have local changes.' }
$untracked = & git -C $starter status --porcelain -- Elephant Elephant.meta Tracking/bridge.py Tracking/test_bridge.py
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect starter working-tree status.' }
if ($untracked) { throw 'Starter contains uncommitted or untracked required source paths.' }

if (Test-Path (Join-Path $assets 'Elephant')) {
    throw 'Assets/Elephant already exists. Refusing to overwrite shared assets.'
}
if (Test-Path (Join-Path $assets 'Elephant.meta')) {
    throw 'Assets/Elephant.meta already exists. Refusing to overwrite metadata.'
}

$patch = Join-Path $project 'TrackingPatches\bridge-windows-udp-reset.patch'
if (-not (Test-Path -LiteralPath $patch)) { throw "Required tracking patch is missing: $patch" }

# Finish every preflight before copying anything into Assets.
Invoke-GitChecked @('-C', $starter, 'apply', '--check', $patch)
Copy-Item -LiteralPath (Join-Path $starter 'Elephant') -Destination (Join-Path $assets 'Elephant') -Recurse
Copy-Item -LiteralPath (Join-Path $starter 'Elephant.meta') -Destination (Join-Path $assets 'Elephant.meta')
Invoke-GitChecked @('-C', $starter, 'apply', $patch)

Write-Output 'DigiPhant shared assets restored. Create Tracking/.venv, then open Assets/Scenes/SampleScene.unity.'
