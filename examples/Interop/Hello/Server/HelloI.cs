// Copyright (c) ZeroC, Inc. All rights reserved.

using Ice;

namespace Demo;

public class HelloI : HelloDisp_
{
    public override string sayHello(string name, Current current)
    {
        Console.WriteLine($"{name} says hello!");
        return $"Hello, {name}!";
    }
}
