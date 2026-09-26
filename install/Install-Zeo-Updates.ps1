param([ValidateSet('All','Core','Nav','PDC','Ore')][string]$Plugin='All',[switch]$ValidateOnly)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Catalog-Profile.ps1')
$root=Join-Path $env:APPDATA 'Pulsar/Legacy'
$sourcePath=Join-Path $root 'Sources/sources.xml'
$profilePath=Join-Path $root 'Profiles/Current.xml'
if(-not(Test-Path -LiteralPath $sourcePath) -or -not(Test-Path -LiteralPath $profilePath)){throw 'Pulsar Legacy must be installed and launched once before setup.'}
$selected=if($Plugin -eq 'All'){@('Core','Nav','PDC','Ore')}else{@($Plugin)}
$sourceBefore=[IO.File]::ReadAllText($sourcePath);$profileBefore=[IO.File]::ReadAllText($profilePath)
[xml]$sources=$sourceBefore;[xml]$zeoProfileDocument=$profileBefore
Set-ZeoCatalogSource $sources
Set-ZeoCatalogProfile $zeoProfileDocument $selected
if($ValidateOnly){Write-Host ('Configuration preview passed for '+($selected -join ', ')+'. Nothing changed.');return}
$busy=@(Get-Process -ErrorAction SilentlyContinue | Where-Object {$_.ProcessName -in @('Legacy','Interim','SpaceEngineers','ZeoOverlay','ZeoNavOverlay','ZeoPdcOverlay','ZeosOreOverlay')})
if($busy.Count){throw 'Close Space Engineers and its overlays first, then run this installer again. Nothing changed.'}
$backup=Join-Path $root ('ZeoCatalogBackups/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $backup 'sources.xml')
Copy-Item -LiteralPath $profilePath -Destination (Join-Path $backup 'Current.xml')
try{
    if([IO.File]::ReadAllText($sourcePath) -ne $sourceBefore -or [IO.File]::ReadAllText($profilePath) -ne $profileBefore){throw 'Pulsar configuration changed during preparation. Run again after closing it.'}
    $sources.Save($sourcePath);$zeoProfileDocument.Save($profilePath)
    [xml]$verifySource=[IO.File]::ReadAllText($sourcePath);[xml]$verifyProfile=[IO.File]::ReadAllText($profilePath)
    if($verifySource.DocumentElement.OuterXml -ne $sources.DocumentElement.OuterXml -or $verifyProfile.DocumentElement.OuterXml -ne $zeoProfileDocument.DocumentElement.OuterXml){throw 'Configuration readback verification failed.'}
}catch{
    Copy-Item -LiteralPath (Join-Path $backup 'sources.xml') -Destination $sourcePath -Force
    Copy-Item -LiteralPath (Join-Path $backup 'Current.xml') -Destination $profilePath -Force
    throw
}
Write-Host ('Zeo automatic catalog updates configured: '+($selected -join ', '))
Write-Host 'Start Space Engineers through Pulsar Legacy. It will download the published plugins and matching overlays.'
Write-Host 'Old local DLLs remain backed by their existing files but are disabled in the current profile. Plugin settings, other plugins, workshop mods and Subsystem Targeter are unchanged.'
Write-Host ('Profile/source backup: '+$backup)
Write-Host 'Future releases arrive after a catalog refresh and full game restart. Pulsar may cache the source list for two hours.'
