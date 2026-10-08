<#
.SYNOPSIS
  Membangun artefak rilis Makdon secara lokal (langkah yang sama dengan .github/workflows/release.yml).

.DESCRIPTION
  Urutan (docs/DISTRIBUTION.md bagian 8):
    1. build solusi Release (harus 0 warning) dan test (semua hijau);
    2. dotnet publish dengan profil win-x64 (self-contained, folder);
    3. menyusun folder isi rilis (publish + LICENSE + THIRD-PARTY-NOTICES*.txt; tanpa Makdon.portable dan tanpa data\);
    4. installer Inno Setup (installer\Makdon.iss) bila ISCC.exe ditemukan;
    5. zip portable (isi di atas + Makdon.portable);
    6. pemeriksaan: Makdon.portable tidak boleh ada di bahan installer;
    7. SHA256SUMS.txt (format sha256sum).
  Keluaran di artifacts\<versi>\ (di-.gitignore):
    Makdon-<versi>-setup-x64.exe, Makdon-<versi>-portable-x64.zip, SHA256SUMS.txt.
  Versi dibaca dari <Version> di src\Makdon\Makdon.csproj.
  Skrip ini tidak menyentuh registri. Satu-satunya pengecualian adalah -VerifyInstallerContents (lihat di bawah).

.PARAMETER IsccPath
  Path ISCC.exe. Bawaan: dicari di PATH lalu lokasi umum Inno Setup 6. Bila tidak ditemukan, installer dilewati dengan peringatan
  (kecuali -RequireInstaller).

.PARAMETER RequireInstaller
  Gagal bila installer tidak bisa dibangun (dipakai CI).

.PARAMETER VerifyInstallerContents
  Setelah installer jadi, pasang diam-diam ke folder sementara (/CURRENTUSER /VERYSILENT), pastikan Makdon.exe ada dan Makdon.portable
  tidak ada, lalu copot. BERISIKO: ini MENULIS HKCU sementara (kunci uninstall dan asosiasi) dengan AppId yang sama dengan instalasi
  Makdon sungguhan, sehingga pemasangan/pencopotan uji bisa menimpa atau menghapus instalasi Anda. Karena itu ditolak bila kunci
  uninstall Makdon sudah ada (HKCU atau HKLM, view 64-bit) dan ditolak di luar GitHub Actions (GITHUB_ACTIONS=true) kecuali -Force.

.PARAMETER Force
  Hanya bersama -VerifyInstallerContents: izinkan di luar CI. Kunci uninstall yang sudah ada tetap menolak.

.PARAMETER SkipTests
  Lewati dotnet test (hanya untuk percobaan cepat; CI tidak memakainya).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1 -RequireInstaller -VerifyInstallerContents
#>
[CmdletBinding()]
param(
    [string]$IsccPath,
    [switch]$RequireInstaller,
    [switch]$VerifyInstallerContents,
    [switch]$Force,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Csproj = Join-Path $Root 'src\Makdon\Makdon.csproj'
$Solution = Join-Path $Root 'Makdon.sln'
$IssFile = Join-Path $Root 'installer\Makdon.iss'

function Invoke-Native([string]$What, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What gagal (kode keluar $LASTEXITCODE)." }
}

function Write-Step([string]$Text) { Write-Host "`n== $Text" -ForegroundColor Cyan }

function Find-Iscc([string]$Explicit) {
    if ($Explicit) {
        if (-not (Test-Path -LiteralPath $Explicit)) { throw "ISCC.exe tidak ada di '$Explicit'." }
        return (Resolve-Path -LiteralPath $Explicit).Path
    }
    $cmd = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ }
    return $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

# --- Versi ---
$xml = [xml](Get-Content -LiteralPath $Csproj -Raw)
$Version = ($xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $Version) { throw "<Version> tidak ditemukan di $Csproj." }
Write-Host "Versi: $Version"

$Out = Join-Path $Root "artifacts\$Version"
$StageRoot = Join-Path $Out 'stage'
$Stage = Join-Path $StageRoot 'Makdon'
$PortableName = "Makdon-$Version-portable-x64"
$PortableDir = Join-Path $StageRoot $PortableName
$ZipPath = Join-Path $Out "$PortableName.zip"
$SetupName = "Makdon-$Version-setup-x64.exe"
$SetupPath = Join-Path $Out $SetupName

if (Test-Path -LiteralPath $Out) { Remove-Item -LiteralPath $Out -Recurse -Force }
New-Item -ItemType Directory -Path $Out | Out-Null

# --- 1. Build (0 warning) dan test ---
Write-Step 'Build Release'
$buildLog = Join-Path $Out 'build.log'
Invoke-Native 'dotnet build' { dotnet build $Solution -c Release --no-incremental --nologo 2>&1 | Tee-Object -FilePath $buildLog | Out-Host }
$summary = Select-String -Path $buildLog -Pattern '^\s*(\d+) Warning\(s\)' | Select-Object -Last 1
if (-not $summary) { throw 'Ringkasan warning build tidak ditemukan di log.' }
if ([int]$summary.Matches[0].Groups[1].Value -ne 0) { throw "Build menghasilkan warning (lihat $buildLog); rilis wajib 0 warning." }
Remove-Item -LiteralPath $buildLog

if ($SkipTests) {
    Write-Warning 'Test dilewati (-SkipTests).'
}
else {
    Write-Step 'Test'
    Invoke-Native 'dotnet test' { dotnet test (Join-Path $Root 'src\Makdon.Tests') -c Release --no-build --nologo }
}

# --- 2. Publish ---
Write-Step 'Publish win-x64 (self-contained)'
$PublishDir = Join-Path $Root 'src\Makdon\bin\Release\net10.0-windows\win-x64\publish'
if (Test-Path -LiteralPath $PublishDir) { Remove-Item -LiteralPath $PublishDir -Recurse -Force }
Invoke-Native 'dotnet publish' { dotnet publish (Join-Path $Root 'src\Makdon') -c Release -p:PublishProfile=win-x64 --nologo }
if (-not (Test-Path -LiteralPath (Join-Path $PublishDir 'Makdon.exe'))) { throw "Makdon.exe tidak ada di $PublishDir." }

# --- 3. Folder isi rilis ---
Write-Step 'Menyusun folder isi rilis'
New-Item -ItemType Directory -Path $Stage | Out-Null
Copy-Item -Path (Join-Path $PublishDir '*') -Destination $Stage -Recurse
# Penanda dan folder data dibuat pengguna/aplikasi, tidak boleh ikut dari publish.
Remove-Item -LiteralPath (Join-Path $Stage 'Makdon.portable') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $Stage 'data') -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $Root 'LICENSE') -Destination $Stage
Copy-Item -LiteralPath (Join-Path $Root 'THIRD-PARTY-NOTICES.txt') -Destination $Stage

# Pemberitahuan pihak ketiga milik runtime .NET (disalin dari paket runtime yang dipakai publish).
$runtimeConfig = Get-Content -LiteralPath (Join-Path $Stage 'Makdon.runtimeconfig.json') -Raw | ConvertFrom-Json
$netCore = $runtimeConfig.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' } | Select-Object -First 1
$netCoreVersion = if ($netCore) { $netCore.version } else { $null }
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$dotnetNotices = if ($netCoreVersion) { Join-Path $packages "microsoft.netcore.app.runtime.win-x64\$netCoreVersion\THIRD-PARTY-NOTICES.TXT" }
if ($dotnetNotices -and (Test-Path -LiteralPath $dotnetNotices)) {
    Copy-Item -LiteralPath $dotnetNotices -Destination (Join-Path $Stage 'THIRD-PARTY-NOTICES-DOTNET.txt')
}
else {
    throw "THIRD-PARTY-NOTICES.TXT paket runtime .NET (versi '$netCoreVersion') tidak ditemukan di $packages; pemberitahuan runtime wajib disertakan."
}
# Pemberitahuan WPF tidak ada di paket runtime; dibawa di repo (sumber: dotnet/wpf, THIRD-PARTY-NOTICES.TXT pada tag versi runtime).
$wpfNotices = Join-Path $Root 'THIRD-PARTY-NOTICES-WPF.txt'
if (-not (Test-Path -LiteralPath $wpfNotices)) { throw "THIRD-PARTY-NOTICES-WPF.txt tidak ada di $Root." }
Copy-Item -LiteralPath $wpfNotices -Destination $Stage

$stageFiles = Get-ChildItem -LiteralPath $Stage -Recurse -File
$stageMb = [math]::Round(($stageFiles | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "Folder isi rilis: $($stageFiles.Count) berkas, $stageMb MB"

# --- 6a. Penanda portable tidak boleh ada di bahan installer ---
if (Get-ChildItem -LiteralPath $Stage -Recurse -Force -Filter 'Makdon.portable') {
    throw 'Makdon.portable ditemukan di folder bahan installer.'
}

# --- 4. Installer ---
$installerBuilt = $false
$iscc = Find-Iscc $IsccPath
if (-not $iscc) {
    $msg = 'ISCC.exe (Inno Setup 6) tidak ditemukan; installer DILEWATI. Pasang Inno Setup 6.7.x atau berikan -IsccPath.'
    if ($RequireInstaller) { throw $msg }
    Write-Warning $msg
}
else {
    Write-Step "Installer Inno Setup ($iscc)"
    Invoke-Native 'ISCC' { & $iscc /Qp "/DAppVersion=$Version" "/DPublishDir=$Stage" "/DOutputDir=$Out" $IssFile }
    if (-not (Test-Path -LiteralPath $SetupPath)) { throw "Installer tidak ada di $SetupPath setelah ISCC selesai." }
    $installerBuilt = $true

    if ($VerifyInstallerContents) {
        Write-Step 'Verifikasi isi installer (pasang sementara, lalu copot)'
        # Pemasangan uji memakai AppId yang sama dengan instalasi nyata dan menulis HKCU; lindungi mesin pengembang.
        if ($env:GITHUB_ACTIONS -ne 'true' -and -not $Force) {
            throw '-VerifyInstallerContents hanya untuk CI (GITHUB_ACTIONS=true); beri -Force bila yakin mesin ini tidak punya instalasi Makdon.'
        }
        $uninstallSubKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{2025C09D-1945-431D-BEBF-18C8EC3A9A78}_is1'
        foreach ($hive in 'CurrentUser', 'LocalMachine') {
            $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, [Microsoft.Win32.RegistryView]::Registry64)
            try {
                $existing = $base.OpenSubKey($uninstallSubKey)
                if ($existing) {
                    $existing.Dispose()
                    throw "Kunci uninstall Makdon sudah ada di $hive; -VerifyInstallerContents ditolak karena akan menimpa/mencopot instalasi yang ada."
                }
            }
            finally { $base.Dispose() }
        }
        $probe = Join-Path ([IO.Path]::GetTempPath()) ("makdon-verify-" + [Guid]::NewGuid().ToString('N'))
        $log = Join-Path $Out 'verify-install.log'
        try {
            $p = Start-Process -FilePath $SetupPath -Wait -PassThru -ArgumentList @(
                '/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', "/DIR=`"$probe`"", "/LOG=`"$log`"")
            if ($p.ExitCode -ne 0) { throw "Pemasangan uji gagal (kode keluar $($p.ExitCode)); lihat $log." }
            if (-not (Test-Path -LiteralPath (Join-Path $probe 'Makdon.exe'))) { throw 'Makdon.exe tidak terpasang oleh installer.' }
            if (Get-ChildItem -LiteralPath $probe -Recurse -Force -Filter 'Makdon.portable') { throw 'Installer memasang Makdon.portable.' }
            Write-Host 'Isi installer OK: Makdon.exe ada, Makdon.portable tidak ada.'
        }
        finally {
            $uninstaller = Join-Path $probe 'unins000.exe'
            if (Test-Path -LiteralPath $uninstaller) {
                Start-Process -FilePath $uninstaller -Wait -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' | Out-Null
            }
            Remove-Item -LiteralPath $probe -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# --- 5. Zip portable ---
Write-Step 'Zip portable'
Copy-Item -LiteralPath $Stage -Destination $PortableDir -Recurse
New-Item -ItemType File -Path (Join-Path $PortableDir 'Makdon.portable') | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($PortableDir, $ZipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
Remove-Item -LiteralPath $StageRoot -Recurse -Force

# --- 7. Checksum (format sha256sum: "<hash huruf kecil>  <nama>", akhiran baris LF) ---
Write-Step 'SHA256SUMS.txt'
$artifacts = @()
if ($installerBuilt) { $artifacts += $SetupPath }
$artifacts += $ZipPath
$lines = foreach ($a in $artifacts) {
    $hash = (Get-FileHash -LiteralPath $a -Algorithm SHA256).Hash.ToLowerInvariant()
    '{0}  {1}' -f $hash, (Split-Path -Leaf $a)
}
$sumsPath = Join-Path $Out 'SHA256SUMS.txt'
[IO.File]::WriteAllText($sumsPath, (($lines -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))

Write-Step 'Selesai'
Get-ChildItem -LiteralPath $Out -File | ForEach-Object { '{0,10:N1} MB  {1}' -f ($_.Length / 1MB), $_.Name } | Write-Host
Get-Content -LiteralPath $sumsPath | Write-Host
if (-not $installerBuilt) { Write-Warning 'Installer TIDAK dibangun; SHA256SUMS.txt hanya memuat zip portable.' }
