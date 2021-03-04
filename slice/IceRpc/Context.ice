/// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc
{
    /// A request context. Each operation has a <code>Context</code> as its implicit final parameter.
    [cs:generic:SortedDictionary] dictionary<string, string> Context;
}
