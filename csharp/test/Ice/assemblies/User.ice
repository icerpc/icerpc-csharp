
#include <Base.ice>

[[3.7]]

[[suppress-warning(reserved-identifier)]]

module IceRpc::Test::Assemblies::User
{
    class UserInfo
    {
    }

    interface Registry
    {
        UserInfo getUserInfo(string id, IceRpc::Test::Assemblies::Core::Data data);
    }
}
