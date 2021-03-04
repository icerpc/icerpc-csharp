//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc::Test::DefaultServant
{
    interface MyObject
    {
        string getName();
    }
}
