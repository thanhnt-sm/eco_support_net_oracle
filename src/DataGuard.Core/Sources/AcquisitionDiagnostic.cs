namespace DataGuard.Core.Sources;

/// <summary>
/// A non-fatal problem found while acquiring contracts (an unreadable file, an oversized literal, a partially parsed
/// ModelSnapshot). Acquisition continues with what it could read; the CLI reports every diagnostic and treats the run as
/// unevaluated (exit 3) unless <c>--allow-unevaluated</c> is set (red-team H2, rec 15).
/// </summary>
/// <param name="Kind">Stable machine-readable category, for example <c>ModelSnapshotPartialParse</c>.</param>
/// <param name="Path">Path of the source the diagnostic refers to, as supplied by the caller.</param>
/// <param name="Message">Single-line description without secrets.</param>
public sealed record AcquisitionDiagnostic(string Kind, string Path, string Message);
