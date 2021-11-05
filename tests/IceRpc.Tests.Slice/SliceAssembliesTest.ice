// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#include <A.ice>

module IceRpc::Tests::Slice
{
    interface AssembliesOperations
    {
        IceRpc::Tests::ReferencedAssemblies::ClassA opA(IceRpc::Tests::ReferencedAssemblies::ClassA b);
    }
}
