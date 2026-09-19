using System.Collections.Immutable;
using DataGuard;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using DataGuard.Core.Validation;

var configuration = new DataGuardConfiguration();
configuration.Deconstruct(
    out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _,
    out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _);
using var pipeline = DataGuardApi.CreatePipeline(configuration);
var result = new ValidationResult(0, 0, 0, 0, 0, ImmutableArray<ContractViolation>.Empty, TimeSpan.Zero, "1.0");
result.Deconstruct(out _, out _, out _, out _, out _, out _, out _, out _);
var snapshotColumn = new SnapshotColumn("ID", "int", null, null, null, null, false, null);
snapshotColumn.Deconstruct(out _, out _, out _, out _, out _, out _, out _, out _);
var snapshotTable = new SnapshotTable("dbo.Customers", new[] { snapshotColumn });
snapshotTable.Deconstruct(out _, out _);
var baseline = new BaselineFile(3, DateTimeOffset.UnixEpoch, "1.0", "Snapshot", "test", "hash", Array.Empty<BaselineViolation>(), new[] { snapshotTable });
baseline.Deconstruct(out _, out _, out _, out _, out _, out _, out _, out _);
var procedure = new StoredProcedureDescriptor("procedure:1", "GetCustomer", "dbo", string.Empty, Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>(), false);
procedure.Deconstruct(out _, out _, out _, out _, out _, out _, out _, out _);
var legacy = new ConcurrentValidationEngine().ValidateAsync(Array.Empty<ContractDescriptor>(), Array.Empty<IContractRule>()).GetAwaiter().GetResult();
Console.WriteLine($"{pipeline.GetType().FullName}:{result.IsClean}:{legacy.Count}");
