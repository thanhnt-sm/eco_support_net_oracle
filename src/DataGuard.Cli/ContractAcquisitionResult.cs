using DataGuard.Core.Abstractions;

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
        string message)
    {
        Status = status;
        Contracts = contracts;
        Message = message;
    }

    public static ContractAcquisitionResult Complete(IReadOnlyList<ContractDescriptor> contracts) =>
        new(ContractAcquisitionStatus.Complete, contracts, "acquisition completed");

    public ContractAcquisitionStatus Status { get; }
    public IReadOnlyList<ContractDescriptor> Contracts { get; }
    public string Message { get; }
}
