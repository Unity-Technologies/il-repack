[CmdletBinding()]
Param ()

dir $env

$ErrorActionPreference = 'Stop'

Write-Host "Restoring nuget packages..."
nuget restore
if ($LASTEXITCODE -gt 0) { 
    throw "Could not restore nuget packages"
}

Write-Host "Building ILRepack..."
$env:DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet msbuild .\ILRepack.sln -t:ILRepack -p:Configuration=Release -m
if ($LASTEXITCODE -gt 0) { 
    throw "Could not build ILRepack"
}

$artifacts_dir = mkdir ".\Build\Release" -Force
$target_exe = Join-Path $artifacts_dir "ILRepack.exe" 
$target_dll = Join-Path $artifacts_dir "ILRepack.dll"
Write-Host "Preparing Artifacts in $artifacts_dir..."
$ilrepacksnk = Get-ChildItem -Path .\ILRepack\ -Filter ILRepack.snk -Recurse  | Resolve-Path -Relative | Select-Object -First 1 
$ilrepack = Get-ChildItem -Path .\ILRepack\bin\Release -Filter ILRepack.exe -Recurse | Resolve-Path -Relative | Select-Object -First 1
$repack_list = Get-ChildItem .\ILRepack\bin\Release -Include *.dll, *.exe -Recurse | Resolve-Path -Relative

Write-Host "Found ILRepack.exe in $($ilrepack.Directory.Fullname)"
Write-Host "Repacking everything into an executable $target_exe..."
& "$ilrepack" /log /wildcards /internalize /ndebug /out:"$target_exe" /target:exe $repack_list
Write-Host "Repacking everything into a library $target_exe..."
& $ilrepack /log /wildcards /internalize /keyfile:"$ilrepacksnk" /out:"$target_dll" /target:library $repack_list

Write-Host "Recording job:$($env:YAMATO_JOB_ID) into $artifacts_dir..."
@{
    "id" = $env:YAMATO_JOB_ID
    "project" = $env:YAMATO_PROJECT_NAME
} | ConvertTo-Json | Out-File -FilePath "$artifacts_dir\job-build.json" -Force

