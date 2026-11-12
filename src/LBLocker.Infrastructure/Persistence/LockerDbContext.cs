using LBLocker.Domain.Parcels;
using LBLocker.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LBLocker.Infrastructure.Persistence;

public sealed class LockerDbContext(DbContextOptions<LockerDbContext> options) : DbContext(options)
{
    public DbSet<Parcel> Parcels => Set<Parcel>();
    public DbSet<Allocation> Allocations => Set<Allocation>();
    public DbSet<RecipientRecord> Recipients => Set<RecipientRecord>();
    public DbSet<AllocationCredentialRecord> AllocationCredentials => Set<AllocationCredentialRecord>();
    public DbSet<ImportedEventRecord> ImportedEvents => Set<ImportedEventRecord>();
    public DbSet<NotificationDispatchRecord> NotificationDispatches => Set<NotificationDispatchRecord>();
    public DbSet<ParcelEventRecord> ParcelEvents => Set<ParcelEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Parcel>(entity =>
        {
            entity.ToTable("Parcels");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ExternalParcelId).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.ExternalParcelId).IsUnique();
            entity.Property(x => x.RecipientId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Size).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.TargetLockerId).HasMaxLength(100);
            entity.Property(x => x.RegisteredAt).IsRequired();

            entity.HasMany(x => x.Allocations)
                .WithOne()
                .HasForeignKey("ParcelId")
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            entity.Navigation(x => x.Allocations)
                .HasField("_allocations")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<Allocation>(entity =>
        {
            entity.ToTable("Allocations", table =>
            {
                table.HasCheckConstraint("CK_Allocations_ExpiryAfterAllocation", "\"ExpiresAt\" > \"AllocatedAt\"");
                table.HasCheckConstraint("CK_Allocations_ReleaseAfterAllocation", "\"ReleasedAt\" IS NULL OR \"ReleasedAt\" >= \"AllocatedAt\"");
            });
            entity.HasKey(x => x.Id);

            // ParcelId is an explicit shadow FK: the domain object only exposes
            // the owning parcel through Parcel.Allocations.
            entity.Property<Guid>("ParcelId").IsRequired();
            entity.Property(x => x.LockerId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CompartmentId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.AllocatedAt).IsRequired();
            entity.Property(x => x.ExpiresAt).IsRequired();
            entity.Property(x => x.ReleasedAt);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Ignore(x => x.IsReleased);

            entity.HasIndex("ParcelId", nameof(Allocation.ReleasedAt));
            entity.HasIndex("ParcelId").IsUnique().HasFilter("\"ReleasedAt\" IS NULL");
            entity.HasIndex(x => new { x.LockerId, x.CompartmentId }).IsUnique()
                .HasFilter("\"ReleasedAt\" IS NULL");
        });

        modelBuilder.Entity<RecipientRecord>(entity =>
        {
            entity.ToTable("Recipients");
            entity.HasKey(x => x.ParcelId);
            entity.Property(x => x.ExternalRecipientId).HasMaxLength(100).IsRequired();
            entity.HasOne<Parcel>().WithOne()
                .HasForeignKey<RecipientRecord>(x => x.ParcelId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.PhoneNumber).HasMaxLength(40);
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.Property(x => x.PostalCode).HasMaxLength(20);
            entity.HasIndex(x => x.PersonalDataErasedAt);
        });

        modelBuilder.Entity<AllocationCredentialRecord>(entity =>
        {
            entity.ToTable("AllocationCredentials");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.AllocationId).IsUnique();
            entity.Property(x => x.EncryptedPin).IsRequired();
            entity.Property(x => x.KeyVersion).HasMaxLength(50).IsRequired();
            entity.HasOne<Allocation>().WithOne()
                .HasForeignKey<AllocationCredentialRecord>(x => x.AllocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ImportedEventRecord>(entity =>
        {
            entity.ToTable("ImportedEvents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventId).HasMaxLength(150).IsRequired();
            entity.HasIndex(x => x.EventId).IsUnique();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.FailureReason).HasMaxLength(500);
            entity.Property(x => x.ParcelExternalId).HasMaxLength(100);
        });

        modelBuilder.Entity<NotificationDispatchRecord>(entity =>
        {
            entity.ToTable("NotificationDispatches");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.ExternalDispatchId).HasMaxLength(150);
            entity.Property(x => x.ErrorCode).HasMaxLength(100);
            entity.HasIndex(x => new { x.AllocationId, x.AttemptedAt });
            entity.HasIndex(x => x.ExternalDispatchId).IsUnique()
                .HasFilter("\"ExternalDispatchId\" IS NOT NULL");
            entity.HasOne<Allocation>().WithMany()
                .HasForeignKey(x => x.AllocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ParcelEventRecord>(entity =>
        {
            entity.ToTable("ParcelEvents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasIndex(x => new { x.ParcelId, x.OccurredAt });
            entity.HasOne<Parcel>().WithMany()
                .HasForeignKey(x => x.ParcelId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}