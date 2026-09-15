; Epic Pencil - instalador (BASE, Inno Setup 6+).
;
; Compilar DEPOIS do publish (gera dist\win-x64\EpicPencil-<versao>\):
;   1. powershell -ExecutionPolicy Bypass -File scripts\publish-win-x64.ps1
;   2. iscc installer\inno\setup.iss
; Saida: installer\output\EpicPencil-Setup-<versao>.exe
;
; Decisoes (ver docs/DISTRIBUTION.md):
; - Instalacao por maquina em {autopf}\Epic Pencil (exige admin).
; - Atualizacao = reinstalar por cima; %LocalAppData%\EpicPencil (logs e
;   futuros settings) NUNCA e tocado - nem na atualizacao, nem ao desinstalar.
; - Sem autostart (o app nao tem tray/servico nesta versao).
; - TODO antes do instalador final: adicionar assets\epic-pencil.ico e
;   descomentar a linha SetupIconFile abaixo.
;
; NOTA: arquivo intencionalmente so com ASCII (sem acentos). O Inno Setup le
; .iss sem BOM como ANSI e caracteres UTF-8 sairiam com mojibake na UI.

#define AppVersion "0.1.0"
#define AppName "Epic Pencil"
#define AppExe "EpicPencil.Shell.exe"
#define Publisher "Epic Pencil"

[Setup]
AppId={{A7B4C2D1-8E3F-4A5B-9C6D-1F2A3B4C5D6E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\Epic Pencil
DefaultGroupName={#AppName}
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809 (build 17763) = minimo suportado pelo .NET 9 WPF.
MinVersion=10.0.17763
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir=..\output
OutputBaseFilename=EpicPencil-Setup-{#AppVersion}
UninstallDisplayIcon={app}\{#AppExe}
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=no
;SetupIconFile=..\..\assets\epic-pencil.ico

[Languages]
Name: "portuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na area de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Files]
; Fonte: saida do scripts\publish-win-x64.ps1 (nunca o bin\ do build local).
Source: "..\..\dist\win-x64\EpicPencil-{#AppVersion}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Iniciar o {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Intencionalmente VAZIO: dados do usuario em %LocalAppData%\EpicPencil
; (logs, futuros settings) sao preservados. Instalacao limpa = desinstalar
; e apagar %LocalAppData%\EpicPencil manualmente.
