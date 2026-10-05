using System.Security.Cryptography;
using System.Text;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Reporting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DataGuard.Core.Tests")]

namespace DataGuard.Core.Sources;

/// <summary>
/// Extracts raw SQL query contracts and stored-procedure call sites, with their expected C# target shapes and call-site
/// arguments, directly from C# source projects. Scans Dapper (Query*, Execute*), EF Core (FromSql*, ExecuteSql*,
/// SqlQuery*) and ADO.NET (CommandText, CommandType.StoredProcedure, Parameters.Add*) usage.
/// </summary>
/// <remarks>
/// Partial parts live in <c>Sources/CSharp/</c>: statement recognition and placeholder scanning (SqlText), call-site
/// bindings (Bindings), procedure call parsing (Procedures), target shapes (TargetShape), the per-file passes (Passes)
/// and file/reference discovery (Discovery).
/// </remarks>
public sealed partial class ProjectCSharpSqlSource : IContractSource
{
    /// <summary>Longest SQL literal (chars) fed to rules; larger literals are skipped with a DG1291 note and an <see cref="AcquisitionDiagnosticKind.OversizedLiteral"/> diagnostic.</summary>
    public const int MaxSqlLiteralLength = 256 * 1024;

    private readonly string _projectOrPath;
    private readonly ProgressEmitter? _progress;
    private readonly List<AcquisitionDiagnostic> _diagnostics = new();
    private IReadOnlyList<AcquisitionDiagnostic> _lastRunDiagnostics = Array.Empty<AcquisitionDiagnostic>();

    public ProjectCSharpSqlSource(string projectOrPath, ProgressEmitter? progress = null)
    {
        _projectOrPath = projectOrPath ?? throw new ArgumentNullException(nameof(projectOrPath));
        _progress = progress;
    }

    public string SourceId => "project-csharp-sql";

    public string DisplayName => "C# Project SQL Source";

    /// <summary>
    /// Gets the acquisition diagnostics of the last <see cref="ExtractContractsAsync"/> run: unreadable files, files with
    /// C# syntax errors, oversized SQL literals and call sites skipped by <c>[SkipContractCheck]</c>.
    /// </summary>
    public IReadOnlyList<AcquisitionDiagnostic> Diagnostics => _lastRunDiagnostics;

    /// <summary>Gets the same list as <see cref="Diagnostics"/> (name kept for callers that read it after a run).</summary>
    public IReadOnlyList<AcquisitionDiagnostic> LastRunDiagnostics => _lastRunDiagnostics;

    /// <summary>Gets the number of call sites skipped by <c>[SkipContractCheck]</c> in the last run.</summary>
    public int SkippedContractCount { get; private set; }

    public Task<IReadOnlyList<ContractDescriptor>> ExtractContractsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _diagnostics.Clear();
        SkippedContractCount = 0;
        try
        {
            return Task.FromResult(ExtractCore(cancellationToken));
        }
        finally
        {
            _lastRunDiagnostics = _diagnostics.ToArray();
        }
    }

    private IReadOnlyList<ContractDescriptor> ExtractCore(CancellationToken cancellationToken)
    {
        var csFiles = DiscoverSourceFiles(_projectOrPath);
        if (csFiles.Count == 0)
        {
            return Array.Empty<ContractDescriptor>();
        }

        csFiles.Sort(StringComparer.Ordinal);
        var scanRoot = ResolveScanRoot(_projectOrPath);
        var repoRoot = FindRepositoryRoot(scanRoot) ?? scanRoot;

        var syntaxTrees = new List<SyntaxTree>();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        foreach (var file in csFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text;
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                text = reader.ReadToEnd();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
            {
                AddDiagnostic(AcquisitionDiagnosticKind.UnreadableFile, RelativePath(repoRoot, file), $"Could not read source file: {ex.Message}");
                continue;
            }

            var tree = CSharpSyntaxTree.ParseText(text, parseOptions, path: file, cancellationToken: cancellationToken);
            var firstError = tree.GetDiagnostics(cancellationToken).FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            if (firstError is not null)
            {
                var line = firstError.Location.GetLineSpan().StartLinePosition.Line + 1;
                AddDiagnostic(
                    AcquisitionDiagnosticKind.ParseFailed,
                    RelativePath(repoRoot, file),
                    $"C# syntax error at line {line} ({firstError.Id}: {firstError.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}); SQL extraction from this file may be incomplete");
            }

            syntaxTrees.Add(tree);
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "DataGuard_SourceAnalysis",
            syntaxTrees: syntaxTrees,
            references: ResolveMetadataReferences(_projectOrPath),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var run = new ExtractionRun(this, repoRoot, IndexSyntaxTypes(syntaxTrees, cancellationToken), cancellationToken);
        foreach (var tree in syntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scan = new TreeScan(run, tree, compilation.GetSemanticModel(tree));
            run.EmitAll(scan.ScanCallSites());
        }

        // Unreferenced SQL constants/static readonly fields (global second pass after all call sites are known).
        foreach (var tree in syntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scan = new TreeScan(run, tree, compilation.GetSemanticModel(tree));
            run.EmitAll(scan.ScanUnreferencedConstants());
        }

        return run.Descriptors;
    }

    private void AddDiagnostic(AcquisitionDiagnosticKind kind, string path, string message)
    {
        var diagnostic = new AcquisitionDiagnostic(kind, path, message);
        if (!_diagnostics.Contains(diagnostic))
        {
            _diagnostics.Add(diagnostic);
        }
    }

    /// <summary>
    /// Builds the stable descriptor id <c>project-sql:{repo-relative path}:{span start}:{sha256(whitespace-normalized SQL)[..8]}</c>.
    /// </summary>
    internal static string BuildDescriptorId(string relativePath, int spanStart, string sqlText)
    {
        var normalized = NormalizeWhitespace(sqlText);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var shortHash = Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
        return $"project-sql:{relativePath}:{spanStart.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{shortHash}";
    }

    private static string RelativePath(string root, string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return string.Empty;
        }

        var relative = Path.GetRelativePath(root, Path.GetFullPath(filePath));
        return relative.Replace('\\', '/');
    }

    private static string ResolveScanRoot(string projectOrPath)
    {
        var full = Path.GetFullPath(projectOrPath);
        if (Directory.Exists(full))
        {
            return full;
        }

        return Path.GetDirectoryName(full) ?? full;
    }

    private static string? FindRepositoryRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var marker = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    /// <summary>A call site found by a pass, before skip checks, deduplication and descriptor construction.</summary>
    private sealed class SqlCandidate
    {
        public string SqlText { get; init; } = string.Empty;

        public Location Location { get; init; } = Location.None;

        /// <summary>Gets the node whose enclosing members/types are checked for <c>[SkipContractCheck]</c>.</summary>
        public SyntaxNode AnchorNode { get; init; } = null!;

        public SemanticModel Model { get; init; } = null!;

        /// <summary>Gets the span of the node that produced the SQL text; a stored-procedure candidate replaces a raw one with the same key.</summary>
        public TextSpan? SourceKey { get; init; }

        public string? TargetTypeName { get; init; }

        public IReadOnlyList<PropertyDescriptor> ExpectedProperties { get; init; } = Array.Empty<PropertyDescriptor>();

        public string? ProviderHint { get; init; }

        /// <summary>Gets a value indicating whether the call uses <c>CommandType.StoredProcedure</c> (the SQL text is synthesized).</summary>
        public bool IsStoredProcedure { get; init; }

        /// <summary>Gets the procedure name as written for <c>CommandType.StoredProcedure</c> calls.</summary>
        public string? ProcedureRawName { get; init; }

        public IReadOnlyList<CallBinding> Bindings { get; init; } = Array.Empty<CallBinding>();

        /// <summary>
        /// Gets a value indicating whether <see cref="Bindings"/> is the whole argument list. False for a
        /// <c>CommandType.StoredProcedure</c> call whose parameters the extractor cannot see.
        /// </summary>
        public bool ArgumentsKnown { get; init; } = true;
    }

    /// <summary>Per-run state: descriptors, deduplication sets and diagnostics.</summary>
    private sealed class ExtractionRun
    {
        private readonly ProjectCSharpSqlSource _owner;
        private readonly HashSet<string> _seenSql = new(StringComparer.Ordinal);
        private readonly List<ContractDescriptor> _descriptors = new();

        public ExtractionRun(
            ProjectCSharpSqlSource owner,
            string repoRoot,
            Dictionary<string, List<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>> syntaxTypes,
            CancellationToken cancellationToken)
        {
            _owner = owner;
            RepoRoot = repoRoot;
            SyntaxTypes = syntaxTypes;
            CancellationToken = cancellationToken;
        }

        public string RepoRoot { get; }

        public Dictionary<string, List<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>> SyntaxTypes { get; }

        public CancellationToken CancellationToken { get; }

        public HashSet<string> SeenSqlTexts { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<ContractDescriptor> Descriptors => _descriptors;

        public void ReportOversized(Location location, int length)
        {
            var lineSpan = location.GetLineSpan();
            var line = lineSpan.StartLinePosition.Line + 1;
            Console.WriteLine($"[WARN] DG1291 SQL literal in {Path.GetFileName(lineSpan.Path)}:{line} is {length} chars (cap {MaxSqlLiteralLength}); skipped");
            _owner.AddDiagnostic(
                AcquisitionDiagnosticKind.OversizedLiteral,
                RelativePath(RepoRoot, lineSpan.Path),
                $"SQL literal at line {line} is {length} chars (cap {MaxSqlLiteralLength}); skipped");
        }

        /// <summary>
        /// Emits one tree's candidates in pass order. When a <c>CommandType.StoredProcedure</c> candidate and a raw
        /// candidate come from the same SQL-producing node, only the stored-procedure one is kept.
        /// </summary>
        public void EmitAll(IReadOnlyList<SqlCandidate> candidates)
        {
            var procedureKeys = candidates
                .Where(c => c.IsStoredProcedure && c.SourceKey is not null)
                .Select(c => c.SourceKey!.Value)
                .ToHashSet();

            foreach (var candidate in candidates)
            {
                if (!candidate.IsStoredProcedure && candidate.SourceKey is { } key && procedureKeys.Contains(key))
                {
                    continue;
                }

                Emit(candidate);
            }
        }

        private void Emit(SqlCandidate candidate)
        {
            var sqlText = candidate.SqlText;
            var location = candidate.Location;
            var lineSpan = location.GetLineSpan();
            var filePath = lineSpan.Path;
            var lineNumber = lineSpan.StartLinePosition.Line + 1;
            var fileName = Path.GetFileName(filePath);
            var relativePath = RelativePath(RepoRoot, filePath);

            // Cap literal size before any rule regex sees the text (red-team F11: regex DoS via a huge literal).
            if (sqlText.Length > MaxSqlLiteralLength)
            {
                ReportOversized(location, sqlText.Length);
                return;
            }

            if (HasSkipContractCheck(candidate.AnchorNode, candidate.Model, CancellationToken))
            {
                _owner.SkippedContractCount++;
                _owner.AddDiagnostic(
                    AcquisitionDiagnosticKind.SkippedByAttribute,
                    relativePath,
                    $"SQL call site at line {lineNumber} skipped by [SkipContractCheck] (span {location.SourceSpan.Start})");
                return;
            }

            var key = $"{filePath}:{location.SourceSpan.Start}:{sqlText.Trim()}";
            if (!_seenSql.Add(key))
            {
                return;
            }

            var opType = ClassifySqlOperation(sqlText);
            var tables = ExtractReferencedTables(sqlText);
            var targetDisplay = string.IsNullOrEmpty(candidate.TargetTypeName) ? "untyped" : candidate.TargetTypeName;
            var detail = $"Found SQL in {fileName}:{lineNumber} targeting {targetDisplay}";

            _owner._progress?.Emit(new ProgressEvent(
                ProgressEventKind.ContractDiscovered,
                "Acquiring contracts",
                detail,
                new Dictionary<string, object?>
                {
                    ["Operation"] = opType.ToString(),
                    ["Tables"] = tables,
                    ["ProviderHint"] = candidate.ProviderHint,
                }));

            Console.WriteLine($"[INFO] {detail}");

            IReadOnlyList<ParameterDescriptor> parameters;
            string? procedureName = null;
            string? procedureSchema = null;
            string? procedurePackage = null;
            var oracleStyle = string.Equals(candidate.ProviderHint, "oracle", StringComparison.Ordinal);
            if (candidate.IsStoredProcedure && candidate.ProcedureRawName is not null)
            {
                (procedureName, procedureSchema, procedurePackage) = SplitProcedureName(candidate.ProcedureRawName, oracleStyle);
                parameters = BuildBoundProcedureParameters(candidate.Bindings, CancellationToken);
            }
            else if (TryParseProcedureCall(sqlText) is { } call)
            {
                (procedureName, procedureSchema, procedurePackage) = SplitProcedureName(call.RawName, oracleStyle || call.IsPlSqlBlock);
                parameters = BuildTextualProcedureParameters(call, candidate.Bindings, CancellationToken);
            }
            else
            {
                parameters = BuildPlaceholderParameters(sqlText, candidate.Bindings, CancellationToken);
            }

            var descriptor = new RawSqlDescriptor(
                Id: BuildDescriptorId(relativePath, location.SourceSpan.Start, sqlText),
                SqlText: sqlText,
                Parameters: parameters,
                ResultColumns: Array.Empty<ColumnDescriptor>(),
                Location: location,
                ExpectedProperties: candidate.ExpectedProperties,
                TargetTypeName: candidate.TargetTypeName,
                OperationType: opType,
                ReferencedTables: tables,
                ConnectionProviderHint: candidate.ProviderHint)
            {
                IsStoredProcedure = candidate.IsStoredProcedure,
                ProcedureName = procedureName,
                ProcedureSchema = procedureSchema,
                ProcedurePackage = procedurePackage,
                ArgumentsKnown = candidate.ArgumentsKnown,
            };

            SeenSqlTexts.Add(sqlText.Trim());
            _descriptors.Add(descriptor);
        }
    }
}
