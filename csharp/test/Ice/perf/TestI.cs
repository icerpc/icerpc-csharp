// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Threading;

namespace IceRpc.Test.Perf
{
    public sealed class PerformanceI : IPerformance
    {
        private static readonly byte[] _bytes = new byte[1024000]; // 1MB];

        public void SendBytes(byte[] seq, Dispatch dispatch, CancellationToken cancel)
        {
        }

        public System.ReadOnlyMemory<byte> ReceiveBytes(int size, Dispatch dispatch, CancellationToken cancel)
        {
            Debug.Assert(size <= _bytes.Length);
            return new(_bytes, 0, size);
        }

        public void Shutdown(Dispatch dispatch, CancellationToken cancel) =>
            current.Server.ShutdownAsync();
    }
}
