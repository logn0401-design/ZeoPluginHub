namespace ZeoCore
{
    internal static class NetworkTrackIdentity
    {
        internal static long Resolve(long id,long entity,long emitter,long reporter,bool friendlySelf)
        {
            if(id!=0)return id;
            if(entity!=0)return entity;
            if(!friendlySelf)return emitter;
            return reporter;
        }
    }
}
