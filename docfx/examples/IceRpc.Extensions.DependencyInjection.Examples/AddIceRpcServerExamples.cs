// Copyright (c) ZeroC, Inc.

using GreeterExample;
using IceRpc.Slice;
using IceRpc.Transports;
using IceRpc.Transports.Slic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Extensions.DependencyInjection.Examples;

// This class provides code snippets used by the doc-comments of the AddIceRpcServer examples.
public static class AddIceRpcServerExamples
{
    public static void AddDefaultServer(string[] args)
    {
        #region DefaultServer
        var router = new Router(); // the dispatch pipeline

        IHostBuilder builder = Host.CreateDefaultBuilder(args);
        builder.UseContentRoot(AppContext.BaseDirectory).ConfigureServices((hostContext, services) =>
        {
            // Load and register the server certificate as a singleton so it stays alive and gets disposed.
            services.AddSingleton<X509Certificate2>(sp =>
                X509CertificateLoader.LoadPkcs12FromFile(
                    Path.Combine(hostContext.HostingEnvironment.ContentRootPath, "server.p12"),
                    password: null,
                    keyStorageFlags: X509KeyStorageFlags.Exportable));

            // Configure the server authentication options using the server certificate.
            services
                .AddOptions<ServerOptions>()
                .Configure<X509Certificate2>((options, serverCertificate) =>
                    options.ServerAuthenticationOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificateContext = SslStreamCertificateContext.Create(
                            serverCertificate,
                            additionalCertificates: null)
                    });

            services.AddIceRpcServer(router);
        });
        #endregion
    }

    public static void AddServerWithOptions(string[] args)
    {
        #region ServerWithOptions
        var router = new Router(); // the dispatch pipeline

        IHostBuilder builder = Host.CreateDefaultBuilder(args);
        builder.UseContentRoot(AppContext.BaseDirectory).ConfigureServices((hostContext, services) =>
        {
            // Load and register the server certificate as a singleton so it stays alive and gets disposed.
            services.AddSingleton<X509Certificate2>(sp =>
                X509CertificateLoader.LoadPkcs12FromFile(
                    Path.Combine(
                        hostContext.HostingEnvironment.ContentRootPath,
                        hostContext.Configuration.GetValue<string>("Certificate:File")!),
                    password: null,
                    keyStorageFlags: X509KeyStorageFlags.Exportable));

            // Bind the server options to the configuration "Server" section, and add a Configure callback to
            // configure its authentication options.
            services
                .AddOptions<ServerOptions>()
                .Bind(hostContext.Configuration.GetSection("Server"))
                .Configure<X509Certificate2>((options, serverCertificate) =>
                    options.ServerAuthenticationOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificateContext = SslStreamCertificateContext.Create(
                            serverCertificate,
                            additionalCertificates: null)
                    });

            services.AddIceRpcServer(router);
        });
        #endregion
    }

    public static void AddServerWithSlic(string[] args)
    {
        #region ServerWithSlic
        var router = new Router(); // the dispatch pipeline

        IHostBuilder builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices(services =>
            // Inject an IMultiplexedServerTransport singleton implemented by Slic.
            services
                .AddSingleton<IMultiplexedServerTransport>(
                    provider => new SlicServerTransport(provider.GetRequiredService<IDuplexServerTransport>()))
                .AddIceRpcServer(router));
        #endregion
    }

    public static void AddServerWithDispatcherBuilder(string[] args)
    {
        #region ServerWithDispatcherBuilder
        IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args);
        hostBuilder.UseContentRoot(AppContext.BaseDirectory).ConfigureServices((hostContext, services) =>
        {
            // Load and register the server certificate as a singleton so it stays alive and gets disposed.
            services.AddSingleton<X509Certificate2>(sp =>
                X509CertificateLoader.LoadPkcs12FromFile(
                    Path.Combine(hostContext.HostingEnvironment.ContentRootPath, "server.p12"),
                    password: null,
                    keyStorageFlags: X509KeyStorageFlags.Exportable));

            // Configure the server authentication options using the server certificate.
            services
                .AddOptions<ServerOptions>()
                .Configure<X509Certificate2>((options, serverCertificate) =>
                    options.ServerAuthenticationOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificateContext = SslStreamCertificateContext.Create(
                            serverCertificate,
                            additionalCertificates: null)
                    });

            services.AddIceRpcServer(builder =>
                // Configure the dispatch pipeline:
                builder
                    .UseTelemetry()
                    .UseLogger()
                    .Map<IGreeterService>());
        });
        #endregion
    }

    public static void AddServerWithNamedOptions(string[] args)
    {
        #region ServerWithNamedOptions
        var router = new Router(); // the dispatch pipeline

        IHostBuilder builder = Host.CreateDefaultBuilder(args);
        builder.UseContentRoot(AppContext.BaseDirectory).ConfigureServices((hostContext, services) =>
        {
            // Load and register the server certificate as a singleton so it stays alive and gets disposed.
            services.AddSingleton<X509Certificate2>(sp =>
                X509CertificateLoader.LoadPkcs12FromFile(
                    Path.Combine(
                        hostContext.HostingEnvironment.ContentRootPath,
                        hostContext.Configuration.GetValue<string>("Certificate:File")!),
                    password: null,
                    keyStorageFlags: X509KeyStorageFlags.Exportable));

            // The server options for the icerpc server
            services
                .AddOptions<ServerOptions>("IceRpcGreeter") // named option
                .Bind(hostContext.Configuration.GetSection("IceRpcGreeter"))
                .Configure<X509Certificate2>((options, serverCertificate) =>
                    options.ServerAuthenticationOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificateContext = SslStreamCertificateContext.Create(
                            serverCertificate,
                            additionalCertificates: null)
                    });

            // The server options for the ice server
            services
                .AddOptions<ServerOptions>("IceGreeter")
                .Bind(hostContext.Configuration.GetSection("IceGreeter"))
                .Configure<X509Certificate2>((options, serverCertificate) =>
                    options.ServerAuthenticationOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificateContext = SslStreamCertificateContext.Create(
                            serverCertificate,
                            additionalCertificates: null)
                    });

            // We pass the named server options to get the correct server options for each server.
            services.AddIceRpcServer("IceRpcGreeter", router);
            services.AddIceRpcServer("IceGreeter", router);
        });
        #endregion
    }
}
