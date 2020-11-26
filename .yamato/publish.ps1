[CmdletBinding()]
Param (
    [Parameter(Mandatory=$true)][string] $stevedore_repository,
    [Parameter(Mandatory=$true)][string] $stevedore_upload_tool_url,
    [Parameter(Mandatory=$true)][string] $ilrepack_version
)

# For debugging
rm ilrepack.zip -Force -ErrorAction Ignore
rm StevedoreUpload.exe -Force -ErrorAction Ignore

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Package for stevedore
Write-Host "Preparing publishing package..."
$artifacts_dir = mkdir ".\Build\Release" -Force
$dest_dir = Join-Path $(Resolve-Path .\) "ilrepack.zip"
Copy-Item ".\.yamato\README.md" $artifacts_dir -Force
Copy-Item ".\Third party notices.md" $artifacts_dir -Force
Copy-Item ".\LICENSE.md" $artifacts_dir -Force
Remove-Item "$artifacts_dir\*.json"
Add-Type -Assembly "System.IO.Compression.FileSystem";
[System.IO.Compression.ZipFile]::CreateFromDirectory($artifacts_dir, $dest_dir);

Write-Host "Uploading $dest_dir($ilrepack_version) to Stevedore $stevedore_repository repositry..."
Invoke-WebRequest -Uri $stevedore_upload_tool_url -OutFile ./StevedoreUpload.exe
.\StevedoreUpload.exe --repo=$stevedore_repository --version=$ilrepack_version $dest_dir