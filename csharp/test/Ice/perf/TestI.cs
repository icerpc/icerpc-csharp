// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Test.Perf
{
    public sealed class PerformanceI : IAsyncPerformance
    {
        private static readonly byte[] _bytes = new byte[1024000]; // 1MB];

        public ValueTask SendBytesAsync(byte[] seq, Dispatch dispatch, CancellationToken cancel) => default;

        public ValueTask<ReadOnlyMemory<byte>> ReceiveBytesAsync(int size, Dispatch dispatch, CancellationToken cancel)
        {
            Debug.Assert(size <= _bytes.Length);
            return new(new ReadOnlyMemory<byte>(_bytes, 0, size));
        }

        public ValueTask ShutdownAsync(Dispatch dispatch, CancellationToken cancel)
        {
            _ = dispatch.Server!.ShutdownAsync();
            return default;
        }
    }
}
