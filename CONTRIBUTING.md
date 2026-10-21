# Contributing

Thanks for helping make Blazor testing better.

## Build and test

```bash
dotnet build BlazorTestPlatform.slnx
for p in tests/*/*.csproj; do dotnet test --project "$p"; done
pwsh samples/Contoso.UI.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test --project samples/Contoso.UI.Tests
```

## Guidelines

* **Driver parity first.** If you add an interaction or assertion, implement it in both drivers and add a story to the sample that exercises it under every render mode.
* **Determinism is a feature.** Never introduce wall-clock time, unseeded randomness, culture-dependent formatting or unordered enumeration into anything that reaches a snapshot, fingerprint, baseline or story id.
* **Stable contracts.** Story ids (`StoryId.Slugify`), snapshot names (`SnapshotNaming`), the `DeterministicRandom` sequence and the frame contract (`data-btp-*` and `window.__btp`) are public contracts. Change them only with a migration note.
* **Use axe ids** for accessibility rules so results line up across engines.
* Public APIs need XML documentation. CI builds with warnings as errors.
