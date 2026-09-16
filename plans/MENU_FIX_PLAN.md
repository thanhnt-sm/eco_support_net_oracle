# Fix missing DataGuard menu in Visual Studio

## Context
The DataGuard extension menu items are missing in Visual Studio. The settings page under Options is visible, but the commands (like "Run Validation") do not appear under the Tools menu. This is because the compiled VSCT resource name defaults to the assembly name, but the package explicitly expects "Menus.ctmenu".

## Approach
1.  **Update Project File**: Edit `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj`.
    -   Locate the `<VSCTCompile Include="Commands\DataGuard.vsct" />` element.
    -   Add `<ResourceName>Menus.ctmenu</ResourceName>` inside the `VSCTCompile` node. This instructs MSBuild to output the `.ctmenu` file with the name required by the `ProvideMenuResource("Menus.ctmenu", 1)` attribute on `DataGuardPackage`.

## Critical files & anchors
-   `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj`: Update the `<VSCTCompile>` item group to include the explicit `ResourceName`.

## Verification
-   Build the `DataGuard.VisualStudio.csproj` project.
-   Check the compiled VSIX. It should now contain a `.ctmenu` resource named `Menus.ctmenu`.
-   (Manual verification if VS is available): Install the VSIX in the Experimental Instance and verify that the "DataGuardGroup" commands appear under the `Tools` menu.

### Build Instructions for Testing
To build the installation files for Visual Studio, VS Code, and NuGet packages:

1.  **Visual Studio Extension (.vsix):**
    ```bash
    dotnet build src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj -c Release
    ```
    The output `.vsix` will be located in `src/DataGuard.VisualStudio/bin/Release/`.

2.  **Visual Studio Code Extension (.vsix):**
    ```bash
    cd src/DataGuard.VSCode
    npm install
    npx vsce package
    ```
    The output `.vsix` will be created in the `src/DataGuard.VSCode/` directory.

3.  **NuGet Packages (.nupkg):**
    ```bash
    # From the repository root
    dotnet pack DataGuard.sln -c Release
    ```
    The output `.nupkg` files (for CLI, Analyzers, etc.) will be located in their respective `bin/Release/` folders.

## Assumptions & contingencies
-   Assume the user is referring to the Visual Studio extension based on the exact match of the Options page text ("Custom CLI Executable Path"). The VS Code settings use different descriptions.