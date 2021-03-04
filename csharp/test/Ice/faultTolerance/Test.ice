//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc::Test::FaultTolerance
{

interface TestIntf
{
    void shutdown();
    void abort();
    idempotent int pid();
}

interface Cleaner
{
    void cleanup();
}

}
