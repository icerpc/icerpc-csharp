// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary></summary>
    public class Pipeline : IInvoker
    {
        private IInvoker? _invoker;
        private ImmutableList<Func<IInvoker, IInvoker>> _interceptorList =
            ImmutableList<Func<IInvoker, IInvoker>>.Empty;

        private readonly IInvoker _lastInvoker =
            new InlineInvoker((request, cancel) =>
                request.Connection is Connection connection ? TempConnectionInvokeAsync(connection, request, cancel) :
                    throw new ArgumentNullException($"{nameof(request.Connection)} is null", nameof(request)));

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
            (_invoker ??= CreateInvoker(_lastInvoker)).InvokeAsync(request, cancel);

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

        protected virtual IInvoker CreateInvoker(IInvoker lastInvoker)
        {
            IInvoker pipeline = lastInvoker;

            IEnumerable<Func<IInvoker, IInvoker>> interceptorEnumerable = _interceptorList;
            foreach (Func<IInvoker, IInvoker> interceptor in interceptorEnumerable.Reverse())
            {
                pipeline = interceptor(pipeline);
            }
            return pipeline;
        }

        static private async Task<IncomingResponse> TempConnectionInvokeAsync(
                Connection connection,
                OutgoingRequest request,
                CancellationToken cancel)
        {
            if (Activity.Current != null && Activity.Current.Id != null)
            {
                request.WriteActivityContext(Activity.Current);
            }

            SocketStream? stream = null;
            try
            {
                using IDisposable? socketScope = connection.StartScope();

                // Create the outgoing stream.
                stream = connection.CreateStream(!request.IsOneway);

                // Send the request and wait for the sending to complete.
                await stream.SendRequestFrameAsync(request, cancel).ConfigureAwait(false);

                // TODO: move this set to stream.SendRequestFrameAsync
                request.IsSent = true;

                using IDisposable? streamSocket = stream.StartScope();
                connection.Logger.LogSentRequest(request);

                // The request is sent, notify the progress callback.
                // TODO: Get rid of the sentSynchronously parameter which is always false now?
                if (request.Progress is IProgress<bool> progress)
                {
                    progress.Report(false);
                    request.Progress = null; // Only call the progress callback once (TODO: revisit this?)
                }

                // Wait for the reception of the response.
                IncomingResponse response = request.IsOneway ?
                    new IncomingResponse(connection, request.PayloadEncoding) :
                    await stream.ReceiveResponseFrameAsync(cancel).ConfigureAwait(false);
                response.Connection = connection;

                if (!request.IsOneway)
                {
                    connection.Logger.LogReceivedResponse(response);
                }
                return response;
            }
            finally
            {
                // Release one ref-count
                stream?.Release();
            }
        }
    }
}
