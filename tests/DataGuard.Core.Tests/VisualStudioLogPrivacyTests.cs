using DataGuard.VisualStudio;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class VisualStudioLogPrivacyTests
{
    [Theory]
    [InlineData("Password=", "")]
    [InlineData("Password=\"", "\"")]
    [InlineData("Authorization: Bearer ", "")]
    public void LongSecretsLeaveNoSuffix(string prefix, string suffix)
    {
        Assert.DoesNotContain("SECRETTAIL", LogPrivacyPolicy.Redact(prefix + new string('x', 600) + "SECRETTAIL" + suffix));
    }

    [Theory]
    [InlineData("User Id=PRIVATEUSER;Password=secret", "PRIVATEUSER")]
    [InlineData("SELECT 'PRIVATEVALUE' FROM T", "PRIVATEVALUE")]
    [InlineData("failed at C:\\Users\\PRIVATEUSER\\a.cs:12", "PRIVATEUSER")]
    [InlineData("failed at /home/PRIVATEUSER/a.cs:12", "PRIVATEUSER")]
    [InlineData("contact PRIVATEUSER@example.test", "PRIVATEUSER")]
    [InlineData("Server=PRIVATEHOST;Database=private", "PRIVATEHOST")]
    public void PrivateValuesAreNotRecorded(string input, string secret)
    {
        Assert.DoesNotContain(secret, LogPrivacyPolicy.Redact(input));
    }

    [Theory]
    [InlineData("Password={alpha}}SECRETTAIL};Uid=x")]
    [InlineData("Password=\"alpha;SECRETTAIL")]
    [InlineData("Password='alpha;SECRETTAIL")]
    [InlineData("Password={alpha;SECRETTAIL")]
    public void EscapedOrUnterminatedCredentialValuesLeaveNoTail(string input)
    {
        Assert.DoesNotContain("SECRETTAIL", LogPrivacyPolicy.Redact(input));
    }

    [Fact]
    public void FixedIdeSafeOptionRemainsReadableWithoutExemptingOtherQuotedText()
    {
        Assert.Equal("Unrecognized command or argument '--ide-safe'.", LogPrivacyPolicy.Redact("Unrecognized command or argument '--ide-safe'."));
        Assert.DoesNotContain("PRIVATE", LogPrivacyPolicy.Redact("Unrecognized command or argument '--PRIVATE'."));
        Assert.DoesNotContain("--ide-safe", LogPrivacyPolicy.Redact("SELECT '--ide-safe' FROM PRIVATE_TABLE"));
        Assert.DoesNotContain("--ide-safe", LogPrivacyPolicy.Redact("Password='--ide-safe'"));
    }

    [Fact]
    public void OversizedInputFailsClosedInsteadOfLeakingPartialValues()
    {
        Assert.DoesNotContain("PRIVATE", LogPrivacyPolicy.Redact(new string('x', 70000) + "PRIVATE"));
    }

    [Fact]
    public void SafeDiagnosticMetadataSurvivesAndRedactionIsIdempotent()
    {
        const string safe = "DG019 line=12 column=4 provider=oracle exit=3 duration=25ms";
        Assert.Equal(safe, LogPrivacyPolicy.Redact(safe));
        var redacted = LogPrivacyPolicy.Redact("Password=private;Server=private");
        Assert.Equal(redacted, LogPrivacyPolicy.Redact(redacted));
    }

    [Fact]
    public void Utf8RecordLimitCountsBytes()
    {
        var text = LogPrivacyPolicy.Redact(new string('界', 3000));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(text) <= LogPrivacyPolicy.MaximumRecordBytes);
    }

    [Fact]
    public void NewlinesCannotForgeRecords()
    {
        var result = LogPrivacyPolicy.Redact("failure\r\n[INFO] forged\u001b[31m");
        Assert.DoesNotContain("\n", result);
        Assert.DoesNotContain("\r", result);
        Assert.DoesNotContain('\u001b', result);
    }
}

public sealed class VisualStudioBoundedLogTests
{
    [Fact]
    public void RotationRetainsOnlyOneBoundedBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dataguard-log-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "diagnostic.log");
            var record = new string('a', 8192);
            for (var index = 0; index < 400; index++)
            {
                BoundedLogFile.Append(path, record);
            }

            Assert.InRange(new FileInfo(path).Length, 1, BoundedLogFile.MaximumFileBytes);
            Assert.InRange(new FileInfo(path + ".1").Length, 1, BoundedLogFile.MaximumFileBytes);
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void OversizedLegacyLogDoesNotBecomeOversizedBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dataguard-log-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "diagnostic.log");
            File.WriteAllText(path, new string('x', BoundedLogFile.MaximumFileBytes + 1));
            File.WriteAllText(path + ".1", new string('x', BoundedLogFile.MaximumFileBytes + 1));
            BoundedLogFile.Append(path, "safe");
            Assert.Equal("safe", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".1"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
