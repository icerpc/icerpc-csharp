// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

module Demo
{
    interface Hello
    {
        idempotent string sayHello(string name);
    }
}
