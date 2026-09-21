$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = [IO.Path]::GetFullPath((Join-Path $root '..\..'))
$pulsar = Join-Path $env:APPDATA 'Pulsar'
$script:ZeoCatalogProbe = @((Join-Path $pulsar 'Libraries\Legacy'), (Join-Path $pulsar 'Libraries\Compiler'), $pulsar)
$resolver = [System.ResolveEventHandler] {
    param($sender, $eventArgs)
    $simple = (New-Object Reflection.AssemblyName($eventArgs.Name)).Name + '.dll'
    foreach ($folder in $script:ZeoCatalogProbe) {
        $file = Join-Path $folder $simple
        if (Test-Path -LiteralPath $file -PathType Leaf) { return [Reflection.Assembly]::LoadFrom($file) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    $assembly = [Reflection.Assembly]::LoadFrom((Join-Path $pulsar 'Libraries\Legacy\Pulsar.Shared.dll'))
    $type = $assembly.GetType('Pulsar.Shared.Data.PluginData', $true)
    $serializer = New-Object System.Xml.Serialization.XmlSerializer($type)
    $reader = [IO.File]::OpenRead((Join-Path $repo 'Plugins\ZeosOreHelper.xml'))
    try { $data = $serializer.Deserialize($reader) } finally { $reader.Dispose() }
    if ($data.GetType().Name -ne 'GitHubPlugin') { throw 'Wrong plugin type' }
    if ($data.SourceDirectories.Length -ne 1 -or $data.SourceDirectories[0] -ne 'loader/ZeosOreHelper/') { throw 'Incorrect compiler scope' }
    if ($data.RepoId -ne 'logn0401-design/ZeoPluginHub' -or $data.Commit -notmatch '^[a-f0-9]{40}$') { throw 'Invalid repository or commit' }
    if ($data.Runtimes -ne 'CLR' -or $data.Platforms -ne 'Windows') { throw 'Incorrect runtime constraints' }
    if ($data.Assets.Length -ne 2) { throw 'Expected two paired assets' }
    foreach ($asset in $data.Assets) {
        $actual = (Get-FileHash -LiteralPath (Join-Path $repo $asset.Path) -Algorithm SHA256).Hash
        if ($actual -ne $asset.Sha256) { throw 'Asset hash mismatch' }
    }
    if (-not $data.Assets[0].Reference -or -not $data.Assets[1].Extract) { throw 'Incorrect asset mode' }
    Write-Output 'PASS: descriptor deserialized by installed Pulsar 2.4.2; source scope, commit pin, runtime constraints and paired asset hashes validated.'
} finally { [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver) }
