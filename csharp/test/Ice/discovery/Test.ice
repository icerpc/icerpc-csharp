//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

// TODO: move this test to Ice/discovery

[[suppress-warning(reserved-identifier)]]

module IceRpc::Test::Discovery
{
    interface TestIntf
    {
        string getAdapterId();
    }

    interface Controller
    {
        void activateServer(string name, string adapterId, string replicaGroupId);
        void deactivateServer(string name);

        void addObject(string oaName, string identityAndFacet);
        void removeObject(string oaName, string identityAndFacet);

        void shutdown();
    }
}
