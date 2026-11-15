using Shift.App.Services;
using Xunit;
namespace Shift.Tests;
public class ScenarioLocalizationTests
{
    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Scenario_findings_resolve_existing_doctor_codes(string language)
    {
        var strings = new Strings { Language = language };
        foreach (var prefix in new[] { "Scenario.PreflightError: ", "Scenario.PreflightObservation: " })
        {
            var text = strings.LocalizeMessage(prefix + "Doctor.AccessDenied, Doctor.MissingPath");
            Assert.Contains(strings["Doctor.AccessDenied"], text);
            Assert.DoesNotContain("Doctor.Doctor", text);
            Assert.DoesNotContain("Scenario.", text);
        }
        Assert.DoesNotContain("Scenario.", strings.LocalizeMessage("Scenario.PreflightStopped"));
        Assert.DoesNotContain("Scenario.", strings.LocalizeMessage("Scenario.LaunchErrorStopped"));
    }
}
