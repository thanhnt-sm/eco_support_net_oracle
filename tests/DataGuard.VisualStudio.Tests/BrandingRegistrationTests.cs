using System;
using System.Reflection;
using System.Resources;
using FluentAssertions;
using Microsoft.VisualStudio.Shell;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

public sealed class BrandingRegistrationTests
{
    [Fact]
    public void OptionsCategory_LocalizesDisplay_AndKeepsAutomationAndStorageIdentity()
    {
        var assembly = typeof(DataGuardPackage).Assembly;
        var resources = new ResourceManager("VSPackage", assembly);
        foreach (var page in typeof(DataGuardPackage).GetCustomAttributes<ProvideOptionPageAttribute>())
        {
            page.CategoryName.Should().Be("DataGuard");
            page.CategoryResourceID.Should().Be(110);
            page.ProvidesLocalizedCategoryName.Should().BeTrue();
            page.SupportsAutomation.Should().BeTrue();
            resources.GetString(page.CategoryResourceID.ToString(System.Globalization.CultureInfo.InvariantCulture)).Should().Be("StackAccord");
        }

        typeof(DataGuardPackage).GetCustomAttributes<ProvideOptionPageAttribute>().Should().HaveCount(2);

        // DialogPage storage defaults to AutomationObject.GetType().FullName; unchanged page types retain it.
        typeof(DataGuardOptionsPage).FullName.Should().Be("DataGuard.VisualStudio.DataGuardOptionsPage");
        typeof(DataGuardRulesOptionsPage).FullName.Should().Be("DataGuard.VisualStudio.DataGuardRulesOptionsPage");
        typeof(DataGuardOptionsPage).GUID.Should().Be(new Guid("6e1a9b24-342a-4a6c-9477-981504d6cfa1"));
        typeof(DataGuardRulesOptionsPage).GUID.Should().Be(new Guid("5802fc76-4ebd-4ab7-8014-9918c889f048"));
    }
}
