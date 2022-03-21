// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Features.Internal
{
    /// <summary>A feature that specifies the request ID of an Ice outgoing request. It also provides the response task
    /// completion source used by the Ice protocol connection implementation to wait for the response.</summary>
    internal sealed class IceOutgoingRequest
    {
        /// <summary>The request ID.</summary>
        internal int Id { get; }

        /// <summary>The task completion source that will be completed when the response is received.</summary>
        /// <remarks>The pipe reader holds the full response frame starting with the reply status and can be read
        /// synchronously with TryRead.</remarks>
        internal TaskCompletionSource<PipeReader> IncomingResponseCompletionSource { get; }

        internal IceOutgoingRequest(int id)
        {
            Id = id;
            IncomingResponseCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
