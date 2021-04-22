// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Collections.Generic;
using IceRpc;

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
        /*builder.AddJsonConsole(configure =>
        {
            configure.IncludeScopes = true;
            configure.JsonWriterOptions = new System.Text.Json.JsonWriterOptions()
            {
                Indented = true
            };
        });*/
    });

await using var communicator = new Communicator(ref args,
                                                configuration.GetSection("AppSettings").GetChildren().ToDictionary(
                                                    entry => entry.Key,
                                                    entry => entry.Value),
                                                loggerFactory);

IHelloPrx twoway = communicator.GetPropertyAsProxy("Hello.Proxy", IHelloPrx.Factory) ??
    throw new ArgumentException("invalid proxy");

Console.Write("Say Hello: ");
string? greeting = Console.ReadLine();
var context = new Dictionary<string, string>()
{
    { "User", "Jose" }
};
Console.Out.WriteLine(twoway.SayHello(greeting, context));
