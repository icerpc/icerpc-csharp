//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

#include <Ice/BuiltinSequences.ice>
#include <Ice/Context.ice>

[[suppress-warning(reserved-identifier)]]

module IceRpc::Test::Proxy
{
    interface RelativeTest
    {
        int doIt();
    }

    interface Callback
    {
        int op(RelativeTest relativeTest);
    }

    interface MyClass
    {
        void shutdown();
        IceRpc::Context getContext();

        RelativeTest opRelative(Callback callback);
    }

    interface MyDerivedClass : MyClass
    {
        Object* echo(Object* obj);
    }
}
