using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SuperImoti.Web.Models;

namespace SuperImoti.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Listing> Listings => Set<Listing>();

    public DbSet<District> Districts => Set<District>();

    public DbSet<PropertyType> PropertyTypes => Set<PropertyType>();

    public DbSet<Feature> Features => Set<Feature>();

    public DbSet<ListingFeature> ListingFeatures => Set<ListingFeature>();

    public DbSet<Seller> Sellers => Set<Seller>();

    public DbSet<ListingPhoto> ListingPhotos => Set<ListingPhoto>();

    public DbSet<ListingEvent> ListingEvents => Set<ListingEvent>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<Favorite> Favorites => Set<Favorite>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no decimal type. Storing decimals as REAL lets SQL compare, sort and average prices.
        configurationBuilder.Properties<decimal>().HaveConversion<double>();

        // Enums are stored as readable text ("Sale", "Active") instead of numbers.
        configurationBuilder.Properties<DealKind>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ListingStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<Currency>().HaveConversion<string>().HaveMaxLength(10);
        configurationBuilder.Properties<ListingEventKind>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PropertyCategory>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Creates the ASP.NET Core Identity tables (users, roles, logins…).
        base.OnModelCreating(builder);

        builder.Entity<Listing>(entity =>
        {
            entity.HasIndex(l => l.ExternalId).IsUnique();
            entity.HasIndex(l => new { l.Status, l.DealKind, l.CreatedUtc });
            entity.HasIndex(l => l.Price);
            entity.HasIndex(l => l.DuplicateGroupId);

            // Covers the district statistics, so they are read from the index instead of the whole table.
            entity.HasIndex(l => new { l.Status, l.DistrictId, l.DealKind, l.PropertyTypeId, l.Price, l.PricePerSqm })
                .HasDatabaseName("IX_Listings_Stats");

            // A district or property type that still has listings cannot be deleted.
            entity.HasOne(l => l.District)
                .WithMany(d => d.Listings)
                .HasForeignKey(l => l.DistrictId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(l => l.PropertyType)
                .WithMany(t => t.Listings)
                .HasForeignKey(l => l.PropertyTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(l => l.Seller)
                .WithMany(s => s.Listings)
                .HasForeignKey(l => l.SellerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(l => l.Owner)
                .WithMany(u => u.Listings)
                .HasForeignKey(l => l.OwnerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<District>().HasIndex(d => new { d.City, d.Name }).IsUnique();
        builder.Entity<PropertyType>().HasIndex(t => t.Name).IsUnique();
        builder.Entity<Feature>().HasIndex(f => f.Name).IsUnique();

        builder.Entity<Seller>(entity =>
        {
            entity.HasIndex(s => s.Subdomain).IsUnique();
            entity.HasIndex(s => s.NormalizedName);
        });

        builder.Entity<ListingFeature>(entity =>
        {
            entity.HasKey(lf => new { lf.ListingId, lf.FeatureId });
            entity.HasIndex(lf => lf.FeatureId);

            entity.HasOne(lf => lf.Listing)
                .WithMany(l => l.Features)
                .HasForeignKey(lf => lf.ListingId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(lf => lf.Feature)
                .WithMany(f => f.Listings)
                .HasForeignKey(lf => lf.FeatureId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ListingPhoto>(entity =>
        {
            entity.HasIndex(p => new { p.ListingId, p.Position });

            entity.HasOne(p => p.Listing)
                .WithMany(l => l.Photos)
                .HasForeignKey(p => p.ListingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ListingEvent>(entity =>
        {
            entity.HasIndex(e => new { e.ListingId, e.OccurredUtc });
            entity.HasIndex(e => new { e.Kind, e.OccurredUtc });
            entity.HasIndex(e => e.SourceId).IsUnique();

            entity.HasOne(e => e.Listing)
                .WithMany(l => l.Events)
                .HasForeignKey(e => e.ListingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Comment>(entity =>
        {
            entity.HasIndex(c => new { c.ListingId, c.CreatedUtc });

            entity.HasOne(c => c.Listing)
                .WithMany(l => l.Comments)
                .HasForeignKey(c => c.ListingId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.Author)
                .WithMany(u => u.Comments)
                .HasForeignKey(c => c.AuthorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Favorite>(entity =>
        {
            entity.HasKey(f => new { f.UserId, f.ListingId });
            entity.HasIndex(f => f.ListingId);

            entity.HasOne(f => f.User)
                .WithMany(u => u.Favorites)
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(f => f.Listing)
                .WithMany(l => l.FavoritedBy)
                .HasForeignKey(f => f.ListingId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
