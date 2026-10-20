using Shift.Domain;
using Shift.Infrastructure;
using Xunit;
namespace Shift.Tests;

public sealed class LaunchRequestTests
{
    [Fact]
    public void Url_quotes_and_spaces_are_escaped_before_shell_dispatch()
    {
        var request = LaunchRequestFactory.Create(new()
        {
            Name = "URL",
            Kind = ItemKind.Url,
            Target = "https://example.com/a b?q=\"hello\""
        }, @"C:\Windows");
        Assert.Equal("https://example.com/a%20b?q=%22hello%22", request.FileName);
        Assert.Equal("", request.Arguments);
        Assert.True(request.UseShellExecute);
    }
    [Fact]
    public void Url_environment_placeholders_are_never_expanded()
    {
        const string key = "SHIFT_TEST_URL_TOKEN";
        Environment.SetEnvironmentVariable(key, "changed");
        try
        {
            var request = LaunchRequestFactory.Create(new()
            {
                Name = "URL",
                Kind = ItemKind.Url,
                Target = "https://example.com/%" + key + "%"
            }, @"C:\Windows");
            Assert.DoesNotContain("changed", request.FileName);
        }
        finally { Environment.SetEnvironmentVariable(key, null); }
    }
    [Fact]
    public void Powershell_command_is_one_argument_with_no_hidden_window_or_policy_bypass()
    {
        const string text = "Write-Output 'a b'; Get-Date";
        var request = LaunchRequestFactory.Create(new()
        {
            Name = "Command",
            Kind = ItemKind.Command,
            Target = text
        }, @"C:\Windows");
        Assert.False(request.UseShellExecute);
        Assert.False(request.CreateNoWindow);
        Assert.Equal(new[] { "-NoLogo", "-NoProfile", "-Command", text }, request.ArgumentList);
        Assert.EndsWith("powershell.exe", request.FileName);
    }
    [Fact]
    public void Cmd_disables_autorun_and_preserves_explicit_command()
    {
        const string text = "echo SHIFT";
        var request = LaunchRequestFactory.Create(new()
        {
            Name = "Command",
            Kind = ItemKind.Command,
            Shell = CommandShell.Cmd,
            Target = text
        }, @"C:\Windows");
        Assert.Equal("/d /s /c \"echo SHIFT\"", request.Arguments);
        Assert.False(request.UseShellExecute);
        Assert.False(request.CreateNoWindow);
    }
    [Fact]
    public void App_arguments_are_separate_and_missing_paths_are_readable_errors()
    {
        var item = new WorkspaceItem { Name = "App", Kind = ItemKind.App, Target = @"C:\Apps\Tool.exe", Arguments = "--project \"C:\\My Project\"" };
        var request = LaunchRequestFactory.Create(item, @"C:\Windows", _ => true);
        Assert.Equal(item.Target, request.FileName);
        Assert.Equal(item.Arguments, request.Arguments);
        Assert.Throws<FileNotFoundException>(() => LaunchRequestFactory.Create(item, @"C:\Windows", _ => false));
        Assert.Throws<DirectoryNotFoundException>(() => LaunchRequestFactory.Create(item with { WorkingDirectory = @"C:\Missing" }, @"C:\Windows", _ => true, _ => false));
    }
}
