namespace RepoPolicy.Tests;

public class RegisterParsingTests
{
    private const string Header = "| ID | Tool | Rule | Scope | Reason | Owner | Expires |\n|---|---|---|---|---|---|---|\n";

    [Fact]
    public void Parses_rows_of_the_register_table()
    {
        var markdown = "# Exceptions\n\nSome text.\n\n" + Header +
                       "| EX-001 | trivy | CVE-2026-0001 | image | No fix yet | @santorest | 2026-12-31 |\n" +
                       "| EX-002 | zap | 10049 | /health | Health is cacheable by design | @santorest | 2027-01-15 |\n\nAfter the table.\n";

        var entries = ExceptionsRegister.Parse(markdown);

        Assert.Equal(2, entries.Count);
        Assert.Equal(new RegisterEntry("EX-001", "trivy", "CVE-2026-0001", "image", "No fix yet", "@santorest", new DateOnly(2026, 12, 31)), entries[0]);
        Assert.Equal("10049", entries[1].Rule);
    }

    [Fact]
    public void A_header_only_table_is_an_empty_register() => Assert.Empty(ExceptionsRegister.Parse(Header));

    [Fact]
    public void A_row_with_the_wrong_number_of_columns_names_its_line()
    {
        var ex = Assert.Throws<FormatException>(() => ExceptionsRegister.Parse(Header + "| EX-001 | trivy | CVE-1 | image | reason | 2026-12-31 |\n"));
        Assert.Contains("line 3", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-13-01")]
    [InlineData("31/12/2026")]
    [InlineData("")]
    public void A_bad_expiry_date_names_its_line(string date)
    {
        var ex = Assert.Throws<FormatException>(() =>
            ExceptionsRegister.Parse(Header + $"| EX-001 | trivy | CVE-1 | image | reason | @me | {date} |\n"));
        Assert.Contains("line 3", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_without_the_register_table_is_rejected() =>
        Assert.Throws<FormatException>(() => ExceptionsRegister.Parse("# Exceptions\n\nNo table here.\n"));
}
