// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::CodeGeneration
{
    interface MyInterfaceBase
    {
        void opBase();
    }

    interface MyInterfaceDerived : MyInterfaceBase
    {
        void opDerived();
    }

    interface MyInterfaceMostDerived : MyInterfaceDerived
    {
        void opMostDerived();
    }
}
