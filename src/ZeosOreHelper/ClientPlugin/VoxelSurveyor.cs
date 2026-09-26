using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ModAPI;
using VRage.Voxels;
using VRageMath;

namespace ZeosOreHelper
{
    public sealed class VoxelSurveyor
    {
        public enum SurveyState
        {
            Pending,
            Ready,
            NoOre,
            NoStorage,
            ReadError
        }

        public sealed class OreStat
        {
            public string Ore;
            public int Samples;
            public double EstimatedVolume;
            public double PercentOfSolid;
            public bool Rare;
            public byte MaterialIndex;
        }

        public sealed class RoidRecord
        {
            public long EntityId;
            internal ZeoOreShared.SdxScanData SdxScan;
            public IMyVoxelBase Voxel;
            public string StorageName;
            public Vector3D Position;
            public Vector3D SizeMeters;
            public double MaxDimensionMeters;
            public double Radius;
            public double Distance;
            public int LastSeenFrame;
            public int LastScanFrame;
            public int Lod;
            public int TotalSamples;
            public int SolidSamples;
            public bool Verified,HistoricalLead;
            public long FineTotalSamples,FineSolidSamples;
            public int VerificationLod=-1;

            // v0.5 simple ranking
            public int Score; // legacy/debug mirror of QualityIndex
            public double QualityIndex;
            public string Grade = "X";
            public bool MustHit;
            public bool Pinned;
            public bool Skipped;

            public SurveyState State = SurveyState.Pending;
            public string Error = "";
            public readonly List<OreStat> Ores = new List<OreStat>();
        }

        private const int StaleFrames = 600;
        private const int IsoLevel = 127;

        private readonly Dictionary<long, RoidRecord> _records = new Dictionary<long, RoidRecord>();
        private readonly HashSet<IMyEntity> _entityBuffer = new HashSet<IMyEntity>();
        private readonly Queue<long> _queue = new Queue<long>();
        private readonly HashSet<long> _queued = new HashSet<long>();
        private readonly double[] _materialContent=new double[256];
        private readonly int[] _materialCounts = new int[256];
        private readonly MyStorageData _cache = new MyStorageData(MyStorageDataTypeFlags.ContentAndMaterial);
        private readonly HudSettings _settings;
        private readonly RoidCacheStore _cacheStore;

        public VoxelSurveyor(HudSettings settings)
        {
            _settings = settings ?? new HudSettings();
            _cacheStore = new RoidCacheStore(_settings);
        }

        private int _lastDiscoverFrame = -10000;
        private int _lastScanFrame = -10000;
        private long _priorityEntityId;
        private bool _fineTurn;

        public int VisibleRoidCount { get { return _records.Count; } }
        public int ReadyCount { get { return _records.Values.Count(x => x.State == SurveyState.Ready || x.State == SurveyState.NoOre); } }
        public int ErrorCount { get { return _records.Values.Count(x => x.State == SurveyState.NoStorage || x.State == SurveyState.ReadError); } }
        public int PendingCount { get { return _records.Values.Count(x => x.State == SurveyState.Pending); } }
        public int PinnedCount { get { return _records.Values.Count(x => x.Pinned); } }
        public int SkippedCount { get { return _records.Values.Count(x => x.Skipped); } }
        public int CachedCount { get { return _cacheStore != null ? _cacheStore.Count : 0; } }
        public IEnumerable<RoidRecord> Records { get { return _records.Values; } }

        public void Reset()
        {
            try { if (_cacheStore != null) _cacheStore.FlushNow(); } catch { }
            _verification=null;_verifyQueue.Clear();_verifyPending.Clear();
            _records.Clear();
            _queue.Clear();
            _queued.Clear();
            _entityBuffer.Clear();
            _priorityEntityId = 0;
            _lastDiscoverFrame = -10000;
            _lastScanFrame = -10000;
        }

        public void Update(int frame, double rangeMeters)
        {
            if (MyAPIGateway.Session == null || MyAPIGateway.Entities == null)
                return;

            if (frame - _lastDiscoverFrame >= _settings.DiscoverEveryFrames)
            {
                _lastDiscoverFrame = frame;
                Discover(frame, rangeMeters);
            }

            if (frame - _lastScanFrame >= _settings.ScanEveryFrames)
            {
                if((_fineTurn||_queue.Count==0)&&StepVerification(frame)){_fineTurn=false;_lastScanFrame=frame;try{_cacheStore.Tick(frame);}catch{}return;}
                _fineTurn=true;
                var id = DequeueNext(frame);
                if (id != 0)
                {
                    _lastScanFrame = frame;
                    RoidRecord r;
                    if (_records.TryGetValue(id, out r))
                        ScanRoid(r, frame);
                }
            }

            try { _cacheStore.Tick(frame); } catch { }
        }

        public void ForceDiscoverAndQueue(int frame, double rangeMeters, bool clearResults)
        {
            if (clearResults)
            {
                _verification=null;_verifyQueue.Clear();_verifyPending.Clear();
                foreach (var r in _records.Values)
                {
                    r.State = SurveyState.Pending;
                    r.Error = "";
                    r.Ores.Clear();r.Verified=false;r.VerificationLod=-1;r.FineTotalSamples=0;r.FineSolidSamples=0;
                    r.Score = 0;
                    r.QualityIndex = 0;
                    r.Grade = "X";
                    r.MustHit = false;
                    r.SolidSamples = 0;
                    r.TotalSamples = 0;
                    r.LastScanFrame = 0;
                }
                _queue.Clear();
                _queued.Clear();
            }

            Discover(frame, rangeMeters);
            foreach (var r in _records.Values)
                QueueIfNeeded(r, frame, true);
        }

        private sealed class FineScan {
            internal RoidRecord Record;internal IMyStorage Storage;internal int Lod;internal ZeoOreShared.OreScanChunks Chunks;
            internal long Total,Solid;internal readonly long[] Counts=new long[256];internal readonly double[] Content=new double[256];
        }
        private FineScan _verification;
        private readonly Queue<long> _verifyQueue=new Queue<long>();private readonly HashSet<long> _verifyPending=new HashSet<long>();
        internal int VerificationPending {get{return _verifyPending.Count;}}
        internal bool QueueVerification(long id){RoidRecord r;if(_verifyPending.Count>=8||!_records.TryGetValue(id,out r)||r.State!=SurveyState.Ready||r.Verified||r.Lod<=0||!_verifyPending.Add(id))return false;_verifyQueue.Enqueue(id);return true;}
        private bool StepVerification(int frame){
            // Refill from retained coarse results when the bounded queue drains.
            // Otherwise a discovery burst can permanently lose record candidates.
            var search=Plugin.Instance?.Search;
            if(_verification==null&&_verifyQueue.Count==0&&search!=null&&search.AutoLearn&&search.Values.B("VerifyNewRecords",true)){
                foreach(var candidate in _records.Values.Where(r=>r.State==SurveyState.Ready&&!r.Verified&&r.Lod>0&&r.Ores.Any(o=>_settings.IsOreEnabled(o.Ore)&&o.EstimatedVolume>0)).OrderByDescending(r=>r.Ores.Where(o=>_settings.IsOreEnabled(o.Ore)).Sum(o=>o.EstimatedVolume)).Take(8))QueueVerification(candidate.EntityId);
            }
            if(_verification==null){while(_verifyQueue.Count>0){long id=_verifyQueue.Dequeue();RoidRecord r;if(!_records.TryGetValue(id,out r)||r.Voxel==null||r.Voxel.Closed){_verifyPending.Remove(id);continue;}try{var storage=r.Voxel.Storage;if(storage==null||storage.Closed){_verifyPending.Remove(id);continue;}int lod=Math.Max(0,r.Lod-1);var size=storage.Size;var dims=new Vector3I(((size.X-1)>>lod)+1,((size.Y-1)>>lod)+1,((size.Z-1)>>lod)+1);int edge=16;while((long)(edge*2)*(edge*2)*(edge*2)<=Math.Min(262144,_settings.MaxSampleCells))edge*=2;_verification=new FineScan{Record=r,Storage=storage,Lod=lod,Chunks=new ZeoOreShared.OreScanChunks(dims.X,dims.Y,dims.Z,edge)};break;}catch{_verifyPending.Remove(id);}}}
            var job=_verification;if(job==null)return false;
            try{
                if(job.Record.Voxel==null||job.Record.Voxel.Closed||job.Storage.Closed||!ReferenceEquals(job.Record.Voxel.Storage,job.Storage))throw new InvalidOperationException("Asteroid unloaded during verification");
                var chunk=job.Chunks.Current;var min=new Vector3I(chunk.X,chunk.Y,chunk.Z);var max=new Vector3I(chunk.MaxX,chunk.MaxY,chunk.MaxZ);
                job.Storage.PinAndExecute(storage=>{_cache.Resize(max-min+Vector3I.One);storage.ReadRange(_cache,MyStorageDataTypeFlags.ContentAndMaterial,job.Lod,min,max);for(int i=0;i<_cache.SizeLinear;i++){int material=_cache.Material(i);byte content=_cache.Content(i);job.Content[material]+=content/255.0;job.Total++;if(content>IsoLevel){job.Solid++;job.Counts[material]++;}}});
                if(!job.Chunks.Advance()){var measured=new Dictionary<string,OreStat>(StringComparer.OrdinalIgnoreCase);for(int i=0;i<256;i++){if(job.Counts[i]<=0)continue;var def=MyDefinitionManager.Static.GetVoxelMaterialDefinition((byte)i);if(def==null)continue;string ore=CleanOreName(def.MinedOre);if(string.IsNullOrWhiteSpace(ore)||ore.Equals("Stone",StringComparison.OrdinalIgnoreCase))continue;OreStat stat;if(!measured.TryGetValue(ore,out stat)){stat=new OreStat{Ore=ore,MaterialIndex=(byte)i,Rare=def.IsRare};measured[ore]=stat;}stat.Samples=(int)Math.Min(int.MaxValue,(long)stat.Samples+job.Counts[i]);stat.EstimatedVolume+=ZeoOreShared.OreLearningStore.Volume(job.Content[i],job.Lod,MyVoxelConstants.VOXEL_SIZE_IN_METRES);}
                    job.Record.Ores.Clear();foreach(var stat in measured.Values){stat.PercentOfSolid=job.Solid>0?stat.Samples*100.0/job.Solid:0;job.Record.Ores.Add(stat);}job.Record.Verified=true;job.Record.VerificationLod=job.Lod;job.Record.FineTotalSamples=job.Total;job.Record.FineSolidSamples=job.Solid;job.Record.State=measured.Count>0?SurveyState.Ready:SurveyState.NoOre;job.Record.LastScanFrame=frame;RecalculateRoid(job.Record);Plugin.Instance?.ObserveScan(job.Record);_cacheStore.Update(job.Record);_verifyPending.Remove(job.Record.EntityId);_verification=null;
                }
            }catch(Exception ex){Plugin.Log("Fine scan deferred: "+ex.Message);_verifyPending.Remove(job.Record.EntityId);_verification=null;}
            return true;
        }

        public void Prioritize(long entityId)
        {
            if (entityId == 0) return;
            RoidRecord r;
            if (!_records.TryGetValue(entityId, out r)) return;
            if (r.State != SurveyState.Pending && r.State != SurveyState.ReadError && r.State != SurveyState.NoStorage) return;
            _priorityEntityId = entityId;
        }

        public RoidRecord GetRecord(long entityId)
        {
            RoidRecord r;
            return entityId != 0 && _records.TryGetValue(entityId, out r) ? r : null;
        }

        public bool TogglePin(long entityId)
        {
            var r = GetRecord(entityId);
            if (r == null) return false;
            r.Pinned = !r.Pinned;
            try
            {
                if (r.Pinned) _cacheStore.Update(r);
                else
                {
                    _cacheStore.Update(r);
                    _cacheStore.RemoveIfUnpinned(r);
                }
                _cacheStore.FlushNow();
            }
            catch { }
            return r.Pinned;
        }

        public bool ToggleSkip(long entityId)
        {
            var r = GetRecord(entityId);
            if (r == null) return false;
            r.Skipped = !r.Skipped;
            return r.Skipped;
        }

        public int ClearSkips()
        {
            var n = 0;
            foreach (var r in _records.Values)
            {
                if (!r.Skipped) continue;
                r.Skipped = false;
                n++;
            }
            return n;
        }

        public void CacheSettingsChanged()
        {
            try
            {
                _cacheStore.Reevaluate(_records.Values);
                _cacheStore.SettingsChanged();
            }
            catch { }
        }

        public int ClearCurrentSectorCache()
        {
            try { return _cacheStore.ClearCurrentSector(GetObserverPosition()); }
            catch { return 0; }
        }

        public int ClearOldCache()
        {
            try { return _cacheStore.ClearOld(); }
            catch { return 0; }
        }

        public int ClearAllCache(bool keepPinned)
        {
            try { return _cacheStore.ClearAll(keepPinned); }
            catch { return 0; }
        }

        private void Discover(int frame, double rangeMeters)
        {
            _entityBuffer.Clear();
            try
            {
                MyAPIGateway.Entities.GetEntities(_entityBuffer, delegate(IMyEntity e)
                {
                    return e is IMyVoxelBase;
                });
            }
            catch (Exception ex)
            {
                Plugin.Log("Voxel discovery failed: " + ex.Message);
                return;
            }

            var observer = GetObserverPosition();
            foreach (var ent in _entityBuffer)
            {
                var voxel = ent as IMyVoxelBase;
                if (!IsAsteroidCandidate(voxel))
                    continue;

                Vector3D pos;
                Vector3D sizeMeters;
                double radius;
                try
                {
                    var box = voxel.WorldAABB;
                    pos = box.Center;
                    sizeMeters = box.Max - box.Min;
                    radius = sizeMeters.Length() * 0.5;
                }
                catch
                {
                    pos = voxel.GetPosition();
                    sizeMeters = Vector3D.Zero;
                    radius = 0;
                }

                var maxDimension = Math.Max(sizeMeters.X, Math.Max(sizeMeters.Y, sizeMeters.Z));
                var distance = Vector3D.Distance(observer, pos);
                if (distance - radius > rangeMeters)
                    continue;

                RoidRecord r;
                if (!_records.TryGetValue(voxel.EntityId, out r))
                {
                    r = new RoidRecord
                    {
                        EntityId = voxel.EntityId,
                        Voxel = voxel,
                        StorageName = SafeStorageName(voxel),
                        Position = pos,
                        SizeMeters = sizeMeters,
                        MaxDimensionMeters = maxDimension,
                        Radius = radius,
                        Distance = distance,
                        LastSeenFrame = frame,
                        State = SurveyState.Pending
                    };
                    try { _cacheStore.ApplyFlags(r); } catch { }
                    _records.Add(r.EntityId, r);
                    QueueIfNeeded(r, frame, true);
                }
                else
                {
                    r.Voxel = voxel;
                    r.StorageName = SafeStorageName(voxel);
                    r.Position = pos;
                    r.SizeMeters = sizeMeters;
                    r.MaxDimensionMeters = maxDimension;
                    r.Radius = radius;
                    r.Distance = distance;
                    r.LastSeenFrame = frame;
                    QueueIfNeeded(r, frame, false);
                }
            }

            var remove = new List<long>();
            foreach (var pair in _records)
            {
                if (frame - pair.Value.LastSeenFrame > StaleFrames)
                    remove.Add(pair.Key);
            }
            for (var i = 0; i < remove.Count; i++)
            {
                _records.Remove(remove[i]);
                _queued.Remove(remove[i]);
                if (_priorityEntityId == remove[i]) _priorityEntityId = 0;
            }
        }

        private long DequeueNext(int frame)
        {
            if (_priorityEntityId != 0)
            {
                var id = _priorityEntityId;
                _priorityEntityId = 0;
                RoidRecord r;
                if (_records.TryGetValue(id, out r) && ShouldScan(r, frame))
                {
                    _queued.Remove(id);
                    return id;
                }
            }

            while (_queue.Count > 0)
            {
                var id = _queue.Dequeue();
                _queued.Remove(id);
                RoidRecord r;
                if (_records.TryGetValue(id, out r) && ShouldScan(r, frame))
                    return id;
            }
            return 0;
        }

        private void QueueIfNeeded(RoidRecord r, int frame, bool force)
        {
            if (r == null || _queued.Contains(r.EntityId)) return;
            if (!force && !ShouldScan(r, frame)) return;
            _queue.Enqueue(r.EntityId);
            _queued.Add(r.EntityId);
        }

        private bool ShouldScan(RoidRecord r, int frame)
        {
            if (r == null) return false;
            if (r.State == SurveyState.Pending)
                return true;
            if (r.State == SurveyState.ReadError || r.State == SurveyState.NoStorage)
                return r.LastScanFrame <= 0 || frame - r.LastScanFrame >= 600;
            return r.LastScanFrame > 0 && frame - r.LastScanFrame >= _settings.RescanAfterFrames;
        }

        private void ScanRoid(RoidRecord r, int frame)
        {
            if (r == null || r.Voxel == null)
                return;

            r.Error = "";
            r.Ores.Clear();
            r.Score = 0;
            r.QualityIndex = 0;
            r.Grade = "X";
            r.MustHit = false;
            r.SolidSamples = 0;
            r.TotalSamples = 0;
            r.LastScanFrame = frame;

            IMyStorage storage = null;
            try { storage = r.Voxel.Storage; } catch { }
            if (storage == null || storage.Closed)
            {
                r.State = SurveyState.NoStorage;
                r.Error = "NO STORAGE";
                return;
            }

            try
            {
                storage.PinAndExecute(delegate(IMyStorage pinned)
                {
                    SamplePinnedStorage(r, pinned);
                });
            }
            catch (Exception ex)
            {
                r.State = SurveyState.ReadError;
                r.Error = ex.GetType().Name + ": " + ex.Message;
                Plugin.Log("Roid " + r.EntityId + " read failed: " + r.Error);
            }
        }

        private void SamplePinnedStorage(RoidRecord r, IMyStorage storage)
        {
            Array.Clear(_materialCounts, 0, _materialCounts.Length);Array.Clear(_materialContent,0,256);

            var size = storage.Size;
            var lod = ChooseLod(size);
            var max = new Vector3I(
                Math.Max(0, (size.X - 1) >> lod),
                Math.Max(0, (size.Y - 1) >> lod),
                Math.Max(0, (size.Z - 1) >> lod));
            var min = Vector3I.Zero;
            var dims = max - min + Vector3I.One;

            long totalLong = (long)dims.X * dims.Y * dims.Z;
            if (totalLong <= 0 || totalLong > int.MaxValue)
                throw new InvalidOperationException("Invalid LOD sample size " + dims);

            _cache.Resize(dims);
            storage.ReadRange(_cache, MyStorageDataTypeFlags.ContentAndMaterial, lod, min, max);

            var solid = 0;
            for (var i = 0; i < _cache.SizeLinear; i++)
            {
                _materialContent[_cache.Material(i)]+=_cache.Content(i)/255.0;
                if (_cache.Content(i) <= IsoLevel)
                    continue;
                solid++;
                _materialCounts[_cache.Material(i)]++;
            }

            r.Verified=lod==0;r.VerificationLod=lod==0?0:-1;
            r.Lod = lod;
            r.TotalSamples = _cache.SizeLinear;
            r.SolidSamples = solid;

            var aggregate = new Dictionary<string, OreStat>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < _materialCounts.Length; i++)
            {
                var count = _materialCounts[i];
                if (count <= 0) continue;

                MyVoxelMaterialDefinition def = null;
                try { def = MyDefinitionManager.Static.GetVoxelMaterialDefinition((byte)i); }
                catch { }
                if (def == null) continue;

                var ore = CleanOreName(def.MinedOre);
                if (string.IsNullOrWhiteSpace(ore)) continue;
                if (ore.Equals("Stone", StringComparison.OrdinalIgnoreCase)) continue;

                OreStat stat;
                if (!aggregate.TryGetValue(ore, out stat))
                {
                    stat = new OreStat { Ore = ore, Samples = 0, Rare = def.IsRare, MaterialIndex = (byte)i };
                    aggregate.Add(ore, stat);
                }
                stat.Samples += count;
                stat.EstimatedVolume+=ZeoOreShared.OreLearningStore.Volume(_materialContent[i],lod,MyVoxelConstants.VOXEL_SIZE_IN_METRES);
                if (def.IsRare) stat.Rare = true;
            }

            foreach (var pair in aggregate)
            {
                pair.Value.PercentOfSolid = solid > 0 ? pair.Value.Samples * 100.0 / solid : 0.0;
                r.Ores.Add(pair.Value);
            }

            r.State = r.Ores.Count > 0 ? SurveyState.Ready : SurveyState.NoOre;
            RecalculateRoid(r);
            Plugin.Instance?.ObserveScan(r);
            try { _cacheStore.Update(r); } catch { }
        }

        private int ChooseLod(Vector3I size)
        {
            var lod = _settings.PreferredLod;
            while (lod < 9)
            {
                var x = Math.Max(1, ((size.X - 1) >> lod) + 1);
                var y = Math.Max(1, ((size.Y - 1) >> lod) + 1);
                var z = Math.Max(1, ((size.Z - 1) >> lod) + 1);
                var cells = (long)x * y * z;
                if (cells <= _settings.MaxSampleCells)
                    break;
                lod++;
            }
            return lod;
        }

        private static bool IsAsteroidCandidate(IMyVoxelBase voxel)
        {
            if (voxel == null || voxel.Closed || voxel.MarkedForClose)
                return false;

            var type = voxel.GetType().FullName ?? voxel.GetType().Name ?? "";
            if (type.IndexOf("Planet", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (type.IndexOf("VoxelPhysics", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            try
            {
                var storage = voxel.Storage;
                if (storage != null && !storage.Closed)
                {
                    var s = storage.Size;
                    var maxDim = Math.Max(s.X, Math.Max(s.Y, s.Z));
                    if (maxDim > 4096)
                        return false;
                }
            }
            catch { }

            return true;
        }

        public static Vector3D GetObserverPosition()
        {
            try
            {
                var player = MyAPIGateway.Session.Player;
                var entity = player != null && player.Controller != null && player.Controller.ControlledEntity != null
                    ? player.Controller.ControlledEntity.Entity
                    : null;
                if (entity != null)
                    return entity.GetPosition();
            }
            catch { }
            try { return MyAPIGateway.Session.Camera.Position; }
            catch { return Vector3D.Zero; }
        }

        private static string SafeStorageName(IMyVoxelBase voxel)
        {
            try { return voxel.StorageName ?? ""; }
            catch { return ""; }
        }

        public bool HasWantedOre(RoidRecord r)
        {
            if (r == null) return false;
            for (var i = 0; i < r.Ores.Count; i++)
            {
                var o = r.Ores[i];
                if (!_settings.IsOreEnabled(o.Ore)) continue;
                if (_settings.GetOreWeight(o.Ore) <= 0) continue;
                if (o.PercentOfSolid + 0.000001 < _settings.EffectiveOreMinimumPercent(o.Ore)) continue;
                return true;
            }
            return false;
        }

        public bool MatchesWantedOreMode(RoidRecord r)
        {
            if (r == null) return false;
            if (!_settings.RequireAllWantedOres) return HasWantedOre(r);

            var enabled = 0;
            for (var i = 0; i < HudSettings.KnownOres.Length; i++)
            {
                var ore = HudSettings.KnownOres[i];
                if (!_settings.IsOreEnabled(ore) || _settings.GetOreWeight(ore) <= 0) continue;
                enabled++;
                var found = false;
                for (var j = 0; j < r.Ores.Count; j++)
                {
                    if (!r.Ores[j].Ore.Equals(ore, StringComparison.OrdinalIgnoreCase)) continue;
                    if (r.Ores[j].PercentOfSolid + 0.000001 >= _settings.EffectiveOreMinimumPercent(ore))
                        found = true;
                    break;
                }
                if (!found) return false;
            }
            return enabled > 0;
        }

        public bool GradeMeetsMinimum(RoidRecord r)
        {
            if (r == null) return false;
            return HudSettings.GradeRank(r.Grade) >= HudSettings.GradeRank(_settings.MinimumGrade);
        }

        public void RecalculateScores()
        {
            foreach (var r in _records.Values) RecalculateRoid(r);
            try { _cacheStore.Reevaluate(_records.Values); } catch { }
        }

        private void RecalculateRoid(RoidRecord r)
        {
            if (r == null) return;

            r.Ores.Sort(delegate(OreStat a, OreStat b)
            {
                var aOn = _settings.IsOreEnabled(a.Ore) && _settings.GetOreWeight(a.Ore) > 0 &&
                          a.PercentOfSolid + 0.000001 >= _settings.EffectiveOreMinimumPercent(a.Ore);
                var bOn = _settings.IsOreEnabled(b.Ore) && _settings.GetOreWeight(b.Ore) > 0 &&
                          b.PercentOfSolid + 0.000001 >= _settings.EffectiveOreMinimumPercent(b.Ore);
                if (aOn != bOn) return bOn.CompareTo(aOn);
                var wa = aOn ? _settings.GetOreWeight(a.Ore) : 0;
                var wb = bOn ? _settings.GetOreWeight(b.Ore) : 0;
                if (wa != wb) return wb.CompareTo(wa);
                if (a.PercentOfSolid != b.PercentOfSolid) return b.PercentOfSolid.CompareTo(a.PercentOfSolid);
                if (a.Samples != b.Samples) return b.Samples.CompareTo(a.Samples);
                return string.Compare(a.Ore, b.Ore, StringComparison.OrdinalIgnoreCase);
            });

            r.MustHit = false;
            var weightedConcentration = 0.0;
            var qualifying = 0;
            var icePercent = 0.0;

            for (var i = 0; i < r.Ores.Count; i++)
            {
                var o = r.Ores[i];
                if (!_settings.IsOreEnabled(o.Ore)) continue;
                var w = _settings.GetOreWeight(o.Ore);
                if (w <= 0) continue;
                if (o.PercentOfSolid + 0.000001 < _settings.EffectiveOreMinimumPercent(o.Ore)) continue;

                qualifying++;
                if (o.Ore.Equals("Ice", StringComparison.OrdinalIgnoreCase))
                {
                    icePercent = Math.Max(icePercent, o.PercentOfSolid);
                    // Ice naturally occupies a much larger fraction than metal ores.
                    // Normalize it to a 0..1 concentration before applying the same
                    // user weight so a 50% Ice roid does not dwarf every metal roid.
                    weightedConcentration += w * (o.PercentOfSolid / 100.0);
                    if (o.PercentOfSolid + 0.000001 >= _settings.PureIceMustPercent)
                        r.MustHit = true;
                }
                else
                {
                    weightedConcentration += w * o.PercentOfSolid;
                }
            }

            if (qualifying == 0)
            {
                r.QualityIndex = 0;
                r.Score = 0;
                r.Grade = "X";
                return;
            }

            // Transparent metal-ore model:
            // sum(weight * sampled-solid %) * 10, optionally with a mild size
            // factor. Ice is normalized above because its useful percentages are
            // naturally tens-to-hundreds instead of sub-1% like most metal ores.
            // Distance NEVER changes grade; it only changes order in
            // QUALITY + DISTANCE mode.
            var quality = weightedConcentration * 10.0;
            if (_settings.UseSizeFactor && r.MaxDimensionMeters > 1)
            {
                var sizeFactor = Math.Sqrt(Math.Max(1.0, r.MaxDimensionMeters) / 300.0);
                sizeFactor = Math.Max(0.75, Math.Min(1.35, sizeFactor));
                quality *= sizeFactor;
            }

            if (r.MustHit) quality = Math.Max(_settings.GradeS, quality);

            r.QualityIndex = Math.Min(999.0, quality);
            r.Score = (int)Math.Round(r.QualityIndex);

            if (r.MustHit || r.QualityIndex >= _settings.GradeS) r.Grade = "S";
            else if (r.QualityIndex >= _settings.GradeA) r.Grade = "A";
            else if (r.QualityIndex >= _settings.GradeB) r.Grade = "B";
            else if (r.QualityIndex >= _settings.GradeC) r.Grade = "C";
            else r.Grade = "D";

            // Ice usability floor requested by the user. These are intentionally
            // simple and can be recalibrated later from live mining yield data.
            if (!r.MustHit && icePercent > 0.0)
            {
                var iceFloor = icePercent >= 75.0 ? "A" :
                               icePercent >= 40.0 ? "B" :
                               icePercent >= 10.0 ? "C" : "D";
                if (HudSettings.GradeRank(iceFloor) > HudSettings.GradeRank(r.Grade))
                    r.Grade = iceFloor;
            }
        }

        public double GetRankValue(RoidRecord r)
        {
            if (r == null) return double.MinValue;
            if (_settings.RankingMode == "nearest") return -r.Distance;
            var q = r.QualityIndex;
            if (_settings.RankingMode == "quality_distance")
            {
                // Mild travel penalty: at 60 km the quality rank is reduced by at
                // most 35%. Grade itself remains stable.
                var penalty = Math.Min(0.35, Math.Max(0.0, r.Distance) / 60000.0 * 0.35);
                q *= 1.0 - penalty;
            }
            return q;
        }

        private static string CleanOreName(string ore)
        {
            if (string.IsNullOrWhiteSpace(ore)) return "";
            ore = ore.Trim();
            if (ore.StartsWith("sdx_", StringComparison.OrdinalIgnoreCase))
            {
                var p = ore.LastIndexOf('_');
                if (p >= 0 && p + 1 < ore.Length) ore = ore.Substring(p + 1);
            }
            return ore;
        }

        public static string OreCode(string ore)
        {
            if (string.IsNullOrWhiteSpace(ore)) return "?";
            var n = ore.ToLowerInvariant();
            if (n.Contains("uranium")) return "U";
            if (n.Contains("tungsten")) return "W";
            if (n.Contains("titanium")) return "Ti";
            if (n.Contains("platinum")) return "Pt";
            if (n.Contains("gold")) return "Au";
            if (n.Contains("silver")) return "Ag";
            if (n.Contains("copper")) return "Cu";
            if (n.Contains("lead")) return "Pb";
            if (n.Contains("cobalt")) return "Co";
            if (n.Contains("magnesium")) return "Mg";
            if (n.Contains("nickel")) return "Ni";
            if (n.Contains("iron")) return "Fe";
            if (n.Contains("silicon")) return "Si";
            if (n.Contains("boron")) return "B";
            if (n.Contains("organic")) return "Org";
            if (n.Contains("ice")) return "Ice";
            return ore.Length <= 3 ? ore : ore.Substring(0, 3);
        }

        public string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ZEOS ORE HELPER v0.5 MINING GRADES");
            sb.AppendLine("Visible roids: " + VisibleRoidCount);
            sb.AppendLine("Read: " + ReadyCount + " | Pending: " + PendingCount + " | Errors: " + ErrorCount);
            sb.AppendLine("Pinned: " + PinnedCount + " | Skipped: " + SkippedCount + " | Disk cache: " + CachedCount);
            sb.AppendLine("Ranking: " + _settings.RankingMode + " | Min grade: " + _settings.MinimumGrade +
                          " | Cache: " + (_settings.CacheEnabled ? "ON" : "OFF") + " " + _settings.CacheMinimumGrade + "+");
            sb.AppendLine();

            var list = _records.Values.ToList();
            list.Sort(delegate(RoidRecord a, RoidRecord b)
            {
                if (a.Pinned != b.Pinned) return b.Pinned.CompareTo(a.Pinned);
                var ar = HudSettings.GradeRank(a.Grade);
                var br = HudSettings.GradeRank(b.Grade);
                if (ar != br) return br.CompareTo(ar);
                var av = GetRankValue(a);
                var bv = GetRankValue(b);
                if (Math.Abs(av - bv) > 0.0001) return bv.CompareTo(av);
                return a.Distance.CompareTo(b.Distance);
            });

            for (var i = 0; i < list.Count; i++)
            {
                var r = list[i];
                sb.Append("ROID ").Append(r.EntityId)
                  .Append(" | ").Append((r.Distance / 1000.0).ToString("0.00")).Append(" km")
                  .Append(" | STATE ").Append(r.State)
                  .Append(" | GRADE ").Append(r.Grade).Append(r.MustHit ? " MUST" : "")
                  .Append(" | QUALITY ").Append(r.QualityIndex.ToString("0.0"))
                  .Append(" | PIN ").Append(r.Pinned ? "Y" : "N")
                  .Append(" | SKIP ").Append(r.Skipped ? "Y" : "N")
                  .Append(" | DIA ").Append(r.MaxDimensionMeters.ToString("0")).Append(" m")
                  .Append(" | SIZE ").Append(r.SizeMeters.X.ToString("0")).Append('x').Append(r.SizeMeters.Y.ToString("0")).Append('x').Append(r.SizeMeters.Z.ToString("0")).Append(" m")
                  .Append(" | LOD ").Append(r.Lod)
                  .Append(" | SOLID ").Append(r.SolidSamples).Append('/').Append(r.TotalSamples)
                  .AppendLine();
                sb.Append("  STORAGE ").AppendLine(r.StorageName ?? "");
                if (!string.IsNullOrWhiteSpace(r.Error))
                    sb.Append("  ERROR ").AppendLine(r.Error);
                if (r.Ores.Count == 0)
                {
                    sb.AppendLine("  ORES none detected");
                }
                else
                {
                    sb.Append("  ORES ");
                    for (var j = 0; j < r.Ores.Count; j++)
                    {
                        if (j > 0) sb.Append(" | ");
                        var o = r.Ores[j];
                        sb.Append(o.Ore).Append(' ').Append(o.Samples)
                          .Append(" (").Append(o.PercentOfSolid.ToString("0.00")).Append("%)")
                          .Append(_settings.IsOreEnabled(o.Ore) ? " W" + _settings.GetOreWeight(o.Ore) : " OFF");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
