// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/Service.ice>

module IceRpc::Tests::CodeGeneration
{
    interface MyInterfaceBase
    {
        void opBase();
    }

    interface MyInterfaceDerived : MyInterfaceBase, Service
    {
        void opDerived();
    }

    interface MyInterfaceMostDerived : MyInterfaceDerived
    {
        void opMostDerived();
    }
}
