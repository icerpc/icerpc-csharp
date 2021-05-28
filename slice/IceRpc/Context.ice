/// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc
{
    /// A request context.
    [cs:generic:SortedDictionary] dictionary<string, string> Context;
}
