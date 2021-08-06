using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>An interceptor that logs request and response messages using a logger with "IceRpc" category.
    /// </summary>
    public class LoggerInterceptor : IInvoker
    {
        private readonly ILogger _logger;
        private readonly IInvoker _next;

        /// <summary>Constructs a logger interceptor.</summary>
        /// <param name="next">The next invoker in the invocation pipeline.</param>
        /// <param name="loggerFactory">The logger factory used to create the logger.</param>
        public LoggerInterceptor(IInvoker next, ILoggerFactory loggerFactory)
        {
            _next = next;
            _logger = loggerFactory.CreateLogger("IceRpc");
        }

        async Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // TODO we now log the sending of the request before it is actually sent
            // and it might never be sent
            using IDisposable? connectionScope = request.Connection?.StartScope();
            _logger.LogSentRequest(request.Path,
                                   request.Operation,
                                   request.PayloadSize,
                                   request.PayloadEncoding);
            try
            {
                IncomingResponse response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
                if (!request.IsOneway)
                {
                    _logger.LogReceivedResponse(response.ResultType);
                }
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogRequestException(request.Path, request.Operation, ex);
                throw;
            }
        }
    }
}
