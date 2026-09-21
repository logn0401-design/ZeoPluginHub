$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = [IO.Path]::GetFullPath((Join-Path $root '..\..'))
$testDir = Join-Path $root 'artifacts'
$game = 'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64'
$descriptor = [xml](Get-Content -LiteralPath (Join-Path $repo 'Plugins\ZeosOreHelper.xml') -Raw)
$runtime = Join-Path $repo ($descriptor.PluginData.Asset | Where-Object Name -eq 'ZeosOreHelperRuntime').Path
$overlayArchive = Join-Path $repo ($descriptor.PluginData.Asset | Where-Object Name -eq 'ZeosOreOverlayPackage').Path
$script:ZeoProbeFolders = @((Split-Path $runtime), $testDir, $game)
$resolver = [System.ResolveEventHandler] {
    param($sender, $eventArgs)
    $simple = (New-Object Reflection.AssemblyName($eventArgs.Name)).Name + '.dll'
    foreach ($folder in $script:ZeoProbeFolders) {
        $candidate = Join-Path $folder $simple
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return [Reflection.Assembly]::LoadFrom($candidate) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$checks = New-Object 'System.Collections.Generic.List[string]'
function Check($ok, [string]$name) {
    if (-not $ok) { throw "FAIL: $name" }
    $checks.Add($name)
}
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $fixture = Join-Path $testDir ('overlay-' + [Guid]::NewGuid().ToString('N'))
    [IO.Compression.ZipFile]::ExtractToDirectory($overlayArchive, $fixture)
    $assembly = [Reflection.Assembly]::LoadFrom($runtime)
    $pluginType = $assembly.GetType('ZeosOreHelper.Plugin', $true)
    $flags = [Reflection.BindingFlags]'NonPublic,Static'
    $before = $pluginType.GetField('DataDirectory', $flags).GetValue($null)
    $plugin = [Activator]::CreateInstance($pluginType)
    $assets = New-Object 'System.Collections.Generic.Dictionary[string,string]'
    $threw = $false
    try { $plugin.LoadAssets($assets) } catch { $threw = $true }
    Check $threw 'Missing overlay asset rejected'
    $assets['ZeosOreOverlayPackage'] = Join-Path $fixture 'missing'
    $threw = $false
    try { $plugin.LoadAssets($assets) } catch { $threw = $true }
    Check $threw 'Missing executable rejected'
    $incomplete = Join-Path $fixture 'incomplete'
    New-Item -ItemType Directory -Path $incomplete | Out-Null
    Copy-Item -LiteralPath (Join-Path $fixture 'ZeosOreOverlay.exe') -Destination $incomplete
    $assets['ZeosOreOverlayPackage'] = $incomplete
    $threw = $false
    try { $plugin.LoadAssets($assets) } catch { $threw = $true }
    Check $threw 'Missing EXE config rejected'
    $assets['ZeosOreOverlayPackage'] = $fixture
    $plugin.LoadAssets($assets)
    $path = $pluginType.GetProperty('CatalogOverlayPath', $flags).GetValue($null, $null)
    Check ($path -eq (Join-Path $fixture 'ZeosOreOverlay.exe')) 'Exact extracted overlay path selected'
    $after = $pluginType.GetField('DataDirectory', $flags).GetValue($null)
    Check ($before -eq $after -and $after -eq (Join-Path $env:APPDATA 'Pulsar\ZeosOreHelper')) 'Persistent data directory preserved'
    $wrapper = [Reflection.Assembly]::LoadFrom((Join-Path $testDir 'ZeosOreHelper.dll'))
    $entryType = $wrapper.GetType('Zeo.Ore.PulsarCatalog.EntryPoint', $true)
    $entry = [Activator]::CreateInstance($entryType)
    $entry.LoadAssets($assets)
    Check ($null -ne $entryType.GetMethod('OpenConfigDialog')) 'Pulsar configuration action retained'
    Check ($null -ne $entryType.GetMethod('LoadAssets')) 'Actual compiled Pulsar entry loads its runtime and overlay assets'
    $checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testDir 'asset-checks.json') -Encoding UTF8
    Write-Output ('PASS: ' + $checks.Count + ' asset and dependency checks; no game, overlay, Init, or Update started.')
} finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
