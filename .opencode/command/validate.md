---
description: Build, test and self-test the Epic Pencil increment and report GO/NO-GO against budgets.
---

Run the full verification sequence for the current increment and report results against the perf budgets in the `epic-pencil-guardrails` skill:

1. `dotnet build EpicPencil.sln --nologo -v minimal` (from repo root; use the SDK path if `dotnet` is not on PATH).
2. `dotnet test EpicPencil.sln --nologo`.
3. `dotnet run --project prototypes/S1.Overlay --no-build -- --selftest`.
4. `dotnet run --project src/EpicPencil.Shell/EpicPencil.Shell.csproj --no-build -- --selftest`.
5. Grep `src/EpicPencil.Core` for `System\.Windows|DllImport|Presentation` (expect zero code hits).

Report: build warnings/errors, test counts, both self-test outputs with timing numbers + hardware context if known, purity-check result, and a verdict (GO green / NO-GO with the failing gate). $ARGUMENTS
