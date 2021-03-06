// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Test.DefaultServant
{
    public sealed class MyObject : IMyObject
    {
        public ValueTask IcePingAsync(Current current, CancellationToken cancel)
        {
            string path = current.Path;

            if (path == "/foo/ObjectNotExist")
            {
                throw new ServiceNotFoundException();
            }
            return default;
        }

        public string GetName(Current current, CancellationToken cancel)
        {
            string path = current.Path;

            if (path == "/foo/ObjectNotExist")
            {
                throw new ServiceNotFoundException();
            }
            return path;
        }
    }
}
