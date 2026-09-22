<#
.SYNOPSIS
  Build, verify and publish Sir Word-a-lot into dist/SirWordALot.

.DESCRIPTION
  Runs the four verification layers in order — unit tests, headless Avalonia
  checks, rendered previews and the pixel measurements taken from them — then
  publishes the self-contained single-file host. A release never skips them.

.EXAMPLE
  .\build.ps1
  .\build.ps1 -SkipTests      # local iteration only; never for a release
#>
param(
    [ValidateSet('win-x64')]
    [string] $Rid = 'win-x64',
    [string] $Config = 'Release',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$distApp = Join-Path $PSScriptRoot "dist/SirWordALot"
$preview = Join-Path $PSScriptRoot "dist/preview"

function Write-Step([string] $text) { Write-Host "`n$text" -ForegroundColor Cyan }

function Invoke-Checked([string] $what, [scriptblock] $action) {
    & $action
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

Write-Step '[1/6] Verifying versions agree...'
. (Join-Path $PSScriptRoot 'version.ps1')
$versions = Show-Report $PSScriptRoot $null
if (-not $versions.AllOk) { throw 'Versions disagree; run version.ps1 before building.' }
$version = $versions.Target

Write-Step "[2/6] Building ($Config)..."
Invoke-Checked 'dotnet build' { dotnet build SirWordALot.csproj -c $Config --nologo }

if ($SkipTests) {
    Write-Host "`n[3/6] Skipping tests and checks (-SkipTests)" -ForegroundColor Yellow
} else {
    Write-Step '[3/6] Running tests, headless checks and measured previews...'
    Invoke-Checked 'dotnet test' { dotnet test tests/SirWordALot.Tests.csproj -c $Config --nologo }
    Invoke-Checked 'tools/Checks' { dotnet run --project tools/Checks -c $Config }
    Invoke-Checked 'tools/Preview' { dotnet run --project tools/Preview -c $Config -- $preview }
    Invoke-Checked 'tools/Measure' { dotnet run --project tools/Measure -c $Config -- $preview }
}

Write-Step '[4/6] Closing any running copy of this build...'
# Publishing over a running exe fails on Windows. Scope this to our own output.
Get-Process -Name SirWordALot -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($distApp, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object {
        Write-Host "  stopping pid $($_.Id)" -ForegroundColor DarkGray
        Stop-Process -Id $_.Id -Force
    }
Start-Sleep -Milliseconds 400

Write-Step "[5/6] Publishing self-contained $Rid..."
if (Test-Path -LiteralPath $distApp) { Remove-Item -LiteralPath $distApp -Recurse -Force }
Invoke-Checked 'dotnet publish' {
    dotnet publish SirWordALot.csproj -c $Config -r $Rid --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        --nologo -o $distApp
}

Write-Step '[6/6] Proving the published binary actually runs...'
$exe = Join-Path $distApp 'SirWordALot.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Publish produced no executable at $exe" }

# --selftest opens SQLite and loads the embedded dictionary, both of which
# resolve differently inside a single-file bundle than under dotnet run.
$log = Join-Path $env:TEMP 'swl-build-selftest.txt'
$proc = Start-Process -FilePath $exe -ArgumentList '--selftest' -NoNewWindow -Wait -PassThru -RedirectStandardOutput $log
$output = (Get-Content -LiteralPath $log -Raw).Trim()
if ($proc.ExitCode -ne 0) { throw "Self-test failed (exit $($proc.ExitCode)): $output" }

$reported = (& $exe --version | Out-String).Trim()
if ($reported -notmatch [regex]::Escape($version)) {
    throw "Published binary reports '$reported' but the release version is $version."
}

$item = Get-Item -LiteralPath $exe
Write-Host ''
Write-Host "Done. Output: $($item.FullName)" -ForegroundColor Green
Write-Host ("  size:      {0} MB" -f [math]::Round($item.Length / 1MB, 1))
Write-Host "  version:   $version"
Write-Host "  self-test: $output"
Write-Host "  SHA256:    $((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash)"
