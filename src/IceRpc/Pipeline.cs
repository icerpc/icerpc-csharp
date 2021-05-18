// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A pipeline is an invoker created from zero or more interceptors installed by calling <see cref="Use"/>.
    /// The last invoker of the pipeline calls the connection carried by the request or throws
    /// <see cref="ArgumentNullException"/> if this connection is null.</summary>
    public sealed class Pipeline : IInvoker
    {
        private IInvoker? _invoker;
        private ImmutableList<Func<IInvoker, IInvoker>> _interceptorList =
            ImmutableList<Func<IInvoker, IInvoker>>.Empty;

        /// <inheritdoc/>
        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
            (_invoker ??= CreateInvokerPipeline()).InvokeAsync(request, cancel);

        /// <summary>Installs one or more interceptors.</summary>
        /// <param name="interceptor">One or more interceptors.</param>
        /// <exception name="InvalidOperationException">Thrown if this method is called after the first call to
        /// <see cref="InvokeAsync"/>.</exception>
        public void Use(params Func<IInvoker, IInvoker>[] interceptor)
        {
            if (_invoker != null)
            {
                throw new InvalidOperationException(
                    "interceptors must be installed before the first call to InvokeAsync");
            }
            _interceptorList = _interceptorList.AddRange(interceptor);
        }

        /// <summary>Creates a new pipeline with this pipeline's interceptor stack plus the specified interceptors.
        /// </summary>
        /// <param name="interceptor">One or more interceptors.</param>
        /// <remarks>This method can be called after calling <see cref="InvokeAsync"/>.</remarks>
        public Pipeline With(params Func<IInvoker, IInvoker>[] interceptor) =>
            new() { _interceptorList = _interceptorList.AddRange(interceptor) };

        /// <summary>Creates a pipeline of invokers by starting with the last invoker and applying all interceptors in
        /// reverse order of installation. This method is called by the first call to <see cref="InvokeAsync"/>.
        /// </summary>
        /// <returns>The pipeline of invokers.</returns>
        private IInvoker CreateInvokerPipeline()
        {
            IInvoker pipeline = Proxy.NullInvoker;

            IEnumerable<Func<IInvoker, IInvoker>> interceptorEnumerable = _interceptorList;
            foreach (Func<IInvoker, IInvoker> interceptor in interceptorEnumerable.Reverse())
            {
                pipeline = interceptor(pipeline);
            }
            return pipeline;
        }
    }
}
