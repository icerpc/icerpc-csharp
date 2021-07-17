// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/Service.ice>

module IceRpc::Tests::CodeGeneration
{
    interface MyInterfaceMostDerived;

    interface MyInterfaceBase
    {
        MyInterfaceMostDerived opBase(MyInterfaceBase p);
    }

    interface MyInterfaceDerived : MyInterfaceBase
    {
        MyInterfaceBase opDerived(MyInterfaceMostDerived p);
    }

    interface MyInterfaceMostDerived : MyInterfaceDerived
    {
        void opMostDerived();
    }
}
