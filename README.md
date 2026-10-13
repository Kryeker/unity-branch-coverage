# Code Coverage Branch Enricher (`com.kryeker.codecoverage-branch`)

Standalone Unity package: wrapper over `com.unity.testtools.codecoverage@1.2.7`
that backfills synthetic OpenCover `<BranchPoint>`s into coverage XML, so HTML /
Cobertura / SonarQube reports show a branch-coverage column.

## Why synthetic

Unity 2020.3's `UnityEngine.TestTools.Coverage` API only exposes **sequence points**
(`line/column/hitCount/ilOffset`). The stock `OpenCoverReporter` therefore always writes
empty `BranchPoints`. This package disassembles each covered method's IL (via the
`Mono.Reflection` copy vendored inside the coverage package) and emits two branch paths
per conditional branch (`Cond_Branch`: fall-through + target) and one per `switch` target.

**Visit counts are heuristic**: a path counts as visited when the sequence point at its
target IL offset was hit. This is an approximation, not instrumentation — treat the
branch % as directional, not exact.

When the covered assembly is not loaded (batch/CI context), the enricher falls back to
one branch path per sequence point (`SynthesizeFromSequencePoints`), so the branch
column is still populated.

## Wiring

- `BranchCoverageEnricher` (`[InitializeOnLoad]`) subscribes to
  `UnityEditor.TestTools.CodeCoverage.Events.onCoverageSessionFinished` and enriches every
  result XML in `SessionResultPaths` in place (skips `*_fullEmpty.xml` baselines).
- `BranchEnricherMenu` adds `Tools > Code Coverage > Enrich Branch Points In OpenCover XMLs...`
  for re-running enrichment over an existing results folder.
- `BranchSynthesizer` holds the IL → `BranchPoint` logic; `EnrichFile(path)` is public so
  CI scripts can call it after a batch run and before ReportGenerator.

## Install

Via git URL in `Packages/manifest.json`:

```json
"com.kryeker.codecoverage-branch": "https://github.com/<owner>/codecoverage-branch.git"
```

Or as a local folder during development:

```json
"com.kryeker.codecoverage-branch": "file:../codecoverage-branch"
```

Requires `com.unity.testtools.codecoverage: 1.2.7` (declared in this package's `package.json`).

## Tests

`Tests/branch-example.xml` is a hand-written fixture showing the enriched output shape
(4 sequence points → 4 branch points, `branchCoverage="75.00"`).

## C# level

C# 7.3 (Unity 2020.3 policy): no nullable reference types, no `System.Text.Json`, no
`ValueTask`/`Range`/`Index`.
