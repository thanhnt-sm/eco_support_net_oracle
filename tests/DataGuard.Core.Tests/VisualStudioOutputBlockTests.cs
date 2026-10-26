using DataGuard.VisualStudio;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class VisualStudioOutputBlockTests
{
    [Fact]
    public void Result_WithoutSummaryDoesNotInventCountsOrCoverage()
    {
        var text = OutputBlockFormatter.RenderLines(OutputBlockFormatter.Result(new ProgressReadResult()));

        Assert.Contains("\r\nResult: No final validation summary was produced.\r\n", text);
        Assert.DoesNotContain("0 errors", text);
        Assert.DoesNotContain("Coverage:", text);
        Assert.DoesNotContain("Ground truth:", text);
    }

    [Fact]
    public void Banner_HasPhysicalLinesForEachField()
    {
        var text = OutputBlockFormatter.RenderLines(OutputBlockFormatter.Banner(
            "Run Validation", "Checks contracts.", "synthetic", "synthetic.yml", "ide-safe", "previous results kept", new[] { "Rules:  7 enabled, 2 disabled" }));

        var expectedLines = new[]
        {
            OutputBlockFormatter.Separator,
            "StackAccord (Beta) — Run Validation",
            OutputBlockFormatter.Separator,
            "What:   Checks contracts.",
            "Scope:  synthetic",
            "Config: synthetic.yml",
            "Mode:   ide-safe",
            "Errors: previous results kept",
            "Rules:  7 enabled, 2 disabled",
            OutputBlockFormatter.Separator,
            string.Empty,
        };
        Assert.Equal(string.Join("\r\n", expectedLines), text);
    }

    [Fact]
    public void Result_HasPhysicalLinesForCountsGroundTruthAndAction()
    {
        var text = OutputBlockFormatter.RenderLines(OutputBlockFormatter.Result(new ProgressReadResult
        {
            HasSummary = true,
            ErrorCount = 2,
            WarningCount = 1,
            GroundTruth = "schema+entities",
        }));

        Assert.Contains("\r\nResult: 2 errors, 1 warnings\r\nGround truth: schema+entities\r\nAction:", text);
        Assert.EndsWith("\r\n" + OutputBlockFormatter.Separator + "\r\n", text);
    }

    [Fact]
    public void Inventory_PreservesPhysicalLinesAndSanitizesWholeRuleIdFields()
    {
        var inventory = new RuleInventory();
        inventory.Add(new RuleInventoryItem("DG001", "synthetic", 1));
        inventory.Add(new RuleInventoryItem("Password=\"HEAD\r\nSECRETTAIL\";\n[OK] forged", "synthetic", 0));
        var text = OutputBlockFormatter.RenderLines(inventory.BuildLines());

        Assert.Contains("\r\n[StackAccord] Rules Evaluated: 2 (DG001, ", text);
        Assert.Contains("\r\n[StackAccord] Rules with Findings: DG001 (1)\r\n", text);
        Assert.DoesNotContain("HEAD", text);
        Assert.DoesNotContain("SECRETTAIL", text);
        Assert.DoesNotContain("\r\n[OK]", text);
        Assert.Equal(6, text.Split(new[] { "\r\n" }, StringSplitOptions.None).Length);
    }

    [Theory]
    [InlineData("Password=\"HEAD\r\nSECRETTAIL\";\r\n[OK] forged\u001b[31m")]
    [InlineData("Password='HEAD\r\nSECRETTAIL';\u202e[OK] forged")]
    [InlineData("Password={HEAD\r\nSECRETTAIL};\n[OK] forged")]
    public void UntrustedMultilineField_CannotLeakSecretOrCreateLogicalRecords(string field)
    {
        var text = OutputBlockFormatter.RenderLines(OutputBlockFormatter.Banner(
            "Run Validation", "Checks contracts.", field, "synthetic.yml", "ide-safe", "kept", Array.Empty<string>()));

        Assert.DoesNotContain("HEAD", text);
        Assert.DoesNotContain("SECRETTAIL", text);
        Assert.DoesNotContain("\r\n[OK]", text);
        Assert.DoesNotContain('\u001b', text);
        Assert.DoesNotContain('\u202e', text);
        Assert.Equal(10, text.Split(new[] { "\r\n" }, StringSplitOptions.None).Length);
        Assert.Contains("\r\nConfig: synthetic.yml\r\nMode:   ide-safe\r\n", text);
    }
}
