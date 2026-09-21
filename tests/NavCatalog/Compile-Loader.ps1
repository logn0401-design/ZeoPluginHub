param(
    [Parameter(Mandatory=$true)][string]$GameBin,
    [Parameter(Mandatory=$true)][string]$PulsarDir,
    [Parameter(Mandatory=$true)][string]$RuntimeDll,
    [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference = 'Stop'
$GameBin = [IO.Path]::GetFullPath($GameBin)
$PulsarDir = [IO.Path]::GetFullPath($PulsarDir)
$RuntimeDll = [IO.Path]::GetFullPath($RuntimeDll)
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @(Get-ChildItem -LiteralPath $GameBin -File | Where-Object { $_.Name -match '^(SpaceEngineers|VRage|Sandbox|ProtoBuf).*\.dll$' -and $_.Name -ne 'VRage.Native.dll' } | ForEach-Object { $_.BaseName })
$refs += @('Microsoft.CSharp','0Harmony','DiscordRPC','Newtonsoft.Json','Mono.Cecil','NLog','System.Windows.Forms','System.Windows.Forms.DataVisualization','System.Xaml','System.Windows.Controls.Ribbon','PresentationCore','PresentationFramework','WindowsBase')
$init = @{ References=$refs; ProbeDirectories=@($framework,(Join-Path $framework 'WPF'),$GameBin,(Join-Path $PulsarDir 'Libraries\Legacy')); LogFile=(Join-Path $OutputDir 'compiler.log') }
$request = @{ AssemblyName='ZeoNav_Pulsar_Test'; DebugBuild=$false; Flags=@('TRACE','NETFRAMEWORK','PULSAR'); References=@($RuntimeDll); Sources=@(@{ Name='loader/ZeoNav/EntryPoint.cs'; Data=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $repo 'loader\ZeoNav\EntryPoint.cs'))) }) }
$info = New-Object Diagnostics.ProcessStartInfo
$info.FileName = Join-Path $PulsarDir 'Libraries\Compiler\Compiler.exe'
$info.WorkingDirectory = $OutputDir
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.WindowStyle = 'Hidden'
$info.RedirectStandardInput = $true
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$compiler = [Diagnostics.Process]::Start($info)
$buffer = New-Object IO.MemoryStream
$copy = $compiler.StandardOutput.BaseStream.CopyToAsync($buffer)
$errors = $compiler.StandardError.ReadToEndAsync()
$writer = New-Object IO.BinaryWriter($compiler.StandardInput.BaseStream)
foreach ($message in @($init,$request)) {
    $bytes = [Text.Encoding]::UTF8.GetBytes(($message | ConvertTo-Json -Depth 10 -Compress))
    $writer.Write([int]$bytes.Length)
    $writer.Write([byte[]]$bytes)
}
$writer.Flush()
$compiler.StandardInput.Close()
if (!$compiler.WaitForExit(60000)) { $compiler.Kill(); throw 'Compiler timed out.' }
$null = $copy.GetAwaiter().GetResult()
if ($compiler.ExitCode -ne 0) { throw ('Compiler failed: ' + $errors.GetAwaiter().GetResult()) }
$buffer.Position = 0
$reader = New-Object IO.BinaryReader($buffer)
$responses = @()
while ($buffer.Position -lt $buffer.Length) {
    $size = $reader.ReadInt32()
    if ($size -lt 0 -or $buffer.Position + $size -gt $buffer.Length) { throw 'Invalid compiler packet.' }
    $responses += ([Text.Encoding]::UTF8.GetString($reader.ReadBytes($size)) | ConvertFrom-Json)
}
if ($responses.Count -ne 2) { throw 'Expected two compiler responses.' }
$result = $responses[1]
if ($result.Assembly) { [IO.File]::WriteAllBytes((Join-Path $OutputDir 'ZeoNav.dll'),[Convert]::FromBase64String($result.Assembly)) }
$result.PSObject.Properties.Remove('Assembly')
$result.PSObject.Properties.Remove('Symbols')
$json = $result | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText((Join-Path $OutputDir 'compile-result.json'),$json)
$json
if (!$result.Success) { exit 1 }
