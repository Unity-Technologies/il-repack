[CmdletBinding()]
Param (
    [Parameter(Mandatory=$true)][string] $stevedore_repository,
    [Parameter(Mandatory=$true)][string] $stevedore_upload_tool_url,
    [Parameter(Mandatory=$true)][string] $ilrepack_version
)

# For debugging
rmdir ".\artifacts\ilrepack\" -Force -Recurse -ErrorAction Ignore
rm publish.zip -Force -ErrorAction Ignore
rm StevedoreUpload.exe -Force -ErrorAction Ignore

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Package for stevedore
Write-Host "Preparing publishing package..."
$artifacts_dir = mkdir ".\Build\Release" -Force
$ilrepack_dir = mkdir ".\artifacts\ilrepack\" -Force
$dest_dir = Join-Path $(Resolve-Path .\) "publish.zip"
Copy-Item ".\.yamato\README.md" $artifacts_dir -Force
Copy-Item ".\Third party notices.md" $artifacts_dir -Force
Copy-Item ".\LICENSE.md" $artifacts_dir -Force
Remove-Item "$artifacts_dir\*.json"
Add-Type -Assembly "System.IO.Compression.FileSystem";
[System.IO.Compression.ZipFile]::CreateFromDirectory($artifacts_dir, $dest_dir);

# Rename with Hash
$filehash = Get-FileHash -Algorithm SHA256 $dest_dir
$stevedore_artifactid = "$ilrepack_dir\$($ilrepack_version)_$($filehash.Hash).zip".ToLowerInvariant()
mv $dest_dir $stevedore_artifactid -Force

Invoke-WebRequest -Uri $stevedore_upload_tool_url -OutFile ./StevedoreUpload.exe
.\StevedoreUpload.exe --repo=$stevedore_repository --version=$ilrepack_version $stevedore_artifactid