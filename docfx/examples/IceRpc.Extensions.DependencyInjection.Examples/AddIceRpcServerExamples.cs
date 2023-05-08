// Copyright (c) ZeroC, Inc.

using GreeterExample;
using IceRpc.Transports;
using IceRpc.Transports.Quic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IceRpc.Extensions.DependencyInjection.Examples;

// This class provides code snippets used by the doc-comments of the AddIceRpcServer examples.
public static class AddIceRpcServerExamples
{
    public static void AddDefaultServer(string[] args)
    {
        #region DefaultServer
        var router = new Router(); // the dispatch pipeline

        IHostBuilder builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices(services => services.AddIceRpcServer(router));
        #endregion
    }

    public static void AddServerWithOptions(string[] args)
    {
        #region ServerWithOptions
        var router = new Router(); // the dispatch pipeline

        IHostBuilder builder = Host.CreateDefaultBuilder(args);
        builder.UseContentRoot(AppContext.BaseDirectory).ConfigureServices((hostContext, services) =>
        {
            services
                .AddOptions<ServerOptions>()
                // Read the server options from configuration.
                .Bind(hostContext.Configuration.GetSection("Server"));

            services.AddIceRpcServer(router);
        });
        #endregion
    }

    public static void AddServerWithQuic(string[] args)
    {
        #region ServerWithQuic
        var router = new Router(); // the dispatch pipeline

        IHostBuilder builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices(services =>
            // Inject an IMultiplexedServerTransport singleton implemented by QUIC.
            services
                .AddSingleton<IMultiplexedServerTransport>(provider => new QuicServerTransport())
                .AddIceRpcServer(router));
        #endregion
    }

    public static void AddServerWithDispatcherBuilder(string[] args)
    {
        #region ServerWithDispatcherBuilder
        IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args);
        hostBuilder.ConfigureServices(services =>
            services
                .AddIceRpcServer(builder =>
                    // Configure the dispatch pipeline:
                    builder
                        .UseTelemetry()
                        .UseLogger()
                        .Map<IGreeterService>()));
        #endregion
    }

    public static void AddServerWithNamedOptions(string[] args)
    {
        #region ServerWithNamedOptions
        var router = new Router(); // the dispatch pipeline

        IHostBuilder builder = Host.CreateDefaultBuilder(args);
        builder.UseContentRoot(AppContext.BaseDirectory).ConfigureServices((hostContext, services) =>
        {
            // The server options for the icerpc server
            services
                .AddOptions<ServerOptions>("IceRpcGreeter") // named option
                .Bind(hostContext.Configuration.GetSection("IceRpcGreeter"));

            // The server options for the ice server
            services
                .AddOptions<ServerOptions>("IceGreeter")
                .Bind(hostContext.Configuration.GetSection("IceGreeter"));

            // We pass the named server options to get the correct server options for each server.
            services.AddIceRpcServer("IceRpcGreeter", router);
            services.AddIceRpcServer("IceGreeter", router);
        });
        #endregion
    }
}
