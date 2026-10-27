param([string]$Dotnet = 'dotnet', [string]$InstallerCompiler = '')
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'src\Luma.csproj'
$publishPath = Join-Path $PSScriptRoot 'app'
& $Dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $publishPath
if ($LASTEXITCODE -ne 0) { throw 'Luma build failed.' }
Write-Output "Luma is ready at $publishPath\Luma.exe"
if ($InstallerCompiler) {
    & $InstallerCompiler (Join-Path $PSScriptRoot 'installer\Luma.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Luma installer build failed.' }
}
