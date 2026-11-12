// Copyright (c) ZeroC, Inc.

using System.Diagnostics;

namespace IceRpc.Telemetry.Internal;

/// <summary>Provides an extension method for <see cref="ActivitySource" /> to create the activity of a request.
/// </summary>
internal static class ActivitySourceExtensions
{
    extension(ActivitySource activitySource)
    {
        /// <summary>Creates the activity of a request, without starting it.</summary>
        /// <param name="kind">The activity kind.</param>
        /// <param name="path">The path of the request.</param>
        /// <param name="operation">The operation of the request.</param>
        /// <returns>The new activity. This activity doesn't belong to the activity source when no listener samples it.
        /// </returns>
        internal Activity CreateRequestActivity(ActivityKind kind, string path, string operation)
        {
            string name = $"{path}/{operation}";
            KeyValuePair<string, object?>[] tags =
            [
                new("rpc.system.name", "icerpc"),
                new("rpc.method", operation),
                new("icerpc.path", path),
            ];

            // A sampler sees only the tags given to CreateActivity.
            Activity? activity = activitySource.CreateActivity(name, kind, parentContext: default, tags);
            if (activity is null)
            {
                activity = new Activity(name);
                foreach ((string key, object? value) in tags)
                {
                    activity.AddTag(key, value);
                }
            }
            return activity;
        }
    }
}
