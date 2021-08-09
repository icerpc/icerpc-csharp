// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;

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

await using var connection = new Connection
{
    LoggerFactory = loggerFactory,
    RemoteEndpoint = configuration.GetSection("AppSettings").GetValue<string>("Hello.Endpoint")
};

var pipeline = new Pipeline();
pipeline.Use(Interceptors.CustomTelemetry(new Interceptors.TelemetryOptions { LoggerFactory = loggerFactory}));
pipeline.Use(Interceptors.Logger(loggerFactory));

IHelloPrx twoway = HelloPrx.FromConnection(connection, invoker: pipeline);

Console.Write("Say Hello: ");
string? greeting = Console.ReadLine();
Console.Out.WriteLine(await twoway.SayHelloAsync(greeting));
