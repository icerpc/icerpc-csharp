//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc::Test::Echo
{

//
// This object is available with the identity "__echo".
//
interface Echo
{
    void shutdown();
}

}
