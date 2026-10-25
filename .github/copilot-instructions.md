# Instructions for GitHub Copilot

Follow [AGENTS.md](../AGENTS.md): it holds the conventions for this repository.

When reviewing a pull request:

- Before reporting a security, robustness, or design concern, read [DESIGN-DECISIONS.md](../DESIGN-DECISIONS.md). Do
  not report a concern it records as a deliberate decision.
- `.slice`, `.proto`, and `.ice` files are trusted build inputs. Do not report validation, sanitization, or injection
  concerns about what the code generators emit from them; a bad value fails at C# compile time by design.
- Do not flag API-breaking changes on `main`; they are expected there. On a release branch (`0.6.x`, ...), flag them.
