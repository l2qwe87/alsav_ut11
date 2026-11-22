using Alsav.ApiGateway.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Alsav.ApiGateway.Data;

public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options)
{
    public DbSet<StockSnapshotEntity> StockSnapshots => Set<StockSnapshotEntity>();
    public DbSet<StockMovementEntity> StockMovements => Set<StockMovementEntity>();
    public DbSet<ProductCompositionEntity> ProductCompositions => Set<ProductCompositionEntity>();
    public DbSet<ApiKeyEntity> ApiKeys => Set<ApiKeyEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockSnapshotEntity>(b =>
        {
            b.ToTable("stock_snapshots");
            b.HasKey(x => x.Id);
            b.Property(x => x.SnapshotDate).IsRequired();
            b.Property(x => x.Warehouse).HasMaxLength(200);
            b.Property(x => x.Article).HasMaxLength(200);
            b.Property(x => x.Characteristic).HasMaxLength(200);
            b.Property(x => x.Series).HasMaxLength(200);
            b.Property(x => x.Barcode).HasMaxLength(200);
            b.Property(x => x.Name).HasMaxLength(500);
            b.Property(x => x.Unit).HasMaxLength(50);
            b.Property(x => x.Type).HasMaxLength(200);
            b.HasIndex(x => new { x.Warehouse, x.Article, x.Characteristic, x.Series });
        });

        modelBuilder.Entity<StockMovementEntity>(b =>
        {
            b.ToTable("stock_movements");
            b.HasKey(x => x.Id);
            b.Property(x => x.Period).IsRequired();
            b.Property(x => x.Warehouse).HasMaxLength(200);
            b.Property(x => x.Article).HasMaxLength(200);
            b.Property(x => x.Characteristic).HasMaxLength(200);
            b.Property(x => x.Series).HasMaxLength(200);
            b.HasIndex(x => new { x.Warehouse, x.Article, x.Characteristic, x.Series, x.Period });
        });

        modelBuilder.Entity<ProductCompositionEntity>(b =>
        {
            b.ToTable("product_compositions");
            b.HasKey(x => x.Id);
            b.Property(x => x.ProductArticle).HasMaxLength(200);
            b.Property(x => x.ProductCharacteristic).HasMaxLength(200);
            b.Property(x => x.ProductSeries).HasMaxLength(200);
            b.Property(x => x.ComponentArticle).HasMaxLength(200);
            b.Property(x => x.ComponentCharacteristic).HasMaxLength(200);
            b.Property(x => x.ComponentSeries).HasMaxLength(200);
            b.Property(x => x.Warehouse).HasMaxLength(200);
            b.HasIndex(x => new { x.Warehouse, x.ProductArticle, x.ProductCharacteristic, x.ProductSeries });
            b.HasIndex(x => new { x.Warehouse, x.ComponentArticle, x.ComponentCharacteristic, x.ComponentSeries });
        });

        modelBuilder.Entity<ApiKeyEntity>(b =>
        {
            b.ToTable("api_keys");
            b.HasKey(x => x.Id);
            b.Property(x => x.Key).IsRequired().HasMaxLength(500);
            b.Property(x => x.ClientId).IsRequired().HasMaxLength(200);
            b.Property(x => x.CreatedAt).IsRequired();
            b.HasIndex(x => x.Key).IsUnique();
            b.HasIndex(x => x.ClientId);
        });
    }
}
