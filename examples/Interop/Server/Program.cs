// Copyright (c) ZeroC, Inc. All rights reserved.

// using statement -communicator is automatically destroyed at the end of this statement
using var communicator = Ice.Util.initialize(ref args);

// Destroy the communicator on Ctrl+C or Ctrl+Break
Console.CancelKeyPress += (sender, eventArgs) => communicator.destroy();

if (args.Length > 0)
{
    Console.Error.WriteLine("too many arguments");
}
else
{
    var adapter = communicator.createObjectAdapterWithEndpoints("Hello", "tcp -p 10000");
    adapter.add(new Demo.HelloI(), Ice.Util.stringToIdentity("hello"));
    adapter.activate();
    communicator.waitForShutdown();
}
