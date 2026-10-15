# Changelog

All notable changes to `com.kryeker.codecoverage-branch` are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.0.0] - 2026-10-15

### Added
- Initial standalone package: `BranchCoverageEnricher` (`[InitializeOnLoad]`)
  backfills synthetic OpenCover `<BranchPoint>`s after each coverage session.
- `BranchSynthesizer`: IL → `BranchPoint` logic (two paths per `Cond_Branch`,
  one per `switch` target) plus `SynthesizeFromSequencePoints` fallback for
  batch/CI contexts where the covered assembly is not loaded.
- `BranchEnricherMenu`: `Tools > Code Coverage > Enrich Branch Points In OpenCover XMLs...`
  for re-running enrichment over an existing results folder.
- `Tests/branch-example.xml`: hand-written fixture showing the enriched output shape.
- `Tests/Editor/BranchSynthesizerTests.cs`: EditMode NUnit tests for the pure
  synthesis logic (`SynthesizeFromSequencePoints` mapping/sequencing/guards,
  `Synthesize` guards and cond-branch pairing).
- `LICENSE.md` (MIT), `README.md`, `CHANGELOG.md`.
