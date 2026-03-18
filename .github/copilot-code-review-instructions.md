# Copilot Code Review Instructions

## API-Breaking Changes

The `main` branch is our development branch. When a pull request targets `main`, do **not** flag
API-breaking changes (such as removing or renaming public methods, classes, or extension methods, or changing
method signatures). Breaking changes are expected and intentional on `main`.

For pull requests targeting any other branch (e.g., release branches), continue to flag API-breaking changes as
warnings.
