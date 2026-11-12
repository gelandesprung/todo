param(
    [switch]$Installer,
    [string]$IscPath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$sdkDir = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
$toolsDir = Join-Path $root "tools"

# Locate a .NET SDK in this order: user-level install, any unpacked SDK in tools\,
# PATH, then any SDK zip in tools\ (unpacked on demand). Version number is not fixed.
function Resolve-Dotnet {
    $installed = Join-Path $sdkDir "dotnet.exe"
    if (Test-Path $installed) { return $installed }

    if (Test-Path $toolsDir) {
        $unpacked = Get-ChildItem $toolsDir -Directory -Filter "dotnet-sdk-*-win-x64" -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending | Select-Object -First 1
        if ($unpacked) {
            $exe = Join-Path $unpacked.FullName "dotnet.exe"
            if (Test-Path $exe) { return $exe }
        }
    }

    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    if (Test-Path $toolsDir) {
        $zip = Get-ChildItem $toolsDir -File -Filter "dotnet-sdk-*-win-x64.zip" -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending | Select-Object -First 1
        if ($zip) {
            Write-Host "== Unpacking local SDK zip: $($zip.FullName) =="
            New-Item -ItemType Directory -Force -Path $sdkDir | Out-Null
            Expand-Archive -Path $zip.FullName -DestinationPath $sdkDir -Force
            $exe = Join-Path $sdkDir "dotnet.exe"
            if (Test-Path $exe) { return $exe }
        }
    }

    throw "No .NET 8 SDK found. Put the SDK zip or its unpacked folder in $toolsDir"
}

$dotnet = Resolve-Dotnet
Write-Host "== SDK: $dotnet =="
& $dotnet --version
if ($LASTEXITCODE -ne 0) { throw "dotnet SDK not available" }

Write-Host "== Icons =="
& (Join-Path $PSScriptRoot "make-icon.ps1")

Write-Host "== Build / publish portable single file =="
$proj = Join-Path $root "src\DayTodo\DayTodo.csproj"
$out = Join-Path $root "dist\portable"
New-Item -ItemType Directory -Force -Path $out | Out-Null
& $dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:DebugSymbols=false `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $out "DayTodo.exe"
if (-not (Test-Path $exe)) { throw "expected output missing: $exe" }
$mb = [Math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ("Portable exe: {0} ({1} MB)" -f $exe, $mb)

if ($Installer) {
    Write-Host "== Build installer =="
    $iscc = $IscPath
    if (-not $iscc) {
        foreach ($p in @(
            "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
            "C:\Program Files\Inno Setup 6\ISCC.exe",
            (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
        )) {
            if (Test-Path $p) { $iscc = $p; break }
        }
    }
    if (-not $iscc) {
        Write-Warning "Inno Setup 6 not found. Install it, then run: publish.ps1 -Installer"
        Write-Warning "Or compile manually: ISCC.exe installer.iss"
        return
    }
    $iss = Join-Path $PSScriptRoot "installer.iss"
    & $iscc $iss
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }
    Get-ChildItem (Join-Path $root "dist\installer") -Filter "*.exe" | ForEach-Object {
        Write-Host ("Installer: {0} ({1} MB)" -f $_.FullName, [Math]::Round($_.Length / 1MB, 1))
    }
}

Write-Host "Done."
