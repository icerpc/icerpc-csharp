// Copyright (c) ZeroC, Inc.

module IceRpc::Slice::Tests
{
    class MyClassB;
    class MyClassC;

    class MyClassA
    {
        MyClassB theB;
        MyClassC theC;
    }

    class MyClassB : MyClassA
    {
        MyClassA theA;
    }

    class MyClassC
    {
        MyClassB theB;
    }

    // Tests for operations that accept and return class instances
    interface ClassOperations
    {
        // Compact format
        Value opAnyClassCompact(Value p1);
        MyClassA opMyClassCompact(MyClassA p1);

        // Sliced format
        ["format:sliced"] Value opAnyClassSliced(Value p1);
        ["format:sliced"] MyClassA opMyClassSliced(MyClassA p1);
    }
}
