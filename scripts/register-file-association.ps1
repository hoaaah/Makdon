<#
.SYNOPSIS
  Mendaftarkan Makdon sebagai aplikasi untuk berkas .md/.markdown (menu "Buka dengan") di HKCU.

.DESCRIPTION
  Hanya menulis ke HKEY_CURRENT_USER sehingga TIDAK memerlukan hak administrator.
  Yang didaftarkan:
    - ProgID "Makdon.Markdown" (perintah buka, ikon)
    - Applications\Makdon.exe (agar muncul di daftar "Buka dengan")
    - OpenWithProgids pada tiap ekstensi
    - Capabilities + RegisteredApplications (agar muncul di Pengaturan > Aplikasi bawaan)
  Windows 10/11 melindungi pilihan aplikasi bawaan (UserChoice): skrip ini tidak dapat memaksa Makdon
  menjadi bawaan. Setelah mendaftar, klik kanan berkas .md > Buka dengan > Pilih aplikasi lain > Makdon
  (centang "Selalu gunakan"), atau atur di Pengaturan > Aplikasi > Aplikasi bawaan.
  Parameter -SetDefault hanya menulis nilai bawaan ekstensi di HKCU\Software\Classes, yang berlaku bila
  pengguna belum pernah memilih aplikasi untuk ekstensi itu. Nilai bawaan lama dicadangkan ke
  HKCU\Software\Makdon\PreviousDefault (nama value = ekstensi) dan dipulihkan oleh unregister-file-association.ps1.

  Batalkan dengan unregister-file-association.ps1.

.PARAMETER ExePath
  Path Makdon.exe. Bawaan: hasil publish (src\Makdon\bin\Release\net10.0-windows\win-x64\publish\Makdon.exe),
  atau Makdon.exe di samping skrip ini.

.PARAMETER Extensions
  Ekstensi yang didaftarkan (hanya huruf kecil/angka, mis. .md). Bawaan: .md dan .markdown.

.PARAMETER SetDefault
  Juga menulis nilai bawaan ekstensi (lihat catatan UserChoice di atas).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\register-file-association.ps1 -WhatIf

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\register-file-association.ps1 -ExePath "C:\Apps\Makdon\Makdon.exe"
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ExePath,
    [string[]]$Extensions = @('.md', '.markdown'),
    [switch]$SetDefault
)

$ErrorActionPreference = 'Stop'

$ProgId = 'Makdon.Markdown'
$AppName = 'Makdon'
$AppExe = 'Makdon.exe'
$Description = 'Editor dan pratinjau Markdown'

if (-not $ExePath) {
    $candidates = @(
        (Join-Path $PSScriptRoot '..\src\Makdon\bin\Release\net10.0-windows\win-x64\publish\Makdon.exe'),
        (Join-Path $PSScriptRoot '..\publish\Makdon.exe'),
        (Join-Path $PSScriptRoot 'Makdon.exe')
    )
    $ExePath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $ExePath -or -not (Test-Path -LiteralPath $ExePath)) {
    throw "Makdon.exe tidak ditemukan. Jalankan 'dotnet publish src/Makdon -p:PublishProfile=win-x64' atau berikan -ExePath."
}
$ExePath = (Resolve-Path -LiteralPath $ExePath).Path

# Normalisasi ekstensi: ".md", huruf kecil.
$Extensions = $Extensions | ForEach-Object { '.' + $_.TrimStart('.').ToLowerInvariant() } | Select-Object -Unique
foreach ($ext in $Extensions) {
    # Ekstensi dipakai sebagai nama subkey registri: tolak yang mengandung karakter lain (mis. "\", spasi, "..").
    if ($ext -cnotmatch '^\.[a-z0-9]+$') {
        throw "Ekstensi tidak valid: '$ext'. Gunakan hanya huruf dan angka, mis. .md"
    }
}

$command = '"{0}" "%1"' -f $ExePath
$classes = 'Software\Classes'

function Set-RegistryValue([string]$SubKey, [string]$Name, $Value, [Microsoft.Win32.RegistryValueKind]$Kind = 'String') {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($SubKey)
    try { $key.SetValue($Name, $Value, $Kind) } finally { $key.Dispose() }
}

if ($PSCmdlet.ShouldProcess("HKCU\$classes", "Daftarkan $AppName untuk $($Extensions -join ', ')")) {
    # ProgID
    Set-RegistryValue "$classes\$ProgId" '' 'Dokumen Markdown'
    Set-RegistryValue "$classes\$ProgId" 'FriendlyTypeName' 'Dokumen Markdown'
    Set-RegistryValue "$classes\$ProgId\DefaultIcon" '' ('"{0}",0' -f $ExePath)
    Set-RegistryValue "$classes\$ProgId\shell" '' 'open'
    Set-RegistryValue "$classes\$ProgId\shell\open" 'FriendlyAppName' $AppName
    Set-RegistryValue "$classes\$ProgId\shell\open" 'MUIVerb' "Buka dengan $AppName"
    Set-RegistryValue "$classes\$ProgId\shell\open\command" '' $command

    # Applications\Makdon.exe (daftar "Buka dengan")
    Set-RegistryValue "$classes\Applications\$AppExe" 'FriendlyAppName' $AppName
    Set-RegistryValue "$classes\Applications\$AppExe\DefaultIcon" '' ('"{0}",0' -f $ExePath)
    Set-RegistryValue "$classes\Applications\$AppExe\shell\open\command" '' $command
    foreach ($ext in $Extensions) {
        Set-RegistryValue "$classes\Applications\$AppExe\SupportedTypes" $ext ''
    }

    # Per ekstensi: tawarkan ProgID di "Buka dengan".
    foreach ($ext in $Extensions) {
        Set-RegistryValue "$classes\$ext\OpenWithProgids" $ProgId ([byte[]]@()) 'None'
        if ($SetDefault) {
            # Cadangkan nilai bawaan lama (kecuali sudah milik kita, atau cadangan sudah ada dari pemanggilan sebelumnya).
            $existing = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$classes\$ext")
            $previous = $null
            if ($null -ne $existing) {
                try { $previous = $existing.GetValue('', $null) } finally { $existing.Dispose() }
            }
            $backupKey = "Software\$AppName\PreviousDefault"
            $backup = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($backupKey)
            $hasBackup = $false
            if ($null -ne $backup) {
                try { $hasBackup = $null -ne $backup.GetValue($ext, $null) } finally { $backup.Dispose() }
            }
            if (-not $hasBackup -and $previous -is [string] -and $previous -ne '' -and $previous -ne $ProgId) {
                Set-RegistryValue $backupKey $ext $previous
            }
            Set-RegistryValue "$classes\$ext" '' $ProgId
        }
    }

    # Capabilities: muncul di Pengaturan > Aplikasi bawaan.
    Set-RegistryValue "Software\$AppName\Capabilities" 'ApplicationName' $AppName
    Set-RegistryValue "Software\$AppName\Capabilities" 'ApplicationDescription' $Description
    foreach ($ext in $Extensions) {
        Set-RegistryValue "Software\$AppName\Capabilities\FileAssociations" $ext $ProgId
    }
    Set-RegistryValue 'Software\RegisteredApplications' $AppName "Software\$AppName\Capabilities"

    # Beri tahu shell supaya ikon/menu diperbarui.
    Add-Type -Namespace MakdonSetup -Name Shell -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll")]
public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
'@
    [MakdonSetup.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

    Write-Host "Terdaftar: $ExePath"
    Write-Host "Ekstensi : $($Extensions -join ', ')  (HKCU, tanpa admin)"
    if (-not $SetDefault) {
        Write-Host "Klik kanan berkas .md > Buka dengan > Pilih aplikasi lain > $AppName, atau atur di Pengaturan > Aplikasi bawaan."
    }
}
