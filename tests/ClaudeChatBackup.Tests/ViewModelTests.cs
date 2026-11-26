using ClaudeChatBackup.Gui.ViewModels;

namespace ClaudeChatBackup.Tests;

public sealed class ViewModelTests
{
    static BackupManifest Manifest(string userProfile, params string[] cwds) => new()
    {
        UserProfile = userProfile,
        Sessions = cwds.Select((c, i) => new SessionInfo { Id = $"s{i}", Cwd = c }).ToList(),
    };

    [Fact]
    public void Suggests_missing_folders_grouped_by_parent()
    {
        var m = Manifest(@"C:\Users\ana", @"D:\Code\A", @"D:\Code\B", @"E:\Solto", @"C:\Users\ana\repo", @"F:\Existe\X");
        var suggestions = BackupsViewModel.SuggestMappings(m, dir => dir.StartsWith(@"F:\"));
        Assert.Equal([@"D:\Code", @"E:\Solto"], suggestions);
    }

    [Fact]
    public void Existing_parent_is_not_suggested_only_the_missing_projects()
    {
        var m = Manifest(@"C:\Users\ana", @"D:\Code\A", @"D:\Code\B", @"D:\Code\C");
        var suggestions = BackupsViewModel.SuggestMappings(m, dir => dir is @"D:\Code" or @"D:\Code\C");
        Assert.Equal([@"D:\Code\A", @"D:\Code\B"], suggestions);
    }

    [Fact]
    public void Suggestion_keeps_the_broadest_folder()
    {
        var m = Manifest(@"C:\Users\ana", @"D:\Code\A\Sub", @"D:\Code\B");
        Assert.Equal([@"D:\Code"], BackupsViewModel.SuggestMappings(m, _ => false));
    }

    [Fact]
    public void Filter_matches_title_folder_and_id_ignoring_case()
    {
        SessionInfo[] sessions =
        [
            new() { Id = "local_abc", Title = "Lineage2", Cwd = @"E:\lineage2" },
            new() { Id = "local_def", Title = "ERP - Server", Cwd = @"D:\Work\ERP" },
        ];
        Assert.Single(SessionsViewModel.Filter(sessions, "lineage"));
        Assert.Single(SessionsViewModel.Filter(sessions, "work"));
        Assert.Single(SessionsViewModel.Filter(sessions, "DEF"));
        Assert.Equal(2, SessionsViewModel.Filter(sessions, "  ").Count);
    }

    [Fact]
    public void Invalid_zip_is_listed_but_cannot_be_restored()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccb-tests", $"vm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "lixo.zip"), "não é zip");
            var entry = Assert.Single(BackupsViewModel.List([dir]));
            Assert.False(entry.CanRestore);
            Assert.Equal("Arquivo inválido", BackupsViewModel.DescribeContent(entry));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(500, "500 B")]
    [InlineData(2048, "2,0 KB")]
    [InlineData(3 * 1024 * 1024, "3,0 MB")]
    public void Size_is_formatted_in_pt_br(long bytes, string expected) => Assert.Equal(expected, Formatting.Size(bytes));
}
