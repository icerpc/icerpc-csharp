// Copyright (c) ZeroC, Inc.

module Journal
{
    /// Represents a logger that receives each message in a separate oneway request.
    interface SimpleLogger
    {
        /// Logs a message.
        /// @param message The message to log.
        ["oneway"] void log(string message);
    }
}
