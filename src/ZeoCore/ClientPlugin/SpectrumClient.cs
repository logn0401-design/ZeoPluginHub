using System;
using System.Collections.Generic;
using System.IO;
using ProtoBuf;
using Sandbox.ModAPI;
using VRageMath;

namespace ZeoCore
{
    internal sealed class SpectrumClient : IDisposable
    {
        private const long Channel = 400790;
        private bool _registered;
        private Func<byte[]> _getClientDetections;
        private int _lastRequestFrame = -100000;
        private string _lastError = "";

        #pragma warning disable 0649 // protobuf fills these fields at runtime
        [ProtoContract]
        internal struct DetectionData
        {
            [ProtoMember(1)] public long EmitterId;
            [ProtoMember(2)] public Vector3D Position;
            [ProtoMember(3)] public Vector3D Velocity;
            [ProtoMember(4)] public float Strength;
            [ProtoMember(5)] public string FactionTag;
            [ProtoMember(6)] public Dictionary<string, float> EmissionTags;
            [ProtoMember(7)] public int DetectedAt;
            [ProtoMember(8)] public bool SelfOwned;
            [ProtoMember(9)] public string DetailText;
        }
        #pragma warning restore 0649

        public bool Ready { get { return _getClientDetections != null; } }
        public string LastError { get { return _lastError; } }

        public void Update(int frame)
        {
            if (MyAPIGateway.Utilities == null) return;
            if (!_registered)
            {
                _registered = true;
                MyAPIGateway.Utilities.RegisterMessageHandler(Channel, HandleMessage);
                Request(frame);
            }
            else if (!Ready && frame - _lastRequestFrame >= 300)
            {
                Request(frame);
            }
        }

        private void Request(int frame)
        {
            _lastRequestFrame = frame;
            try { MyAPIGateway.Utilities.SendModMessage(Channel, "init"); }
            catch (Exception ex) { _lastError = ex.GetType().Name; }
        }

        private void HandleMessage(object obj)
        {
            try
            {
                var readOnly = obj as IReadOnlyDictionary<string, Delegate>;
                if (readOnly != null)
                {
                    Delegate d;
                    if (readOnly.TryGetValue("GetClientDetections", out d))
                        _getClientDetections = d as Func<byte[]>;
                }
                else
                {
                    var dict = obj as IDictionary<string, Delegate>;
                    if (dict != null)
                    {
                        Delegate d;
                        if (dict.TryGetValue("GetClientDetections", out d))
                            _getClientDetections = d as Func<byte[]>;
                    }
                }

                if (_getClientDetections != null)
                {
                    _lastError = "";
                    Plugin.Log("Spectrum API READY: GetClientDetections");
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        public List<DetectionData> Read()
        {
            var result = new List<DetectionData>();
            if (_getClientDetections == null) return result;

            try
            {
                byte[] bytes = _getClientDetections();
                if (bytes == null || bytes.Length == 0) return result;

                try
                {
                    using (var ms = new MemoryStream(bytes, false))
                    {
                        var decoded = Serializer.Deserialize<List<DetectionData>>(ms);
                        if (decoded != null) return decoded;
                    }
                }
                catch
                {
                    // Some Spectrum builds use the game utility serializer. Keep
                    // this fallback because the live SDX2 API has changed before.
                }

                try
                {
                    var decoded = MyAPIGateway.Utilities.SerializeFromBinary<List<DetectionData>>(bytes);
                    if (decoded != null) return decoded;
                }
                catch { }
            }
            catch (Exception ex)
            {
                _lastError = ex.GetType().Name + ": " + ex.Message;
            }
            return result;
        }

        public void Dispose()
        {
            try
            {
                if (_registered && MyAPIGateway.Utilities != null)
                    MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, HandleMessage);
            }
            catch { }
            _registered = false;
            _getClientDetections = null;
        }
    }
}
