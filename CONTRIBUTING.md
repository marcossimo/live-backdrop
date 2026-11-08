# Contributing

Thanks for helping! Bug reports, ideas and pull requests are all welcome.

## Before you start

- For anything bigger than a small fix, open an issue first so we can agree on the approach.
- Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): it explains how the plugin and the overlay
  fit together, and which approaches were already tried and ruled out.

## Setup

- Windows 10 2004+ or Windows 11, the Claude desktop app
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) for the overlay
- [Claude Code](https://docs.claude.com/en/docs/claude-code/overview) for the plugin

```
dotnet test overlay/LiveBackdrop.sln
dotnet run --project overlay/src
claude --plugin-dir .
```

While developing, point the overlay at a scratch settings file with
`dotnet run --project overlay/src -- --settings C:\temp\settings.json`.

## Checks

Every pull request runs these in CI; please run them locally first:

```
dotnet build overlay/LiveBackdrop.sln -c Release   # warnings are errors
dotnet test overlay/LiveBackdrop.sln -c Release
claude plugin validate .
claude plugin test .
```

## Style

- Everything in English: code, comments, UI text, commits.
- Follow `.editorconfig`. C#: nullable enabled, warnings as errors, one type per file.
  TypeScript: strict, no `any`.
- Comments explain *why*, not what the code says.
- Keep the settings file compatible: new fields need defaults on both sides
  (`overlay/src/Settings.cs` and `hooks/settings.ts`), with tests.
- Add a line to `CHANGELOG.md` under **Unreleased**.

## Commits and pull requests

- Use [Conventional Commits](https://www.conventionalcommits.org/): `feat(panel): add blur`,
  `fix(overlay): follow the window across monitors`.
- One topic per pull request. Describe what changed, why, and how you tested it, with a
  screenshot or GIF for anything visible.

## Releases

Maintainers tag `vX.Y.Z` on `main`; the release workflow builds the overlay and publishes it.
Keep `version` in `.claude-plugin/plugin.json` and `<Version>` in
`overlay/src/LiveBackdrop.csproj` in step with the tag.
