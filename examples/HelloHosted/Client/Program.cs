// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Configure;
using IceRpc.Transports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace ClientApp;

public class Program
{
    public class HelloOptions
    {
        public ConnectionOptions ConnectionOptions { get; set; } = new();

        [Required(ErrorMessage = "the Endpoint setting is required.")]
        public Endpoint? Endpoint { get; set; }
    }

    public static void Main(string[] args) => CreateHostBuilder(args).Build().Run();

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices((hostContext, services) =>
            {
                services.AddHostedService<HelloService>();
                services.AddOptions<HelloOptions>()
                    .Bind(hostContext.Configuration.GetSection("Hello"))
                    .ValidateDataAnnotations();

                services.AddSingleton<IClientTransport<IMultiplexedNetworkConnection>>(serviceProvider =>
                    {
                        IConfiguration configuration = hostContext.Configuration.GetSection("Transport");
                        TcpClientOptions tcpOptions = configuration?.GetValue<TcpClientOptions>("Tcp") ?? new();
                        tcpOptions.AuthenticationOptions = new SslClientAuthenticationOptions()
                        {
                            RemoteCertificateValidationCallback =
                                CertificateValidaton.GetServerCertificateValidationCallback(
                                    certificateAuthorities: new X509Certificate2Collection
                                    {
                                        new X509Certificate2(
                                            Path.Combine(
                                                hostContext.HostingEnvironment.ContentRootPath,
                                                configuration.GetValue<string>("CertificateAuthoritiesFile")))
                                    })
                        };
                        return new SlicClientTransport(new TcpClientTransport(tcpOptions));
                    });

                services.AddSingleton<IInvoker>(serviceProvider =>
                    {
                        ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                        return new Pipeline()
                            .UseLogger(loggerFactory)
                            .UseTelemetry(new TelemetryOptions { LoggerFactory = loggerFactory });
                    });
            });

    private class HelloService : BackgroundService
    {
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly Connection _connection;
        private readonly HelloPrx _proxy;

        public HelloService(
            IClientTransport<IMultiplexedNetworkConnection> clientTransport,
            IOptions<HelloOptions> options,
            IInvoker invoker,
            ILoggerFactory loggerFactory,
            IHostApplicationLifetime applicationLifetime)
        {
            _applicationLifetime = applicationLifetime;

            _connection = new Connection()
            {
                MultiplexedClientTransport = clientTransport,
                Options = options.Value.ConnectionOptions,
                RemoteEndpoint = options.Value.Endpoint!,
                LoggerFactory = loggerFactory
            };

            _proxy = HelloPrx.FromConnection(_connection, invoker: invoker);
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            await _proxy.SayHelloAsync("Hello!", cancel: cancellationToken);
            _applicationLifetime.StopApplication();
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
            await _connection.ShutdownAsync(cancellationToken);
        }
    }
}
