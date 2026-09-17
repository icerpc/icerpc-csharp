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

- In PR descriptions, commit messages, and Markdown docs, prefer `#1234` over full GitHub URLs when referencing issues
  or PRs in this repo (`icerpc/icerpc-csharp`). Use full URLs only when linking to other repositories.
- Don't reference GitHub issues or PRs in source code or code comments (no `#1234`, no URLs). A comment must stand
  on its own; the issue belongs in the commit message and PR description.
- XML doc comments use `<summary>`, `<remarks>`, `<param>`, etc. — not Markdown.
- A comment states what the code cannot show: a why, a constraint, a contract, a non-obvious rule. It never narrates
  the code below it; assume the reader reads the code.
- A comment never describes the bug a change fixes or the behavior it replaces. That history belongs in the PR
  description, not in the source.
- Keep comments terse. Delete a comment that does not earn its keep.

## Branch conventions

- Development is trunk-based: fixes land on `main` first, then are cherry-picked or backported to release branches.
- `main` is the active development branch. **API-breaking changes are allowed and expected on `main`.**
  Do not flag, warn about, or suggest `[Obsolete]` shims for source-/binary-breaking changes targeting `main`.
- Each release series gets a branch named after it, created when its first version is released: `0.6.x` was
  created with the `0.6.0` release.
- Changes on a release branch must remain source- and binary-compatible with the first version of that series.
  Never introduce a breaking change there on your own; flag it instead. Maintainers make an exception only when a
  high-impact bug has no workaround short of the breaking fix.

## Tests

- Test projects live under `tests/`. Use NUnit (`[Test]`, `Assert.That(..., Is.X)`).
- Test method names use `Snake_case_descriptions` (e.g. `Scoped_service_is_disposed_before_response_payload_is_read`).

## Build / test commands

- Build the whole solution: `dotnet build IceRpc.slnx`
- Run all tests: `dotnet test`
- Run a single test project: `dotnet test tests/<Project>/<Project>.csproj --filter "FullyQualifiedName~<TestClass>"`

## Pull requests

- Every PR description ends with a `## What's Changed entry` section. Read
  [.github/pull_request_template.md](.github/pull_request_template.md) for the format and the rules before writing
  it — the template is not injected automatically when a PR is created from the command line.
- Set the PR's milestone to the release that will include its What's Changed entry, and give the PR the labels of the
  issue it fixes. From the command line, pass `--milestone` and `--label` to `gh pr create`, or set them afterwards
  with `gh pr edit`.

## Product documentation

The IceRPC documentation published at [docs.icerpc.dev](https://docs.icerpc.dev) lives in a separate repository,
`icerpc/icerpc-docs`. Only the API reference is generated from this repository (from the XML doc comments).

- When preparing a PR, consider how the change affects the product documentation: a new feature that needs to be
  documented, or a change to the behavior, API, or configuration of a feature the documentation already describes.
  Internal changes (refactoring, tests, build, CI) usually need nothing.
- A change that affects the documentation requires a follow-up in `icerpc/icerpc-docs`: create the companion PR, or
  file an issue there describing the pages or topics to add or update. Link the companion PR or issue in the PR
  description, and link back to this PR from it.

## Design decisions

[DESIGN-DECISIONS.md](DESIGN-DECISIONS.md) records deliberate design decisions that can look like defects: trust
boundaries, conventions, and trade-offs, most of them from `ai-audit` findings closed as "not planned". Read it before
reporting a security, robustness, or design concern (audit finding, review comment, new issue), and before changing
code in a way that reverses one of these decisions. The ones most likely to matter during ordinary coding tasks:

- `.slice`, `.proto`, and `.ice` files are trusted build inputs, not untrusted data; the generators don't validate or
  sanitize what they emit from them.
- Complete every `PipeReader` and `PipeWriter` with `Complete`, never `CompleteAsync`; a `Complete` implementation must
  not block.
- Unknown wire values (enumerators, parameters, fields) are tolerated for forward compatibility, not rejected.
- Limits are enforced systemically (decoder allocation budgets, frame size limits), not with a guard at each call site.
- Don't add defensive code for paths that only fire when a documented contract is already violated.
