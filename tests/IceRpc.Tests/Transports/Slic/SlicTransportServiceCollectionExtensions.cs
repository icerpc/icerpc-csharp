// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IceRpc.Tests.Transports.Slic;

internal static class SlicTransportServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        internal IServiceCollection AddSlicTest() =>
            services.AddMultiplexedTransportTest().AddColocTransport().AddSlicTransport();
    }
}
