"""Exercise the installed Pulsar compiler without starting SE or touching profiles."""
import base64, json, os, pathlib, struct, subprocess, sys, xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parent
repo = root.parent.parent
source = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else repo
pulsar = pathlib.Path(os.environ['APPDATA']) / 'Pulsar'
game = pathlib.Path(r'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64')
framework = pathlib.Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319'
out = root / 'artifacts'
out.mkdir(exist_ok=True)
refs = sorted({p.stem for pattern in ['SpaceEngineers*.dll','VRage*.dll','Sandbox*.dll','ProtoBuf*.dll'] for p in game.glob(pattern) if p.name != 'VRage.Native.dll'})
refs += ['Microsoft.CSharp','0Harmony','DiscordRPC','Newtonsoft.Json','Mono.Cecil','NLog', 'System.Windows.Forms','System.Windows.Forms.DataVisualization','System.Xaml','System.Windows.Controls.Ribbon','PresentationCore','PresentationFramework','WindowsBase']
init = {'References':refs, 'ProbeDirectories':[str(framework),str(framework/'WPF'),str(game),str(pulsar/'Libraries/Legacy')], 'LogFile':str(out/'compiler.log')}
catalog = (source/'loader/ZeosOreHelper').exists()
files = sorted((source/'loader/ZeosOreHelper' if catalog else source/'ClientPlugin').glob('*.cs'))
if catalog:
    pass
elif (source/'Shared').exists():
    files += sorted((source/'Shared').glob('*.cs'))
else:
    files += [source/'ZeoOverlay'/n for n in ['OverlaySettings.cs','HudLayout.cs']]
request = {'AssemblyName':'ZeosOreHelper_Pulsar_Test', 'DebugBuild':False, 'Flags':['TRACE','NETFRAMEWORK','PULSAR'], 'Sources':[{'Name':p.relative_to(source).as_posix(),'Data':base64.b64encode(p.read_bytes()).decode()} for p in files], 'References':[]}
if catalog:
    descriptor=ET.parse(source/'Plugins/ZeosOreHelper.xml').getroot()
    request['References'] = [str((source/a.attrib['Path']).resolve()) for a in descriptor.findall('Asset') if a.attrib.get('Reference')=='true']
def packet(obj):
    data = json.dumps(obj).encode()
    return struct.pack('<i',len(data))+data
exe = pulsar/'Libraries/Compiler/Compiler.exe'
result = subprocess.run([str(exe)], input=packet(init)+packet(request), stdout=subprocess.PIPE, stderr=subprocess.PIPE, cwd=out, creationflags=subprocess.CREATE_NO_WINDOW, timeout=90)
data = result.stdout
responses=[]
while data:
    size=struct.unpack('<i',data[:4])[0]
    responses.append(json.loads(data[4:4+size]))
    data=data[4+size:]
if result.returncode != 0 or len(responses) != 2:
    print(result.stderr.decode(errors='replace'), responses)
    raise SystemExit(1)
response=responses[1]
assembly=response.pop('Assembly',None)
response.pop('Symbols',None)
if assembly:
    (out/'ZeosOreHelper.dll').write_bytes(base64.b64decode(assembly))
(out/'compile-result.json').write_text(json.dumps(response,indent=2))
print(json.dumps(response,indent=2))
raise SystemExit(0 if response.get('Success') else 1)
