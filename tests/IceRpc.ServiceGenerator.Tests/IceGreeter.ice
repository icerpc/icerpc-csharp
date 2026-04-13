// Copyright (c) ZeroC, Inc.

module IceRpc::ServiceGenerator::Tests
{
    interface Greeter
    {
        void opIce();
        string opIceWithArgs(string message);
    }
}
