# Contributing

Thanks for helping make Blazor testing better.

## Build and test

```bash
dotnet build BlazorTestPlatform.slnx
for p in tests/*/*.csproj; do dotnet test --project "$p"; done
pwsh samples/Contoso.UI.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test --project samples/Contoso.UI.Tests
```

On Windows and macOS, the first browser run writes local baselines (`*-win32.png`, `*-darwin.png`) into `samples/Contoso.UI.Tests/__snapshots__/`. They are git-ignored: CI compares against the committed Linux baselines. To add or update those, run `scripts/visual-tests.sh --update` (or `pwsh scripts/visual-tests.ps1 -Update`), which renders them in CI's Playwright image, and commit the `*-linux` images.

## Guidelines

* **Driver parity first.** If you add an interaction or assertion, implement it in all three drivers (bUnit, Playwright and the in-app driver behind the workbench's **Run interactions**) and add a story to the sample that exercises it under every render mode. The sample runs every play script through all three.
* **Determinism is a feature.** Never introduce wall-clock time, unseeded randomness, culture-dependent formatting or unordered enumeration into anything that reaches a snapshot, fingerprint, baseline or story id.
* **Stable contracts.** Story ids (`StoryId.Slugify`), snapshot names (`SnapshotNaming`), the `DeterministicRandom` sequence and the frame contract (`data-btp-*` and `window.__btp`) are public contracts. Change them only with a migration note.
* **Use axe ids** for accessibility rules so results line up across engines.
* Public APIs need XML documentation. The packages under `src/` build with warnings as errors.
