//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

module ZeroC::Ice::Test::Binding
{

interface TestIntf
{
    string getAdapterName();
}

interface RemoteServer
{
    TestIntf getTestIntf();

    void deactivate();
}

interface RemoteCommunicator
{
    RemoteServer createServer(string name, string transport);
    RemoteServer createServerWithEndpoints(string name, string endpoints);

    void deactivateServer(RemoteServer server);

    void shutdown();
}

}
