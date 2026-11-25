<#
.SYNOPSIS
  Turns Ball Out War concept art into 3D models with the Meshy API, and saves them into the Unity project.

.DESCRIPTION
  Reads Tools/Meshy/jobs.json. For each job it sends the job's images to Meshy (image-to-3d for one image,
  multi-image-to-3d for 2-4), waits for the model, optionally rigs it, and downloads the results to
  Assets/Art/Models/<job>/. Progress is saved in Tools/Meshy/tasks/<job>.json, so running again resumes
  finished or in-flight jobs instead of paying for them twice. Delete that file to regenerate a job.

  Needs MESHY_API_KEY in the environment (create a key at https://www.meshy.ai/developers/keys).

.EXAMPLE
  .\Meshy.ps1 -List                 # show jobs and their state
  .\Meshy.ps1 -Job bolt -DryRun     # show the request without sending it
  .\Meshy.ps1 -Job bolt             # generate one job
  .\Meshy.ps1 -All                  # generate every job (asks before spending credits)
  .\Meshy.ps1 -All -Yes             # ... without asking
#>
param(
    [string[]] $Job,
    [switch] $All,
    [switch] $List,
    [switch] $DryRun,
    [switch] $NoRig,
    [switch] $Yes,
    [int] $PollSeconds = 15
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Api = 'https://api.meshy.ai/openapi/v1'
$TaskDir = Join-Path $PSScriptRoot 'tasks'
$ModelDir = Join-Path $Root 'Assets\Art\Models'
$Config = Get-Content (Join-Path $PSScriptRoot 'jobs.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force $TaskDir | Out-Null

# Credits per Meshy's pricing page (textured model 30, rigging 5).
$ModelCredits = 30
$RigCredits = 5

function Get-State($name) {
    $path = Join-Path $TaskDir "$name.json"
    if (Test-Path $path) { return Get-Content $path -Raw | ConvertFrom-Json }
    return [pscustomobject]@{ name = $name; endpoint = $null; taskId = $null; status = 'NOT STARTED'; rigTaskId = $null; rigStatus = $null; downloaded = $false }
}

function Save-State($state) {
    $state | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $TaskDir "$($state.name).json") -Encoding utf8
}

function Get-ApiKey {
    $key = $env:MESHY_API_KEY
    if (-not $key) { $key = [Environment]::GetEnvironmentVariable('MESHY_API_KEY', 'User') }
    if (-not $key) { throw 'MESHY_API_KEY is not set. Create a key at https://www.meshy.ai/developers/keys, then: setx MESHY_API_KEY "<your key>" and open a new terminal.' }
    return $key
}

function Invoke-Meshy([string] $Method, [string] $Path, $Body) {
    $headers = @{ Authorization = "Bearer $(Get-ApiKey)" }
    for ($attempt = 1; ; $attempt++) {
        try {
            if ($Body) {
                $json = $Body | ConvertTo-Json -Depth 6 -Compress
                return Invoke-RestMethod -Method $Method -Uri "$Api/$Path" -Headers $headers -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($json))
            }
            return Invoke-RestMethod -Method $Method -Uri "$Api/$Path" -Headers $headers
        }
        catch {
            $status = $null
            if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
            $detail = $_.ErrorDetails.Message
            if ($status -eq 429 -and $attempt -le 40) {
                Write-Host "  Meshy is busy (429: $detail); waiting 30 s..." -ForegroundColor Yellow
                Start-Sleep -Seconds 30
                continue
            }
            if ($status -ge 500 -and $attempt -le 3) { Start-Sleep -Seconds 10; continue }
            throw "Meshy $Method $Path failed ($status): $detail"
        }
    }
}

function ConvertTo-DataUri([string] $relativePath) {
    $path = Join-Path $Root $relativePath
    if (-not (Test-Path $path)) { throw "Image not found: $relativePath" }
    $ext = [IO.Path]::GetExtension($path).TrimStart('.').ToLower()
    if ($ext -eq 'jpg') { $ext = 'jpeg' }
    return "data:image/$ext;base64," + [Convert]::ToBase64String([IO.File]::ReadAllBytes($path))
}

function New-Request($job) {
    $body = [ordered]@{}
    foreach ($p in $Config.defaults.PSObject.Properties) { $body[$p.Name] = $p.Value }
    if ($job.options) { foreach ($p in $job.options.PSObject.Properties) { $body[$p.Name] = $p.Value } }
    if ($job.images.Count -eq 1) { $endpoint = 'image-to-3d'; $body['image_url'] = ConvertTo-DataUri $job.images[0] }
    elseif ($job.images.Count -le 4) { $endpoint = 'multi-image-to-3d'; $body['image_urls'] = @($job.images | ForEach-Object { ConvertTo-DataUri $_ }) }
    else { throw "$($job.name): Meshy takes at most 4 images." }
    return @{ endpoint = $endpoint; body = $body }
}

function Save-Url([string] $url, [string] $path) {
    if (-not $url) { return }
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    Invoke-WebRequest -Uri $url -OutFile $path -UseBasicParsing
    Write-Host "  saved $($path.Substring($Root.Length + 1))"
}

function Save-Results($job, $task, $rig) {
    $dir = Join-Path $ModelDir $job.name
    $urls = $task.model_urls
    if ($urls.fbx) { Save-Url $urls.fbx (Join-Path $dir "$($job.name).fbx") }
    if ($urls.glb) { Save-Url $urls.glb (Join-Path $dir "$($job.name).glb") }
    if ($task.texture_urls) {
        $t = @($task.texture_urls)[0]
        foreach ($map in 'base_color', 'metallic', 'normal', 'roughness', 'emission') {
            if ($t.$map) { Save-Url $t.$map (Join-Path $dir "$($job.name)_$map.png") }
        }
    }
    if ($task.thumbnail_url) { Save-Url $task.thumbnail_url (Join-Path $Root "Docs\Concepts\models\$($job.name).png") }
    if ($rig -and $rig.result) {
        Save-Url $rig.result.rigged_character_fbx_url (Join-Path $dir "$($job.name)_rigged.fbx")
        if ($rig.result.basic_animations) {
            Save-Url $rig.result.basic_animations.walking_fbx_url (Join-Path $dir "$($job.name)_walking.fbx")
            Save-Url $rig.result.basic_animations.running_fbx_url (Join-Path $dir "$($job.name)_running.fbx")
        }
    }
}

# ---- Choose jobs ----
$selected = @($Config.jobs)
if ($Job) { $Job = @($Job | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
if ($Job) { $selected = @($Config.jobs | Where-Object { $Job -contains $_.name }) }
if ($List -or (-not $All -and -not $Job)) {
    foreach ($j in $Config.jobs) {
        $s = Get-State $j.name
        $rig = if ($j.rig) { " rig:$($s.rigStatus)" } else { '' }
        "{0,-14} {1,-9} {2} image(s)  {3}{4}{5}" -f $j.name, $j.kind, $j.images.Count, $s.status, $rig, $(if ($s.downloaded) { '  (downloaded)' } else { '' })
    }
    if (-not $List) { "`nUse -Job <name>, -All, or -DryRun. See Get-Help .\Meshy.ps1 -Detailed." }
    return
}
if ($selected.Count -eq 0) { throw "No job named $($Job -join ', ') in jobs.json." }

if ($DryRun) {
    foreach ($j in $selected) {
        $r = New-Request $j
        $shown = [ordered]@{}
        foreach ($k in $r.body.Keys) { $shown[$k] = if ($k -like 'image_url*') { '<' + @($r.body[$k]).Count + ' image(s) as data URI>' } else { $r.body[$k] } }
        "POST $Api/$($r.endpoint)  ($($j.name))"
        $shown | ConvertTo-Json -Depth 4
    }
    return
}

# ---- Estimate and confirm ----
$toCreate = @($selected | Where-Object { -not (Get-State $_.name).taskId })
$toRig = @($selected | Where-Object { $_.rig -and -not $NoRig -and -not (Get-State $_.name).rigTaskId })
$credits = $toCreate.Count * $ModelCredits + $toRig.Count * $RigCredits
if ($credits -gt 0 -and -not $Yes) {
    Write-Host "This will start $($toCreate.Count) model(s) and $($toRig.Count) rig(s): about $credits Meshy credits."
    if ((Read-Host 'Continue? (y/n)') -ne 'y') { return }
}

# ---- Submit ----
foreach ($j in $toCreate) {
    $r = New-Request $j
    $res = Invoke-Meshy POST $r.endpoint $r.body
    $s = Get-State $j.name
    $s.endpoint = $r.endpoint; $s.taskId = $res.result; $s.status = 'PENDING'; $s.downloaded = $false
    Save-State $s
    Write-Host "$($j.name): started $($r.endpoint) task $($res.result)"
}

# ---- Poll until everything is done ----
$pending = [System.Collections.ArrayList]@($selected)
while ($pending.Count -gt 0) {
    foreach ($j in @($pending)) {
        $s = Get-State $j.name
        if (-not $s.taskId) { $pending.Remove($j); continue }
        $task = Invoke-Meshy GET "$($s.endpoint)/$($s.taskId)"
        $s.status = $task.status
        Save-State $s
        if ($task.status -in 'FAILED', 'CANCELED') {
            Write-Host "$($j.name): $($task.status) $($task.task_error | ConvertTo-Json -Compress). Delete tasks\$($j.name).json to retry." -ForegroundColor Red
            $pending.Remove($j); continue
        }
        if ($task.status -ne 'SUCCEEDED') { Write-Host "$($j.name): $($task.status) $($task.progress)%"; continue }

        $rig = $null
        if ($j.rig -and -not $NoRig) {
            if (-not $s.rigTaskId) {
                $res = Invoke-Meshy POST 'rigging' @{ input_task_id = $s.taskId; height_meters = 1.0 }
                $s.rigTaskId = $res.result; $s.rigStatus = 'PENDING'; Save-State $s
                Write-Host "$($j.name): model ready, rigging started"
                continue
            }
            $rig = Invoke-Meshy GET "rigging/$($s.rigTaskId)"
            $s.rigStatus = $rig.status; Save-State $s
            if ($rig.status -in 'PENDING', 'IN_PROGRESS') { Write-Host "$($j.name): rigging $($rig.progress)%"; continue }
            if ($rig.status -ne 'SUCCEEDED') {
                Write-Host "$($j.name): rigging $($rig.status) $($rig.task_error | ConvertTo-Json -Compress); saving the unrigged model." -ForegroundColor Yellow
                $rig = $null
            }
        }
        if (-not $s.downloaded) {
            Save-Results $j $task $rig
            $s.downloaded = $true; Save-State $s
        }
        Write-Host "$($j.name): done" -ForegroundColor Green
        $pending.Remove($j)
    }
    if ($pending.Count -gt 0) { Start-Sleep -Seconds $PollSeconds }
}
Write-Host "`nModels are in Assets\Art\Models. Preview images are in Docs\Concepts\models."
