// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Internal;

/// <summary>Extension methods for struct <see cref="ReadResult"/>.</summary>
internal static class ReadResultExtensions
{
    internal static void ThrowIfCanceled(this ReadResult readResult, Protocol protocol)
    {
        if (readResult.IsCanceled)
        {
            throw protocol == Protocol.Ice ? new ConnectionClosedException() : new OperationCanceledException();
        }
    }
}
