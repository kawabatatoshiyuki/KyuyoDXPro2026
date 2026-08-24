; 給与計算DXPro スタンドアロン版 Inno Setup スクリプト
; 事前に build-package.ps1 を実行して package\app と package\migrator を用意しておくこと。
; Inno Setup (https://jrsoftware.org/isinfo.php) でコンパイルすると setup 実行ファイルが生成される。

#define AppName "給与計算DXPro"
#define AppVersion "26.4.0.0"
#define AppYear "2026"
#define Publisher "周豊システム"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
; 既定は C:\syuhousystem\PayCalcDXPro\browser\<年度>。環境により管理者権限が要る場合は昇格を許可する。
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline dialog
DefaultDirName=C:\syuhousystem\PayCalcDXPro\browser\{#AppYear}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputBaseFilename=PayCalcDXPro_Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Files]
Source: "package\app\*";      DestDir: "{app}\app";      Flags: recursesubdirs ignoreversion
Source: "package\migrator\*"; DestDir: "{app}\migrator"; Flags: recursesubdirs ignoreversion
; マニュアル（PDF）。存在する場合のみ同梱する。
Source: "Documents\*";        DestDir: "{app}\Documents"; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist

[Dirs]
; 給与データ格納先（暗号化DB＋鍵）
Name: "{localappdata}\PayCalcDXPro\data"

[Icons]
Name: "{group}\{#AppName}";           Filename: "{app}\app\PayCalcDXPro.Web.exe"; WorkingDir: "{app}\app"
Name: "{autodesktop}\{#AppName}";     Filename: "{app}\app\PayCalcDXPro.Web.exe"; WorkingDir: "{app}\app"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "デスクトップにショートカットを作成する"; GroupDescription: "アイコン:"

[Run]
; 任意：インストール完了後にアプリを起動
Filename: "{app}\app\PayCalcDXPro.Web.exe"; Description: "{#AppName} を起動する"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; アプリ本体のみ削除。給与データ（{localappdata}\PayCalcDXPro\data）は残す。
Type: filesandordirs; Name: "{app}"
