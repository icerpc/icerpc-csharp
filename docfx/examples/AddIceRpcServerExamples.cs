// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Transports.Quic;

// This class provides code snippets used by the doc-comments of the AddIceRpcServer examples.
public static class AddIceRpcServerExamples
{
    public static void AddDefaultServer()
    {
        #region DefaultServer
        var router = new Router(); // the dispatch pipeline

        var builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices(services => services.AddIceRpcServer(router));
        #endregion
    }

    public static void AddServerWithOptions()
    {
        #region ServerWithOptions
        var router = new Router(); // the dispatch pipeline

        var builder = Host.CreateDefaultBuilder(args);
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

    public static void AddServerWithQuic()
    {
        #region ServerWithQuic
        var router = new Router(); // the dispatch pipeline

        var builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices(services =>
            // Inject an IMultiplexedServerTransport singleton implemented by QUIC.
            services
                .AddSingleton<IMultiplexedServerTransport>(provider => new QuicServerTransport())
                .AddIceRpcServer(router));
        #endregion
    }

    public static void AddServerWithDispatcherBuilder()
    {
        #region ServerWithDispatcherBuilder
        var hostBuilder = Host.CreateDefaultBuilder(args);
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

    public static void AddServerWithNamedOptions()
    {
        #region ServerWithNamedOptions
        var router = new Router(); // the dispatch pipeline

        var builder = Host.CreateDefaultBuilder(args);
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
