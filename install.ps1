# Installs DelFast for the current user (no admin): builds, copies to
# %LOCALAPPDATA%\Programs\DelFast, adds it to the user PATH, adds an Explorer right-click
# entry and a Start Menu shortcut.
#   .\install.ps1              install or update
#   .\install.ps1 -Uninstall   remove all of it
param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\DelFast'
$menuKeys = 'HKCU:\Software\Classes\Directory\shell\DelFast', 'HKCU:\Software\Classes\*\shell\DelFast'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\DelFast.lnk'

function Set-UserPath([string[]]$entries) {
    # Registry API, not [Environment]: it keeps %VARS% in the existing PATH unexpanded.
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
    $key.SetValue('Path', ($entries -join ';'), [Microsoft.Win32.RegistryValueKind]::ExpandString)
    $key.Close()

    # Tell Explorer, or terminals it opens keep the old PATH until the next sign-in.
    Add-Type -Namespace DelFast -Name Win32 -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode)]
public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
'@
    $ignored = [UIntPtr]::Zero
    [DelFast.Win32]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [UIntPtr]::Zero, 'Environment', 2, 2000, [ref]$ignored) | Out-Null
}

function Get-UserPath {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment')
    $value = $key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    $key.Close()
    return @($value -split ';' | Where-Object { $_ })
}

if ($Uninstall) {
    foreach ($k in $menuKeys) { if (Test-Path -LiteralPath $k) { Remove-Item -LiteralPath $k -Recurse } }
    if (Test-Path $shortcut) { Remove-Item $shortcut }
    Set-UserPath @(Get-UserPath | Where-Object { $_ -ne $dest })
    if (Test-Path $dest) { Remove-Item $dest -Recurse }
    Write-Host 'DelFast removed.'
    return
}

$repo = $PSScriptRoot
# The native linker step locates Visual Studio through vswhere, which is not on PATH by default.
$env:Path += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
$out = Join-Path $repo 'bin\install'
if (Test-Path $out) { Remove-Item $out -Recurse }

dotnet publish (Join-Path $repo 'Cli\DelFast.Cli.csproj') -c Release -o $out -v q
if ($LASTEXITCODE) { throw 'CLI publish failed' }
dotnet publish (Join-Path $repo 'Context Menu\DelFastContextMenu.csproj') -c Release -o $out -v q
if ($LASTEXITCODE) { throw 'context menu publish failed' }
dotnet publish (Join-Path $repo 'DelFast.csproj') -c Release -o (Join-Path $out 'gui') -r win-x64 --self-contained false -p:PublishReadyToRun=true -v q
if ($LASTEXITCODE) { throw 'GUI publish failed' }

New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $out 'dfast.exe'), (Join-Path $out 'DelFastContextMenu.exe') $dest -Force
Copy-Item (Join-Path $out 'gui\*') $dest -Force -Exclude '*.pdb'

$paths = Get-UserPath
if ($paths -notcontains $dest) { Set-UserPath ($paths + $dest) }

$menuExe = Join-Path $dest 'DelFastContextMenu.exe'
foreach ($k in $menuKeys) {
    New-Item -Path $k -Force | Out-Null
    Set-ItemProperty -LiteralPath $k -Name '(default)' -Value 'Delete permanently (DelFast)'
    New-Item -Path "$k\command" -Force | Out-Null
    Set-ItemProperty -LiteralPath "$k\command" -Name '(default)' -Value "`"$menuExe`" `"%1`""
}

$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($shortcut)
$lnk.TargetPath = Join-Path $dest 'DelFast.exe'
$lnk.WorkingDirectory = $dest
$lnk.Save()

Write-Host "Installed to $dest. Open a new terminal to use dfast."
