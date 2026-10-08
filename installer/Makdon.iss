; Installer Makdon (Inno Setup 6.7.x). Rancangan: docs/DISTRIBUTION.md bagian 4 dan 5.
;
; Dipanggil oleh scripts/build-release.ps1 / CI, bukan dikompilasi sembarangan:
;   ISCC.exe /DAppVersion=0.1.0 "/DPublishDir=C:\...\stage\Makdon" "/DOutputDir=C:\...\artifacts\0.1.0" installer\Makdon.iss
;   AppVersion  versi dari <Version> di src/Makdon/Makdon.csproj (wajib)
;   PublishDir  folder isi yang dipasang: hasil publish self-contained (wajib)
;   OutputDir   folder keluaran installer (opsional, bawaan ..\artifacts\<versi>)
;
; Catatan folder: program per pengguna masuk %LOCALAPPDATA%\Programs\Makdon, berbeda dari folder data aplikasi
; %LOCALAPPDATA%\Makdon (settings.json, crash.log).
; Isi registri harus identik dengan docs/DISTRIBUTION.md bagian 4.1 dan scripts/register-file-association.ps1.

#ifndef AppVersion
  #error "AppVersion wajib diberikan: ISCC /DAppVersion=x.y.z"
#endif
#ifndef PublishDir
  #error "PublishDir wajib diberikan: ISCC /DPublishDir=<folder publish>"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\" + AppVersion
#endif
; Penanda mode portable tidak boleh ikut terpasang (aplikasi akan salah mengira dirinya portable).
#if FileExists(AddBackslash(PublishDir) + "Makdon.portable")
  #error "PublishDir memuat Makdon.portable; installer tidak boleh membawanya."
#endif

#define AppName "Makdon"
#define AppExe "Makdon.exe"
#define ProgId "Makdon.Markdown"

[Setup]
; AppId TIDAK BOLEH BERUBAH: identitas upgrade/uninstall dan dipakai aplikasi untuk mendeteksi instalasi.
; Kurung kurawal pertama di-escape; kunci uninstall menjadi {2025C09D-...}_is1.
AppId={{2025C09D-1945-431D-BEBF-18C8EC3A9A78}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Arief Wijaya
AppPublisherURL=https://github.com/hoaaah/Makdon
AppSupportURL=https://github.com/hoaaah/Makdon/issues
AppUpdatesURL=https://github.com/hoaaah/Makdon/releases
DefaultDirName={autopf}\{#AppName}
UsePreviousAppDir=yes
DisableDirPage=auto
DisableProgramGroupPage=yes
MinVersion=10.0.14393
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ChangesAssociations=yes
; Nama tetap, dibuat aplikasi mode terpasang; setup dan uninstaller meminta pengguna menutup Makdon.
AppMutex=Makdon.AppMutex,Global\Makdon.AppMutex
LicenseFile=..\LICENSE
SetupIconFile=..\src\Makdon\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=Makdon-{#AppVersion}-setup-x64

[Languages]
; Indonesian.isl bukan bawaan Inno Setup: salinan terjemahan tidak resmi ada di installer\Languages.
Name: "indonesian"; MessagesFile: "Languages\Indonesian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
indonesian.DowngradeBlocked=Makdon versi %1 sudah terpasang, lebih baru daripada versi installer ini (%2).%n%nPemasangan dibatalkan. Hapus versi yang terpasang terlebih dahulu bila Anda memang ingin menurunkan versi.
english.DowngradeBlocked=Makdon version %1 is already installed and is newer than this installer (version %2).%n%nSetup will now exit. Uninstall the installed version first if you really want to downgrade.
indonesian.AskRemoveUserData=Hapus juga pengaturan dan catatan galat Makdon?%n%n%1%n%2
english.AskRemoveUserData=Also remove Makdon settings and crash logs?%n%n%1%n%2
indonesian.AllUsersDataKept=Makdon sudah dihapus. Pengaturan dan catatan galat tiap pengguna tidak dihapus dan tetap ada di profil masing-masing (%APPDATA%\Makdon dan %LOCALAPPDATA%\Makdon).
english.AllUsersDataKept=Makdon has been removed. Each user's settings and crash logs were not deleted and remain in their profiles (%APPDATA%\Makdon and %LOCALAPPDATA%\Makdon).

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Tanpa [InstallDelete] bermasukan wildcard: pengguna boleh memilih folder bersama. Uninstaller menghapus berkas dari log lintas versi.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "Makdon.portable"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Root HKA: HKCU untuk instalasi per pengguna, HKLM untuk semua pengguna.
; ProgID
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: ""; ValueData: "Dokumen Markdown"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "Dokumen Markdown"
Root: HKA; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Buka dengan {#AppName}"
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
; Applications\Makdon.exe (daftar "Buka dengan")
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""
; OpenWithProgids: hanya nilainya yang dihapus saat uninstall, kuncinya milik ekstensi (bisa dipakai aplikasi lain).
; Tidak menulis nilai bawaan ekstensi dan tidak menyentuh UserChoice.
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; Flags: uninsdeletevalue
; Software\Makdon dihapus hanya bila kosong: skrip pengembangan memakai Software\Makdon\PreviousDefault.
; Dideklarasikan sebelum Capabilities karena uninstall memproses entri dari bawah ke atas.
Root: HKA; Subkey: "Software\{#AppName}"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\{#AppName}\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\{#AppName}\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Editor dan pratinjau Markdown"
Root: HKA; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".md"; ValueData: "{#ProgId}"
Root: HKA; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".markdown"; ValueData: "{#ProgId}"
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#AppName}"; ValueData: "Software\{#AppName}\Capabilities"; Flags: uninsdeletevalue

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{2025C09D-1945-431D-BEBF-18C8EC3A9A78}_is1';

// Buang akhiran pra-rilis/metadata SemVer ("0.2.0-beta.1" menjadi "0.2.0") agar StrToVersion dapat memprosesnya.
function CleanVersion(const S: String): String;
var
  I: Integer;
begin
  Result := S;
  for I := 1 to Length(S) do
    if (S[I] = '-') or (S[I] = '+') then
    begin
      Result := Copy(S, 1, I - 1);
      Exit;
    end;
end;

// True bila versi terpasang (di root registri tertentu) lebih baru daripada versi installer ini.
function IsNewerInstalled(RootKey: Integer; var Installed: String): Boolean;
var
  Existing, Mine: Int64;
begin
  Result := False;
  if RegQueryStringValue(RootKey, UninstallKey, 'DisplayVersion', Installed) then
    if StrToVersion(CleanVersion(Installed), Existing) and StrToVersion(CleanVersion('{#AppVersion}'), Mine) then
      Result := ComparePackedVersion(Existing, Mine) > 0;
end;

function InitializeSetup: Boolean;
var
  Installed: String;
begin
  Result := True;
  // Instalasi per pengguna (HKCU) maupun semua pengguna (HKLM); view 64-bit karena installer 64-bit.
  if IsNewerInstalled(HKCU64, Installed) or IsNewerInstalled(HKLM64, Installed) then
  begin
    SuppressibleMsgBox(FmtMessage(CustomMessage('DowngradeBlocked'), [Installed, '{#AppVersion}']),
      mbError, MB_OK, IDOK);
    Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RoamingDir, LocalDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;
  // /SILENT dan /VERYSILENT: tidak bertanya dan tidak menghapus data.
  if UninstallSilent then
    Exit;

  if IsAdminInstallMode then
  begin
    // Instalasi semua pengguna: konstanta userappdata/localappdata mengacu ke akun admin yang sedang elevated, bisa
    // berbeda dari pengguna yang login. Jangan hapus apa pun dan jangan menelusuri profil lain; cukup informasikan.
    MsgBox(CustomMessage('AllUsersDataKept'), mbInformation, MB_OK);
    Exit;
  end;

  RoamingDir := ExpandConstant('{userappdata}\{#AppName}');
  LocalDir := ExpandConstant('{localappdata}\{#AppName}');
  // Bawaan Tidak (MB_DEFBUTTON2).
  if MsgBox(FmtMessage(CustomMessage('AskRemoveUserData'), [RoamingDir, LocalDir]),
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
  begin
    DelTree(RoamingDir, True, True, True);
    DelTree(LocalDir, True, True, True);
  end;
end;
