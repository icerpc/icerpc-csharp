// Copyright (c) ZeroC, Inc.

using Google.Protobuf.WellKnownTypes;
using IceRpc.Protobuf.Internal;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Protobuf.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class ServiceProviderExtensionsTests
{
    [Test]
    public void Create_protobuf_client_with_no_params()
    {
        var connection = new Connection();
        var serviceCollection = 
            new ServiceCollection()
                .AddSingleton<IInvoker>(connection)
                .Add

    }
}
