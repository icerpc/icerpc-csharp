// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc
{
    /// A request context.
    typealias Context = [cs:generic("SortedDictionary")] dictionary<string, string>;
}
