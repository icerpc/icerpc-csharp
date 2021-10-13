// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Configure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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

IConfigurationSection section = configuration.GetSection("AppSettings").GetSection("Hello");
await using var connection = new Connection
{
    //LoggerFactory = loggerFactory,
    RemoteEndpoint = section.GetValue<string>("Endpoint"),
    Options = section.GetSection("ConnectionOptions").Get<ConnectionOptions>()
};

var pipeline = new Pipeline();
pipeline.UseTelemetry(new TelemetryOptions { LoggerFactory = loggerFactory});
pipeline.UseLogger(loggerFactory);

IHelloPrx twoway = HelloPrx.FromConnection(connection, invoker: pipeline);

Console.Write("Say Hello: ");
string? greeting = Console.ReadLine();
Console.Out.WriteLine(await twoway.SayHelloAsync(greeting));
