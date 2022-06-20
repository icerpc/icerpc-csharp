// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Slice;

await using var connection1 = new ClientConnection("icerpc://127.0.0.1:10001");
await using var connection2 = new ClientConnection("icerpc://127.0.0.1:10002");

// Add the JWT interceptor to the invocation pipeline.
Pipeline? pipeline = new Pipeline().UseJwt();

IAuthPrx auth = AuthPrx.FromConnection(connection1, invoker: pipeline);
IHelloPrx hello = HelloPrx.FromConnection(connection2, invoker: pipeline);

using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

CancellationToken cancel = cancellationSource.Token;

Console.Write("To say hello to the server, type your name: ");
if (Console.ReadLine() is string name)
{
    // Sign-in to the Auth server to acquire the JWT token.
    await auth.SignInAsync(name, name.ToLowerInvariant());
    while (true)
    {
        // The token is set to expire after 5 seconds, this will cause JWT token validation to
        // fail with a DispatchException and ErrorCode = InvalidCredentials.
        try
        {
            Console.WriteLine(await hello.SayHelloAsync());
        }
        catch (DispatchException ex) when (ex.ErrorCode == DispatchErrorCode.InvalidCredentials)
        {
            Console.WriteLine("request failed: invalid or expired JWT token.");
            break;
        }

        Console.WriteLine("Looping in 1 second, press Ctrl+C to exit");
        await Task.Delay(TimeSpan.FromSeconds(1), cancel);
    }

}
