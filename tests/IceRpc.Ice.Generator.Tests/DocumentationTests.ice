// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::Tests
{
    // This file contains Slice definitions for testing the mapping of doc comments

    /// The User class represents an entity that has access to the system, the associated
    /// {@link email} address must be unique.
    class User
    {
        /// The user name.
        string name;
        /// The email associated with the user account.
        string email;
    }

    /// The exception that is thrown by {@link UserManager::registerAccount} when a user account
    /// with the same {@link User::email} already exists.
    exception UserAlreadyExistsException {}

    /// The exception that is thrown by {@link UserManager::registerAccount} when the provided
    /// email address is not valid.
    exception InvalidEmailAddressException
    {
        /// The reason why the email address validation failed.
        string reason;
    }

    /// A simple service for managing the system users.
    /// @see User
    interface UserManager
    {
        /// Registers a new user account.
        /// @param name The user name.
        /// @param email The user email.
        /// @throws UserAlreadyExistsException When a user with the same email address already exists.
        /// @throws InvalidEmailAddressException When the provided email address is not a valid email address.
        /// @return The user object.
        User registerAccount(string name, string email)
            throws UserAlreadyExistsException, InvalidEmailAddressException;
    }
}
