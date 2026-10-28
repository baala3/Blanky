# Contributing to Blanky

Thanks for helping make Blanky better.

## Reporting bugs and requesting features

Open an [issue](https://github.com/baala3/Blanky/issues).
For audio or crash problems, attach `%LOCALAPPDATA%\Blanky\blanky.log` and the Windows version.

## Code contributions

1. Fork the repo and create a branch from `main`.
2. Build and test on Windows with the .NET 10 SDK:

   ```powershell
   dotnet build Blanky.slnx -c Debug -p:Platform=x64
   dotnet build tests/Blanky.Core.Tests
   tests/Blanky.Core.Tests/bin/Debug/net10.0/Blanky.Core.Tests.exe
   ```

3. Open a pull request describing what changed and how you tested it.

Guidelines:

- Playback rules live in `Blanky.Core` and should come with tests in `tests/Blanky.Core.Tests`.
- Warnings are errors, and the analyzers run on every build.
- Match the surrounding style; `.editorconfig` covers formatting.
- When Blankie has a feature, follow its behavior unless Windows conventions call for something else.

## Sounds

New sounds must have a license that allows redistribution (CC0, CC BY or public domain).
Add the file to `src/Blanky/Assets/Sounds`, an entry to `src/Blanky/Assets/sounds.json`, and a row to `CREDITS.md`.

## Website

```bash
cd website
npm ci
npm run dev
```

Screenshots come from the real app: `python3 scripts/screenshots.py` (from WSL, after a Debug build).
