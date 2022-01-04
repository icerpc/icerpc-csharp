// Copyright (c) ZeroC, Inc. All rights reserved.

[cs:namespace(IceRpc)]
module Ice
{
    /// This exception is thrown when a server tries to register endpoints for an object adapter that is already active.
    exception AdapterAlreadyActiveException
    {
    }

    /// This exception is thrown when an object adapter was not found.
    exception AdapterNotFoundException
    {
    }

    /// This exception is thrown when the provided replica group is invalid.
    exception InvalidReplicaGroupIdException
    {
    }

    /// This exception is thrown when an Ice object was not found.
    exception ObjectNotFoundException
    {
    }

    /// This exception is thrown when a server was not found.
    exception ServerNotFoundException
    {
    }

    /// Client applications use Locator to resolve locations and well-known proxies. The Locator object also allows
    /// server applications to retrieve a proxy to the LocatorRegistry object.
    interface Locator
    {
        /// Finds an object by identity and facet and returns a proxy that provides a location or endpoint(s) that can
        /// be used to reach the object using the ice1 protocol.
        /// @param id The identity.
        /// @return An ice1 proxy that provides a location or endpoint(s), or null if an object with the requested
        /// identity was not found.
        /// @throws ObjectNotFoundException Thrown if an object with the requested identity was not found. The caller
        /// should treat this exception like a null return value.
        idempotent findObjectById(id: Identity) -> IceRpc::Service?;

        /// Finds an object adapter by id and returns a proxy that provides the object adapter's endpoint(s). This
        /// operation is for object adapters using the ice1 protocol.
        /// @param id The adapter ID.
        /// @return An ice1 proxy with the adapter's endpoint(s), or null if an object adapter with adapter ID 'id' was
        /// not found.
        /// @throws AdapterNotFoundException Thrown if an object adapter with this adapter ID was not found. The caller
        /// should treat this exception like a null return value.
        idempotent findAdapterById(id: string) -> IceRpc::Service?;

        /// Gets the locator registry.
        /// @return The locator registry, or null if this locator has no registry.
        idempotent getRegistry() -> LocatorRegistry?;
    }

    /// A server application registers the endpoints of its indirect object adapters with the LocatorRegistry object.
    interface LocatorRegistry
    {
        /// Registers or unregisters the endpoints of an object adapter that uses the ice1 protocol.
        /// @param id The adapter ID.
        /// @param proxy A dummy direct proxy created by the object adapter that provides the object adapter's
        /// endpoints. The locator considers an object adapter to be active after it has registered its endpoints. When
        /// proxy is null, the endpoints are unregistered and the locator considers the object adapter inactive.
        /// @throws AdapterNotFoundException Thrown if the locator only allows registered object adapters to register
        /// their active endpoints and no object adapter with this adapter ID was registered with the locator.
        /// @throws AdapterAlreadyActiveException Thrown if an object adapter with the same adapter ID has already
        /// registered its endpoints.
        // Note: idempotent is not quite correct, and kept only for backwards compatibility with old implementations.
        idempotent setAdapterDirectProxy(id: string, proxy: IceRpc::Service?);

        /// Registers or unregisters the endpoints of an object adapter that uses the ice1 protocol. This object adapter
        /// is member of a replica group.
        /// @param adapterId The adapter ID.
        /// @param replicaGroupId The replica group ID.
        /// @param proxy A dummy direct proxy created by the object adapter that provides the object adapter's
        /// endpoints. The locator considers an object adapter to be active after it has registered its endpoints. When
        /// proxy is null, the endpoints are unregistered and the locator considers the object adapter inactive.
        /// @throws AdapterNotFoundException Thrown if the locator only allows registered object adapters to register
        /// their active endpoints and no object adapter with this adapter ID was registered with the locator.
        /// @throws AdapterAlreadyActiveException Thrown if an object adapter with the same adapter ID has already
        /// @throws InvalidReplicaGroupIdException Thrown if the given replica group does not match the replica group
        /// associated with the adapter ID in the locator's database.
        // Note: idempotent is not quite correct, and kept only for backwards compatibility with old implementations.
        idempotent setReplicatedAdapterDirectProxy(
            adapterId: string,
            replicaGroupId: string,
            proxy: IceRpc::Service?,
        );

        /// Registers a proxy for a server's Process object.
        /// @param serverId The server ID.
        /// @param proxy A proxy for the server's Process object.
        /// @throws ServerNotFoundException Thrown if the locator does not know a server with this server ID.
        idempotent setServerProcessProxy(serverId: string, proxy: Process);
    }

    /// This interface is implemented by services that implement the Ice::Locator interface, and is advertised as an Ice
    /// object with the identity 'Ice/LocatorFinder'. This allows clients to retrieve the locator proxy with just the
    /// endpoint information of the service.
    interface LocatorFinder
    {
        /// Gets the locator proxy implemented by the service hosting this finder object. The proxy might point to
        /// several replicas.
        /// @return The locator proxy.
        getLocator() -> Locator;
    }
}
