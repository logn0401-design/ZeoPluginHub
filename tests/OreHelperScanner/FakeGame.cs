// Deterministic storage/camera substitutes. These do not validate game API internals.
using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;
namespace VRage.Game { internal static class Marker {} }
namespace VRage.Voxels {
 public enum MyStorageDataTypeFlags {ContentAndMaterial}
 public class MyStorageData {
  internal Vector3I Size;internal byte[] ContentData,MaterialData;
  public MyStorageData(MyStorageDataTypeFlags f){}public void Resize(Vector3I size){Size=size;ContentData=new byte[size.X*size.Y*size.Z];MaterialData=new byte[ContentData.Length];}
  public byte Content(int i)=>ContentData[i];public byte Material(int i)=>MaterialData[i];
 }
 public static class MyVoxelCoordSystems {public static void VoxelCoordToWorldPosition(MatrixD matrix,Vector3D translation,Vector3D half,ref Vector3I coord,out Vector3D world){world=translation+Vector3D.TransformNormal((Vector3D)coord-half,matrix);}}
}
namespace Sandbox.Game.Entities {
 public class FakeStorage {
  public bool Closed;public int Reads,MaxRead;public bool Fail;
  public readonly Dictionary<Vector3I,byte> Ore=new Dictionary<Vector3I,byte>();
  public void PinAndExecute(Action<FakeStorage> a){a(this);}
  public void ReadRange(VRage.Voxels.MyStorageData data,VRage.Voxels.MyStorageDataTypeFlags flags,int lod,Vector3I min,Vector3I max){
   if(Fail)throw new InvalidOperationException("fixture storage unavailable");Reads++;MaxRead=Math.Max(MaxRead,data.ContentData.Length);int index=0;
   for(int z=min.Z;z<=max.Z;z++)for(int y=min.Y;y<=max.Y;y++)for(int x=min.X;x<=max.X;x++,index++){byte ore;if(Ore.TryGetValue(new Vector3I(x,y,z),out ore)){data.ContentData[index]=255;data.MaterialData[index]=ore;}}
  }
 }
 public class FakePosition {public MatrixD WorldMatrixRef=MatrixD.Identity;}
 public class MyVoxelMap {
  public bool Closed;public FakeStorage Storage=new FakeStorage();public Vector3I StorageMin=Vector3I.Zero,StorageMax=new Vector3I(512);
  public MatrixD WorldMatrix=MatrixD.Identity;public Vector3D SizeInMetresHalf=new Vector3D(256);public FakePosition PositionComp=new FakePosition();
 }
}
namespace Sandbox.Definitions {
 public sealed class Material {public string MinedOre;}
 public sealed class MyDefinitionManager {public static MyDefinitionManager Static=new MyDefinitionManager();public Material GetVoxelMaterialDefinition(byte b){return new Material{MinedOre=b==1?"Uranium":b==2?"Iron":"Stone"};}}
}
namespace Sandbox.ModAPI {
 public sealed class Camera {public MatrixD WorldMatrix=MatrixD.Identity;public Vector3D Position=Vector3D.Zero;}
 public sealed class Session {public Camera Camera=new Camera();}
 public static class MyAPIGateway {public static Session Session=new Session();}
}
namespace ZeosOreHelper {
 internal static class Plugin {internal static void Log(string s){}}
 internal sealed class HudSettings {
  internal bool MarkersEnabled=true;internal readonly HashSet<string> Disabled=new HashSet<string>();
  internal bool IsOreEnabled(string ore)=>!Disabled.Contains(ore);internal bool GetOreShowPing(string ore)=>!Disabled.Contains(ore);internal string GetOreColor(string ore)=>"#65FF73";
 }
 internal static class ScreenUtils {internal static Vector2D WorldToScreen(Vector3D p,out bool off){off=false;return new Vector2D(p.X/1000,p.Y/1000);}internal static bool IsOutsideHud(Vector2D p)=>Math.Abs(p.X)>1||Math.Abs(p.Y)>1;}
 internal sealed class VoxelSurveyor {
  internal sealed class RoidRecord {internal long EntityId;internal Sandbox.Game.Entities.MyVoxelMap Voxel;internal Vector3D Position;internal double Distance,Radius=100;internal bool Skipped;internal ZeoOreShared.SdxScanData SdxScan;}
  internal List<RoidRecord> Records=new List<RoidRecord>();internal RoidRecord GetRecord(long id)=>Records.FirstOrDefault(r=>r.EntityId==id);
 }
}
namespace AsteroidScanner.Data.Scripts.AsteroidScanner {
 public class Session {public static Session Instance{get;set;}public Dictionary<int,List<Asteroid>> Asteroids=new Dictionary<int,List<Asteroid>>();public Asteroid CurrentlyScanning;}
 public class Asteroid {public long EntityId;public Vector3D Position;public int ScanType=2;public Scan ScanData=new Scan();}
 public class Scan {public int ScanType=2;public float Volume=10240;public Dictionary<string,float> Ore=new Dictionary<string,float>{{"Uranium",1024}};}
}
