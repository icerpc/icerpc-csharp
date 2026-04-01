// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::Base::Tests
{
    ["cs:identifier:REnamedClass"]
    class OriginalClass
    {
        ["cs:identifier:renamedX"] int x;
    }

    ["cs:identifier:REnamedException"]
    exception OriginalException {}

    exception DerivedException : OriginalException {}
}
