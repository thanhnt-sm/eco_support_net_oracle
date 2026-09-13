using System.Collections.Immutable;
using DataGuard;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Validation;

var configuration = new DataGuardConfiguration();
configuration.Deconstruct(
    out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _,
    out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _);
using var pipeline = DataGuardApi.CreatePipeline(configuration);
var result = new ValidationResult(0, 0, 0, 0, 0, ImmutableArray<ContractViolation>.Empty, TimeSpan.Zero, "1.0");
result.Deconstruct(out _, out _, out _, out _, out _, out _, out _, out _);
var legacy = new ConcurrentValidationEngine().ValidateAsync(Array.Empty<ContractDescriptor>(), Array.Empty<IContractRule>()).GetAwaiter().GetResult();
Console.WriteLine($"{pipeline.GetType().FullName}:{result.IsClean}:{legacy.Count}");
