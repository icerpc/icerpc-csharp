// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Test;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Test.Proxy
{
    internal sealed class MyDerivedClass : IMyDerivedClass
    {
        private SortedDictionary<string, string>? _ctx;

        public IServicePrx? Echo(IServicePrx? obj, Current c, CancellationToken cancel) => obj;

        public void Shutdown(Current current, CancellationToken cancel) =>
            current.Server.ShutdownAsync();

        public IReadOnlyDictionary<string, string> GetContext(Current current, CancellationToken cancel) => _ctx!;

        public ValueTask<bool> IceIsAAsync(string typeId, Current current, CancellationToken cancel)
        {
            _ctx = current.Context;
            return new(typeof(IMyDerivedClass).GetAllIceTypeIds().Contains(typeId));
        }

        public IRelativeTestPrx OpRelative(ICallbackPrx callback, Current current, CancellationToken cancel)
        {
            TestHelper.Assert(callback.Connection != null);
            var path = $"/{System.Guid.NewGuid()}";

            IRelativeTestPrx relativeTest =
                TestHelper.AddWithGuid<IRelativeTestPrx>(current.Server, new RelativeTest());

            relativeTest.Connection = null;
            relativeTest.Endpoints = ImmutableList<Endpoint>.Empty;

            TestHelper.Assert(callback.Op(relativeTest, cancel: cancel) == 1);
            return relativeTest;
        }
    }

    internal sealed class RelativeTest : IRelativeTest
    {
        private int _count;

        public int DoIt(Current current, CancellationToken cancel) => ++_count;
    }
}
