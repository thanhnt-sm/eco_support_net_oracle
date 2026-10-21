# StackAccord Extension Testing Guide

## 1. Installation

1. Build or select a verified VSIX using the [extension build guide](../05-operations/extension-build-guide.md); do not assume a Debug output already exists.
2. Close Visual Studio and launch the Visual Studio VSIX installer for that package.
3. Restart the intended supported host and record the package hash and host version.

## 2. CLI Executable Discovery

The VSIX bundles a self-contained `cli\dataguard.exe`. `CliLocator` checks an explicit custom absolute executable path first, then the bundled CLI, `DATAGUARD_CLI_PATH`, standard install directories and filtered PATH candidates. An invalid explicit custom path fails instead of silently selecting another executable.

The extension does not install a NuGet global tool when discovery fails. Test the missing-CLI error; reinstall a verified VSIX or configure an accepted executable via **Tools → Options → DataGuard → General → Custom CLI Executable Path**. See the [installation guide](../05-operations/installation-guide.md).

`CliArgumentBuilder` sets `DOTNET_ROLL_FORWARD=LatestMajor`; this setting is not proof that a framework-dependent override works on every later .NET version. Exercise the actual executable/runtime combination and retain its diagnostics.

## 3. Options & Configuration

You can access the configuration settings at: **Tools** -> **Options** -> **DataGuard**.
* **General**:
  * **Diagnostics & Logging**: Toggle `Enable Detailed Logging` and specify a custom `Log Directory`.
  * **CLI Configuration**: Specify a `Custom CLI Executable Path` to override auto-discovery.
  * **Automation**:
    * `Run Validation on Build`: Automatically run validation on build success (default: false).
    * `Validation Timeout (seconds)`: Timeout for CLI `validate` executions, clamped between 5 and 900 seconds (default: 300).
    * `Assessment Timeout (seconds)`: Timeout for CLI `assess` executions, clamped between 5 and 900 seconds (default: 60).
* **Validation Rules**: Displays descriptions for all rules (e.g. `DG002`, `DG003`, `DG016`, etc.) and allows you to toggle each rule on or off. All rules are **enabled by default**.

## 4. Validating Projects

1. Open a solution that has a `.dataguard.yml` configuration file.
2. Under the **Tools** menu, locate the **StackAccord (Beta)** sub-menu and click **Run Validation**.
3. Output Window:
   * Navigate to the **Output** window and select **StackAccord (Beta)** from the "Show output from:" dropdown.
   * Review the command status, CLI diagnostics and exit outcome. Missing executables produce an error; no auto-install success message is expected.
4. The generated validation violations will automatically populate the Visual Studio **Error List**.

## 5. Filtering Validation Results

1. Go to **Tools** -> **Options** -> **DataGuard** -> **Validation Rules**.
2. Uncheck any rule (e.g., *DG016: Phantom Column Reference*).
3. Run validation again (via **Tools** -> **StackAccord (Beta)** -> **Run Validation**).
4. The extension passes the disabled rule IDs to the CLI via `validate --skip-rules <ids>` (for example `--skip-rules DG016`), so the CLI never emits those violations.
5. Check the **Error List**: you will notice that the violations for the disabled rule are no longer present.

## 6. Validation Rules & Descriptions Catalog

Under **Tools** -> **StackAccord (Beta)**, you can click **Validation Rules & Descriptions** to output a detailed list of all rules and their current enabled/disabled state to the Output window. This is highly useful for sharing the exact configuration during a team review.
