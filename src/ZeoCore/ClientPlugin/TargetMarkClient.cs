using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using VRageMath;

namespace ZeoCore
{
    internal sealed class TargetMarkObservation
    {
        internal string[] Ids;
        internal Vector3D Position;
        internal double Age,Remaining;
    }
    // One bounded worker, immutable game-thread snapshots, no game objects cross threads.
    internal sealed class TargetMarkClient:IDisposable
    {
        private readonly Uri _uri;
        private readonly string _device,_key;
        private readonly object _sync=new object();
        private readonly JavaScriptSerializer _json=new JavaScriptSerializer{MaxJsonLength=131072};
        private HttpWebRequest _request;
        private int _busy,_generation;
        private bool _disposed;
        private string _context="";
        private TargetMarkObservation[] _rows=new TargetMarkObservation[0];
        private long _received;
        private string _status="Ready";
        internal string Status { get {lock(_sync)return _status;} }
        internal TargetMarkClient(ZeoConfig config){_uri=new Uri(config.GetAccountBaseUri(),"/v1/zeo/device/target-marks");_device=config.DeviceId;_key=config.GetDeviceKey();}
        internal void Context(string context)
        {
            lock(_sync){if(_context==context)return;_context=context;_generation++;_rows=new TargetMarkObservation[0];_request?.Abort();}
        }
        internal List<TargetMarkObservation> Snapshot()
        {
            lock(_sync){var result=new List<TargetMarkObservation>(_rows.Length);double elapsed=(Stopwatch.GetTimestamp()-_received)/(double)Stopwatch.Frequency;
                foreach(var r in _rows)if(r.Age+elapsed<=10&&r.Remaining>elapsed)result.Add(new TargetMarkObservation{Ids=r.Ids,Position=r.Position,Age=r.Age+elapsed,Remaining=r.Remaining-elapsed});return result;}
        }
        internal bool Send(Dictionary<string,object> body)
        {
            int generation;
            lock(_sync){if(_disposed||_busy!=0)return false;_busy=1;generation=_generation;}
            body["device_id"]=_device;body["device_key"]=_key;body["schema"]="zeo.target-mark.v1";
            ThreadPool.QueueUserWorkItem(_=>{
                try{
                    long started=Stopwatch.GetTimestamp();
                    var data=Encoding.UTF8.GetBytes(_json.Serialize(body));
                    var req=(HttpWebRequest)WebRequest.Create(_uri);req.Method="POST";req.ContentType="application/json";req.AllowAutoRedirect=false;req.Timeout=5000;req.ReadWriteTimeout=5000;req.Proxy=null;req.ContentLength=data.Length;
                    lock(_sync){if(_disposed||generation!=_generation)return;_request=req;}
                    using(var s=req.GetRequestStream())s.Write(data,0,data.Length);
                    string raw;
                    using(var response=req.GetResponse())using(var input=response.GetResponseStream())using(var buffer=new MemoryStream()){
                        var bytes=new byte[4096];int n;while((n=input.Read(bytes,0,bytes.Length))>0){if(buffer.Length+n>131072)throw new InvalidDataException();buffer.Write(bytes,0,n);}raw=Encoding.UTF8.GetString(buffer.ToArray());
                    }
                    var parsed=_json.Deserialize<Dictionary<string,object>>(raw);
                    if((string)parsed["schema"]!="zeo.target-mark.v1")throw new InvalidDataException();
                    var result=new List<TargetMarkObservation>();
                    foreach(var value in (IEnumerable)parsed["marks"]){
                        if(result.Count>=64)break;var row=value as Dictionary<string,object>;if(row==null)continue;
                        var ids=new List<string>();foreach(var id in (IEnumerable)row["target_ids"])if(ids.Count<8)ids.Add((string)id);
                        var xyz=new List<double>();foreach(var v in (IEnumerable)row["position"])xyz.Add(Convert.ToDouble(v));
                        if(xyz.Count!=3||xyz.Exists(v=>double.IsNaN(v)||double.IsInfinity(v)||Math.Abs(v)>1e12))continue;
                        result.Add(new TargetMarkObservation{Ids=ids.ToArray(),Position=new Vector3D(xyz[0],xyz[1],xyz[2]),Age=Math.Max(0,Convert.ToDouble(row["age_ms"])/1000),Remaining=Math.Min(90,(Convert.ToDouble(row["expires_at_ms"])-Convert.ToDouble(parsed["server_time_ms"]))/1000)});
                    }
                    lock(_sync)if(!_disposed&&generation==_generation){_rows=result.ToArray();_received=started;_status="Connected // "+result.Count+" attack mark(s)";}
                }catch(WebException ex){lock(_sync)if(generation==_generation){_rows=new TargetMarkObservation[0];var r=ex.Response as HttpWebResponse;_status=r==null?"Target network unavailable":"Target network HTTP "+(int)r.StatusCode;r?.Dispose();}}
                catch{lock(_sync)if(generation==_generation){_rows=new TargetMarkObservation[0];_status="Target response unavailable";}}
                finally{lock(_sync){_busy=0;_request=null;}}
            });return true;
        }
        public void Dispose(){lock(_sync){_disposed=true;_generation++;_rows=new TargetMarkObservation[0];_request?.Abort();}}
    }
}
