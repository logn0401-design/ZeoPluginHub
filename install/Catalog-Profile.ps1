$ZeoCatalogIds=@{Core='ZeoCore';Nav='ZeoNav';PDC='ZeoPDC';Ore='ZeosOreHelper'}
$ZeoLocalIds=@{Core=@('ZeoCore.dll','ZeoCore.PerformanceCandidate.dll','ZeoCore.InventoryCandidate.dll');Nav=@('ZeoNav.dll');PDC=@('ZeoPDC.dll');Ore=@('ZeosOreHelper.dll')}
function Ensure-XmlChild($document,$parent,[string]$name){
    $node=$parent.SelectSingleNode($name)
    if($null -eq $node){$node=$document.CreateElement($name);[void]$parent.AppendChild($node)}
    return $node
}
function Set-XmlText($document,$parent,[string]$name,[string]$value){
    $node=Ensure-XmlChild $document $parent $name
    $node.InnerText=$value
}
function Set-ZeoCatalogSource([xml]$document){
    if($document.DocumentElement.LocalName -ne 'SourcesConfig'){throw 'Unrecognized Pulsar sources configuration.'}
    $hubs=Ensure-XmlChild $document $document.DocumentElement 'RemoteHubSources'
    $matches=@($hubs.SelectNodes('RemoteHub') | Where-Object {$_.Repo -eq 'logn0401-design/ZeoPluginHub'})
    if($matches.Count -gt 1){throw 'Duplicate Zeo source records found; inspect before changing.'}
    if($matches.Count){$hub=$matches[0]}else{$hub=$document.CreateElement('RemoteHub');[void]$hubs.AppendChild($hub)}
    Set-XmlText $document $hub 'Name' 'Zeo Plugins'
    Set-XmlText $document $hub 'Repo' 'logn0401-design/ZeoPluginHub'
    Set-XmlText $document $hub 'Branch' 'main'
    Set-XmlText $document $hub 'Enabled' 'true'
    Set-XmlText $document $hub 'Trusted' 'true'
    foreach($old in @($hub.SelectNodes('LastCheck|Hash'))){[void]$hub.RemoveChild($old)}
}
function Set-ZeoCatalogProfile([xml]$document,[string[]]$plugins){
    if($document.DocumentElement.LocalName -ne 'Profile'){throw 'Unrecognized Pulsar profile.'}
    $github=Ensure-XmlChild $document $document.DocumentElement 'GitHub'
    $local=Ensure-XmlChild $document $document.DocumentElement 'Local'
    foreach($plugin in $plugins){
        if(-not $ZeoCatalogIds.ContainsKey($plugin)){throw 'Unknown plugin selection.'}
        $id='logn0401-design/ZeoPluginHub.'+$ZeoCatalogIds[$plugin]
        foreach($entry in @($local.SelectNodes('string'))){if($entry.InnerText -in $ZeoLocalIds[$plugin]){[void]$local.RemoveChild($entry)}}
        $existing=@($github.SelectNodes('GitHubPluginConfig') | Where-Object {$_.Id -eq $id})
        if($existing.Count -gt 1){throw 'Duplicate catalog entries found; inspect the profile first.'}
        if($existing.Count){$entry=$existing[0]}else{$entry=$document.CreateElement('GitHubPluginConfig');[void]$github.AppendChild($entry);Set-XmlText $document $entry 'Id' $id}
        foreach($pin in @($entry.SelectNodes('SelectedVersion'))){[void]$entry.RemoveChild($pin)}
    }
}
