<#
  Builds the AuroraPad release files into .\artifacts:
    - artifacts\installer\AuroraPad-<version>-win-x64.msi   (installer)
    - artifacts\AuroraPad-<version>-win-x64-portable.zip    (portable, single exe)
  Usage:  .\build-installer.ps1            (version from the app project)
          .\build-installer.ps1 -Version 1.3.0
  Requires the .NET 9 SDK. WiX comes from NuGet automatically.
#>
param([string]$Version)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

if (-not $Version) {
    $Version = ([xml](Get-Content "$root\src\AuroraPad.App\AuroraPad.App.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
Write-Host "AuroraPad $Version"

$app = "$root\artifacts\app"
Remove-Item $app -Recurse -Force -ErrorAction SilentlyContinue

# 1. Self-contained single-file exe (no .NET install needed on the user's PC).
dotnet publish "$root\src\AuroraPad.App" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:Version=$Version -o $app
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# 2. MSI installer.
dotnet build "$root\installer" -c Release -p:AppVersion=$Version -p:AppDir=$app
if ($LASTEXITCODE -ne 0) { throw "installer build failed" }

# 3. Portable zip.
$zip = "$root\artifacts\AuroraPad-$Version-win-x64-portable.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path "$app\AuroraPad.exe" -DestinationPath $zip

Get-ChildItem "$root\artifacts\installer\*.msi", $zip | ForEach-Object { Write-Host ("  {0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) }
