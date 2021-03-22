// Copyright (c) ZeroC, Inc. All rights reserved.

module Test::Foo::Bar
{
    interface C
    {
        void shutdown();
    }
}

module Test
{
    module X
    {
        module Y::Z
        {
            interface D
            {
                void shutdown();
            }
        }

        interface E
        {
            void shutdown();
        }
    }
}
