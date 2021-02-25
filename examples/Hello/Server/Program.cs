// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using ZeroC.Ice;

try
{
    IConfiguration configuration = new ConfigurationBuilder()
       .AddJsonFile("appsettings.json", optional: true)
       .Build();

    using var loggerFactory = LoggerFactory.Create(
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
    await using var server = new Server(communicator,
        new ServerOptions()
        {
            Name = "Hello",
            Endpoints = configuration.GetSection("AppSettings").GetValue<string>("Hello.Endpoints")
        });

    // Destroy the server on Ctrl+C or Ctrl+Break
    Console.CancelKeyPress += (sender, eventArgs) =>
    {
        eventArgs.Cancel = true;
        _ = server.ShutdownAsync();
    };

    server.Add("hello", new Hello());

    await server.ActivateAsync();
    await server.ShutdownComplete;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

return 0;
