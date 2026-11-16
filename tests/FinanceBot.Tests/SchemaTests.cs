using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// SC-4: every user-owned table carries user_id. Guards future migrations, not just today's schema.
[Collection("Api")]
public sealed class SchemaTests(ApiFactory f)
{
    [Fact]
    public async Task AllUserDataTablesHaveUserId()
    {
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var ct = TestContext.Current.CancellationToken;

        // Not vacuous: the schema really has the user data table
        var existing = await db.Database.SqlQueryRaw<string>(
            "SELECT table_name AS \"Value\" FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_name = 'transactions'").ToListAsync(ct);
        Assert.Single(existing);

        var withoutUserId = await db.Database.SqlQueryRaw<string>(
            "SELECT t.table_name AS \"Value\" FROM information_schema.tables t " +
            "WHERE t.table_schema = 'public' AND t.table_type = 'BASE TABLE' " +
            "AND t.table_name NOT IN ('users') AND t.table_name NOT LIKE '\\_\\_%' " +
            "AND NOT EXISTS (SELECT 1 FROM information_schema.columns c " +
            "WHERE c.table_schema = 'public' AND c.table_name = t.table_name AND c.column_name = 'user_id')")
            .ToListAsync(ct);
        Assert.Empty(withoutUserId);
    }
}
