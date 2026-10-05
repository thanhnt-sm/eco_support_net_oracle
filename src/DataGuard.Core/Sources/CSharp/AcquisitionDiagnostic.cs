namespace DataGuard.Core.Sources;

/// <summary>
/// A problem met while acquiring contracts from source code that did not stop the run but means the
/// extracted contract set may be incomplete (or intentionally reduced, for <c>[SkipContractCheck]</c>).
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Path">Repository-relative path (forward slashes) of the file concerned.</param>
/// <param name="Message">Human-readable detail, including the line when one applies.</param>
public sealed record AcquisitionDiagnostic(AcquisitionDiagnosticKind Kind, string Path, string Message);

/// <summary>
/// Category of an <see cref="AcquisitionDiagnostic"/>.
/// </summary>
public enum AcquisitionDiagnosticKind
{
    /// <summary>A source file could not be read; its SQL was not extracted.</summary>
    UnreadableFile,

    /// <summary>A source file has C# syntax errors; extraction ran on the error-tolerant tree and may be incomplete.</summary>
    ParseFailed,

    /// <summary>A SQL literal longer than <see cref="ProjectCSharpSqlSource.MaxSqlLiteralLength"/> characters was skipped.</summary>
    OversizedLiteral,

    /// <summary>A call site was skipped because its method or an enclosing type carries <c>[SkipContractCheck]</c>.</summary>
    SkippedByAttribute,
}
