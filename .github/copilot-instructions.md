# Copilot Code Review Instructions

## CRITICAL: Do NOT flag API-breaking changes

This repository's `main` branch is our active development branch. All pull requests that target `main` are
allowed to make API-breaking changes. This is by design.

**You must never comment on, warn about, or suggest alternatives for any of the following in PRs targeting
`main`:**

- Removing or renaming public methods, properties, classes, interfaces, or enums
- Changing method signatures (adding, removing, or reordering parameters)
- Removing overloads or extension methods
- Changing return types
- Removing or renaming public fields or constants
- Any other source-breaking or binary-breaking API change

Do not suggest adding `[Obsolete]` attributes, compatibility overloads, or migration paths for these changes.
They are intentional. Skip them entirely.

For pull requests targeting any other branch (e.g., release branches), flag API-breaking changes as warnings.