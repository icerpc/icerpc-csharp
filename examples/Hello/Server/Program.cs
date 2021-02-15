// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using ZeroC.Ice;

try
{
    IConfiguration configuration = new ConfigurationBuilder()
       .AddJsonFile("appsettings.json", optional: true)
       .Build();

    var loggerFactory = LoggerFactory.Create(
        builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
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

    await using var communicator = new Communicator(loggerFactory: loggerFactory);
    await using var adapter = new ObjectAdapter(communicator,
        new ObjectAdapterOptions()
        {
            Name = "Hello",
            Endpoints = configuration.GetSection("AppSettings").GetValue<string>("Hello.Endpoints")
        });

    // Destroy the adapter on Ctrl+C or Ctrl+Break
    Console.CancelKeyPress += (sender, eventArgs) =>
    {
        eventArgs.Cancel = true;
        _ = adapter.ShutdownAsync();
    };

    adapter.Add("hello", new Hello());

    await adapter.ActivateAsync();
    await adapter.ShutdownComplete;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

return 0;
