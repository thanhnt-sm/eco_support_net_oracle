// <copyright file="DataGuardRulesOptionsPage.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

/// <summary>
/// Options dialog page under Tools -&gt; Options -&gt; DataGuard -&gt; Validation Rules.
/// Displays clear descriptions of all DataGuard contract, schema, dialect, and security
/// validation rules, and allows enabling/disabling each rule (enabled by default).
/// </summary>
[ComVisible(true)]
[Guid("5802fc76-4ebd-4ab7-8014-9918c889f048")]
public class DataGuardRulesOptionsPage : DialogPage
{
    // =========================================================================
    // Category 1: Contract & Schema Alignment
    // =========================================================================

    /// <summary>Gets or sets a value indicating whether DG002/DG101 (Parameter Count &amp; Type Match) is enabled.</summary>
    [Category("1. Contract & Schema Alignment")]
    [DisplayName("DG002 & DG101: Parameter Count & Type Match")]
    [Description("Validates parameter count and data types between C# calls (Dapper, ADO.NET, EF Core) and database stored procedures or queries. Catches missing/extra parameters and incompatible data types.")]
    public bool EnableParameterMismatch { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether DG003 (Parameter Direction) is enabled.</summary>
    [Category("1. Contract & Schema Alignment")]
    [DisplayName("DG003: Parameter Direction (In/Out/Return)")]
    [Description("Validates parameter directions (Input, Output, InputOutput, ReturnValue). Catches missing ParameterDirection.Output declarations when reading SP outputs.")]
    public bool EnableParameterDirection { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether DG004 (Result Set Column Shape) is enabled.</summary>
    [Category("1. Contract & Schema Alignment")]
    [DisplayName("DG004: Result Set Column Shape")]
    [Description("Matches result set columns returned by SQL queries or stored procedures against C# entity or DTO properties. Detects missing, extra, or renamed columns.")]
    public bool EnableColumnShape { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether DG005 (Nullable Compatibility) is enabled.</summary>
    [Category("1. Contract & Schema Alignment")]
    [DisplayName("DG005: Nullable Compatibility")]
    [Description("Detects mismatches between non-nullable C# properties and nullable database columns (or vice versa), preventing NullReferenceException or InvalidCastException.")]
    public bool EnableNullableMismatch { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether DG006 (Naming Convention Compliance) is enabled.</summary>
    [Category("1. Contract & Schema Alignment")]
    [DisplayName("DG006: Naming Convention Compliance")]
    [Description("Verifies naming convention compliance (e.g. snake_case to PascalCase or camelCase mapping) between database identifiers and C# models.")]
    public bool EnableNamingConvention { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether DG015 (Phantom Table Reference) is enabled.</summary>
    [Category("1. Contract & Schema Alignment")]
    [DisplayName("DG015: Phantom Table Reference")]
    [Description("Detects references to tables or views in SQL queries that do not exist in the database schema or committed schema snapshot.")]
    public bool EnablePhantomTable { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether DG016 (Phantom Column Reference &amp; Raw SQL Parse Status) is enabled.</summary>
    [Category("1. Contract & Schema Alignment")]
    [DisplayName("DG016: Phantom Column & Raw SQL Parse Status")]
    [Description("Detects references to columns in SQL queries that do not exist on the target tables, and flags malformed raw SQL syntax or un-parsable statements.")]
    public bool EnablePhantomColumn { get; set; } = true;

    // =========================================================================
    // Category 2: Length & Memory Overflow
    // =========================================================================

    /// <summary>Gets or sets a value indicating whether DG007 (Entity Length Exceeds Column) is enabled.</summary>
    [Category("2. Length & Memory Overflow")]
    [DisplayName("DG007: Entity Length Exceeds Column")]
    [Description("Detects C# entity properties whose declared maximum length ([MaxLength], [StringLength]) exceeds the database column definition (VARCHAR(N)), preventing runtime truncation errors.")]
    public bool EnableLengthExceedsColumn { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether DG008 (Multi-Byte Length Overflow Risk) is enabled.</summary>
    [Category("2. Length & Memory Overflow")]
    [DisplayName("DG008: Multi-Byte Length Overflow Risk")]
    [Description("Warns about multi-byte character overflow risks (Vietnamese accents, UTF-8, emoji) when storing into byte-length columns (e.g. Oracle VARCHAR2(N BYTE), MySQL utf8mb4).")]
    public bool EnableByteLengthOverflow { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether DG009 (Inferred Size Fallback Risk) is enabled.</summary>
    [Category("2. Length & Memory Overflow")]
    [DisplayName("DG009: Inferred Size Fallback Risk")]
    [Description("Detects database parameters created without explicit size/length specification, which causes ADO.NET/Dapper to infer size per query and pollutes database query execution plans.")]
    public bool EnableInferredSizeFallback { get; set; } = true;

    // =========================================================================
    // Category 3: SQL Dialect & Portability
    // =========================================================================

    /// <summary>Gets or sets a value indicating whether DG010-DG013 (SQL Dialect Leaks &amp; Compatibility) is enabled.</summary>
    [Category("3. SQL Dialect & Portability")]
    [DisplayName("DG010-DG013: SQL Dialect Leaks & Compatibility")]
    [Description("Detects vendor-specific SQL syntax or functions used against the wrong database provider (e.g. Oracle NVL/DECODE/SYSDATE in SQL Server, or T-SQL TOP/ISNULL/GETDATE() in Oracle).")]
    public bool EnableDialectSyntaxLeak { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether DG014 (Unmapped Type Usage) is enabled.</summary>
    [Category("3. SQL Dialect & Portability")]
    [DisplayName("DG014: Unmapped Type Usage")]
    [Description("Detects usage of database types or C# types that do not have valid or safe mapping definitions in the configured provider.")]
    public bool EnableUnmappedTypeUsage { get; set; } = true;

    // =========================================================================
    // Category 4: Performance
    // =========================================================================

    /// <summary>Gets or sets a value indicating whether DG017 (Avoid SELECT *) is enabled.</summary>
    [Category("4. Performance")]
    [DisplayName("DG017: Avoid SELECT *")]
    [Description("Warns against using 'SELECT *' in queries. Recommends explicit column lists to reduce network bandwidth, enable index coverage, and guarantee stable result set shapes.")]
    public bool EnableSelectStar { get; set; } = true;

    // =========================================================================
    // Category 5: IDE & Code Tracking
    // =========================================================================

    /// <summary>Gets or sets a value indicating whether DG001 (Track Unvalidated SQL Calls) is enabled.</summary>
    [Category("5. IDE & Code Tracking")]
    [DisplayName("DG001: Track Unvalidated SQL Calls")]
    [Description("Identifies and highlights SQL or stored procedure execution calls in C# code that have not yet been evaluated by DataGuard.")]
    public bool EnableUnvalidatedSqlCall { get; set; } = true;

    /// <summary>
    /// Checks if a specific rule ID is currently enabled by user options.
    /// </summary>
    /// <param name="ruleId">The rule identifier (e.g. DG002, DG017).</param>
    /// <returns>True if enabled; false if disabled.</returns>
    public bool IsRuleEnabled(string? ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return true;
        }

        switch (ruleId.Trim().ToUpperInvariant())
        {
            case "DG001":
                return this.EnableUnvalidatedSqlCall;
            case "DG002":
            case "DG101":
                return this.EnableParameterMismatch;
            case "DG003":
                return this.EnableParameterDirection;
            case "DG004":
                return this.EnableColumnShape;
            case "DG005":
                return this.EnableNullableMismatch;
            case "DG006":
                return this.EnableNamingConvention;
            case "DG007":
                return this.EnableLengthExceedsColumn;
            case "DG008":
                return this.EnableByteLengthOverflow;
            case "DG009":
                return this.EnableInferredSizeFallback;
            case "DG010":
            case "DG011":
            case "DG012":
            case "DG013":
                return this.EnableDialectSyntaxLeak;
            case "DG014":
                return this.EnableUnmappedTypeUsage;
            case "DG015":
                return this.EnablePhantomTable;
            case "DG016":
                return this.EnablePhantomColumn;
            case "DG017":
                return this.EnableSelectStar;
            default:
                return true;
        }
    }

    /// <summary>
    /// Returns the complete catalog of validation rules with their description and current status.
    /// </summary>
    /// <returns>List of rule descriptors.</returns>
    public IReadOnlyList<RuleDescriptor> GetRuleCatalog()
    {
        return new List<RuleDescriptor>
        {
            new ("DG001", "Track Unvalidated SQL Calls", "IDE & Code Tracking", "Identifies SQL/SP calls in C# code not yet validated by DataGuard.", this.EnableUnvalidatedSqlCall),
            new ("DG002, DG101", "Parameter Count & Type Match", "Contract & Schema Alignment", "Validates parameter count and data types between C# calls and DB SP/queries.", this.EnableParameterMismatch),
            new ("DG003", "Parameter Direction (In/Out/Return)", "Contract & Schema Alignment", "Validates parameter directions (Input, Output, InputOutput, ReturnValue).", this.EnableParameterDirection),
            new ("DG004", "Result Set Column Shape", "Contract & Schema Alignment", "Matches result set columns returned by SQL queries/SP against C# entity/DTO properties.", this.EnableColumnShape),
            new ("DG005", "Nullable Compatibility", "Contract & Schema Alignment", "Detects mismatches between non-nullable C# properties and nullable DB columns.", this.EnableNullableMismatch),
            new ("DG006", "Naming Convention Compliance", "Contract & Schema Alignment", "Verifies naming convention compliance (e.g. snake_case to PascalCase).", this.EnableNamingConvention),
            new ("DG007", "Entity Length Exceeds Column", "Length & Memory Overflow", "Detects C# entity properties exceeding DB column width definition (VARCHAR(N)).", this.EnableLengthExceedsColumn),
            new ("DG008", "Multi-Byte Length Overflow Risk", "Length & Memory Overflow", "Warns about multi-byte character overflow risks (Vietnamese accents, UTF-8, emoji).", this.EnableByteLengthOverflow),
            new ("DG009", "Inferred Size Fallback Risk", "Length & Memory Overflow", "Detects parameters without explicit size causing plan cache pollution.", this.EnableInferredSizeFallback),
            new ("DG010-013", "SQL Dialect Leaks & Compatibility", "SQL Dialect & Portability", "Detects vendor-specific SQL syntax or functions used against the wrong provider.", this.EnableDialectSyntaxLeak),
            new ("DG014", "Unmapped Type Usage", "SQL Dialect & Portability", "Detects usage of DB types or C# types without safe mapping definitions.", this.EnableUnmappedTypeUsage),
            new ("DG015", "Phantom Table Reference", "Contract & Schema Alignment", "Detects references to tables/views that do not exist in the DB schema.", this.EnablePhantomTable),
            new ("DG016", "Phantom Column Reference & Raw SQL Parse Status", "Contract & Schema Alignment", "Detects non-existent columns and malformed raw SQL syntax.", this.EnablePhantomColumn),
            new ("DG017", "Avoid SELECT * (Performance)", "Performance", "Warns against SELECT * to reduce bandwidth and ensure stable result shapes.", this.EnableSelectStar),
        };
    }

    /// <summary>
    /// Metadata descriptor for a DataGuard validation rule.
    /// </summary>
    public sealed class RuleDescriptor
    {
        public RuleDescriptor(string id, string name, string category, string description, bool isEnabled)
        {
            this.Id = id;
            this.Name = name;
            this.Category = category;
            this.Description = description;
            this.IsEnabled = isEnabled;
        }

        public string Id { get; }

        public string Name { get; }

        public string Category { get; }

        public string Description { get; }

        public bool IsEnabled { get; }
    }
}
