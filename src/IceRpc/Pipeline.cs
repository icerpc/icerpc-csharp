// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>A pipeline is an invoker created from zero or more interceptors installed by calling <see cref="Use"/>.
    /// The last invoker of the pipeline calls the connection carried by the request or throws
    /// <see cref="ArgumentNullException"/> if this connection is null.</summary>
    public sealed class Pipeline : IInvoker
    {
        private readonly Stack<Func<IInvoker, IInvoker>> _interceptorStack = new();
        private readonly Lazy<IInvoker> _invoker;

        /// <summary>Constructs a pipeline.</summary>
        public Pipeline() => _invoker = new Lazy<IInvoker>(CreateInvokerPipeline);

        /// <inheritdoc/>
        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default) =>
            _invoker.Value.InvokeAsync(request, cancel);

        /// <summary>Installs an interceptor at the end of the pipeline.</summary>
        /// <param name="interceptor">The interceptor to install.</param>
        /// <exception cref="InvalidOperationException">Thrown if this method is called after the first call to
        /// <see cref="InvokeAsync"/>.</exception>
        public Pipeline Use(Func<IInvoker, IInvoker> interceptor)
        {
            if (_invoker.IsValueCreated)
            {
                throw new InvalidOperationException(
                    "interceptors must be installed before the first call to InvokeAsync");
            }
            _interceptorStack.Push(interceptor);
            return this;
        }

        /// <summary>Creates a pipeline of invokers by starting with the last invoker installed. This method is called
        /// by the first call to <see cref="InvokeAsync"/>.
        /// </summary>
        /// <returns>The pipeline of invokers.</returns>
        private IInvoker CreateInvokerPipeline()
        {
            IInvoker pipeline = Proxy.DefaultInvoker;

            foreach (Func<IInvoker, IInvoker> interceptor in _interceptorStack)
            {
                pipeline = interceptor(pipeline);
            }
            return pipeline;
        }
    }
}
