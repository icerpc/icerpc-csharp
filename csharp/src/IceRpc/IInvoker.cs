// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>An invoker sends outgoing requests and returns incoming responses.</summary>
    public interface IInvoker
    {
        /// <summary>Sends an outgoing request and returns the corresponding incoming response.</summary>
        /// <param name="request">The outgoing request being sent.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The corresponding <see cref="IncomingResponse"/>.</returns>
        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
            throw new NotImplementedException();
    }

     /// <summary>Adapts an invoker delegate to the <see cref="IInvoker"/> interface.</summary>
    public class InlineInvoker : IInvoker
    {
        private readonly Func<OutgoingRequest, CancellationToken, ValueTask<IncomingResponse>> _function;

        /// <summary>Constructs an InlineInvoker using a delegate.</summary>
        /// <param name="function">The function that implements the invoker's InvokerAsync method.</param>
        public InlineInvoker(Func<OutgoingRequest, CancellationToken, ValueTask<IncomingResponse>> function) =>
            _function = function;

        /// <inheritdoc/>
        Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
            _function(request, cancel);
    }
}
