using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddFilter("IceRpc", LogLevel.Information);
        builder.AddSimpleConsole(configure => configure.IncludeScopes = true);
    });

await using var connectionCache = new ConnectionCache();

// Create an invocation pipeline with the retry and logger interceptors.
Pipeline pipeline = new Pipeline()
    .UseRetry(
        // Make up to 5 attempts before giving up.
        new RetryOptions { MaxAttempts = 5 },
        loggerFactory)
    .UseLogger(loggerFactory)
    .Into(connectionCache);

// We use a logger to ensure proper ordering of the messages on the console.
ILogger logger = loggerFactory.CreateLogger("IceRpc.RetryExample");
