[CmdletBinding()]
Param (
    [Parameter(Mandatory=$true)][string] $yamato_sign_project_id,
    [Parameter(Mandatory=$true)][string] $yamato_sign_project_branch_name,
    [Parameter(Mandatory=$true)][string] $yamato_sign_project_revision,
    [Parameter(Mandatory=$true)][string] $yamato_sign_project_name,
    [Parameter(Mandatory=$true)][string] $yamato_token
)

dir $env

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Yamato API
Set-Variable yamato_api_uri -option Constant -value "https://yamato-api.prd.cds.internal.unity3d.com"
Set-Variable yamato_uri -option Constant -value "https://yamato.cds.internal.unity3d.com"
Set-Variable yamato_api_header -option Constant -value @{"Authorization" = "ApiKey $yamato_token"}

Write-Host "Looking for build job..."
$job_build = Get-ChildItem -Path .\ -Filter job-build.json -Recurse | Select-Object -First 1
$job_build_json = Get-Content $job_build.Fullname | ConvertFrom-Json
Write-Host "Found $($job_build_json.project) build job at $yamato_uri/job/$($job_build_json.id)"

# Queue a new remote signing job
Write-Host "Queuing a new signing job for $($job_build_json.project):$($job_build_json.id) at $yamato_uri/jobs/$yamato_sign_project_id"
$request = @{
    source = @{
        branchname=$yamato_sign_project_branch_name
        revision=$yamato_sign_project_revision
    };
    rebuild = "minimal";
    links = @{
        project = "/projects/$yamato_sign_project_id";
        jobDefinition = "/projects/$yamato_sign_project_id/revisions/$yamato_sign_project_revision/job-definitions/.yamato%2Fyamato-config.yml%23sign-windows-job"
    }
    environmentVariables = @(
        @{ key="JobIdToSign"; value=$job_build_json.id }
        @{ key="Project"; value="ILRepack" }
    )
} | ConvertTo-Json
$response = Invoke-WebRequest -UseBasicParsing -Method POST -Headers $yamato_api_header -Uri "$yamato_api_uri/jobs" -body $request
$json = $response.Content | ConvertFrom-Json
$job_uri = "$yamato_uri/job/$($json.id)"
if ($response.StatusCode -ne 200) {
    throw "Could not create sign job: $response"
} else {
    Write-Host "Created $job_uri"
}

# Wait for it!
while ($json.status -ne "success") {
    Write-Host "Waiting for 60 sec for status: $($json.status)"
    Start-Sleep 60
    $response = Invoke-WebRequest -UseBasicParsing -Method GET -Headers $yamato_api_header -Uri "$yamato_api_uri/jobs/$($json.id)"
    $json = $response.Content | ConvertFrom-Json
    
    if ($response.StatusCode -ne 200) {
        throw "Sign job($job_uri) failed: $response"
    } elseif ($json.status -eq "failed" -or $json.status -eq "cancelled" -or $json.status -eq "disrupted") {
        Write-Host " [${cross_mark_emoji}]"
        throw "Sign job($job_uri) failed: $($json.status)"
    }
}
Write-Host "Job $job_uri completed successfully"

# Good, download the signed executable
Write-Host "Downloading $($json.artifacts.ziplink) and repacking signed artifact from $($json.artifacts.name)"
Invoke-WebRequest -UseBasicParsing -Method GET -Headers $yamato_api_header -Uri $json.artifacts.ziplink -out $json.artifacts.name
Expand-Archive $json.artifacts.name -Force
$unzipped_dir = [io.path]::GetFileNameWithoutExtension($json.artifacts.name)

$artifacts_dir = mkdir ".\Build\Release" -Force
Write-Host "Preparing Artifacts in $artifacts_dir..."
$ilrepack = Get-ChildItem -Path $unzipped_dir -Filter ILRepack.exe -Recurse | Select-Object -First 1

Write-Host "Found ILRepack in $($ilrepack.Fullname) copying into $artifacts_dir..." 
Copy-Item $ilrepack.Fullname $artifacts_dir -Force

Write-Host "Recording job:$($env:YAMATO_JOB_ID) into $artifacts_dir..."
@{
    "id" = $env:YAMATO_JOB_ID
    "project" = $env:YAMATO_PROJECT_NAME
} | ConvertTo-Json | Out-File -FilePath "$artifacts_dir\sign-build.json" -Force
