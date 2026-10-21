using System.IO;
using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>Shared prompt assertions exercised by both Windows and cross-platform CI.</summary>
public partial class LiveDatabaseCommandsTests
{
    [Fact]
    public void Validate_EmptyVariableName_PointsAtTheOptionsPage()
    {
        LiveDatabaseOptions.Validate(null, "sqlserver", null).Error.Should().Be(LiveDatabaseOptions.MissingVariableMessage);
        LiveDatabaseOptions.MissingVariableMessage.Should().Contain("Tools > Options > StackAccord > General > Connection Environment Variable");
    }

    [Fact]
    public void ConfirmationText_NamesHostVariableProviderAndTarget()
    {
        var refresh = LiveDatabaseCommands.BuildConfirmationText(LiveCommandKind.RefreshSnapshot, "db-prod:1433", "MyDb", "sqlserver", @"D:\repo");
        refresh.Should().Be("StackAccord will connect to db-prod:1433 (environment variable MyDb, provider sqlserver) to refresh the schema snapshot into " + Path.Combine(@"D:\repo", ".dataguard-snapshot.json") + ". Continue?");

        var verify = LiveDatabaseCommands.BuildConfirmationText(LiveCommandKind.VerifyShape, "(unknown host)", "MyDb", "oracle", @"D:\repo");
        verify.Should().Be("StackAccord will connect to (unknown host) (environment variable MyDb, provider oracle) to describe every read SQL query under D:\\repo. Continue?");
    }
}
