using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Reporting;

/// <summary>
/// Minimal SARIF 2.1.0 types for DataGuard output.
/// </summary>
public class SarifLog
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "2.1.0";

    [JsonPropertyName("$schema")]
    public string SchemaUri { get; set; } = "https://schemastore.org/schemas/json/sarif-2.1.0.json";

    [JsonPropertyName("runs")]
    public List<Run> Runs { get; set; } = new();

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }
}

public class Run
{
    [JsonPropertyName("tool")]
    public Tool Tool { get; set; } = new();

    [JsonPropertyName("results")]
    public List<Result> Results { get; set; } = new();
}

public class Tool
{
    [JsonPropertyName("driver")]
    public ToolComponent Driver { get; set; } = new();
}

public class ToolComponent
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "DataGuard";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "0.1.0-alpha.1";

    [JsonPropertyName("informationUri")]
    public string InformationUri { get; set; } = "https://github.com/DataGuard/DataGuard";

    [JsonPropertyName("rules")]
    public List<ReportingDescriptor> Rules { get; set; } = new();
}

public class ReportingDescriptor
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("shortDescription")]
    public MultiformatMessageString ShortDescription { get; set; } = new();

    [JsonPropertyName("defaultConfiguration")]
    public ReportingConfiguration DefaultConfiguration { get; set; } = new();
}

public class MultiformatMessageString
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

public class ReportingConfiguration
{
    [JsonPropertyName("level")]
    public string Level { get; set; } = "error";
}

public class Result
{
    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = "";

    [JsonPropertyName("message")]
    public Message Message { get; set; } = new();

    [JsonPropertyName("level")]
    public string Level { get; set; } = "error";

    [JsonPropertyName("locations")]
    public List<SarifLocation> Locations { get; set; } = new();

    [JsonPropertyName("properties")]
    public PropertyBag Properties { get; set; } = new();
}

public class Message
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

public class SarifLocation
{
    [JsonPropertyName("physicalLocation")]
    public PhysicalLocation PhysicalLocation { get; set; } = new();
}

public class PhysicalLocation
{
    [JsonPropertyName("artifactLocation")]
    public ArtifactLocation ArtifactLocation { get; set; } = new();

    [JsonPropertyName("region")]
    public Region Region { get; set; } = new();
}

public class ArtifactLocation
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = "";

    [JsonPropertyName("uriBaseId")]
    public string UriBaseId { get; set; } = "%SRCROOT%";
}

public class Region
{
    [JsonPropertyName("startLine")]
    public int StartLine { get; set; }

    [JsonPropertyName("startColumn")]
    public int StartColumn { get; set; }

    [JsonPropertyName("endLine")]
    public int EndLine { get; set; }

    [JsonPropertyName("endColumn")]
    public int EndColumn { get; set; }
}

public class PropertyBag : Dictionary<string, object>
{
    public PropertyBag()
        : base()
    {
    }

    public PropertyBag(IDictionary<string, object?> dictionary)
        : base(
        dictionary.ToDictionary(k => k.Key, v => v.Value!))
    {
    }
}