//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[3.7]]
[[suppress-warning(reserved-identifier)]]

#include <Namespace.ice>
#include <NoNamespace.ice>

module IceRpc::Test::NamespaceMD
{

interface Initial
{
    NoNamespace::C1 getNoNamespaceC2AsC1();
    NoNamespace::C2 getNoNamespaceC2AsC2();
    NoNamespace::N1::N2::S1 getNoNamespaceN1N2S1();
    void throwNoNamespaceE2AsE1();
    void throwNoNamespaceE2AsE2();
    void throwNoNamespaceNotify();

    WithNamespace::C1 getWithNamespaceC2AsC1();
    WithNamespace::C2 getWithNamespaceC2AsC2();
    WithNamespace::N1::N2::S1 getWithNamespaceN1N2S1();
    M0::M2::M3::S2 getNestedM0M2M3S2();
    void throwWithNamespaceE2AsE1();
    void throwWithNamespaceE2AsE2();

    void shutdown();
}

}
