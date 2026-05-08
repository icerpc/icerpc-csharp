# Repository Guidelines for AI Coding Agents

These conventions apply to all AI coding assistants (Copilot, Claude Code, etc.) working in this repository.

## Code style

- **Line width: 120 columns.** Applies to C# code, doc comments, and regular comments. Reflow accordingly.
  This is enforced informally; see `guidelines = 120` in [.editorconfig](.editorconfig).
  Two exceptions:
  - A method whose return type is a long tuple keeps the tuple on the same line as the method name, even if
    that line exceeds 120 columns.
  - Long string literals may exceed 120 columns rather than being broken up.
- Indentation: 4 spaces (no tabs) for C# files.
- Trailing newline at end of file.

## Comments and documentation

- Prefer `#1234` over full GitHub URLs when referencing issues or PRs in this repo
  (`icerpc/icerpc-csharp`). Use full URLs only when linking to other repositories.
- XML doc comments use `<summary>`, `<remarks>`, `<param>`, etc. — not Markdown.

## Branch conventions

- `main` is the active development branch. **API-breaking changes are allowed and expected on `main`.**
  Do not flag, warn about, or suggest `[Obsolete]` shims for source-/binary-breaking changes targeting `main`.
- For PRs targeting release branches, treat API-breaking changes as warnings worth flagging.

## Tests

- Test projects live under `tests/`. Use NUnit (`[Test]`, `Assert.That(..., Is.X)`).
- Test method names use `Snake_case_descriptions` (e.g. `Scoped_service_is_disposed_before_response_payload_is_read`).

## Build / test commands

- Build the whole solution: `dotnet build IceRpc.slnx`
- Run all tests: `dotnet test`
- Run a single test project: `dotnet test tests/<Project>/<Project>.csproj --filter "FullyQualifiedName~<TestClass>"`
