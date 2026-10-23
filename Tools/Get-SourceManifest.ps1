#requires -Version 7.4
[CmdletBinding()]
param([string]$OutputPath)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$files=[Collections.Generic.List[IO.FileInfo]]::new()
$excluded=[Collections.Generic.List[string]]::new()
$sourceRoots=@('Fablewood.Backend','Fablewood.Backend.IntegrationTests','Tools')
$skipDirectories=@('.git','.vs','.idea','bin','obj','artifacts','testresults','saved','__pycache__','node_modules','.local','.secrets','.data','data','postgres-data','pgdata','volumes','logs')
$allowedExtensions=@('.cs','.csproj','.props','.targets','.json','.sql','.yaml','.yml','.md','.txt','.config','.ps1','.py','.cmd','.bat','.sh','.sln','.slnx','.http','.xml')
$rootWhitelist=@('.gitignore','.gitattributes','.editorconfig','.dockerignore','.env.example','README.md','AGENTS.md','global.json','compose.yaml','compose.yml','Dockerfile','NuGet.Config','Directory.Build.props','Directory.Build.targets','Directory.Packages.props','Start-Backend.cmd','Fablewood.Backend.sln','Fablewood.Backend.slnx')
function Get-RelativeSourcePath([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($projectRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Source path escaped the project root.' }
    return [IO.Path]::GetRelativePath($projectRoot,$full).Replace('\','/')
}
function Test-ExcludedFile([IO.FileInfo]$File) {
    return ($File.Name -match '^\.env(?:\..*)?$' -and $File.Name -ne '.env.example') -or
        $File.Name -like 'appsettings.Local.json' -or $File.Name -like 'appsettings.*.local.json' -or
        $File.Name -in @('backend-credentials.json','server-key.txt','server.json','backend-state.json') -or
        $File.Extension.ToLowerInvariant() -in @('.log','.pyc','.pdb','.dll','.exe','.suo','.user','.tmp','.key','.pem','.pfx','.p12','.sav')
}
function Add-SourceDirectory([string]$Directory) {
    foreach ($entry in Get-ChildItem -LiteralPath $Directory -Force) {
        $relative=Get-RelativeSourcePath $entry.FullName
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse points are not accepted in source: $relative" }
        if ($entry.PSIsContainer) {
            if ($entry.Name.ToLowerInvariant() -in $skipDirectories) { $excluded.Add($relative+'/'); continue }
            Add-SourceDirectory $entry.FullName
            continue
        }
        if (Test-ExcludedFile $entry) { $excluded.Add($relative); continue }
        if ($entry.Extension.ToLowerInvariant() -notin $allowedExtensions -and $entry.Name -notin @('Dockerfile','.env.example','.dockerignore','.editorconfig')) {
            throw "Unclassified source file requires review: $relative"
        }
        $files.Add($entry)
    }
}
foreach ($entry in Get-ChildItem -LiteralPath $projectRoot -Force) {
    if ($entry.PSIsContainer -and $entry.Name -in $sourceRoots) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'A source root is a reparse point.' }
        Add-SourceDirectory $entry.FullName
    } elseif (-not $entry.PSIsContainer -and $entry.Name -in $rootWhitelist) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'A selected root file is a reparse point.' }
        $files.Add($entry)
    } else {
        $suffix=if ($entry.PSIsContainer) { '/' } else { '' }
        $excluded.Add($entry.Name+$suffix)
    }
}
$selected=@($files | Sort-Object FullName -Unique)
$paths=@($selected | ForEach-Object { Get-RelativeSourcePath $_.FullName })
$required=@('.gitignore','README.md','global.json','compose.yaml','.env.example','Start-Backend.cmd',
    'Tools/Start-Backend.ps1','Tools/Get-SourceManifest.ps1',
    'Fablewood.Backend/Fablewood.Backend.csproj','Fablewood.Backend/packages.lock.json',
    'Fablewood.Backend/BackendOptions.cs','Fablewood.Backend/Contracts.cs','Fablewood.Backend/SqlStore.cs','Fablewood.Backend/Program.cs',
    'Fablewood.Backend/Migrations/001_initial.sql','Fablewood.Backend/README.md',
    'Fablewood.Backend.IntegrationTests/Fablewood.Backend.IntegrationTests.csproj',
    'Fablewood.Backend.IntegrationTests/packages.lock.json','Fablewood.Backend.IntegrationTests/Program.cs')
foreach ($relative in $required) { if ($relative -notin $paths) { throw "Required backend source was omitted: $relative" } }
# Only this standalone repository's private files may supply exact-match values.
# Their contents never enter the manifest or console; another project's secrets are not inspected.
$knownSecrets=[Collections.Generic.List[string]]::new()
$credentialFile=Join-Path $projectRoot 'Saved\Backend\backend-credentials.json'
if (Test-Path -LiteralPath $credentialFile -PathType Leaf) {
    try { $privateData=[IO.File]::ReadAllText($credentialFile) | ConvertFrom-Json }
    catch { throw 'Local private credential metadata could not be read (contents suppressed).' }
    foreach ($name in @('databasePassword','serverKey')) {
        $property=$privateData.PSObject.Properties[$name]
        if ($null -ne $property -and $null -ne $property.Value -and ([string]$property.Value).Length -ge 16) { $knownSecrets.Add([string]$property.Value) }
    }
}
$keyFile=Join-Path $projectRoot 'Saved\Backend\server-key.txt'
if (Test-Path -LiteralPath $keyFile -PathType Leaf) {
    $privateKey=[IO.File]::ReadAllText($keyFile).Trim()
    if ($privateKey.Length -ge 16) { $knownSecrets.Add($privateKey) }
}
function Test-Placeholder([string]$Value) {
    return [string]::IsNullOrWhiteSpace($Value) -or $Value -match '^\$\{|^<[^>]+>$|^CHANGE_ME(?:_|$)|^GENERATED_|^YOUR_|^EXAMPLE(?:_|$)|^null$'
}
$records=[Collections.Generic.List[object]]::new()
$totalBytes=[long]0
foreach ($file in $selected) {
    $relative=Get-RelativeSourcePath $file.FullName
    $body=[IO.File]::ReadAllText($file.FullName)
    foreach ($secret in $knownSecrets) {
        if ($body.Contains($secret)) { throw "Live private credential found in source: $relative (value suppressed)" }
    }
    if ($body -match '-----BEGIN (?:RSA |EC |OPENSSH |ENCRYPTED )?PRIVATE KEY-----') { throw "Private key material found: $relative (value suppressed)" }
    if ($file.Extension.ToLowerInvariant() -in @('.json','.yaml','.yml','.config','.ini','.http') -or $file.Name -eq '.env.example') {
        $patterns=@(
            '(?im)^\s*(?:SecurityToken|FABLE_SERVER_KEY|FABLE_PG_PASSWORD|POSTGRES_PASSWORD|password|databasePassword|serverKey|accessToken|refreshToken)\s*(?:=|:)\s*(?<value>[^\r\n#]+)',
            '(?im)["''](?:serverKey|SecurityToken|databasePassword|password|accessToken|refreshToken|FABLE_SERVER_KEY|FABLE_PG_PASSWORD|POSTGRES_PASSWORD)["'']\s*:\s*["''](?<value>[^"''\r\n]+)',
            '(?i)(?:Password|Pwd)\s*=\s*(?<value>[^;"''\s]+)',
            '(?im)^\s*Authorization\s*:\s*Bearer\s+(?<value>[^\s]+)')
        foreach ($pattern in $patterns) {
            foreach ($match in [regex]::Matches($body,$pattern)) {
                $value=$match.Groups['value'].Value.Trim().Trim('"',"'")
                if (-not (Test-Placeholder $value)) { throw "Unexpected literal secret setting in $relative (value suppressed)" }
            }
        }
    }
    $current=Get-Item -LiteralPath $file.FullName
    $digest=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $records.Add([ordered]@{path=$relative;bytes=$current.Length;sha256=$digest})
    $totalBytes+=$current.Length
}
if (-not $OutputPath) {
    $id=(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
    $OutputPath=Join-Path $projectRoot ('Saved\SourceManifest\'+$id+'.json')
}
$OutputPath=[IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) { throw 'Manifest output already exists and was preserved.' }
foreach ($name in $sourceRoots) {
    $directory=[IO.Path]::GetFullPath((Join-Path $projectRoot $name))+[IO.Path]::DirectorySeparatorChar
    if ($OutputPath.StartsWith($directory,[StringComparison]::OrdinalIgnoreCase)) { throw 'Write manifests outside authored source directories.' }
}
foreach ($record in $records) {
    $file=Join-Path $projectRoot $record.path
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne $record.sha256) { throw 'Source changed while collecting the manifest; no manifest was written.' }
}
$manifest=[ordered]@{
    status='ready';kind='standalone-backend-source';createdUtc=[DateTime]::UtcNow.ToString('o');projectRoot=$projectRoot
    fileCount=$records.Count;totalBytes=$totalBytes;files=$records.ToArray();excluded=@($excluded | Sort-Object -Unique)
    sourceRoots=$sourceRoots;rootWhitelist=$rootWhitelist;requiredFiles=$required
    assertions=[ordered]@{requiredFilesIncluded=$true;whitelistedSourcesIncluded=$true;knownPrivateSecretValuesAbsent=$true;literalSecretChecksPassed=$true;knownPrivateSecretCount=$knownSecrets.Count;outputsAndRuntimeDataExcluded=$true}
    limitations=@('Source inventory and secret-pattern check only; no restore, build, database, service or gameplay test.',
        'The scan covers known local secret values, private-key blocks and credential settings in configuration files; it does not prove absence of every possible secret.',
        'Runtime credentials, state, logs, PostgreSQL data and generated binaries are excluded. Original services and other projects are not accessed.')
}
[IO.Directory]::CreateDirectory((Split-Path -Parent $OutputPath)) | Out-Null
$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($manifest | ConvertTo-Json -Depth 10)+[Environment]::NewLine)
$stream=[IO.File]::Open($OutputPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try { $stream.Write($bytes,0,$bytes.Length); $stream.Flush() } finally { $stream.Dispose() }
Write-Output ([pscustomobject]@{status='ready';manifest=$OutputPath;fileCount=$records.Count;totalBytes=$totalBytes})
