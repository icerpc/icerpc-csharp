// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::Tests
{
    ["cs:identifier:REnamedInterface"]
    interface OriginalInterface
    {
        ["cs:identifier:REnamedOp"]
        int renamedOp(["cs:identifier:renamedParam"] int myParam);
    }
}
