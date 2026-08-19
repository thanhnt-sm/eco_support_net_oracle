# DataGuard for VS Code

VS Code extension for [DataGuard](https://github.com/thanhnt-sm/eco_support_net_oracle) contract validation
(entity ↔ stored procedure / raw SQL contracts).

## Features

- Status bar item `$(shield) DataGuard` — click to run validation.
- Command Palette: **DataGuard: Run Validation**.
- Results are printed to the `DataGuard` output channel.

## Requirements

- The `dataguard` CLI must be available on `PATH`:
  `dotnet tool install -g DataGuard.Cli`

## Settings

| Setting                 | Type    | Default          | Description                                            |
| ----------------------- | ------- | ---------------- | ------------------------------------------------------ |
| `dataguard.enabled`     | boolean | `true`           | Enable or disable DataGuard validation.                |
| `dataguard.configPath`  | string  | `.dataguard.yml` | Config file path, relative to the workspace root.      |

## Development

```bash
npm install
npm run compile   # tsc -p ./, outputs to out/
```

Press F5 in VS Code to launch an Extension Development Host.
