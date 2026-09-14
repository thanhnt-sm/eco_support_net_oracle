# Repository Inventory

## Snapshot

- **[CONFIRMED]** Root là git work tree; HEAD `93bf7288324dd746669ad09c5e2a592adc772748`, commit date `2026-09-07T11:06:37+07:00`.
- **[CONFIRMED]** `git ls-files` đếm 445 tracked paths. `git status --porcelain --untracked-files=all` tại thời điểm quét: 130 tracked paths modified, 0 deleted, 0 added và 133 untracked paths (263 path entries). Đây là trạng thái đã có trước discovery; không sửa hoặc dọn dẹp.
- **[CONFIRMED]** Tracked C# source: 51 files/18,427 LOC; tracked C# tests: 31 files/9,447 LOC; tracked Markdown: 136 files; workflow YAML: 6 files.
- **[CONFIRMED]** Top-level tracked distribution gồm `.github` 21, `docs` 143, `research` 50, `src` 89, `tests` 50, `plans` 25, `rules` 5 và các manifest/scripts khác.

## Project surface

Các project nhìn thấy (trạng thái untracked chỉ phản ánh git status, không phải runtime readiness):

| Path | TFM | Trạng thái | Bằng chứng |
|---|---|---|---|
| `src/DataGuard.Core` | `net9.0` | tracked | `DataGuard.Core.csproj`; `Directory.Build.props` |
| `src/DataGuard.SqlServer.Adapter` | `net9.0` | tracked | project file |
| `src/DataGuard.Oracle.Adapter` | `net9.0` | tracked | project file |
| `src/DataGuard.PostgreSql.Adapter` | `net9.0` | tracked | project file |
| `src/DataGuard.MySql.Adapter` | `net9.0` | tracked | project file |
| `src/DataGuard.Analyzers` | `netstandard2.0` | tracked | project file |
| `src/DataGuard.CodeFixes` | `netstandard2.0` | tracked | project file |
| `src/DataGuard.Contracts` | `netstandard2.0` | tracked | project file |
| `src/DataGuard.Cli` | `net9.0` | tracked | project file |
| `src/DataGuard.VisualStudio` | `net472` | tracked | project file |
| `src/DataGuard.Host` | `net9.0` | untracked/WIP | project file |
| `src/DataGuard.Build` | `net9.0` | untracked/WIP | project file |
| `src/DataGuard.LanguageServer` | `net9.0` | untracked/WIP | project file |
| `src/DataGuard.SqlClassification` | `netstandard2.0` | untracked/WIP | project file |
| `tests/DataGuard.Core.Tests` | `net9.0` | tracked | project file |
| `tests/DataGuard.GoldenCorpus.Tests` | `net9.0` | tracked | project file |
| `tests/DataGuard.Analyzers.Tests` | `net9.0` | tracked | project file |
| `tests/DataGuard.CodeFixes.Tests` | `net9.0` | tracked | project file |
| `tests/DataGuard.BinaryCompatibilityFixture` | `net9.0` | untracked/WIP | project file |
| `samples/DataGuard.Sample` | `net9.0` | tracked | project file |
| `benchmarks/DataGuard.Benchmarks` | `net9.0` | tracked | project file |
| `tools/benchmarks/DataGuard.Benchmarks` | `net9.0` | untracked/WIP/duplicate name | project file |

- **[CONFIRMED]** `DataGuard.sln` chứa 19 project entries gồm solution folders và các project Core/adapters/analyzers/CLI/tests/Host/Build/SqlClassification/LanguageServer.
- **[CONFLICT]** Solution không tham chiếu visible `samples/DataGuard.Sample`, `benchmarks/DataGuard.Benchmarks`, `src/DataGuard.VisualStudio`, `tests/DataGuard.BinaryCompatibilityFixture` và project benchmark trùng tên dưới `tools/benchmarks`. Việc này ảnh hưởng đến “solution build” scope nhưng chưa được build để xác minh.

## Shared build policy

- **[CONFIRMED]** `Directory.Build.props:1-41` đặt default `net9.0`, nullable/implicit usings, `TreatWarningsAsErrors`, XML docs, `LangVersion=latest`, `AnalysisLevel=latest`, Release optimize/embedded PDB, package output `nupkg`, MinVer và `RestorePackagesWithLockFile=true`.
- **[CONFIRMED]** Shared analyzers/source-link/MinVer package versions được centralize trong `Directory.Build.props`; từng project vẫn có direct references riêng.
- **[CONFIRMED]** `.gitignore:1-109` loại `bin/obj`, packages, test/coverage, IDE, env/cert/key extensions và runtime dirs `.omo/.omp/.codex/.dataguard*`; `_observability_discovery` chưa nằm trong ignore vì không được sửa file ngoài scope.
- **[CONFIRMED]** Workspace có ignored/generated dirs như `.codegraph`, `.omp`, `.omo`, `.tmp`, `BenchmarkDotNet.Artifacts`, `TestResults`, `coverage`, nhiều `bin/obj` và VS Code `node_modules`. Không đọc nội dung runtime/generated hoặc đưa vào ZIP.

## Scope risk

- **[INFERRED_MEDIUM]** Dirty tree và untracked WIP làm snapshot không tái lập hoàn toàn nếu không có owner xác nhận baseline commit; mọi kết luận ở đây là snapshot static, không phải build artifact.
- **[UNKNOWN]** Branch/remote policy, owner từng project và intended inclusion của WIP/duplicate benchmark chưa có bằng chứng trong workspace.
