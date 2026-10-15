param(
    [string]$Iscc,
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'

$repo    = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'KebuzPlayer\KebuzPlayer.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup |
    ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $project" }

if (-not $AllowDirty -and (Get-Command git -ErrorAction SilentlyContinue)) {
    $dirty = & git -C $repo status --porcelain -- .
    if ($dirty) { throw "Uncommitted changes under $repo, commit them or pass -AllowDirty" }
}

$releaseRoot = Join-Path (Split-Path $repo -Parent) 'release'
$stage       = Join-Path $releaseRoot 'stage'
$out         = Join-Path $releaseRoot $version

if (-not $Iscc) {
    $Iscc = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Iscc -or -not (Test-Path $Iscc)) { throw 'ISCC.exe (Inno Setup 6) not found, pass -Iscc' }

foreach ($dir in @($stage, $out)) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
}

function Publish-Player([string]$name, [string[]]$extra) {
    $dest = Join-Path $stage "$name\KebuzPlayer"
    $build = Join-Path $stage "build-$name"
    & dotnet publish $project -c Release -r win-x64 --nologo -v q `
        -p:DebugType=None -p:DebugSymbols=false `
        "-p:OutDir=$build" -o $dest @extra | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($name) failed" }
    Get-ChildItem $dest -Recurse -Filter '*.pdb' | Remove-Item -Force
    return $dest
}

function New-Zip([string]$folder, [string]$zip) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path $zip) { Remove-Item $zip -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($folder, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)
}

$portable = Publish-Player 'portable' @('--self-contained', 'true', '-p:PublishReadyToRun=true')
$lite     = Publish-Player 'lite' @('--self-contained', 'false')

$installerSource = Join-Path $stage 'installer\KebuzPlayer'
New-Item -ItemType Directory -Force (Split-Path $installerSource -Parent) | Out-Null
Copy-Item $lite $installerSource -Recurse

foreach ($dir in @($portable, $lite)) {
    New-Item -ItemType File -Force (Join-Path $dir 'kebuz.portable') | Out-Null
}

New-Zip $portable (Join-Path $out 'KebuzPlayer-win-x64-portable.zip')
New-Zip $lite (Join-Path $out 'KebuzPlayer-win-x64-lite.zip')

& $Iscc /Q "/DAppVersion=$version" "/DSourceDir=$installerSource" "/DOutputDir=$out" (Join-Path $PSScriptRoot 'KebuzPlayer.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }

$assets = Get-ChildItem $out -File | Where-Object { $_.Name -ne 'checksums.txt' } | Sort-Object Name
$lines = $assets | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
[System.IO.File]::WriteAllText((Join-Path $out 'checksums.txt'), (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))

Get-ChildItem $out -File | Sort-Object Name | ForEach-Object { '{0,10:N1} MB  {1}' -f ($_.Length / 1MB), $_.FullName }
