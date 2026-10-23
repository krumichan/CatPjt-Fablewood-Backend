#requires -Version 7.4
[CmdletBinding()]
param(
    [ValidatePattern('^[a-z0-9][a-z0-9_.-]{0,62}$')][string]$ContainerName='fablewood-postgres',
    [ValidatePattern('^[a-z0-9][a-z0-9_.-]{0,62}$')][string]$VolumeName='fablewood-postgres-data',
    [ValidatePattern('^[a-z0-9][a-z0-9_-]{0,62}$')][string]$ComposeProject='fablewood-backend',
    [ValidateRange(1024,65535)][int]$DatabasePort=55438,
    [ValidateRange(1024,65535)][int]$ApiPort=5281,
    [ValidatePattern('^[a-z][a-z0-9_]{0,62}$')][string]$Schema='fablewood',
    [ValidatePattern('^[a-zA-Z0-9_-]{1,64}$')][string]$ServerId='local-dedicated',
    [ValidateRange(10,300)][int]$ReadyTimeoutSeconds=90,
    [switch]$Restart
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if (-not $IsWindows) { throw 'This launcher currently supports Windows with PowerShell 7.4+.' }
$projectRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$backendRoot=Join-Path $projectRoot 'Fablewood.Backend'
$backendExe=Join-Path $backendRoot 'bin\Release\net10.0\Fablewood.Backend.exe'
$backendDll=Join-Path $backendRoot 'bin\Release\net10.0\Fablewood.Backend.dll'
$composeFile=Join-Path $projectRoot 'compose.yaml'
$privateRoot=Join-Path $projectRoot 'Saved\Backend'
$credentialsPath=Join-Path $privateRoot 'backend-credentials.json'
$keyPath=Join-Path $privateRoot 'server-key.txt'
$serverConfig=Join-Path $privateRoot 'server.json'
$envFile=Join-Path $privateRoot '.env.local'
$statePath=Join-Path $privateRoot 'backend-state.json'
$baseUrl="http://127.0.0.1:$ApiPort"
$docker=(Get-Command docker.exe -ErrorAction Stop).Source
foreach ($required in @($backendExe,$backendDll,$composeFile)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required file is missing: $required. Build the backend in Release first; this launcher does not build it."
    }
}
function Write-BackendJson([string]$Path,$Value) {
    [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 8)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
}
function New-BackendSecret {
    $bytes=[byte[]]::new(32)
    $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return [BitConverter]::ToString($bytes).Replace('-','').ToLowerInvariant()
}
function Assert-LoopbackPortFree([int]$Port,[string]$Description) {
    $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$Port)
    try { $listener.Start() }
    catch { throw "$Description loopback port $Port is already occupied. No unrelated process was stopped." }
    finally { $listener.Stop() }
}
function Get-BackendHealth {
    try {
        $answer=Invoke-RestMethod -Uri "$baseUrl/health" -Method Get -TimeoutSec 3
        if ($answer.status -eq 'ok') { return $true }
    } catch { }
    return $false
}
function Get-OwnedBackendProcess($State) {
    foreach ($property in @('projectRoot','backendExe','backendPid','processStartedUtc','baseUrl')) {
        if ($null -eq $State.PSObject.Properties[$property]) { throw 'Backend ownership state is incomplete; no process was stopped.' }
    }
    if ([IO.Path]::GetFullPath([string]$State.projectRoot) -ne $projectRoot -or
        [IO.Path]::GetFullPath([string]$State.backendExe) -ne $backendExe -or [string]$State.baseUrl -ne $baseUrl) {
        throw 'Backend ownership state belongs to a different project or endpoint; no process was stopped.'
    }
    $owned=Get-Process -Id ([int]$State.backendPid) -ErrorAction SilentlyContinue
    if (-not $owned) { return $null }
    # PowerShell 7.6 may already deserialize ISO timestamps as DateTime; never round-trip them through a culture-formatted string.
    $recordedStart=$State.processStartedUtc
    if ($recordedStart -is [DateTimeOffset]) { $expectedUtc=$recordedStart.UtcDateTime }
    elseif ($recordedStart -is [DateTime]) { $expectedUtc=$recordedStart.ToUniversalTime() }
    else { $expectedUtc=[DateTime]::Parse([string]$recordedStart,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime() }
    if ([IO.Path]::GetFullPath($owned.Path) -ne $backendExe -or $owned.StartTime.ToUniversalTime().Ticks -ne $expectedUtc.Ticks) {
        throw 'The recorded backend PID no longer identifies the owned process; no process was stopped.'
    }
    return $owned
}
# Do not print docker inspect output: container environment includes its DB password.
$existingId=@(& $docker container ls -a --filter "name=^/$containerName$" --format '{{.ID}}' 2>$null)
if ($LASTEXITCODE -ne 0) { throw 'Docker Engine is unavailable.' }
$existingContainer=$null
if ($existingId.Count -gt 1) { throw 'Container identity is ambiguous; no container was changed.' }
if ($existingId.Count -eq 1 -and $existingId[0]) {
    $details=& $docker inspect ([string]$existingId[0]) 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the existing Fablewood container.' }
    $existingContainer=@($details | ConvertFrom-Json)[0]
    if ($existingContainer.Config.Labels.'com.docker.compose.project' -ne $composeProject -or
        $existingContainer.Config.Labels.'com.docker.compose.service' -ne 'postgres') {
        throw 'The requested container name is owned by another service. It was preserved.'
    }
    $mounts=@($existingContainer.Mounts | Where-Object { $_.Type -eq 'volume' -and $_.Name -eq $volumeName -and $_.Destination -eq '/var/lib/postgresql' })
    $bindings=@($existingContainer.HostConfig.PortBindings.'5432/tcp')
    if ($mounts.Count -ne 1 -or $bindings.Count -ne 1 -or $bindings[0].HostIp -ne '127.0.0.1' -or [int]$bindings[0].HostPort -ne $DatabasePort) {
        throw 'Existing PostgreSQL storage or loopback binding differs from this configuration. It was preserved.'
    }
}
$volumes=@(& $docker volume ls --format '{{.Name}}' 2>$null)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect Docker volumes.' }
if ($volumeName -in $volumes) {
    $details=& $docker volume inspect $volumeName 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the Fablewood volume.' }
    $volume=@($details | ConvertFrom-Json)[0]
    if ($volume.Labels.'com.docker.compose.project' -ne $composeProject) { throw 'Existing named volume belongs to another service and was preserved.' }
    if (-not (Test-Path -LiteralPath $credentialsPath)) { throw 'The database volume already exists but its local credentials are missing. Restore them; no password or volume was reset.' }
}
if (-not $existingContainer -or -not $existingContainer.State.Running) { Assert-LoopbackPortFree $DatabasePort 'PostgreSQL' }
$ownedProcess=$null
if (Test-Path -LiteralPath $statePath) {
    $oldState=Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $ownedProcess=Get-OwnedBackendProcess $oldState
    if ($ownedProcess -and -not $Restart) {
        foreach ($privateFile in @($credentialsPath,$keyPath,$serverConfig,$envFile)) {
            if (-not (Test-Path -LiteralPath $privateFile -PathType Leaf)) { throw 'The owned API is running but its private configuration is incomplete. Restore the original files; no process was changed.' }
        }
        if (-not (Get-BackendHealth)) { throw 'The owned API process is running but unhealthy. Use -Restart after checking its private logs.' }
        Write-Output ([pscustomobject]@{status='already-running';backendPid=$ownedProcess.Id;baseUrl=$baseUrl;stateFile=$statePath;serverConfig=$serverConfig})
        return
    }
}
if (-not $ownedProcess) { Assert-LoopbackPortFree $ApiPort 'Backend API' }
[IO.Directory]::CreateDirectory($privateRoot) | Out-Null
# exFAT cannot enforce per-user ACLs. Keep all live secrets in this Git-excluded folder.
function Test-BackendPrivateDirectoryAcl($Descriptor,[string[]]$AllowedSids) {
    if (-not $Descriptor.AreAccessRulesProtected -or $AllowedSids.Count -ne 3) { return $false }
    $rules=@($Descriptor.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))
    if ($rules.Count -ne 3) { return $false }
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $expectedInheritance=[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
    foreach ($rule in $rules) {
        $ruleSid=$rule.IdentityReference.Value
        if ($ruleSid -cnotin $AllowedSids -or -not $seen.Add($ruleSid) -or $rule.IsInherited -or
            $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            $rule.FileSystemRights -ne [Security.AccessControl.FileSystemRights]::FullControl -or
            $rule.InheritanceFlags -ne $expectedInheritance -or
            $rule.PropagationFlags -ne [Security.AccessControl.PropagationFlags]::None) { return $false }
    }
    return $seen.Count -eq 3
}
$aclProtected=$false
try {
    $drive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($privateRoot))
    if ($drive.DriveFormat -eq 'NTFS') {
        $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
        $allowedSids=@($sid.Value,'S-1-5-18','S-1-5-32-544')
        $acl=Get-Acl -LiteralPath $privateRoot
        $originalOwner=$acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
        $originalGroup=$acl.GetGroup([Security.Principal.SecurityIdentifier]).Value
        if (-not (Test-BackendPrivateDirectoryAcl $acl $allowedSids)) {
            $acl.SetAccessRuleProtection($true,$false)
            foreach ($rule in @($acl.GetAccessRules($true,$false,[Security.Principal.SecurityIdentifier]))) { $acl.RemoveAccessRuleSpecific($rule) }
            $inherit=[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
            foreach ($principal in @($sid,[Security.Principal.SecurityIdentifier]::new('S-1-5-18'),[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))) {
                $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($principal,'FullControl',$inherit,'None','Allow'))
            }
            Set-Acl -LiteralPath $privateRoot -AclObject $acl
        }
        $verifiedAcl=Get-Acl -LiteralPath $privateRoot
        if (-not (Test-BackendPrivateDirectoryAcl $verifiedAcl $allowedSids) -or
            $verifiedAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value -cne $originalOwner -or
            $verifiedAcl.GetGroup([Security.Principal.SecurityIdentifier]).Value -cne $originalGroup) { throw 'Private directory ACL verification failed.' }
        $aclProtected=$true
    }
} catch { throw 'Could not restrict the NTFS private configuration folder. No new credentials were written.' }
if (Test-Path -LiteralPath $credentialsPath) {
    $credentials=Get-Content -LiteralPath $credentialsPath -Raw | ConvertFrom-Json
    foreach ($name in @('databasePassword','databasePort','apiPort','schema','serverId','projectRoot')) {
        if ($null -eq $credentials.PSObject.Properties[$name]) { throw 'Stored credentials are incomplete; they were preserved.' }
    }
    if ([string]$credentials.databasePassword -notmatch '^[a-f0-9]{64}$' -or [int]$credentials.databasePort -ne $DatabasePort -or
        [int]$credentials.apiPort -ne $ApiPort -or [string]$credentials.schema -cne $Schema -or [string]$credentials.serverId -cne $ServerId -or
        [IO.Path]::GetFullPath([string]$credentials.projectRoot) -ne $projectRoot) {
        throw 'Stored credentials do not match the requested settings. They were preserved; no database was reset.'
    }
    foreach ($binding in @(@{name='containerName';value=$ContainerName;legacy='fablewood-postgres'},@{name='volumeName';value=$VolumeName;legacy='fablewood-postgres-data'},@{name='composeProject';value=$ComposeProject;legacy='fablewood-backend'})) {
        $property=$credentials.PSObject.Properties[$binding.name]
        if (($null -eq $property -and $binding.value -cne $binding.legacy) -or ($null -ne $property -and [string]$property.Value -cne $binding.value)) {
            throw 'Stored database instance identity differs from the requested instance. Existing storage was preserved.'
        }
    }
    if (-not (Test-Path -LiteralPath $keyPath -PathType Leaf)) { throw 'Server key is missing; restore the existing private key rather than replacing it.' }
    $serverKey=[IO.File]::ReadAllText($keyPath).Trim()
    if ($serverKey -notmatch '^[a-f0-9]{64}$') { throw 'Stored server key has an unexpected format and was preserved.' }
} else {
    if ($existingContainer -or (Test-Path -LiteralPath $keyPath) -or (Test-Path -LiteralPath $serverConfig) -or (Test-Path -LiteralPath $envFile)) {
        throw 'Partial private configuration or an existing database was found. Restore the complete set; no secret was replaced.'
    }
    $credentials=[ordered]@{version=2;projectRoot=$projectRoot;containerName=$ContainerName;volumeName=$VolumeName;composeProject=$ComposeProject;databasePassword=(New-BackendSecret);databasePort=$DatabasePort;apiPort=$ApiPort;schema=$Schema;serverId=$ServerId;serverKeyFile=$keyPath}
    $serverKey=New-BackendSecret
    [IO.File]::WriteAllText($keyPath,$serverKey+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
    Write-BackendJson $credentialsPath $credentials
}
# Minimal required copies: Docker receives only DB credentials; UE receives only its API key.
[IO.File]::WriteAllText($envFile,"FABLE_PG_PORT=$DatabasePort`nFABLE_PG_PASSWORD=$($credentials.databasePassword)`nFABLE_PG_CONTAINER=$ContainerName`nFABLE_PG_VOLUME=$VolumeName`nFABLE_COMPOSE_PROJECT=$ComposeProject`n",[Text.UTF8Encoding]::new($false))
Write-BackendJson $serverConfig ([ordered]@{url=$baseUrl;serverId=$ServerId;serverKey=$serverKey})
$serverKey=$null
$runId=(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$logRoot=Join-Path $privateRoot $runId
[IO.Directory]::CreateDirectory($logRoot) | Out-Null
$composeArgs=@('compose','--project-name',$composeProject,'--env-file',$envFile,'-f',$composeFile,'up','-d','--no-recreate','postgres')
$composeOutput=& $docker @composeArgs 2>&1
[IO.File]::WriteAllLines((Join-Path $logRoot 'compose.log'),[string[]]$composeOutput,[Text.UTF8Encoding]::new($false))
if ($LASTEXITCODE -ne 0) { throw "PostgreSQL startup failed. See $logRoot\compose.log. Existing storage was retained." }
$dbDeadline=[DateTime]::UtcNow.AddSeconds($ReadyTimeoutSeconds)
do {
    $null=& $docker exec $containerName pg_isready -h 127.0.0.1 -p 5432 -U fablewood -d fablewood 2>$null
    if ($LASTEXITCODE -eq 0) { break }
    if ([DateTime]::UtcNow -ge $dbDeadline) { throw 'PostgreSQL did not become ready. Container and volume were retained.' }
    Start-Sleep -Milliseconds 500
} while ($true)
$childEnvironment=@{
    FABLE_DB_CONNECTION="Host=127.0.0.1;Port=$DatabasePort;Database=fablewood;Username=fablewood;Password=$($credentials.databasePassword);Timeout=10;Command Timeout=30"
    FABLE_DB_SCHEMA=$Schema
    FABLE_SERVER_KEY_FILE=$keyPath
    FABLE_SERVER_KEY=$null
    ASPNETCORE_URLS=$baseUrl
    ASPNETCORE_ENVIRONMENT='Production'
}
if ($ownedProcess) {
    # Recheck just before stopping; never stop a reused PID or an unrelated executable.
    $ownedProcess=Get-OwnedBackendProcess $oldState
    if ($ownedProcess) { Stop-Process -InputObject $ownedProcess -ErrorAction Stop; $ownedProcess.WaitForExit(10000) | Out-Null }
    Assert-LoopbackPortFree $ApiPort 'Backend API'
}
$migration=Start-Process -FilePath $backendExe -ArgumentList @('--migrate') -WorkingDirectory $backendRoot -WindowStyle Hidden -Environment $childEnvironment -PassThru -RedirectStandardOutput (Join-Path $logRoot 'migration.stdout.log') -RedirectStandardError (Join-Path $logRoot 'migration.stderr.log')
if (-not $migration.WaitForExit(120000)) {
    Stop-Process -InputObject $migration -ErrorAction SilentlyContinue
    throw 'The owned migration process timed out. Existing database storage was retained.'
}
if ($migration.ExitCode -ne 0) { throw "Explicit schema migration failed with exit code $($migration.ExitCode). See $logRoot. API was not started." }
$api=Start-Process -FilePath $backendExe -WorkingDirectory $backendRoot -WindowStyle Hidden -Environment $childEnvironment -PassThru -RedirectStandardOutput (Join-Path $logRoot 'api.stdout.log') -RedirectStandardError (Join-Path $logRoot 'api.stderr.log')
$state=[ordered]@{status='starting';runId=$runId;projectRoot=$projectRoot;backendPid=$api.Id;backendExe=$backendExe;backendDll=$backendDll;processStartedUtc=$api.StartTime.ToUniversalTime().ToString('o');serverConfig=$serverConfig;baseUrl=$baseUrl;serverId=$ServerId;containerName=$containerName;volume=$volumeName;composeProject=$ComposeProject;databasePort=$DatabasePort;schema=$Schema;logs=$logRoot;privateAclProtected=$aclProtected;health='pending'}
Write-BackendJson $statePath $state
$deadline=[DateTime]::UtcNow.AddSeconds($ReadyTimeoutSeconds)
do {
    $api.Refresh()
    if ($api.HasExited) { $state.status='failed';$state.health='process-exited';Write-BackendJson $statePath $state;throw "The owned API process exited. See $logRoot." }
    if (Get-BackendHealth) { break }
    if ([DateTime]::UtcNow -ge $deadline) {
        Stop-Process -InputObject $api -ErrorAction SilentlyContinue
        $state.status='failed';$state.health='readiness-timeout';Write-BackendJson $statePath $state
        throw "API readiness timed out; only its owned process was stopped. See $logRoot."
    }
    Start-Sleep -Milliseconds 500
} while ($true)
$state.status='ready';$state.health='ok';$state['readyUtc']=[DateTime]::UtcNow.ToString('o')
Write-BackendJson $statePath $state
Write-BackendJson (Join-Path $logRoot 'state.json') $state
Write-Output ([pscustomobject]@{status='ready';backendPid=$api.Id;baseUrl=$baseUrl;stateFile=$statePath;serverConfig=$serverConfig;containerName=$containerName;volume=$volumeName})
