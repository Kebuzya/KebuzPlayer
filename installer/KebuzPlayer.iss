#define AppName "KebuzPlayer"
#define AppPublisher "Kebuzya"
#define FolderName "Kebuz"
#define AppExeName "KebuzPlayer.exe"
#define AppUrl "https://github.com/Kebuzya/KebuzPlayer"
#define RuntimeUrl "https://aka.ms/dotnet/8.0/dotnet-runtime-win-x64.exe"
#define RuntimeFile "dotnet-runtime-win-x64.exe"

#ifndef AppVersion
  #error AppVersion is not defined, run build-release.ps1
#endif
#ifndef SourceDir
  #error SourceDir is not defined, run build-release.ps1
#endif
#ifndef OutputDir
  #define OutputDir "..\..\release\" + AppVersion
#endif

[Setup]
AppId={{A9C95511-F827-4956-8B4A-02DCEA1F3330}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#FolderName}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AppMutex=KebuzPlayer.SingleInstance
CloseApplications=no
LicenseFile=..\LICENSE
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage
OutputDir={#OutputDir}
OutputBaseFilename=KebuzPlayer-Setup-win-x64
SetupIconFile=..\KebuzPlayer\Assets\KebuzPlayer_icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
english.StartMenuIcon=Create a Start menu shortcut
russian.StartMenuIcon=Ярлык в меню «Пуск»
english.Shortcuts=Shortcuts:
russian.Shortcuts=Ярлыки:
english.Integration=Windows integration:
russian.Integration=Интеграция с Windows:
english.FileAssoc=Open audio files in KebuzPlayer
russian.FileAssoc=Открывать аудиофайлы в KebuzPlayer
english.RuntimeCaption=.NET 8 Runtime
russian.RuntimeCaption=.NET 8 Runtime
english.RuntimeDesc=KebuzPlayer needs a runtime that is not installed yet
russian.RuntimeDesc=Плееру нужна среда выполнения, её пока нет на компьютере
english.RuntimeText=KebuzPlayer runs on .NET 8 Runtime, and it is not installed on this computer.%n%nSetup will download it from Microsoft (about 30 MB) and install it before the player.%n%nThe runtime is installed for all users, so Windows will ask for administrator permission (UAC). If you decline, the player is still installed, but it will not start until the runtime is installed.
russian.RuntimeText=KebuzPlayer работает на .NET 8 Runtime, а на этом компьютере её нет.%n%nУстановщик скачает её с сайта Microsoft (около 30 МБ) и установит перед плеером.%n%nСреда выполнения ставится для всех пользователей, поэтому Windows попросит разрешение администратора (UAC). Если отказаться, плеер всё равно установится, но не запустится, пока среда выполнения не установлена.
english.RuntimeDownloadFailed=Could not download .NET 8 Runtime:%n%1%n%nContinue without it? The player will not start until the runtime is installed from https://dotnet.microsoft.com/download/dotnet/8.0
russian.RuntimeDownloadFailed=Не удалось скачать .NET 8 Runtime:%n%1%n%nПродолжить без неё? Плеер не запустится, пока среда выполнения не установлена с https://dotnet.microsoft.com/download/dotnet/8.0
english.RuntimeInstallFailed=.NET 8 Runtime was not installed (code %1). The player will not start until it is installed from https://dotnet.microsoft.com/download/dotnet/8.0
russian.RuntimeInstallFailed=.NET 8 Runtime не установлена (код %1). Плеер не запустится, пока её не установить с https://dotnet.microsoft.com/download/dotnet/8.0
english.RuntimeInstalling=Installing .NET 8 Runtime...
russian.RuntimeInstalling=Установка .NET 8 Runtime...
english.RemoveData=Also delete the library and settings?%n%nPlay counts, favourites, playlists, generated covers and settings will be lost. Your music files are not touched.
russian.RemoveData=Удалить также библиотеку и настройки?%n%nСчётчики прослушиваний, избранное, плейлисты, сгенерированные обложки и настройки пропадут. Сами музыкальные файлы не затрагиваются.

[Tasks]
Name: "startmenuicon"; Description: "{cm:StartMenuIcon}"; GroupDescription: "{cm:Shortcuts}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:Shortcuts}"; Flags: unchecked
Name: "fileassoc"; Description: "{cm:FileAssoc}"; GroupDescription: "{cm:Integration}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: startmenuicon
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Parameters: "--register-default-app"; Tasks: fileassoc; Check: RuntimePresent; Flags: runhidden waituntilterminated runasoriginaluser
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Check: RuntimePresent; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{app}\{#AppExeName}"; Parameters: "--unregister-default-app"; RunOnceId: "UnregisterDefaultApp"; Flags: runhidden waituntilterminated skipifdoesntexist

[Code]
var
  RuntimePage: TOutputMsgWizardPage;
  DownloadPage: TDownloadWizardPage;
  RuntimeMissingAtStart: Boolean;

function RuntimePresent: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.NETCore.App\8.*'), FindRec);
  if Result then
    FindClose(FindRec);
end;

procedure InitializeWizard;
begin
  RuntimeMissingAtStart := not RuntimePresent;
  RuntimePage := CreateOutputMsgPage(wpSelectTasks, CustomMessage('RuntimeCaption'), CustomMessage('RuntimeDesc'), CustomMessage('RuntimeText'));
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = RuntimePage.ID) and not RuntimeMissingAtStart;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID <> wpReady) or RuntimePresent then
    Exit;
  DownloadPage.Clear;
  DownloadPage.Add('{#RuntimeUrl}', '{#RuntimeFile}', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      if DownloadPage.AbortedByUser then
        Result := False
      else
        Result := SuppressibleMsgBox(FmtMessage(CustomMessage('RuntimeDownloadFailed'), [GetExceptionMessage]), mbError, MB_YESNO, IDNO) = IDYES;
    end;
  finally
    DownloadPage.Hide;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Installer: String;
  Code: Integer;
begin
  Result := '';
  Installer := ExpandConstant('{tmp}\{#RuntimeFile}');
  if RuntimePresent or not FileExists(Installer) then
    Exit;
  WizardForm.PreparingLabel.Caption := CustomMessage('RuntimeInstalling');
  WizardForm.PreparingLabel.Visible := True;
  if not ShellExec('', Installer, '/install /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, Code) then
    SuppressibleMsgBox(FmtMessage(CustomMessage('RuntimeInstallFailed'), [IntToStr(Code)]), mbError, MB_OK, IDOK)
  else if (Code = 3010) or (Code = 1641) then
    NeedsRestart := True
  else if Code <> 0 then
    SuppressibleMsgBox(FmtMessage(CustomMessage('RuntimeInstallFailed'), [IntToStr(Code)]), mbError, MB_OK, IDOK);
end;

procedure RemoveUserData;
begin
  DelTree(ExpandConstant('{localappdata}\{#FolderName}\{#AppName}'), True, True, True);
  DelTree(ExpandConstant('{userappdata}\{#FolderName}\{#AppName}'), True, True, True);
  RemoveDir(ExpandConstant('{localappdata}\{#FolderName}'));
  RemoveDir(ExpandConstant('{userappdata}\{#FolderName}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox(CustomMessage('RemoveData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      RemoveUserData;
end;
