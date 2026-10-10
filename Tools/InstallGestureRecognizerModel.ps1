param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\DigiPhantStarter\Tracking\gesture_recognizer.task')
)

$ErrorActionPreference = 'Stop'
$manifest = Get-Content -Raw (Join-Path $PSScriptRoot 'GestureRecognizerModel.json') | ConvertFrom-Json
$destinationPath = [System.IO.Path]::GetFullPath($Destination)
$destinationFolder = Split-Path -Parent $destinationPath
New-Item -ItemType Directory -Force -Path $destinationFolder | Out-Null

function Get-SharedSha256([string]$Path) {
    $share = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read, $share)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return -join ($sha256.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') })
    } finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}

if (Test-Path -LiteralPath $destinationPath) {
    $currentHash = Get-SharedSha256 $destinationPath
    if ($currentHash -eq $manifest.sha256) {
        Write-Output "Gesture model is already verified: $destinationPath"
        exit 0
    }
    throw "Existing gesture model hash does not match the recorded official download. Remove it only if it is not in use, then run this script again."
}

$temporary = "$destinationPath.$([Guid]::NewGuid().ToString('N')).download"
try {
    Invoke-WebRequest -Uri $manifest.url -OutFile $temporary
    $actualHash = Get-SharedSha256 $temporary
    if ($actualHash -ne $manifest.sha256) { throw "Downloaded model hash mismatch: $actualHash" }
    Move-Item -LiteralPath $temporary -Destination $destinationPath
    Write-Output "Installed verified gesture model: $destinationPath"
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
}
