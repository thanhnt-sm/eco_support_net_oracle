# Codecov & GitHub UI Configuration Report (`thanhnt-sm/eco_support_net_oracle`)

## 1. Chrome (`testsurvey` / `Profile 1`) Inspection on Codecov UI
- **Target URLs Inspected via `computer`:**
  - `https://app.codecov.io/github/thanhnt-sm/eco_support_net_oracle/new`
  - `https://app.codecov.io/github/thanhnt-sm/eco_support_net_oracle/config/general`
  - `https://app.codecov.io/github/thanhnt-sm/eco_support_net_oracle`
- **Visual & AX Evidence Captured:**
  - Screenshot 1 (`/new` setup wizard): `C:\Users\thant\AppData\Local\Temp\omp-computer-15a57d03e0925b43.png`
  - Screenshot 2 (`/config/general` settings): `C:\Users\thant\AppData\Local\Temp\omp-computer-15a57d70a1925b45.png`
  - Screenshot 3 (Live Activated Dashboard & Sunburst Chart): `C:\Users\thant\AppData\Local\Temp\omp-computer-15a5850c69d25b4d.png`
- **Verified Settings on Codecov (`/config/general`):**
  - **Repository:** `thanhnt-sm/eco_support_net_oracle` (`language: c#`, `private: false`)
  - **Owner ID:** `0e6acf2d-ef71-4d66-acfe-212f1f5bd92d`
  - **Default Branch (`Branch Context`):** `main`
  - **Global Upload Tokenless Status:** **ENABLED** — Codecov `/config/general` explicitly confirms:
    > *"Uploading with token is now not required. You can upload without a token. Contact your admins to manage the global upload token settings."*

## 2. GitHub Repository & Dependabot Secrets Configuration (`gh secret set`)
Using the authenticated GitHub CLI (`thanhnt-sm`), configured `CODECOV_TOKEN` on `thanhnt-sm/eco_support_net_oracle` (`gh secret list`):
- **GitHub Actions Repository Secret:** `CODECOV_TOKEN` (`Updated: 2026-10-14T05:18:20Z`)
- **GitHub Dependabot Secret:** `CODECOV_TOKEN` (`Updated: 2026-10-14T05:18:21Z`)

## 3. Repository Activation & First Baseline Coverage Upload (`64.08%`)
Uploaded the solution's Cobertura XML reports across all 5 cross-platform test suites (`DataGuard.Core.Tests`, `DataGuard.GoldenCorpus.Tests`, `DataGuard.Analyzers.Tests`, `DataGuard.CodeFixes.Tests`, `DataGuard.Observability.Tests`) for `main` (`commit 3975c063bbe97f9d413de7b75d2f64af19c39b41`, flag `dotnet-unit`):
- **Upload Result URL:** `https://app.codecov.io/github/thanhnt-sm/eco_support_net_oracle/commit/3975c063bbe97f9d413de7b75d2f64af19c39b41`
- **Codecov v2 API Verification (`GET https://api.codecov.io/api/v2/github/thanhnt-sm/repos/eco_support_net_oracle/`):**
  ```json
  {
    "name": "eco_support_net_oracle",
    "private": false,
    "updatestamp": "2026-10-14T05:23:13.466218Z",
    "author": {
      "service": "github",
      "username": "thanhnt-sm",
      "name": "thanhnt-sm"
    },
    "language": "c#",
    "branch": "main",
    "active": true,
    "activated": true,
    "totals": {
      "files": 171,
      "lines": 20626,
      "hits": 13219,
      "misses": 5974,
      "partials": 1433,
      "coverage": 64.08,
      "branches": 3910,
      "methods": 0,
      "sessions": 1,
      "complexity": 0.0,
      "complexity_total": 0.0,
      "complexity_ratio": 0,
      "diff": 0
    }
  }
  ```
- **Status:** `active: true`, `activated: true`, `coverage: 64.08%` (`13,219 / 20,626` lines covered across `171` tracked source files).
