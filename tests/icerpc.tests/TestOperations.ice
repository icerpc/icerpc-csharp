
[[suppress-warning(reserved-identifier)]]

module IceRpc::Ice::Tests::Operations
{
    interface Tester
    {
        void opVoid();
        byte opByte(byte value);
        short opShort(short value);
        string opString(string value);
    }
}
