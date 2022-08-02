// Copyright (c) ZeroC, Inc. All rights reserved.

using Ice;

using Communicator communicator = Util.initialize(ref args);

// Shuts down the server on Ctrl+C
Console.CancelKeyPress += (sender, eventArgs) => communicator.shutdown();

ObjectAdapter adapter = communicator.createObjectAdapterWithServerAddresses("Hello", "tcp -p 10000");
adapter.add(new Demo.HelloI(), Util.stringToIdentity("hello"));
adapter.activate();
communicator.waitForShutdown();
