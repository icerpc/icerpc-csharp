// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using VisitorCenter;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// Create an invocation pipeline, that uses the deadline interceptor and has a default timeout of 500 ms.
Pipeline pipeline = new Pipeline()
    .UseDeadline(defaultTimeout: TimeSpan.FromMilliseconds(500))
    .Into(connection);

var greeter = new GreeterClient(pipeline);

// In this example, the implementation of the greet operation takes about 1 second, and the deadline
// interceptor makes sure the invocation throws TimeoutException after 500 ms.
try
{
    _ = await greeter.GreetAsync(new GreetRequest { Name = Environment.UserName });
}
catch (TimeoutException exception)
{
    Console.WriteLine(exception.Message);
}

// The next invocation uses the deadline feature to configure a higher deadline, which wont be canceled before than the
// SlowChatbot sends a response.

var features = new FeatureCollection();
features.Set<IDeadlineFeature>(DeadlineFeature.FromTimeout(TimeSpan.FromSeconds(10)));

GreetResponse response = await greeter.GreetAsync(new GreetRequest { Name = Environment.UserName }, features);
Console.WriteLine(response.Greeting);

await connection.ShutdownAsync();
