using System;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;

internal sealed class ApiTestProxy : RealProxy
{
    internal ApiTestProxy(Type type):base(type){}
    public override IMessage Invoke(IMessage message)
    {
        var call=(IMethodCallMessage)message;
        var type=((MethodInfo)call.MethodBase).ReturnType;
        object result=type==typeof(void)||!type.IsValueType?null:Activator.CreateInstance(type);
        return new ReturnMessage(result,null,0,call.LogicalCallContext,call);
    }
}
