param(
    [Parameter(Mandatory)][int]$ProcessId,
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+$')][string]$MajorVersion,
    [switch]$ValidateOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Only attach to the process launched by the orchestrator, using the measured host major.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
public static class SmokeRot {
    [DllImport("ole32.dll")] static extern int GetRunningObjectTable(int r, out IRunningObjectTable t);
    [DllImport("ole32.dll")] static extern int CreateBindCtx(int r, out IBindCtx c);
    public static object Find(string expected) {
        IRunningObjectTable table = null; IEnumMoniker iterator = null; IBindCtx context = null;
        try {
            Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out table));
            Marshal.ThrowExceptionForHR(CreateBindCtx(0, out context));
            table.EnumRunning(out iterator);
            var current = new IMoniker[1];
            while (iterator.Next(1, current, IntPtr.Zero) == 0) {
                try {
                    string name; current[0].GetDisplayName(context, null, out name);
                    if (name == expected) { object value; table.GetObject(current[0], out value); return value; }
                } finally { if (current[0] != null) Marshal.ReleaseComObject(current[0]); }
            }
            return null;
        } finally {
            if (iterator != null) Marshal.ReleaseComObject(iterator);
            if (context != null) Marshal.ReleaseComObject(context);
            if (table != null) Marshal.ReleaseComObject(table);
        }
    }
}
'@
if ($ValidateOnly) { exit 0 }
$dte = $null
$deadline = [DateTime]::UtcNow.AddSeconds(105)
try {
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            if ($null -eq $dte) { $dte = [SmokeRot]::Find("!VisualStudio.DTE.${MajorVersion}.0:$ProcessId") }
            if ($null -ne $dte) {
                # Canonical automation category is DataGuard; visible localized label is StackAccord.
                # Read only; no command, solution, validation, or consent action.
                $properties = $dte.Properties('DataGuard', 'General')
                $property = $properties.Item('EnableDetailedLogging')
                $value = $property.Value
                if ($value -is [bool]) {
                    $hostVersion = [string]$dte.Version
                    $dte.Quit()
                    @{ optionsRead = $true; propertyType = 'Boolean'; hostVersion = $hostVersion } |
                        ConvertTo-Json -Compress
                    exit 0
                }
            }
        } catch {
            # Modal license/sign-in/trust/first-run prompts are never dismissed or accepted.
            # Parent watchdog also bounds a COM call that blocks inside the IDE.
        }
        Start-Sleep -Milliseconds 500
    }
    exit 2
} finally {
    if ($null -ne $dte) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($dte) }
}
