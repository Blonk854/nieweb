using Xunit;

namespace Nieweb.DataSources.Sql.Tests;

public sealed class SqlGuardsTests
{
    [Fact]
    public void IsolationPrelude_SetsReadUncommittedAndNoCount()
    {
        Assert.Contains("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED",
            SqlGuards.IsolationPrelude, StringComparison.Ordinal);
        Assert.Contains("SET NOCOUNT ON", SqlGuards.IsolationPrelude, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("SELECT Panel_Id FROM dbo.PANELS WITH (NOLOCK) WHERE Panel_Numeric_Date >= @s")]
    [InlineData("SELECT COUNT(*) FROM dbo.CARDS WHERE Card_Id = @id -- comment")]
    [InlineData("SELECT MAX(Panel_Numeric_Date) FROM dbo.PANELS WITH (NOLOCK);")]
    public void EnsureReadOnly_AcceptsPlainSelects(string sql)
    {
        // Should not throw.
        SqlGuards.EnsureReadOnly(sql);
    }

    [Theory]
    [InlineData("INSERT INTO PANELS VALUES (1)")]
    [InlineData("UPDATE PANELS SET Panel_Status = 1")]
    [InlineData("DELETE FROM PANELS WHERE Panel_Id = 1")]
    [InlineData("DROP TABLE PANELS")]
    [InlineData("ALTER TABLE PANELS ADD COLUMN foo INT")]
    [InlineData("TRUNCATE TABLE PANELS")]
    [InlineData("MERGE INTO PANELS USING src ON ...")]
    [InlineData("EXEC sp_helptext 'foo'")]
    [InlineData("EXECUTE sp_who")]
    [InlineData("GRANT SELECT ON PANELS TO svc_reader")]
    [InlineData("REVOKE SELECT ON PANELS FROM svc_reader")]
    [InlineData("CREATE INDEX ix_foo ON PANELS(Panel_Id)")]
    // Case-insensitive.
    [InlineData("insert into panels values (1)")]
    [InlineData("Update panels Set x=1")]
    // Embedded write keyword inside otherwise-selecty SQL.
    [InlineData("SELECT 1; UPDATE PANELS SET x=1")]
    public void EnsureReadOnly_RejectsWriteKeywords(string sql)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SqlGuards.EnsureReadOnly(sql));
        Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
