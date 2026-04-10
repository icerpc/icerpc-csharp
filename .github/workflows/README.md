# IceRPC Slice Files

The Slice files used to build IceRPC for C# are shared across repositories and originate from different source
repositories.These vendored files must be updated in their source repositories first and then synced to this repository.

The slice.toml file defines which files are synced from which repositories. The build/sync-slice.py script can be used
to sync the vendored Slice files and to verify that they are in sync with their source repositories.
