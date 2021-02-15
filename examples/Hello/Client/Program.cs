// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using ZeroC.Ice;

IConfiguration configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .Build();

var loggerFactory = LoggerFactory.Create(
    builder =>
    {
        builder.AddConfiguration(configuration.GetSection("Logging"));
        builder.AddSimpleConsole(configure => configure.IncludeScopes = true);
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
Console.Out.WriteLine(twoway.SayHello(greeting));
