[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    # Optional local checkout of https://github.com/NVIDIA/nvapi (same commit as
    # native/CMakeLists.txt). Without it CMake downloads NVAPI on the first build.
    [string] $NvapiSource = ''
)

# Builds the native NVAPI helper and the .NET Framework 4.8 EXE, then packs
# release\SMITE2-Patcher-<version>.zip.
# Needs: Visual Studio C++ build tools, CMake 3.24+, .NET SDK 8 or newer.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$nativeBuild = Join-Path $artifacts 'native-build'
$nativeOutput = Join-Path $artifacts 'native'
$managedOutput = Join-Path $artifacts 'build'
$stage = Join-Path $artifacts 'package'
$project = Join-Path $root 'src\Smite2Patcher.csproj'
$version = ([xml](Get-Content -LiteralPath $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

$cmakeArguments = @('-S', (Join-Path $root 'native'), '-B', $nativeBuild, '-A', 'x64', "-DCMAKE_INSTALL_PREFIX=$nativeOutput")
if ($NvapiSource) {
    $cmakeArguments += "-DFETCHCONTENT_SOURCE_DIR_NVAPI=$NvapiSource"
}
cmake @cmakeArguments
if ($LASTEXITCODE -ne 0) { throw 'CMake configuration failed.' }
cmake --build $nativeBuild --config $Configuration --target install
if ($LASTEXITCODE -ne 0) { throw 'Native helper build failed.' }

dotnet build $project -c $Configuration -o $managedOutput
if ($LASTEXITCODE -ne 0) { throw 'Patcher build failed.' }

# Flat layout: PowerShell 5 writes '\' into ZIP folder names, which some unzip tools reject.
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $managedOutput 'SMITE2-Patcher.exe'), (Join-Path $managedOutput 'SMITE2-Patcher.exe.config') -Destination $stage
Copy-Item -LiteralPath (Join-Path $nativeOutput 'SmitePatchNv.dll') -Destination $stage
Copy-Item -LiteralPath (Join-Path $nativeOutput 'licenses\NVIDIA-NVAPI.txt') -Destination (Join-Path $stage 'LICENSE-NVIDIA-NVAPI.txt')
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $stage 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $root 'package\README.txt'), (Join-Path $root 'package\README-UA.txt') -Destination $stage

$release = Join-Path $root 'release'
New-Item -ItemType Directory -Path $release -Force | Out-Null
$zip = Join-Path $release "SMITE2-Patcher-$version.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
$item = Get-Item -LiteralPath $zip
Write-Host ("Packed {0} ({1:N0} KB)" -f $item.FullName, ($item.Length / 1KB))
Write-Host ("SHA-256 {0}" -f (Get-FileHash -LiteralPath $zip).Hash)
