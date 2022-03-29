// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Configure
{
    /// <summary>A pipeline is an invoker created from zero or more interceptors installed by calling <see cref="Use"/>.
    /// The last invoker of the pipeline calls the connection carried by the request or throws
    /// <see cref="ArgumentNullException"/> if this connection is null.</summary>
    public sealed class Pipeline : IInvoker
    {
        private IInvoker? _invoker;
        private readonly List<Func<IInvoker, IInvoker>> _interceptorList = new();

        /// <summary>Constructs an empty pipeline.</summary>
        public Pipeline() => _interceptorList = new();

        /// <inheritdoc/>
        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default) =>
            (_invoker ??= CreateInvokerPipeline()).InvokeAsync(request, cancel);

        /// <summary>Installs an interceptor at the end of the pipeline.</summary>
        /// <param name="interceptor">The interceptor to install.</param>
        /// <exception cref="InvalidOperationException">Thrown if this method is called after the first call to
        /// <see cref="InvokeAsync"/>.</exception>
        public Pipeline Use(Func<IInvoker, IInvoker> interceptor)
        {
            if (_invoker != null)
            {
                throw new InvalidOperationException(
                    "interceptors must be installed before the first call to InvokeAsync");
            }
            _interceptorList.Insert(0, interceptor);
            return this;
        }

        /// <summary>Creates a new pipeline with this pipeline's interceptors plus the specified interceptor.</summary>
        /// <param name="interceptor">The additional interceptor.</param>
        /// <returns>The new pipeline.</returns>
        /// <remarks>This method can be called after calling <see cref="InvokeAsync"/> since it creates a new pipeline.
        /// </remarks>
        public Pipeline With(Func<IInvoker, IInvoker> interceptor) => new Pipeline(_interceptorList).Use(interceptor);

        private Pipeline(IEnumerable<Func<IInvoker, IInvoker>> interceptorList) =>
            _interceptorList = new(interceptorList);

        /// <summary>Creates a pipeline of invokers by starting with the last invoker installed. This method is called
        /// by the first call to <see cref="InvokeAsync"/>.
        /// </summary>
        /// <returns>The pipeline of invokers.</returns>
        private IInvoker CreateInvokerPipeline()
        {
            IInvoker pipeline = Proxy.DefaultInvoker;

            foreach (Func<IInvoker, IInvoker> interceptor in _interceptorList)
            {
                pipeline = interceptor(pipeline);
            }
            return pipeline;
        }
    }
}
