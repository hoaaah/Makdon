<#
.SYNOPSIS
  Membatalkan pendaftaran MdViewer yang dibuat oleh register-file-association.ps1 (HKCU, tanpa admin).

.DESCRIPTION
  Menghapus ProgID, Applications\MdViewer.exe, Capabilities, entri RegisteredApplications, dan nilai
  OpenWithProgids yang menunjuk ke MdViewer. Nilai bawaan ekstensi hanya disentuh bila persis menunjuk ke
  ProgID MdViewer: dipulihkan dari cadangan HKCU\Software\MdViewer\PreviousDefault (dibuat oleh
  register-file-association.ps1 -SetDefault), atau dihapus bila tidak ada cadangan.
  Pilihan pengguna di Windows (UserChoice) tidak disentuh.

.PARAMETER Extensions
  Ekstensi yang dibersihkan (hanya huruf kecil/angka, mis. .md). Bawaan: .md dan .markdown.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\unregister-file-association.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string[]]$Extensions = @('.md', '.markdown')
)

$ErrorActionPreference = 'Stop'

$ProgId = 'MdViewer.Markdown'
$AppName = 'MdViewer'
$AppExe = 'MdViewer.exe'
$classes = 'Software\Classes'
$Extensions = $Extensions | ForEach-Object { '.' + $_.TrimStart('.').ToLowerInvariant() } | Select-Object -Unique
foreach ($ext in $Extensions) {
    if ($ext -cnotmatch '^\.[a-z0-9]+$') {
        throw "Ekstensi tidak valid: '$ext'. Gunakan hanya huruf dan angka, mis. .md"
    }
}

function Remove-RegistryTree([string]$SubKey) {
    $root = [Microsoft.Win32.Registry]::CurrentUser
    $existing = $root.OpenSubKey($SubKey)
    if ($null -eq $existing) { return }
    $existing.Dispose()
    $root.DeleteSubKeyTree($SubKey, $false)
}

function Remove-RegistryValue([string]$SubKey, [string]$Name) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($SubKey, $true)
    if ($null -eq $key) { return }
    try {
        if ($null -ne $key.GetValue($Name, $null)) { $key.DeleteValue($Name, $false) }
    } finally { $key.Dispose() }
}

if ($PSCmdlet.ShouldProcess("HKCU\$classes", "Hapus pendaftaran $AppName")) {
    foreach ($ext in $Extensions) {
        Remove-RegistryValue "$classes\$ext\OpenWithProgids" $ProgId

        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$classes\$ext", $true)
        if ($null -ne $key) {
            try {
                if ($key.GetValue('') -eq $ProgId) {
                    # Pulihkan nilai bawaan lama bila ada cadangan; bila tidak, hapus value bawaan.
                    $backup = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Software\$AppName\PreviousDefault")
                    $previous = $null
                    if ($null -ne $backup) {
                        try { $previous = $backup.GetValue($ext, $null) } finally { $backup.Dispose() }
                    }
                    if ($previous -is [string] -and $previous -ne '') { $key.SetValue('', $previous) }
                    else { $key.DeleteValue('', $false) }
                }
            } finally { $key.Dispose() }
        }
        Remove-RegistryValue "Software\$AppName\PreviousDefault" $ext
    }

    Remove-RegistryTree "$classes\$ProgId"
    Remove-RegistryTree "$classes\Applications\$AppExe"
    Remove-RegistryTree "Software\$AppName\Capabilities"

    # Hapus PreviousDefault bila sudah tidak berisi cadangan.
    $previousKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Software\$AppName\PreviousDefault")
    if ($null -ne $previousKey) {
        $emptyBackup = ($previousKey.ValueCount -eq 0 -and $previousKey.SubKeyCount -eq 0)
        $previousKey.Dispose()
        if ($emptyBackup) { Remove-RegistryTree "Software\$AppName\PreviousDefault" }
    }
    Remove-RegistryValue 'Software\RegisteredApplications' $AppName

    # Hapus Software\MdViewer bila sudah kosong.
    $root = [Microsoft.Win32.Registry]::CurrentUser
    $app = $root.OpenSubKey("Software\$AppName")
    if ($null -ne $app) {
        $empty = ($app.SubKeyCount -eq 0 -and $app.ValueCount -eq 0)
        $app.Dispose()
        if ($empty) { $root.DeleteSubKey("Software\$AppName", $false) }
    }

    Add-Type -Namespace MdViewerSetup -Name Shell -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll")]
public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
'@
    [MdViewerSetup.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

    Write-Host "Pendaftaran $AppName dihapus dari HKCU."
}
