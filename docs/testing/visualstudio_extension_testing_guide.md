# DataGuard Extension Testing Guide

## 1. Installation

1. Open Visual Studio.
2. Go to **Extensions** -> **Manage Extensions**.
3. Install the newly built VSIX located at `src\DataGuard.VisualStudio\bin\Debug\net472\DataGuard.VisualStudio.vsix` (you may need to uninstall the old one first).
4. Restart Visual Studio.

## 2. CLI Executable Auto-Discovery & Auto-Installation

* **Auto-installation**: The extension will automatically look for `dataguard.exe`. If it is missing, it will run `dotnet tool install -g DataGuard.Cli` in the background (using the local `src/DataGuard.Cli/nupkg` directory if available).
* **Roll-forward behavior**: The extension sets `DOTNET_ROLL_FORWARD=LatestMajor` when executing the CLI. This safely mitigates the .NET 9 dependency, permitting the tool to run correctly on .NET 10 without crashes.

## 3. Options & Configuration

You can access the configuration settings at: **Tools** -> **Options** -> **DataGuard**.
* **Validation Rules**: Displays descriptions for all rules (e.g. `DG002`, `DG003`, `DG016`, etc.) and allows you to toggle each rule on or off. All rules are **enabled by default**.

## 4. Validating Projects

1. Open a solution that has a `.dataguard.yml` configuration file.
2. Under the **Tools** menu, locate the **DataGuard** sub-menu and click **Run Validation**.
3. Output Window:
   * Navigate to the **Output** window and select **DataGuard** from the "Show output from:" dropdown.
   * You should see progress logs such as `[DataGuard] validate started...` and `[DataGuard] CLI successfully auto-installed` (if it was freshly installed).
4. The generated validation violations will automatically populate the Visual Studio **Error List**.

## 5. Filtering Validation Results

1. Go to **Tools** -> **Options** -> **DataGuard** -> **Validation Rules**.
2. Uncheck any rule (e.g., *DG016: Phantom Column & Raw SQL Parse Status*).
3. Run validation again (via **Tools** -> **DataGuard** -> **Run Validation**).
4. The extension passes the disabled rule IDs to the CLI via `validate --skip-rules <ids>` (for example `--skip-rules DG016`), so the CLI never emits those violations.
5. Check the **Error List**: you will notice that the violations for the disabled rule are no longer present.

## 6. Validation Rules & Descriptions Catalog

Under **Tools** -> **DataGuard**, you can click **Validation Rules & Descriptions** to output a detailed list of all rules and their current enabled/disabled state to the Output window. This is highly useful for sharing the exact configuration during a team review.
