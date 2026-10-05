using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.TypeCompatibility;

namespace DataGuard.Core.Rules.StoredProcedures;

/// <summary>
/// Resolves a stored-procedure call site (<see cref="RawSqlDescriptor"/>) against the <see cref="StoredProcedureDescriptor"/>
/// catalog in the validation run. Name lookup folds identifiers per provider (Oracle upper, PostgreSQL lower, SQL Server and
/// MySQL case-insensitive; quoted parts are exact); schema/package selection prefers an explicit qualifier, then the configured
/// default, then any unique match; overloads are chosen by binding positional then named arguments, rejecting candidates with
/// unknown arguments or missing required parameters (Input/InputOutput without a default), and scoring the rest by type
/// compatibility. Results are cached per catalog list and descriptor, so DG101, DG002 and DG003 share one resolution.
/// </summary>
public static class StoredProcedureCallResolver
{
    private static readonly ConditionalWeakTable<IReadOnlyList<ContractDescriptor>, ConcurrentDictionary<StoredProcedureMatchOptions, CatalogIndex>> Indexes = new();

    private static readonly string[] OracleSystemPrefixes = { "DBMS_", "UTL_", "OWA_", "HTP", "HTF", "APEX_", "CTX_", "SDO_", "ORD_" };

    /// <summary>Returns true when <paramref name="allContracts"/> holds at least one stored procedure.</summary>
    /// <param name="allContracts">All contracts of the run.</param>
    /// <returns>True when a catalog is present.</returns>
    public static bool HasCatalog(IReadOnlyList<ContractDescriptor> allContracts) =>
        allContracts?.Any(c => c is StoredProcedureDescriptor) == true;

    /// <summary>Formats <c>schema.package.name</c>, omitting empty parts.</summary>
    /// <param name="procedure">The procedure.</param>
    /// <returns>The qualified name.</returns>
    public static string QualifiedName(StoredProcedureDescriptor procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        return string.Join(".", new[] { procedure.Schema, procedure.PackageName, procedure.Name }.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>Resolves <paramref name="call"/> against the procedures in <paramref name="allContracts"/>.</summary>
    /// <param name="call">The call-site descriptor.</param>
    /// <param name="allContracts">All contracts of the run (the catalog is every <see cref="StoredProcedureDescriptor"/>).</param>
    /// <param name="options">Provider, defaults and type table.</param>
    /// <returns>The resolution; <see cref="StoredProcedureResolutionStatus.NotApplicable"/> when the descriptor is not a call or there is no catalog.</returns>
    public static StoredProcedureResolution Resolve(
        RawSqlDescriptor call,
        IReadOnlyList<ContractDescriptor> allContracts,
        StoredProcedureMatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        allContracts ??= Array.Empty<ContractDescriptor>();
        var requested = options ?? new StoredProcedureMatchOptions();
        var effective = requested with
        {
            Provider = string.IsNullOrWhiteSpace(requested.Provider) ? null : TypeCompatibilityRegistry.NormalizeProvider(requested.Provider),
        };
        var index = GetIndex(allContracts, effective);
        return index.Results.GetValue(call, c => new Resolver(index, effective).Resolve(c));
    }

    private static CatalogIndex GetIndex(IReadOnlyList<ContractDescriptor> allContracts, StoredProcedureMatchOptions options)
    {
        var perList = Indexes.GetValue(allContracts, _ => new ConcurrentDictionary<StoredProcedureMatchOptions, CatalogIndex>());
        var index = perList.GetOrAdd(options, _ => new CatalogIndex(allContracts));
        if (index.ContractCount != allContracts.Count)
        {
            index = new CatalogIndex(allContracts);
            perList[options] = index;
        }

        return index;
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private sealed record CatalogEntry(StoredProcedureDescriptor Procedure, string Name, string Schema, string Package, bool IsManual, IReadOnlyList<ParameterDescriptor> Parameters);

    private sealed record Interpretation(SqlNamePart? Schema, SqlNamePart? Package);

    private sealed record BindResult(
        CatalogEntry Entry,
        List<StoredProcedureArgumentBinding> Bindings,
        List<ParameterDescriptor> Missing,
        List<StoredProcedureCallArgument> Extra,
        int Score)
    {
        public bool IsValid => Missing.Count == 0 && Extra.Count == 0;
    }

    private sealed class CatalogIndex
    {
        public CatalogIndex(IReadOnlyList<ContractDescriptor> allContracts)
        {
            ContractCount = allContracts.Count;
            Entries = allContracts.OfType<StoredProcedureDescriptor>().Select(ToEntry).ToList();
            ByUpperName = Entries
                .GroupBy(e => e.Name.ToUpperInvariant(), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            UpperSchemas = Entries.Where(e => e.Schema.Length > 0).Select(e => e.Schema.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
            UpperPackages = Entries.Where(e => e.Package.Length > 0).Select(e => e.Package.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
        }

        public int ContractCount { get; }

        public List<CatalogEntry> Entries { get; }

        public Dictionary<string, List<CatalogEntry>> ByUpperName { get; }

        public HashSet<string> UpperSchemas { get; }

        public HashSet<string> UpperPackages { get; }

        public ConditionalWeakTable<RawSqlDescriptor, StoredProcedureResolution> Results { get; } = new();

        private static CatalogEntry ToEntry(StoredProcedureDescriptor sp)
        {
            var name = sp.Name ?? string.Empty;
            var schema = sp.Schema ?? string.Empty;
            if (name.Contains('.', StringComparison.Ordinal))
            {
                var parts = Sql.SchemaObjectName.Parse(name);
                name = parts.Name;
                if (schema.Length == 0)
                {
                    schema = parts.Schema ?? string.Empty;
                }
            }

            var parameters = (sp.Parameters ?? Array.Empty<ParameterDescriptor>())
                .Where(p => p.Direction != ParameterDirection.ReturnValue && !(p.OrdinalPosition == 0 && string.IsNullOrEmpty(p.Name)))
                .OrderBy(p => p.OrdinalPosition)
                .ThenBy(p => p.Sequence)
                .ToList();
            return new CatalogEntry(
                sp,
                Sql.SchemaObjectName.Unquote(name),
                Sql.SchemaObjectName.Unquote(schema),
                Sql.SchemaObjectName.Unquote(sp.PackageName ?? string.Empty),
                sp.Id?.StartsWith("manual-sp:", StringComparison.Ordinal) == true,
                parameters);
        }
    }

    private sealed class Resolver
    {
        private readonly CatalogIndex _index;
        private readonly StoredProcedureMatchOptions _options;
        private readonly string? _provider;

        public Resolver(CatalogIndex index, StoredProcedureMatchOptions options)
        {
            _index = index;
            _options = options;
            _provider = options.Provider;
        }

        private bool IsOracle => _provider == "oracle";

        // Packages exist on Oracle; with no provider configured both readings of a qualifier are tried.
        private bool SupportsPackages => _provider is null or "oracle";

        public StoredProcedureResolution Resolve(RawSqlDescriptor call)
        {
            var site = StoredProcedureCallParser.Parse(call);
            if (site is null)
            {
                return StoredProcedureResolution.Unresolved(StoredProcedureResolutionStatus.NotApplicable, null, "not a stored-procedure call");
            }

            if (_index.Entries.Count == 0)
            {
                return StoredProcedureResolution.Unresolved(StoredProcedureResolutionStatus.NotApplicable, site, "catalog has no stored procedures");
            }

            if (!SupportsPackages && site.ExplicitSchema is null && site.Qualifiers.Count >= 2 && site.Qualifiers[^2].Text.Length > 0)
            {
                return StoredProcedureResolution.Unresolved(StoredProcedureResolutionStatus.OutOfScope, site, "cross-database call");
            }

            var interpretations = Interpret(site);
            var sameName = _index.ByUpperName.TryGetValue(site.Name.Text.ToUpperInvariant(), out var bucket)
                ? bucket.Where(e => Matches(site.Name, e.Name, e.IsManual)).ToList()
                : new List<CatalogEntry>();

            var matched = sameName
                .Where(e => interpretations.Any(i => Fits(e, i)))
                .ToList();

            if (matched.Count == 0)
            {
                return NoCandidate(site, interpretations, bucket ?? new List<CatalogEntry>());
            }

            matched = Prefer(matched, interpretations);
            var groups = matched
                .GroupBy(e => (Schema: e.Schema.ToUpperInvariant(), Package: e.Package.ToUpperInvariant()))
                .ToList();
            if (groups.Count > 1)
            {
                var names = string.Join(", ", groups.Select(g => QualifiedName(g.First().Procedure)));
                return StoredProcedureResolution.Unresolved(
                    StoredProcedureResolutionStatus.Ambiguous,
                    site,
                    $"ambiguous: '{site.DisplayName}' matches {names}; qualify the call or set DefaultSchema/DefaultPackage",
                    matched.Select(e => e.Procedure).ToList());
            }

            return ResolveOverloads(site, groups[0].ToList());
        }

        private StoredProcedureResolution ResolveOverloads(StoredProcedureCallSite site, List<CatalogEntry> overloads)
        {
            var candidates = overloads.Select(e => e.Procedure).ToList();
            if (!site.ArgumentsKnown)
            {
                // The argument list was not (fully) observed: resolve the procedure and bind whatever arguments were seen
                // (DG002/DG003 still check them), but never report missing or extra arguments (DG101).
                return overloads.Count == 1
                    ? new StoredProcedureResolution(
                        StoredProcedureResolutionStatus.Resolved,
                        site,
                        overloads[0].Procedure,
                        site.Arguments.Count == 0 ? Array.Empty<StoredProcedureArgumentBinding>() : Bind(overloads[0], site).Bindings,
                        Array.Empty<ParameterDescriptor>(),
                        Array.Empty<StoredProcedureCallArgument>(),
                        null,
                        candidates,
                        "call-site arguments not observed")
                    : StoredProcedureResolution.Unresolved(
                        StoredProcedureResolutionStatus.Ambiguous,
                        site,
                        $"call-site arguments not observed; {overloads.Count} overloads",
                        candidates);
            }

            var results = overloads.Select(e => Bind(e, site)).ToList();
            var valid = results.Where(r => r.IsValid).ToList();
            if (valid.Count > 0)
            {
                var bestScore = valid.Max(r => r.Score);
                var best = valid.Where(r => r.Score == bestScore).ToList();
                if (best.Count > 1)
                {
                    return StoredProcedureResolution.Unresolved(
                        StoredProcedureResolutionStatus.Ambiguous,
                        site,
                        $"{best.Count} overloads of '{QualifiedName(best[0].Entry.Procedure)}' accept the call equally well",
                        candidates);
                }

                var winner = best[0];
                return new StoredProcedureResolution(
                    StoredProcedureResolutionStatus.Resolved,
                    site,
                    winner.Entry.Procedure,
                    winner.Bindings,
                    Array.Empty<ParameterDescriptor>(),
                    Array.Empty<StoredProcedureCallArgument>(),
                    null,
                    candidates,
                    null);
            }

            var argumentCount = site.Arguments.Count;
            var nearest = results
                .OrderBy(r => r.Missing.Count + r.Extra.Count)
                .ThenBy(r => r.Extra.Count) // an overload that knows every named argument is the more useful hint
                .ThenBy(r => Math.Abs(r.Entry.Parameters.Count - argumentCount))
                .First();
            return new StoredProcedureResolution(
                StoredProcedureResolutionStatus.NoMatch,
                site,
                null,
                nearest.Bindings,
                nearest.Missing,
                nearest.Extra,
                nearest.Entry.Procedure,
                candidates,
                overloads.Count > 1 ? $"none of {overloads.Count} overloads accepts the call" : null);
        }

        private BindResult Bind(CatalogEntry entry, StoredProcedureCallSite site)
        {
            var parameters = entry.Parameters;
            var bound = new bool[parameters.Count];
            var bindings = new List<StoredProcedureArgumentBinding>();
            var extra = new List<StoredProcedureCallArgument>();
            var next = 0;
            foreach (var argument in site.Arguments)
            {
                if (argument.Direction == ParameterDirection.ReturnValue)
                {
                    continue; // ADO return-value parameters are not arguments
                }

                if (!argument.IsNamed)
                {
                    while (next < parameters.Count && bound[next])
                    {
                        next++;
                    }

                    if (next < parameters.Count)
                    {
                        bound[next] = true;
                        bindings.Add(new StoredProcedureArgumentBinding(argument, parameters[next]));
                        next++;
                    }
                    else
                    {
                        extra.Add(argument);
                    }

                    continue;
                }

                var target = StoredProcedureCallParser.BareParameterName(argument.Name);
                var j = -1;
                for (var k = 0; k < parameters.Count; k++)
                {
                    if (string.Equals(StoredProcedureCallParser.BareParameterName(parameters[k].Name), target, StringComparison.OrdinalIgnoreCase))
                    {
                        j = k;
                        break;
                    }
                }

                if (j < 0 || bound[j])
                {
                    extra.Add(argument);
                    continue;
                }

                bound[j] = true;
                bindings.Add(new StoredProcedureArgumentBinding(argument, parameters[j]));
            }

            var missing = parameters
                .Where((p, i) => !bound[i] && p.Direction is ParameterDirection.Input or ParameterDirection.InputOutput && !p.HasDefault)
                .ToList();
            var score = bindings.Sum(b => ScoreBinding(b));
            return new BindResult(entry, bindings, missing, extra, score);
        }

        private int ScoreBinding(StoredProcedureArgumentBinding binding)
        {
            if (binding.Argument.ClrType is null || _options.TypeCompatibility is null)
            {
                return 1;
            }

            var p = binding.Parameter;
            return _options.TypeCompatibility.Check(binding.Argument.ClrType, p.DataType, p.Precision, p.Scale, p.MaxLength) switch
            {
                TypeCompatibilityResult.Compatible => 2,
                TypeCompatibilityResult.Incompatible => 0,
                _ => 1,
            };
        }

        private StoredProcedureResolution NoCandidate(StoredProcedureCallSite site, List<Interpretation> interpretations, List<CatalogEntry> sameNameAnyCase)
        {
            if (IsSystemProcedure(site, interpretations))
            {
                return StoredProcedureResolution.Unresolved(StoredProcedureResolutionStatus.OutOfScope, site, "system procedure");
            }

            var qualified = interpretations.Where(i => i.Schema is not null || i.Package is not null).ToList();
            if (qualified.Count > 0 && !qualified.Any(IsCoveredByCatalog))
            {
                return StoredProcedureResolution.Unresolved(
                    StoredProcedureResolutionStatus.OutOfScope,
                    site,
                    "schema/package not in catalog");
            }

            var nearest = sameNameAnyCase.FirstOrDefault()?.Procedure ?? NearestByName(site.Name.Text);
            return StoredProcedureResolution.Unresolved(
                StoredProcedureResolutionStatus.NoCandidate,
                site,
                nearest is null ? null : $"nearest: {QualifiedName(nearest)}",
                nearest: nearest);
        }

        private bool IsCoveredByCatalog(Interpretation interpretation) =>
            (interpretation.Schema is null || _index.UpperSchemas.Contains(interpretation.Schema.Value.Text.ToUpperInvariant())) &&
            (interpretation.Package is null || _index.UpperPackages.Contains(interpretation.Package.Value.Text.ToUpperInvariant()));

        private StoredProcedureDescriptor? NearestByName(string name)
        {
            var target = name.ToUpperInvariant();
            var limit = Math.Max(2, target.Length / 3);
            return _index.Entries
                .Where(e => !e.IsManual || _index.Entries.All(x => x.IsManual))
                .Select(e => (Entry: e, Distance: Distance(target, e.Name.ToUpperInvariant())))
                .Where(x => x.Distance <= limit)
                .OrderBy(x => x.Distance)
                .ThenBy(x => x.Entry.Name, StringComparer.Ordinal)
                .Select(x => x.Entry.Procedure)
                .FirstOrDefault();
        }

        private bool IsSystemProcedure(StoredProcedureCallSite site, List<Interpretation> interpretations)
        {
            var name = site.Name.Text.ToUpperInvariant();
            var qualifiers = interpretations
                .SelectMany(i => new[] { i.Schema, i.Package })
                .Concat(site.Qualifiers.Select(q => (SqlNamePart?)q))
                .Where(q => q is not null && q.Value.Text.Length > 0)
                .Select(q => q!.Value.Text.ToUpperInvariant())
                .ToHashSet(StringComparer.Ordinal);
            return _provider switch
            {
                "oracle" => qualifiers.Overlaps(new[] { "SYS", "SYSTEM", "PUBLIC" }) ||
                            qualifiers.Any(q => OracleSystemPrefixes.Any(p => q.StartsWith(p, StringComparison.Ordinal))) ||
                            name.StartsWith("DBMS_", StringComparison.Ordinal),
                "postgresql" => qualifiers.Overlaps(new[] { "PG_CATALOG", "INFORMATION_SCHEMA" }) || name.StartsWith("PG_", StringComparison.Ordinal),
                "mysql" => qualifiers.Overlaps(new[] { "MYSQL", "SYS", "INFORMATION_SCHEMA", "PERFORMANCE_SCHEMA" }),
                _ => qualifiers.Contains("SYS") || name.StartsWith("SP_", StringComparison.Ordinal) || name.StartsWith("XP_", StringComparison.Ordinal),
            };
        }

        private List<Interpretation> Interpret(StoredProcedureCallSite site)
        {
            var qualifiers = site.Qualifiers.Where(q => q.Text.Length > 0).ToList();
            if (site.ExplicitSchema is not null || site.ExplicitPackage is not null)
            {
                var schema = site.ExplicitSchema;
                var package = site.ExplicitPackage;
                var leftover = qualifiers
                    .Where(q => !SameText(q, schema) && !SameText(q, package))
                    .ToList();
                if (leftover.Count > 0)
                {
                    if (SupportsPackages && package is null && schema is not null)
                    {
                        package = leftover[^1];
                    }
                    else if (schema is null)
                    {
                        schema = leftover[^1];
                    }
                }

                return new List<Interpretation> { new(schema, package) };
            }

            var all = site.Qualifiers;
            if (all.Count == 0)
            {
                return new List<Interpretation> { new(null, null) };
            }

            if (!SupportsPackages)
            {
                return new List<Interpretation> { new(NullIfEmpty(all[^1]), null) };
            }

            if (all.Count == 1)
            {
                return new List<Interpretation> { new(all[0], null), new(null, all[0]) };
            }

            var schemaAndPackage = new Interpretation(NullIfEmpty(all[^2]), NullIfEmpty(all[^1]));
            return IsOracle
                ? new List<Interpretation> { schemaAndPackage }
                : new List<Interpretation> { schemaAndPackage, new(NullIfEmpty(all[^1]), null) }; // or database.schema.name
        }

        private bool Fits(CatalogEntry entry, Interpretation interpretation)
        {
            if (interpretation.Schema is { } schema && entry.Schema.Length > 0 && !Matches(schema, entry.Schema, entry.IsManual))
            {
                return false;
            }

            if (interpretation.Package is { } package)
            {
                return entry.Package.Length > 0 && Matches(package, entry.Package, entry.IsManual);
            }

            // "SCHEMA.PROC" names a standalone procedure; a package member needs the package qualifier.
            return interpretation.Schema is null || entry.Package.Length == 0;
        }

        private List<CatalogEntry> Prefer(List<CatalogEntry> matched, List<Interpretation> interpretations)
        {
            if (matched.Any(e => !e.IsManual))
            {
                matched = matched.Where(e => !e.IsManual).ToList();
            }

            if (interpretations.All(i => i.Schema is null) && !string.IsNullOrWhiteSpace(_options.DefaultSchema))
            {
                var defaultSchema = new SqlNamePart(_options.DefaultSchema.Trim(), Quoted: false);
                var inDefault = matched.Where(e => e.Schema.Length == 0 || Matches(defaultSchema, e.Schema, e.IsManual)).ToList();
                if (inDefault.Count > 0)
                {
                    matched = inDefault;
                }
            }

            if (interpretations.All(i => i.Package is null))
            {
                var standalone = matched.Where(e => e.Package.Length == 0).ToList();
                if (standalone.Count > 0)
                {
                    return standalone;
                }

                if (!string.IsNullOrWhiteSpace(_options.DefaultPackage))
                {
                    var defaultPackage = new SqlNamePart(_options.DefaultPackage.Trim(), Quoted: false);
                    var inPackage = matched.Where(e => Matches(defaultPackage, e.Package, e.IsManual)).ToList();
                    if (inPackage.Count > 0)
                    {
                        return inPackage;
                    }
                }
            }

            return matched;
        }

        /// <summary>Compares a call-site identifier with a catalog identifier using the provider's folding rules.</summary>
        private bool Matches(SqlNamePart callSite, string catalog, bool catalogIsManual)
        {
            if (catalogIsManual || _provider is not ("oracle" or "postgresql"))
            {
                return string.Equals(callSite.Text, catalog, StringComparison.OrdinalIgnoreCase);
            }

            var folded = callSite.Quoted
                ? callSite.Text
                : _provider == "oracle" ? callSite.Text.ToUpperInvariant() : callSite.Text.ToLowerInvariant();
            return string.Equals(folded, catalog, StringComparison.Ordinal);
        }

        private static bool SameText(SqlNamePart part, SqlNamePart? other) =>
            other is not null && string.Equals(part.Text, other.Value.Text, StringComparison.OrdinalIgnoreCase);

        private static SqlNamePart? NullIfEmpty(SqlNamePart part) => part.Text.Length == 0 ? null : part;
    }
}
