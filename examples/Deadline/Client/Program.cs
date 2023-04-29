// Copyright (c) ZeroC, Inc.

using GreeterDeadlineExample;
using IceRpc;
using IceRpc.Deadline;
using IceRpc.Features;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// Create an invocation pipeline, that uses the Deadline interceptor and has a default timeout of 500 ms.
Pipeline pipeline = new Pipeline()
    .UseDeadline(defaultTimeout: TimeSpan.FromMilliseconds(500))
    .Into(connection);

var greeterProxy = new GreeterProxy(pipeline);

// The SlowChatbot service implementation used in this example delays the sending of the response 1 second, which is
// greater than the configured default timeout of 500 ms. The invocation would be canceled and the deadline interceptor
// would throw TimeoutException.
try
{
    _ = await greeterProxy.GreetAsync(Environment.UserName);
}
catch (TimeoutException exception)
{
    Console.WriteLine(exception.Message);
}


// The next invocation uses the deadline feature to configure a higher deadline, which wont be canceled before than the
// SlowChatbot sends a response.

var features = new FeatureCollection();
features.Set<IDeadlineFeature>(DeadlineFeature.FromTimeout(TimeSpan.FromSeconds(10)));

string greeting = await greeterProxy.GreetAsync(Environment.UserName, features);
Console.WriteLine(greeting);

await connection.ShutdownAsync();
