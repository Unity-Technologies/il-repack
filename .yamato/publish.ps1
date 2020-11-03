[CmdletBinding()]
Param (
    [Parameter(Mandatory=$true)][string] $stevedore_repository,
    [Parameter(Mandatory=$true)][string] $stevedore_token,
    [Parameter(Mandatory=$true)][string] $ilrepack_version
)

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

Set-Variable stevedore_api_uri -option Constant -value "https://stevedore-upload.ds.unity3d.com"
Set-Variable stevedore_api_header -option Constant -value @{"Authorization" = "Bearer $stevedore_token"}

Write-Host "==================================================================="
dir env:
Write-Host "==================================================================="
ls -Recurse | Format-Table Fullname

# Package for stevedore
Write-Host "Preparing publishing package..."
$artifacts_dir = mkdir ".\Build\Release" -Force
$dest_dir = Join-Path $(Resolve-Path .\) "publish.zip"
Copy-Item .\LICENSE $artifacts_dir -Force
Remove-Item "$artifacts_dir\*.json"
# Compress-Archive -Path $artifacts_dir -DestinationPath .\publish.zip -Force
Add-Type -Assembly "System.IO.Compression.FileSystem" ;
[System.IO.Compression.ZipFile]::CreateFromDirectory($artifacts_dir, $dest_dir);

$filehash = Get-FileHash -Algorithm SHA256 .\publish.zip
$stevedore_artifactid = "ilrepack/$($ilrepack_version)_$($filehash.Hash).zip".ToLowerInvariant()

# Upload
$uri = "$stevedore_api_uri/upload/r/$stevedore_repository/$stevedore_artifactid"
Write-Host "Uploading to stevedore ($uri)"
$response = Invoke-WebRequest -UseBasicParsing `
    -Method POST -Headers $stevedore_api_header -ContentType "multipart/form-data" `
    -Uri $uri `
    -InFile .\publish.zip

$response.Content
if ($response.StatusCode -ne 200) {
    throw "Could not upload to stevedore"
}
exit 0