// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Test;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Test.Proxy
{
    internal sealed class AsyncMyDerivedClass : IAsyncMyDerivedClass
    {
        private IReadOnlyDictionary<string, string>? _ctx;

        public ValueTask<IServicePrx?> EchoAsync(IServicePrx? obj, Current c, CancellationToken cancel) =>
            new(obj);

        public ValueTask ShutdownAsync(Current current, CancellationToken cancel)
        {
            current.Server.ShutdownAsync();
            return default;
        }

        public ValueTask<IReadOnlyDictionary<string, string>> GetContextAsync(
            Current current,
            CancellationToken cancel) =>
            new(_ctx!);

        public ValueTask<bool> IceIsAAsync(string typeId, Current current, CancellationToken cancel)
        {
            _ctx = current.Context;
            return new(typeof(IMyDerivedClass).GetAllIceTypeIds().Contains(typeId));
        }

        public async ValueTask<IRelativeTestPrx> OpRelativeAsync(
            ICallbackPrx callback,
            Current current,
            CancellationToken cancel)
        {
            TestHelper.Assert(callback.Connection != null);
            IRelativeTestPrx relativeTest =
                TestHelper.AddWithGuid<IRelativeTestPrx>(current.Server, new RelativeTest());
            relativeTest.Endpoints = ImmutableList<Endpoint>.Empty;

            TestHelper.Assert(await callback.OpAsync(relativeTest, cancel: cancel) == 1);
            return relativeTest;
        }
    }

    internal sealed class RelativeTest : IRelativeTest
    {
        private int _count;

        public int DoIt(Current current, CancellationToken cancel) => ++_count;
    }
}
