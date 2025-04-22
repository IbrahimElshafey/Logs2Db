using Microsoft.EntityFrameworkCore;

namespace CCSS_DSP_LogsParser;


public class LogsDbContext : DbContext
{
    public DbSet<ParsedLogLine> ParsedLogLines { get; set; }

    private readonly string _connectionString;

    public LogsDbContext(string dbName = "CCSS_DSP_Erros")
    {
        _connectionString = $"Data Source={dbName}.db";
        Database.EnsureCreated();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.EnableSensitiveDataLogging(true);
            optionsBuilder.UseSqlite(_connectionString);
        }
    }

    //protected override void OnModelCreating(ModelBuilder modelBuilder)
    //{
    //    modelBuilder.Entity<ParsedLogLine>(entity =>
    //    {
    //        entity.HasKey(e => e.Id);
    //        entity.Property(e => e.Logger).IsRequired();
    //        entity.Property(e => e.Thread);
    //        entity.Property(e => e.Numeric);
    //        entity.Property(e => e.Timestamp).IsRequired();
    //        entity.Property(e => e.Level).IsRequired();
    //        entity.Property(e => e.User);
    //        entity.Property(e => e.Machine);
    //        entity.Property(e => e.Host);
    //        entity.Property(e => e.Message);
    //    });
    //}
}
