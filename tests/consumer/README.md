# NuGet consumer verification

The in-repo test suites reference the library **projects**, so they
cannot catch packaging defects: a missing analyzer asset in the
`.nupkg`, a broken package dependency, a registration that only works
from the source tree, or a trim/AOT break that project references
mask. This harness is the independent consumer (roadmap Phase 2C
#11/#13).

## What it does

`verify.sh`:

1. Packs the four packages (`ElegantSeries.Flow`,
   `ElegantSeries.Flow.Mvvm`, `ElegantSeries.Flow.Avalonia`,
   `ElegantSeries.Flow.WPF`) into `feed/`.
2. Restores `ConsumerApp/` — a standalone console app that is **not**
   part of the solution — with `nuget.config` source mapping that
   resolves every `ElegantSeries.Flow*` package **only** from that
   feed (third-party dependencies still come from nuget.org). The
   packed version (MinVer) is read from the produced file name and
   passed as `-p:FlowVersion=...`.
3. Builds and runs the consumer (JIT). The consumer follows the
   documented flow — `AddElegantSeriesFlow()`,
   `AddElegantSeriesFlowViews(views => views.RegisterAttributedViews())`
   with `[ViewFor]`-declared Avalonia views — then navigates
   (including an Mvvm `BaseViewModel` page and a KeepAlive round
   trip) and asserts the generator-produced view registrations
   resolve the right view types. It prints one `PASS`/`FAIL` line
   per check and exits non-zero on any failure.
4. Publishes the same consumer with NativeAOT
   (`-p:PublishAot=true`, RID from `FLOW_VERIFY_RID`, default
   `linux-x64`) and runs the native binary through the same checks.

## Running

```bash
tests/consumer/verify.sh
```

Requires the .NET SDK and, for the AOT pass, the platform's native
toolchain (e.g. `gcc`/`zlib` headers on Linux, the C++ workload on
Windows). Set `FLOW_VERIFY_RID=win-x64` etc. on other platforms.

## CI wiring

The workflow files are maintained outside this repository's agent
scope; add a job that runs `tests/consumer/verify.sh` on the
packaging branch (and on Windows with `FLOW_VERIFY_RID=win-x64`
under Git Bash, or by running the four steps manually). A red run
here means the packages — not the code — are broken; do not publish
from a tree whose consumer verification has not passed.

`feed/` and `.work/` are scratch output and are git-ignored.
