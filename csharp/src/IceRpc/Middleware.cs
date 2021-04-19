// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

namespace IceRpc
{
    public static class Middleware
    {
        private static readonly Action<ILogger, long, string, string, string, string, double, Exception> _log =
            LoggerMessage.Define<long, string, string, string, string, double>(
                LogLevel.Information,
                new EventId(LoggerExtensions.MiddlewareEventId, "DispatchSummary"),
                "[{StreamId}] {Operation} on {Path} from {Peer} - {Status} in {Duration:0.0000}ms");

        /// <summary>Creates a middleware that logs request dispatches.</summary>
        public static Func<IDispatcher, IDispatcher> Logger(ILoggerFactory loggerFactory)
        {
            ILogger logger = loggerFactory.CreateLogger("IceRpc");

            return next => new InlineDispatcher(
                async (current, cancel) =>
                {
                    DateTime start = DateTime.Now;

                    string status = "";

                    try
                    {
                        OutgoingResponseFrame response =
                            await next.DispatchAsync(current, cancel).ConfigureAwait(false);

                        status = response.ResultType == ResultType.Failure ? "failure" : "success";

                        return response;
                    }
                    catch (Exception e)
                    {
                        status = $"failure={e.GetType()}";
                        throw;
                    }
                    finally
                    {
                        _log(logger,
                             current.StreamId,
                             current.Operation,
                             current.Path,
                             GetPeer(current.Connection.Endpoint),
                             status,
                             DateTime.Now.Subtract(start).TotalMilliseconds, null!);
                    }
                });
        }
        private static string GetPeer(Endpoint endpoint)
        {
            string peer;

            if (endpoint is IPEndpoint ipEndpoint)
            {
                peer = ipEndpoint.Address.ToString();
            }
            else if (endpoint is ColocatedEndpoint)
            {
                peer = "colocated";
            }
            else
            {
                peer = endpoint.ToString();
            }

            return peer;
        }
    }
}
