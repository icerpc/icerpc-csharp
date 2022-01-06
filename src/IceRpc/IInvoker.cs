// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>An invoker sends outgoing requests and returns incoming responses.</summary>
    public interface IInvoker
    {
        /// <summary>Sends an outgoing request and returns the corresponding incoming response.</summary>
        /// <param name="request">The outgoing request being sent.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The corresponding <see cref="IncomingResponse"/>.</returns>
        Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default);
    }

    /// <summary>Adapts an invoker delegate to the <see cref="IInvoker"/> interface.</summary>
    public class InlineInvoker : IInvoker
    {
        private readonly Func<OutgoingRequest, CancellationToken, Task<IncomingResponse>> _function;

        /// <summary>Constructs an InlineInvoker using a delegate.</summary>
        /// <param name="function">The function that implements the invoker's InvokerAsync method.</param>
        public InlineInvoker(Func<OutgoingRequest, CancellationToken, Task<IncomingResponse>> function) =>
            _function = function;

        /// <inheritdoc/>
        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
            _function(request, cancel);
    }
}
