using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.Plugins;
using VRage.Utils;
using VRageMath;

namespace ZeosOreHelper
{
    public sealed class Plugin : IPlugin
    {
        public const string Name="Zeos Ore Helper";
        public const string Version="1.0.5";
        internal static string CatalogOverlayPath {get;private set;}
        public void LoadAssets(IReadOnlyDictionary<string,string> assets) {
            string directory;
            if(assets==null||!assets.TryGetValue("ZeosOreOverlayPackage",out directory)||string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("Pulsar did not supply the Ore Helper overlay package.");
            string executable=Path.GetFullPath(Path.Combine(directory,"ZeosOreOverlay.exe"));
            if(!File.Exists(executable)||!File.Exists(executable+".config"))
                throw new FileNotFoundException("The Ore Helper overlay package is incomplete.",executable);
            CatalogOverlayPath=executable;
        }
        public static Plugin Instance { get; private set; }
        internal static readonly string DataDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Pulsar","ZeosOreHelper");
        internal static readonly string SurveyLogPath=Path.Combine(DataDirectory,"ore_helper.log");
        private static readonly object LogLock=new object();

        internal ZeoOreShared.OreLearningStore Learning;
        internal ZeoOreShared.OreLearningStore SdxLearning;
        internal readonly SdxScanReader Sdx=new SdxScanReader();
        internal readonly OreDepositSurveyor Deposits=new OreDepositSurveyor();
        internal ZeoOreShared.OreSearchConfig Search;
        private bool _searchWasEnabled;
        internal string SearchSummary {
            get {if(Learning==null)return "Enter a world to learn ore records.";return Learning.VerifiedCount+" verified asteroids / "+Learning.Count+" recorded  |  Verify queue "+(_surveyor==null?0:_surveyor.VerificationPending)+(Learning.LastError.Length>0?" | SAVE ERROR: "+Learning.LastError:"");}
        }
        private string LearningWorldKey(){
            var session=MyAPIGateway.Session;ulong server=0;try{if(MyAPIGateway.Multiplayer.MultiplayerActive)server=MyAPIGateway.Multiplayer.ServerId;}catch{}
            string name="",path="";try{name=session.Name;path=session.CurrentPath;}catch{}
            // Missing identity is deliberately session-local, never a shared "unknown world" file.
            string identity=server!=0?"server:"+server+"|world:"+name:"local:"+path+"|world:"+name;
            if((server==0&&string.IsNullOrEmpty(path))||string.IsNullOrEmpty(name))identity+="|session:"+Guid.NewGuid().ToString("N");
            return ZeoOreShared.OreLearningStore.WorldKey(identity);
        }
        internal void ObserveScan(VoxelSurveyor.RoidRecord record){
            if(Learning==null||Search==null)return;bool verify=false;
            string id=(string.IsNullOrWhiteSpace(record.StorageName)?"id:"+record.EntityId:"storage:"+record.StorageName)+"@"+record.Position.X.ToString("0",CultureInfo.InvariantCulture)+","+record.Position.Y.ToString("0",CultureInfo.InvariantCulture)+","+record.Position.Z.ToString("0",CultureInfo.InvariantCulture);
            foreach(var ore in record.Ores)verify|=Learning.Observe(new ZeoOreShared.OreObservation{Asteroid=id,Ore=ore.Ore,Volume=ore.EstimatedVolume,Percent=ore.PercentOfSolid,Samples=ore.Samples,Lod=record.Verified?Math.Max(0,record.VerificationLod):record.Lod,TotalSamples=record.Verified&&record.VerificationLod<record.Lod?record.FineTotalSamples:record.TotalSamples,SolidSamples=record.Verified&&record.VerificationLod<record.Lod?record.FineSolidSamples:record.SolidSamples,Verified=record.Verified,UtcTicks=DateTime.UtcNow.Ticks},Search.AutoLearn);
            if(verify&&Search.Values.B("VerifyNewRecords",true))_surveyor?.QueueVerification(record.EntityId);
        }
        internal string LearningDetail(string ore){
            if(Learning==null)return "No world loaded.";
            bool richness=Search!=null&&Search.Values.Get("SearchSort")=="Richest ore";
            var local=Learning.Best(ore);var native=SdxLearning?.Best(ore);
            Func<ZeoOreShared.OreBenchmark,string> value=b=>b==null||b.Count==0?"learning":(richness?b.Percent.ToString("0.#")+"%":"~"+b.Volume.ToString("0")+" m3")+" ("+b.Count+")";
            return ore+": local "+value(local)+" | SDX2 "+value(native)+" | min "+((Search==null?.8:Search.Threshold(ore))*100).ToString("0")+"%";
        }
        private bool HandleSearchAction(string command){
            if(command=="verifytop"){if(_surveyor==null)throw new InvalidOperationException("Enter a world first");int count=0;foreach(var r in _surveyor.Records.Where(r=>r.State==VoxelSurveyor.SurveyState.Ready&&r.Ores.Any(o=>_settings.IsOreEnabled(o.Ore)&&o.EstimatedVolume>0)).OrderByDescending(r=>r.Ores.Where(o=>_settings.IsOreEnabled(o.Ore)).Sum(o=>o.EstimatedVolume)).Take(8))if(_surveyor.QueueVerification(r.EntityId))count++;Notify(count+" asteroids queued for finer scans");return true;}
            if(command=="usebenchmarks"){Learning?.UseLatest();SdxLearning?.UseLatest();Learning?.Save(true);SdxLearning?.Save(true);return true;}
            if(command=="resetlearning"){if(Learning==null)throw new InvalidOperationException("Enter a world first");Learning.Reset();SdxLearning?.Reset();return true;}
            return false;
        }

        internal ZeoOreShared.OreLayoutDraft Layout;
        internal ZeoOreShared.OreLayoutAck LayoutAck {get{return _overlay==null?null:_overlay.LayoutAck;}}
        internal bool LayoutFresh {get{return _overlay!=null&&Layout!=null&&_overlay.LayoutAck!=null&&_overlay.LayoutAck.Token==Layout.Token&&(DateTime.UtcNow-_overlay.AckUtc).TotalSeconds<1;}}
        internal OreOverlayFrame LastFrame {get{return _lastOverlayFrame;}}
        internal int NativeListRows {get{return _settings.ListRows;}}
        internal void EnsureOverlay(){if(_overlay==null)_overlay=new OreOverlayBridge();_overlay.EnsureRunning(true);}
        internal void OpenLegacyMenu(){EnsureOverlay();_overlay.OpenMenu();}
        internal void NativeSettingsChanged(){_settings.ReloadIfChanged();OnSettingsReloaded();}
        internal void NativeAction(string command){if(HandleSearchAction(command))return;bool send=false;OnChatMessage(0,"/zeoore "+command,ref send);}
        private HudSettings _settings;
        private VoxelSurveyor _surveyor;
        private OreOverlayBridge _overlay;
        private OreOverlayFrameBuilder _frameBuilder;
        private OreLcdRenderer _lcd;
        private OreOverlayFrame _lastOverlayFrame;
        private bool _worldInitialized,_chatRegistered;
        private int _frame,_sessionGeneration,_lastGameplayFrame=-1,_playerMissingFrames,_lastSoftRebindFrame=-10000,_lastSettingsPoll=-10000,_lastOverlaySend=-10000;
        private IMySession _activeSession;private IMyPlayer _activePlayer;private long _lastControlAnchorId;private Vector3D _lastObserverPosition;private bool _observerPositionValid;
        internal static int PluginFrame { get; private set; }

        public void Init(object gameInstance)
        {
            Instance=this;Directory.CreateDirectory(DataDirectory);_settings=HudSettings.Load();
            var ini=new ZeosOreOverlay.OreOverlaySettings(HudSettings.SettingsPath);if(ini.Get("SearchUiVersion")!="1"){if(ini.I("MaxMarkers",30)==30)ini.Set("MaxMarkers",8);ini.Set("SearchUiVersion","1");ini.Save();_settings.ReloadIfChanged();}Search=new ZeoOreShared.OreSearchConfig(ini);
            // Preserve the v0.5.1 policy: every fresh SE/Pulsar launch starts the mining helper OFF.
            _settings.Enabled=false;_settings.Save();
            _overlay=new OreOverlayBridge();_frameBuilder=new OreOverlayFrameBuilder();_lcd=new OreLcdRenderer();
            Log("============================================================");Log("Zeos Ore Helper v"+Version+" ZEO UI + STREAMER OVERHAUL init");Log("MASTER DEFAULT OFF | UI key="+_settings.MenuKey+" | settings="+HudSettings.SettingsPath);
        }

        public void Update()
        {
            _frame++;PluginFrame=_frame;
            try
            {
                if(_frame-_lastSettingsPoll>=15){_lastSettingsPoll=_frame;if(_settings.ReloadIfChanged())OnSettingsReloaded();}
                if(_overlay==null)_overlay=new OreOverlayBridge();_overlay.EnsureRunning(_settings.AutoStartOverlay);

                var session=MyAPIGateway.Session;
                if(session==null){if(_activeSession!=null||_worldInitialized)ResetWorld("SESSION CLOSED");_activeSession=null;_activePlayer=null;return;}
                int gameplay=session.GameplayFrameCounter;var player=session.Player;
                bool changed=_activeSession==null||!ReferenceEquals(_activeSession,session)||(_lastGameplayFrame>=0&&gameplay+30<_lastGameplayFrame)||(_activePlayer!=null&&player!=null&&!ReferenceEquals(_activePlayer,player));
                if(changed)
                {
                    if(_activeSession!=null||_worldInitialized)ResetWorld("SEAMLESS SESSION/PLAYER CHANGE");
                    _activeSession=session;_activePlayer=player;_lastGameplayFrame=gameplay;_sessionGeneration++;_lastControlAnchorId=GetControlAnchorId(player);_observerPositionValid=TryGetObserverPosition(out _lastObserverPosition);_playerMissingFrames=0;
                }
                else{_lastGameplayFrame=gameplay;if(player!=null)_activePlayer=player;}
                if(player==null){_playerMissingFrames++;if(_playerMissingFrames==15){SoftWorldRebind("PLAYER MISSING DURING HANDOFF");_observerPositionValid=false;_lastControlAnchorId=0;}SendOverlayFrame();return;}
                _playerMissingFrames=0;

                long anchor=GetControlAnchorId(player);Vector3D observer;bool haveObserver=TryGetObserverPosition(out observer);bool anchorChanged=_lastControlAnchorId!=0&&anchor!=0&&anchor!=_lastControlAnchorId;bool teleported=_observerPositionValid&&haveObserver&&Vector3D.DistanceSquared(_lastObserverPosition,observer)>25000000.0;
                if((anchorChanged||teleported)&&_frame-_lastSoftRebindFrame>30){_lastSoftRebindFrame=_frame;SoftWorldRebind(teleported?"POSITION JUMP / SECTOR HANDOFF":"CONTROL ANCHOR CHANGED");}
                _lastControlAnchorId=anchor;if(haveObserver){_lastObserverPosition=observer;_observerPositionValid=true;}

                if(!_worldInitialized){var worldKey=LearningWorldKey();Learning=new ZeoOreShared.OreLearningStore(Path.Combine(DataDirectory,"Learning"),worldKey,Search!=null&&!Search.AutoLearn);SdxLearning=new ZeoOreShared.OreLearningStore(Path.Combine(DataDirectory,"Learning","SDX2"),worldKey,Search!=null&&!Search.AutoLearn);_surveyor=new VoxelSurveyor(_settings);_worldInitialized=true;Log("World initialized. Overlay-only HUD path active.");}
                EnsureChatRegistration();ProcessMenuHotkey();
                if(_surveyor!=null&&_settings.Enabled)_surveyor.Update(_frame,_settings.EffectiveSurveyRangeMeters);
                if(_surveyor!=null&&_settings.Enabled){Sdx.Update(_frame,_surveyor,SdxLearning,Search);Deposits.Update(_frame,_surveyor,Search);}
                Learning?.Save();SdxLearning?.Save();
                SendOverlayFrame();
                if(_settings.Enabled&&_frameBuilder!=null&&_frameBuilder.SelectedEntityId!=0)_surveyor.Prioritize(_frameBuilder.SelectedEntityId);
                if(_lcd!=null)_lcd.Update(_frame,_settings,_lastOverlayFrame);
            }
            catch(Exception ex){Log("Update ERROR: "+ex);}
        }

        // Pulsar Open Config and PgUp share the native menu; legacy remains available.
        public void OpenConfigDialog(){if(!OreNativeUi.IsOpen&&!OreNativeUi.Toggle(this))OpenLegacyMenu();}

        public void Dispose()
        {
            OreNativeUi.Close();
            try{if(_chatRegistered&&MyAPIGateway.Utilities!=null)MyAPIGateway.Utilities.MessageEnteredSender-=OnChatMessage;}catch{}
            try{if(_surveyor!=null)_surveyor.Reset();}catch{}
            try{if(_overlay!=null)_overlay.Dispose();}catch{}
            Learning?.Save(true);SdxLearning?.Save(true);
            _surveyor=null;_overlay=null;_frameBuilder=null;_lcd=null;Instance=null;Log("Plugin disposed.");
        }

        private void SendOverlayFrame()
        {
            if(_overlay==null||_frameBuilder==null||_settings==null)return;
            // ~30 fps max if Update runs at 60 fps; UDP stays comfortably below the safe packet limit.
            if(_frame-_lastOverlaySend<2)return;_lastOverlaySend=_frame;
            _lastOverlayFrame=_frameBuilder.Build(_surveyor,_settings);_lastOverlayFrame.Layout=Layout;_overlay.SendFrame(_lastOverlayFrame);
        }

        private void ProcessMenuHotkey()
        {
            try
            {
                if(OreNativeUi.EditingBinding||MyAPIGateway.Input==null||!GameWindowState.Capture().Focused)return;
                if(!OreNativeUi.IsOpen && (MyAPIGateway.Gui.ChatEntryVisible || MyAPIGateway.Gui.IsCursorVisible))return;MyKeys key=MenuKeyToMyKeys(_settings.MenuKey);
                if(key!=MyKeys.None&&MyAPIGateway.Input.IsNewKeyPressed(key)){if(!OreNativeUi.Toggle(this))OpenLegacyMenu();}
            }
            catch{}
        }

        private static MyKeys MenuKeyToMyKeys(string key)
        {
            return OreMenuBinding.ToKey(key);
        }

        private void OnSettingsReloaded()
        {
            Search=new ZeoOreShared.OreSearchConfig(new ZeosOreOverlay.OreOverlaySettings(HudSettings.SettingsPath));
            if(_settings.Enabled&&!_searchWasEnabled&&Search.AutoLearn){Learning?.UseLatest();SdxLearning?.UseLatest();}_searchWasEnabled=_settings.Enabled;
            try
            {
                if(_surveyor!=null){_surveyor.CacheSettingsChanged();_surveyor.RecalculateScores();}
                if(_overlay!=null)_overlay.SettingsChanged();
            }
            catch(Exception ex){Log("Settings apply failed: "+ex.Message);}
        }

        private void ResetWorld(string reason)
        {
            OreNativeUi.Close();Layout=null;Learning?.Save(true);SdxLearning?.Save(true);Learning=null;SdxLearning=null;Sdx.Reset();Deposits.Reset();Log("Resetting world-bound state: "+reason);try{if(_surveyor!=null)_surveyor.Reset();}catch{} _surveyor=null;_worldInitialized=false;_lastControlAnchorId=0;_observerPositionValid=false;_playerMissingFrames=0;_lastOverlayFrame=null;
        }
        private void SoftWorldRebind(string reason){Sdx.Reset();Deposits.Reset();Log("Soft world rebind: "+reason);try{if(_surveyor!=null)_surveyor.Reset();}catch{}}

        private static long GetControlAnchorId(IMyPlayer player)
        {
            try{if(player==null||player.Controller==null||player.Controller.ControlledEntity==null)return 0;var ent=player.Controller.ControlledEntity.Entity;if(ent==null)return 0;var block=ent as IMyCubeBlock;if(block!=null&&block.CubeGrid!=null)return block.CubeGrid.EntityId;return ent.EntityId;}catch{return 0;}
        }
        private static bool TryGetObserverPosition(out Vector3D p)
        {
            p=Vector3D.Zero;try{var s=MyAPIGateway.Session;var pl=s==null?null:s.Player;if(pl!=null&&pl.Controller!=null&&pl.Controller.ControlledEntity!=null&&pl.Controller.ControlledEntity.Entity!=null){p=pl.Controller.ControlledEntity.Entity.GetPosition();return true;}if(s!=null&&s.Camera!=null){p=s.Camera.WorldMatrix.Translation;return true;}}catch{}return false;
        }

        private void EnsureChatRegistration()
        {
            if(_chatRegistered||MyAPIGateway.Utilities==null)return;try{MyAPIGateway.Utilities.MessageEnteredSender-=OnChatMessage;MyAPIGateway.Utilities.MessageEnteredSender+=OnChatMessage;_chatRegistered=true;}catch(Exception ex){Log("Chat registration deferred: "+ex.Message);}
        }

        private void OnChatMessage(ulong sender,string text,ref bool sendToOthers)
        {
            if(string.IsNullOrWhiteSpace(text))return;string cmd=text.Trim();if(!cmd.StartsWith("/zeoore",StringComparison.OrdinalIgnoreCase)&&!cmd.StartsWith("/orehud",StringComparison.OrdinalIgnoreCase)&&!cmd.StartsWith("/oreprobe",StringComparison.OrdinalIgnoreCase)&&!cmd.StartsWith("/tosore",StringComparison.OrdinalIgnoreCase))return;sendToOthers=false;
            _settings.ReloadIfChanged();
            var a=cmd.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);if(a.Length==1){_settings.Enabled=!_settings.Enabled;_settings.Save();Notify("ZEO ORE "+(_settings.Enabled?"ON":"OFF"),3000);return;}
            string sub=a[1].ToLowerInvariant();
            try
            {
                if(sub=="on"||sub=="off"){_settings.Enabled=sub=="on";_settings.Save();Notify("ZEO ORE "+(_settings.Enabled?"ON":"OFF"),3000);}
                else if(sub=="menu"||sub=="settings"){OpenConfigDialog();}
                else if(sub=="streamer")
                {
                    bool val=!_settings.StreamerMode;if(a.Length>=3){string v=a[2].ToLowerInvariant();if(v=="on")val=true;else if(v=="off")val=false;}
                    _settings.StreamerMode=val;_settings.Save();if(_overlay!=null)_overlay.SettingsChanged();Notify("ZEO ORE STREAMER "+(val?"ON - CAPTURE SAFE / FAIL CLOSED":"OFF"),4500);
                }
                else if(sub=="scan"||sub=="refresh"){if(_surveyor!=null)_surveyor.ForceDiscoverAndQueue(_frame,_settings.EffectiveSurveyRangeMeters,false);Notify("ZEO ORE: survey queue refreshed",3000);}
                else if(sub=="rescan"){if(_surveyor!=null)_surveyor.ForceDiscoverAndQueue(_frame,_settings.EffectiveSurveyRangeMeters,true);Notify("ZEO ORE: full rescan queued",3500);}
                else if(sub=="pin"){long id=_frameBuilder==null?0:_frameBuilder.SelectedEntityId;if(id==0)Notify("ZEO ORE: look at a roid first",3000);else Notify("ZEO ORE PIN "+(_surveyor.TogglePin(id)?"ON":"OFF"),3000);}
                else if(sub=="skip"){long id=_frameBuilder==null?0:_frameBuilder.SelectedEntityId;if(id==0)Notify("ZEO ORE: look at a roid first",3000);else Notify("ZEO ORE SKIP "+(_surveyor.ToggleSkip(id)?"ON":"OFF"),3000);}
                else if(sub=="clearskips"){Notify("ZEO ORE: cleared "+(_surveyor==null?0:_surveyor.ClearSkips())+" skips",3000);}
                else if(sub=="range")
                {
                    if(a.Length>=3&&a[2].Equals("max",StringComparison.OrdinalIgnoreCase)){_settings.MaxLoadedRange=true;_settings.Save();if(_surveyor!=null)_surveyor.ForceDiscoverAndQueue(_frame,_settings.EffectiveSurveyRangeMeters,false);Notify("ZEO ORE RANGE MAX LOADED",3000);}
                    else if(a.Length>=3){double km;if(double.TryParse(a[2],NumberStyles.Float,CultureInfo.InvariantCulture,out km)&&!double.IsNaN(km)&&!double.IsInfinity(km)){_settings.MaxLoadedRange=false;_settings.SurveyRangeMeters=Math.Max(1,Math.Min(1000,km))*1000;_settings.Save();if(_surveyor!=null)_surveyor.ForceDiscoverAndQueue(_frame,_settings.EffectiveSurveyRangeMeters,false);Notify("ZEO ORE RANGE "+km.ToString("0.0")+" km",3000);}}
                }
                else if(sub=="dump"){WriteSurveyLog(_surveyor==null?"NO SURVEYOR":_surveyor.BuildReport());Notify("ZEO ORE dump written",3500);}
                else if(sub=="status")
                {
                    Notify("ZEO ORE "+(_settings.Enabled?"ON":"OFF")+" | STREAM="+(_settings.StreamerMode?"SAFE":"OFF")+" | UI="+_settings.MenuKey+" | ROIDS="+(_surveyor==null?0:_surveyor.VisibleRoidCount)+" | READ="+(_surveyor==null?0:_surveyor.ReadyCount)+" | OVERLAY="+(_overlay!=null&&_overlay.Running?"RUN":"WAIT")+" | SENT="+(_overlay==null?0:_overlay.Sent),7000);
                }
                else ShowHelp();
            }
            catch(Exception ex){Log("Command ERROR: "+ex);Notify("ZEO ORE ERROR: "+ex.GetType().Name,4000);}
        }

        private static void ShowHelp(){MyAPIGateway.Utilities.ShowMessage("ZEO ORE","/zeoore on|off | menu | streamer on|off | scan | rescan");MyAPIGateway.Utilities.ShowMessage("ZEO ORE","/zeoore pin | skip | clearskips | range 45|max | status | dump");MyAPIGateway.Utilities.ShowMessage("ZEO ORE","Default UI key: PgUp. HOME remains reserved for ZeoCore.");}
        private static void WriteSurveyLog(string t){try{Directory.CreateDirectory(DataDirectory);File.WriteAllText(Path.Combine(DataDirectory,"survey-report.txt"),t??"",Encoding.UTF8);}catch(Exception ex){Log("Survey log ERROR: "+ex.Message);}}
        internal static void Notify(string text,int ms=3500){try{MyAPIGateway.Utilities?.ShowNotification(text,ms,"White");}catch{}}
        internal static void Log(string text){try{lock(LogLock){Directory.CreateDirectory(DataDirectory);string line=DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC' ")+text+Environment.NewLine;File.AppendAllText(SurveyLogPath,line);MyLog.Default.WriteLineAndConsole("["+Name+"] "+text);}}catch{}}
    }
}
