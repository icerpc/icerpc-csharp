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

namespace ServerApp
{
    public class HelloOptions
    {
        public ConnectionOptions ConnectionOptions { get; set; } = new();

        [Required(ErrorMessage = "the Endpoint setting is required.")]
        public Endpoint? Endpoint { get; set; }
    }

    public class Program
    {
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

                    services.AddSingleton<IServerTransport<IMultiplexedNetworkConnection>>(serviceProvider =>
                        {
                            IConfiguration configuration = hostContext.Configuration.GetSection("Transport");
                            TcpServerOptions tcpOptions = configuration?.GetValue<TcpServerOptions>("Tcp") ?? new();
                            tcpOptions.AuthenticationOptions = new SslServerAuthenticationOptions()
                            {
                                ServerCertificate = new X509Certificate2(
                                    Path.Combine(
                                        hostContext.HostingEnvironment.ContentRootPath,
                                        configuration.GetValue<string>("CertificateFile")),
                                    configuration.GetValue<string>("CertificatePassword"))
                            };
                            return new SlicServerTransport(new TcpServerTransport(tcpOptions));
                        });

                    services.AddSingleton<IDispatcher>(serviceProvider =>
                        {
                            ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                            var router = new Router();
                            router.UseLogger(loggerFactory);
                            router.UseTelemetry(new TelemetryOptions { LoggerFactory = loggerFactory });
                            router.Map<IHello>(new Hello());
                            return router;
                        });
                });

        private class HelloService : IHostedService
        {
            private readonly Server _server;

            public HelloService(
                IServerTransport<IMultiplexedNetworkConnection> serverTransport,
                IOptions<HelloOptions> options,
                IDispatcher dispatcher,
                ILoggerFactory loggerFactory) =>
                _server = new Server
                {
                    ConnectionOptions = options.Value.ConnectionOptions,
                    Dispatcher = dispatcher,
                    Endpoint = options.Value.Endpoint!,
                    LoggerFactory = loggerFactory,
                    MultiplexedServerTransport = serverTransport
                };

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _server.Listen();
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken) => _server.ShutdownAsync(cancellationToken);
        }
    }
}
