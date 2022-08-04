// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.IO.Pipelines;

namespace IceRpc.Internal;

/// <summary>Extension methods for struct <see cref="ReadResult"/>.</summary>
internal static class ReadResultExtensions
{
    internal static void ThrowIfCanceled(this ReadResult readResult, Protocol protocol, bool readingRequest)
    {
        if (readResult.IsCanceled)
        {
            if (protocol == Protocol.Ice)
            {
                throw new ConnectionClosedException();
            }
            else if (readingRequest)
            {
                throw new DispatchException(DispatchErrorCode.Canceled);
            }
            else
            {
                throw new IceRpcProtocolStreamException(IceRpcStreamErrorCode.OperationCanceled);
            }
        }
    }
}
