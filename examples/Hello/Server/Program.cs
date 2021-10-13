// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Configure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

try
{
    IConfiguration configuration = new ConfigurationBuilder()
       .AddJsonFile("appsettings.json", optional: true)
       .Build();

    using ILoggerFactory loggerFactory = LoggerFactory.Create(
        builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.Configure(factoryOptions =>
            {
                factoryOptions.ActivityTrackingOptions = ActivityTrackingOptions.ParentId |
                                                         ActivityTrackingOptions.SpanId;
            });
            builder.AddSimpleConsole(configure =>
                {
                    configure.IncludeScopes = true;
                    configure.SingleLine = false;
                    configure.UseUtcTimestamp = true;
                });
        });

    var router = new Router();
    router.UseTelemetry(new TelemetryOptions { LoggerFactory = loggerFactory});
    router.UseLogger(loggerFactory);
    router.Map<IHello>(new Hello());

    IConfigurationSection section = configuration.GetSection("AppSettings").GetSection("Hello");
    await using var server = new Server
    {
        Endpoint = section.GetValue<string>("Endpoints"),
        // LoggerFactory = loggerFactory,
        Dispatcher = router,
        ConnectionOptions = section.GetSection("ConnectionOptions").Get<ConnectionOptions>()
    };

    // Destroy the server on Ctrl+C or Ctrl+Break
    Console.CancelKeyPress += (sender, eventArgs) =>
    {
        eventArgs.Cancel = true;
        _ = server.ShutdownAsync();
    };
    server.Listen();
    await server.ShutdownComplete;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

return 0;
