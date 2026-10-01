; ExplorerAlternative のインストーラー（Inno Setup 6）。
; リリースのワークフロー（.github/workflows/release.yml）が、発行済みの publish\ExplorerAlternative.exe を
; 入れて、バージョンを /DAppVersion=1.2.3 で渡してビルドする。手元でビルドする場合：
;   dotnet publish ExplorerAlternative\ExplorerAlternative.csproj -c Release -r win-x64 --self-contained true ^
;       -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
;   ISCC.exe /DAppVersion=1.2.3 installer\ExplorerAlternative.iss
; 出力：installer\output\ExplorerAlternative-v<バージョン>-Setup.exe

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "ExplorerAlternative"
#define AppExe "ExplorerAlternative.exe"

[Setup]
; アプリを識別するID。変えると、別のアプリとして扱われ、上書きの更新ができなくなる。
AppId={{8B6C0F3E-5A47-4C0E-9D3B-7E2A41C5B9F1}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=ExplorerAlternative
AppPublisherURL=https://github.com/Jukiii/ExplorerAlternative
AppSupportURL=https://github.com/Jukiii/ExplorerAlternative/issues
AppUpdatesURL=https://github.com/Jukiii/ExplorerAlternative/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
SetupIconFile=..\ExplorerAlternative\Assets\app.ico
OutputDir=output
OutputBaseFilename=ExplorerAlternative-v{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 既定は、管理者権限が要らない、現在のユーザー向けのインストール（%LocalAppData%\Programs）。
; 「すべてのユーザー」向けにする場合は、起動時の確認で選べる。
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; 起動中のアプリを、更新・削除の前に閉じるよう促す。
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "デスクトップにショートカットを作成する(&D)"; GroupDescription: "追加のショートカット:"; Flags: unchecked

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; アプリの設定画面（Explorer連携）が登録する、フォルダの右クリックメニュー「ExplorerAlternativeで開く」を、
; アンインストール時に削除する（インストール時には、何も作らない）。
Root: HKCU; Subkey: "Software\Classes\Directory\shell\ExplorerAlternative"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\ExplorerAlternative"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Drive\shell\ExplorerAlternative"; Flags: dontcreatekey uninsdeletekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "{#AppName}を起動する(&L)"; Flags: nowait postinstall skipifsilent

; 設定・お気に入り・ワークスペース（%AppData%\ExplorerAlternative\settings.json）は、利用者のデータなので、
; アンインストールしても削除しない（再インストール後に引き継げるようにするため）。
