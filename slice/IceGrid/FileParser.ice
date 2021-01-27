//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceGrid/Admin.ice>

[cs:namespace(ZeroC)]
module IceGrid
{
    /// This exception is raised if an error occurs during parsing.
    exception ParseException
    {
        /// The reason for the failure.
        string reason;
    }

    /// <code>icegridadmin</code> provides a {@link FileParser}
    /// object to transform XML files into {@link ApplicationDescriptor}
    /// objects.
    interface FileParser
    {
        /// Parse a file.
        ///
        /// @param xmlFile Full pathname to the file.
        ///
        /// @param adminProxy An Admin proxy, used only to retrieve default
        /// templates when needed. May be null.
        ///
        /// @return The application descriptor.
        ///
        /// @throws ParseException Raised if an error occurred during parsing.
        idempotent ApplicationDescriptor parse(string xmlFile, Admin* adminProxy);
    }
}
