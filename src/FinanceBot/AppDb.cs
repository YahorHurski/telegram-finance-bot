using Microsoft.EntityFrameworkCore;
using NodaTime;

public sealed class User
{
    public const long OwnerId = 1;   // shortcut: single owner until Phase 7 (registration)
    public long Id { get; set; }
}

public sealed class Transaction
{
    public Guid Id { get; set; }                 // UUIDv7: time-ordered
    public long UserId { get; set; }             // SC-4: every user-owned table carries user_id from the first migration
    public decimal? Amount { get; set; }         // null = amount not parsed (D-03), excluded from the weekly limit
    public string? AmountRaw { get; set; }
    public string? Merchant { get; set; }
    public string? Card { get; set; }
    public Instant OccurredAt { get; set; }      // purchase time, decides the week (D-08)
    public Instant ReceivedAt { get; set; }
    public string RawBody { get; set; } = "";    // text, not jsonb: malformed JSON must still be stored (D-07)
}

public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // No identity on users.id: a seeded id 1 would not advance the sequence and break Phase 7 inserts
        b.Entity<User>(e =>
        {
            e.Property(u => u.Id).ValueGeneratedNever();
            e.HasData(new User { Id = User.OwnerId });
        });
        b.Entity<Transaction>(e =>
        {
            e.Property(t => t.Amount).HasPrecision(12, 2);
            e.HasOne<User>().WithMany().HasForeignKey(t => t.UserId);
            e.HasIndex(t => new { t.UserId, t.OccurredAt });
        });
    }
}
