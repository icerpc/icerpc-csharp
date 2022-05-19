// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>A builder for configuring IceRpc servers.</summary>
public class ServerBuilder
{
    /// <summary>The builder service provider.</summary>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>Construct a server builder.</summary>
    /// <param name="serviceProvider">The service provider.</param>
    public ServerBuilder(IServiceProvider serviceProvider) => ServiceProvider = serviceProvider;


    /// <summary>Builds the server.</summary>
    /// <returns>The new server.</returns>
    public Server Build()
    {
        Console.WriteLine("building server");
        ServerOptions options = ServiceProvider.GetService<IOptions<ServerOptions>>()?.Value ?? new ServerOptions();
        if (ServiceProvider.GetService<IDispatcher>() is IDispatcher dispatcher)
        {
            // TODO move dispatcher out of ServerOptions
            options.Dispatcher = dispatcher;
        }
        return ActivatorUtilities.CreateInstance<Server>(
            ServiceProvider,
            new object[] { options });
    }
}
