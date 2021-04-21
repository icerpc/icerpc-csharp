// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using IceRpc;

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
                                                         ActivityTrackingOptions.SpanId |
                                                         ActivityTrackingOptions.TraceFlags |
                                                         ActivityTrackingOptions.TraceId |
                                                         ActivityTrackingOptions.TraceState;
            });
            builder.AddSimpleConsole(configure =>
                {
                    configure.IncludeScopes = true;
                    configure.SingleLine = false;
                    configure.UseUtcTimestamp = true;
                });
            /*builder.AddJsonConsole(configure =>
            {
                configure.IncludeScopes = true;
                configure.JsonWriterOptions = new System.Text.Json.JsonWriterOptions()
                {
                    Indented = true
                };
            });*/
        });

    await using var server = new Server
    {
        Endpoint = configuration.GetSection("AppSettings").GetValue<string>("Hello.Endpoints"),
        LoggerFactory = loggerFactory,
        Dispatcher = new Hello()
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
