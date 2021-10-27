// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features.Internal
{
    /// <summary>A feature that specifies the request ID of an Ice1 request or response.</summary>
    internal sealed class Ice1Request
    {
        /// <summary>The request ID.</summary>
        internal int Id { get; }

        /// <summary>The task completion source that will be completed when the response is received.</summary>
        internal TaskCompletionSource<ReadOnlyMemory<byte>>? ResponseCompletionSource { get; }

        internal Ice1Request(int id, bool outgoing)
        {
            Id = id;
            if (outgoing)
            {
                // If it's an outgoing request, create the a response completion source.
                ResponseCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }
}
