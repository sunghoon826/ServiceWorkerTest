using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

public class TdmsDataContext : DbContext
{
    public DbSet<TdmsFileData> TdmsFiles { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite("Data Source=tdmsData.db");
    }

    public class TdmsFileData
    {
        public int Id { get; set; } // Auto-increment ID
        public string FileName { get; set; }
        public byte[] Data { get; set; } // BLOB data
    }
}
