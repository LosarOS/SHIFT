using System.Buffers.Binary;
using Shift.Application;
using Shift.Domain;
using Shift.Infrastructure;
using Xunit;
namespace Shift.Tests;

public sealed class QuickWorkspaceSetupTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "shift-discovery-" + Guid.NewGuid());
    public QuickWorkspaceSetupTests() => Directory.CreateDirectory(directory);
    private string Shortcut(string relative, bool valid = true)
    {
        var path = Path.Combine(directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[100];
        if (valid)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 76);
            new Guid("00021401-0000-0000-C000-000000000046").TryWriteBytes(bytes.AsSpan(4, 16));
        }
        File.WriteAllBytes(path, bytes);
        return path;
    }
    private StartMenuApplicationDiscovery Discovery(params ApplicationDiscoveryRoot[] roots) => new(roots.Length == 0 ? [new(directory, ApplicationSource.UserStartMenu)] : roots);
    private static ApplicationCandidate Candidate(string id, string name = "Editor", string path = @"C:\Apps\Editor.lnk") => new(id, name, path, ApplicationSource.UserStartMenu, "Tools");

    [Fact]
    public async Task Discovery_reads_only_shortcut_headers_without_launch_or_mutation()
    {
        var path = Shortcut(Path.Combine("Инструменты", "Редактор проекта.lnk"));
        var before = File.ReadAllBytes(path);
        File.WriteAllText(Path.Combine(directory, "should-not-run.cmd"), "exit /b 99");
        File.WriteAllText(Path.Combine(directory, "notes.txt"), "Private document content");
        var result = await Discovery().DiscoverAsync();
        var item = Assert.Single(result.Candidates);
        Assert.Equal("Редактор проекта", item.Name);
        Assert.Equal("Инструменты", item.Group);
        Assert.Equal(ApplicationSource.UserStartMenu, item.Source);
        Assert.Equal(path, item.ShortcutPath);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(3, Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length);
        Assert.Equal(0, result.InvalidShortcuts);
    }

    [Fact]
    public async Task Corrupt_and_truncated_shortcuts_are_omitted_but_valid_choices_remain()
    {
        Shortcut("Good.lnk");
        Shortcut("Broken.lnk", false);
        File.WriteAllBytes(Path.Combine(directory, "Truncated.lnk"), [76, 0, 0, 0]);
        var result = await Discovery().DiscoverAsync();
        Assert.Equal("Good", Assert.Single(result.Candidates).Name);
        Assert.Equal(2, result.InvalidShortcuts);
    }

    [Fact]
    public async Task Duplicate_roots_do_not_repeat_shortcuts()
    {
        Shortcut("Editor.lnk");
        var result = await Discovery(new(directory, ApplicationSource.UserStartMenu), new(directory, ApplicationSource.SharedStartMenu)).DiscoverAsync();
        Assert.Single(result.Candidates);
    }

    [Fact]
    public async Task Missing_or_relative_roots_do_not_block_an_accessible_source()
    {
        Shortcut("Editor.lnk");
        var result = await Discovery(new("relative", ApplicationSource.Desktop), new(Path.Combine(directory, "missing"), ApplicationSource.SharedStartMenu), new(directory, ApplicationSource.UserStartMenu)).DiscoverAsync();
        Assert.Single(result.Candidates);
        Assert.Equal(2, result.UnavailableLocations);
    }

    [Fact]
    public async Task Empty_sources_are_a_valid_empty_result()
    {
        var result = await Discovery().DiscoverAsync();
        Assert.Empty(result.Candidates);
        Assert.False(result.Limited);
        Assert.Equal(0, result.InvalidShortcuts + result.UnavailableLocations);
    }

    [Fact]
    public async Task Candidate_count_is_bounded_and_reported()
    {
        for (var i = 0; i <= StartMenuApplicationDiscovery.MaximumCandidates; i++) Shortcut($"App{i:000}.lnk");
        var result = await Discovery().DiscoverAsync();
        Assert.Equal(StartMenuApplicationDiscovery.MaximumCandidates, result.Candidates.Count);
        Assert.True(result.Limited);
    }

    [Fact]
    public async Task Excessively_deep_directories_are_not_descended()
    {
        var nested = Path.Combine(Enumerable.Repeat("nested", StartMenuApplicationDiscovery.MaximumDepth + 1).ToArray());
        Shortcut(Path.Combine(nested, "Hidden.lnk"));
        Shortcut("Visible.lnk");
        var result = await Discovery().DiscoverAsync();
        Assert.Equal("Visible", Assert.Single(result.Candidates).Name);
        Assert.True(result.Limited);
    }

    [Fact]
    public async Task Cancellation_returns_no_partial_success()
    {
        Shortcut("Editor.lnk");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Discovery().DiscoverAsync(cancellation.Token));
    }

    [Fact]
    public async Task Inaccessible_shortcut_is_skipped_without_aborting_discovery()
    {
        var path = Shortcut("Denied.lnk");
        Shortcut("Readable.lnk");
        if (OperatingSystem.IsWindows())
        {
            using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            var result = await Discovery().DiscoverAsync();
            Assert.Equal("Readable", Assert.Single(result.Candidates).Name);
            Assert.True(result.UnavailableLocations > 0);
        }
        else
        {
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, UnixFileMode.None);
            try
            {
                var result = await Discovery().DiscoverAsync();
                Assert.Equal("Readable", Assert.Single(result.Candidates).Name);
                Assert.True(result.UnavailableLocations > 0);
            }
            finally { File.SetUnixFileMode(path, mode); }
        }
    }

    [Fact]
    public async Task Symlinked_source_and_file_are_not_followed()
    {
        var target = Path.Combine(directory, "outside");
        Directory.CreateDirectory(target);
        Shortcut(Path.Combine("outside", "Outside.lnk"));
        var source = Path.Combine(directory, "source");
        Directory.CreateDirectory(source);
        Directory.CreateSymbolicLink(Path.Combine(source, "linked-folder"), target);
        File.CreateSymbolicLink(Path.Combine(source, "linked-file.lnk"), Path.Combine(target, "Outside.lnk"));
        var result = await Discovery(new ApplicationDiscoveryRoot(source, ApplicationSource.Desktop)).DiscoverAsync();
        Assert.Empty(result.Candidates);
        Assert.Equal(2, result.UnavailableLocations);
        var linkedRoot = await Discovery(new ApplicationDiscoveryRoot(Path.Combine(source, "linked-folder"), ApplicationSource.Desktop)).DiscoverAsync();
        Assert.Empty(linkedRoot.Candidates);
        Assert.Equal(1, linkedRoot.UnavailableLocations);
    }

    [Fact]
    public async Task Entry_budget_bounds_catalogs_dominated_by_non_shortcuts()
    {
        for (var i = 0; i <= StartMenuApplicationDiscovery.MaximumEntries; i++) File.WriteAllText(Path.Combine(directory, $"entry{i}.txt"), "");
        var result = await Discovery().DiscoverAsync();
        Assert.Empty(result.Candidates);
        Assert.True(result.Limited);
    }

    [Fact]
    public async Task Too_many_source_roots_are_disclosed_as_limited()
    {
        var roots = Enumerable.Range(0, 9).Select(_ => new ApplicationDiscoveryRoot(directory, ApplicationSource.Desktop));
        var result = await new StartMenuApplicationDiscovery(roots).DiscoverAsync();
        Assert.True(result.Limited);
    }

    [Fact]
    public async Task Long_display_names_fit_domain_limit_without_splitting_surrogate_pairs()
    {
        Shortcut(new string('A', 99) + "🚀.lnk");
        var result = await Discovery().DiscoverAsync();
        var name = Assert.Single(result.Candidates).Name;
        Assert.Equal(99, name.Length);
        Assert.False(char.IsSurrogate(name[^1]));
    }

    [Fact]
    public void Explicit_selection_builds_ordered_new_items_without_embedded_parameters()
    {
        var catalog = new[] { Candidate("one"), Candidate("two", "Browser", @"C:\Apps\Browser.lnk") };
        var workspace = QuickWorkspaceSetup.CreateDraft("  Work  ", catalog, ["two", "one"]);
        Assert.Equal("Work", workspace.Name);
        Assert.Equal(new[] { "Browser", "Editor" }, workspace.Items.Select(i => i.Name));
        Assert.Equal(new[] { 0, 1 }, workspace.Items.Select(i => i.Order));
        Assert.All(workspace.Items, item => { Assert.Equal(ItemKind.App, item.Kind); Assert.Empty(item.Arguments); Assert.Empty(item.WorkingDirectory); Assert.True(item.Enabled); });
        Assert.NotEqual(workspace.Items[0].Id, workspace.Items[1].Id);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("too-many")]
    public void Invalid_or_stale_selection_cannot_create_workspace(string scenario)
    {
        var catalog = new[] { Candidate("one") };
        string[] selected = scenario switch { "empty" => [], "duplicate" => ["one", "one"], "unknown" => ["not-in-catalog"], _ => Enumerable.Range(0, 101).Select(i => i.ToString()).ToArray() };
        Assert.Throws<ValidationException>(() => QuickWorkspaceSetup.CreateDraft("Work", catalog, selected));
    }

    [Fact]
    public void Creation_keeps_domain_validation_for_name_and_target()
    {
        Assert.Throws<ValidationException>(() => QuickWorkspaceSetup.CreateDraft(" ", [Candidate("one")], ["one"]));
        Assert.Throws<ValidationException>(() => QuickWorkspaceSetup.CreateDraft("Work", [Candidate("one", path: @"\\server\private\App.lnk")], ["one"]));
        Assert.Throws<ValidationException>(() => QuickWorkspaceSetup.CreateDraft("Work", [Candidate("one", path: @"C:\Scripts\unsafe.cmd")], ["one"]));
    }

    [Fact]
    public void Search_combines_unicode_name_and_group_terms_without_reordering_catalog()
    {
        var catalog = new[] { Candidate("one", "Редактор кода") with { Group = "Разработка" }, Candidate("two", "Редактор заметок") with { Group = "Учёба" } };
        Assert.Equal("one", Assert.Single(QuickWorkspaceSetup.Filter(catalog, "  рЕдАкТоР\tРАЗРАБОТКА  ")).Id);
        Assert.Equal(new[] { "one", "two" }, QuickWorkspaceSetup.Filter(catalog, null).Select(c => c.Id));
        Assert.Empty(QuickWorkspaceSetup.Filter(catalog, "браузер"));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("source")]
    [InlineData("oversize")]
    public void Malformed_catalog_is_a_user_validation_error(string scenario)
    {
        ApplicationCandidate[] catalog = scenario switch
        {
            "duplicate" => [Candidate("one"), Candidate("one")],
            "null" => [Candidate("one"), null!],
            "source" => [Candidate("one") with { Source = (ApplicationSource)999 }],
            _ => Enumerable.Range(0, QuickWorkspaceSetup.MaximumCatalog + 1).Select(i => Candidate(i.ToString())).ToArray()
        };
        var selected = scenario == "oversize" ? "0" : "one";
        var error = Assert.Throws<ValidationException>(() => QuickWorkspaceSetup.CreateDraft("Work", catalog, [selected]));
        Assert.Equal("QuickSelection", Assert.Single(error.Issues).Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_selection_identity_is_rejected_as_validation(string? id)
    {
        Assert.Throws<ValidationException>(() => QuickWorkspaceSetup.CreateDraft("Work", [Candidate("one")], [id!]));
    }

    public void Dispose() => Directory.Delete(directory, true);
}
