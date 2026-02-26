using IISPSupdate.Models;
using Microsoft.EntityFrameworkCore;

namespace IISPSupdate.Data;

/// <summary>
/// Primary EF Core database context for the Windows Update orchestration tool.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectServer> ProjectServers => Set<ProjectServer>();
    public DbSet<ServerScanResult> ServerScanResults => Set<ServerScanResult>();
    public DbSet<UpdateCandidate> UpdateCandidates => Set<UpdateCandidate>();
    public DbSet<ServerSelection> ServerSelections => Set<ServerSelection>();
    public DbSet<ProjectRun> ProjectRuns => Set<ProjectRun>();
    public DbSet<ProjectRunServer> ProjectRunServers => Set<ProjectRunServer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Project>()
            .HasMany(p => p.Servers)
            .WithOne(s => s.Project!)
            .HasForeignKey(s => s.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProjectServer>()
            .HasMany(s => s.ScanResults)
            .WithOne(r => r.ProjectServer!)
            .HasForeignKey(r => r.ProjectServerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ServerScanResult>()
            .HasMany(r => r.Candidates)
            .WithOne(c => c.ServerScanResult!)
            .HasForeignKey(c => c.ServerScanResultId)
            .OnDelete(DeleteBehavior.Cascade);

        // Avoid SQL Server multiple cascade path issues by using Restrict on run-related FKs.
        modelBuilder.Entity<ProjectRun>()
            .HasMany(r => r.Servers)
            .WithOne(rs => rs.ProjectRun!)
            .HasForeignKey(rs => rs.ProjectRunId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProjectRunServer>()
            .HasOne(rs => rs.ProjectServer)
            .WithMany()
            .HasForeignKey(rs => rs.ProjectServerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProjectRunServer>()
            .HasIndex(rs => new { rs.ProjectRunId, rs.ProjectServerId })
            .IsUnique();
    }
}

