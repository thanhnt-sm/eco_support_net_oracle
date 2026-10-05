using DataGuard.Core.Abstractions;
using DataGuard.Core.Sources;

internal enum ContractAcquisitionStatus
{
    Complete,
    Unavailable,
    Incomplete,
    Failed
}

internal sealed class ContractAcquisitionResult
{
    public ContractAcquisitionResult(
        ContractAcquisitionStatus status,
        IReadOnlyList<ContractDescriptor> contracts,
        string message,
        IReadOnlyList<AcquisitionDiagnostic>? diagnostics = null)
    {
        Status = status;
        Contracts = contracts;
        Message = message;
        Diagnostics = diagnostics ?? Array.Empty<AcquisitionDiagnostic>();
    }

    public static ContractAcquisitionResult Complete(IReadOnlyList<ContractDescriptor> contracts, IReadOnlyList<AcquisitionDiagnostic>? diagnostics = null) =>
        new(ContractAcquisitionStatus.Complete, contracts, "acquisition completed", diagnostics);

    public ContractAcquisitionStatus Status { get; }
    public IReadOnlyList<ContractDescriptor> Contracts { get; }
    public string Message { get; }

    /// <summary>Non-fatal problems met while reading sources (unreadable files, oversized literals, skipped call sites).</summary>
    public IReadOnlyList<AcquisitionDiagnostic> Diagnostics { get; }
}
