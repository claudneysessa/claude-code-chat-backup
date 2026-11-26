
namespace ClaudeChatBackup.Tests;

public sealed class PathRemapperTests
{
    [Theory]
    [InlineData(@"D:\Work\SERVER-APP", "D--Work-SERVER-APP")]
    [InlineData(@"E:\ana\Meu Projeto", "E--ana-Meu-Projeto")]
    [InlineData(@"d:\Work\ERPAPP", "d--Work-ERPAPP")]
    public void Project_folder_encoding_matches_claude_code(string cwd, string expected) =>
        Assert.Equal(expected, DataLayout.EncodeProjectDir(cwd));

    [Theory]
    [InlineData(@"C:\Users\ana", @"C:\Users\ana\.claude", @"C:\Users\bia\.claude")]
    [InlineData(@"C:\Users\ana", @"c:\users\ana", @"C:\Users\bia")]
    [InlineData(@"C:\Users\ana", @"C:\Users\anabela", null)]
    [InlineData(@"C:\Users\ana", @"veja C:\Users\ana", null)]
    [InlineData(@"C:\Users\ana\", @"C:\Users\ana\x", @"C:\Users\bia\x")]
    public void Maps_only_whole_path_prefixes(string from, string value, string? expected)
    {
        var r = new PathRemapper([new PathMapping(from, @"C:\Users\bia")]);
        Assert.Equal(expected, r.MapPath(value));
    }

    [Fact]
    public void Longest_mapping_wins()
    {
        var r = new PathRemapper([new PathMapping(@"D:\A", @"E:\A"), new PathMapping(@"D:\A\B", @"F:\B")]);
        Assert.Equal(@"F:\B\c", r.MapPath(@"D:\A\B\c"));
        Assert.Equal(@"E:\A\c", r.MapPath(@"D:\A\c"));
    }

    [Fact]
    public void Untouched_json_lines_keep_exact_bytes()
    {
        var r = new PathRemapper([new PathMapping(@"D:\A", @"E:\A")]);
        var input = "{\"a\":  1, \"u\":\"\\u00e7\"}\n{\"cwd\":\"D:\\\\A\"}\n";
        using var src = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(input));
        using var dst = new MemoryStream();
        r.RemapJsonLinesFile(src, dst, ids: false);
        Assert.Equal("{\"a\":  1, \"u\":\"\\u00e7\"}\n{\"cwd\":\"E:\\\\A\"}\n", System.Text.Encoding.UTF8.GetString(dst.ToArray()));
    }

    [Theory]
    [InlineData("dotclaude/../x")]
    [InlineData("other/x")]
    [InlineData("dotclaude/C:/x")]
    [InlineData("dotclaude//x")]
    public void Unsafe_archive_paths_are_rejected(string path) => Assert.False(DataLayout.IsSafeArchivePath(path));
}
