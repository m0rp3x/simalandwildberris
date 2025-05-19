using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using WBSL.Data.Models;
using WBSL.Models;

namespace WBSL.Data;

public partial class QPlannerDbContext : DbContext
{
    public QPlannerDbContext(DbContextOptions<QPlannerDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<WbCharacteristic> WbCharacteristics { get; set; }

    public virtual DbSet<WbCursor> WbCursors { get; set; }

    public virtual DbSet<WbDimension> WbDimensions { get; set; }

    public virtual DbSet<WbPhoto> WbPhotos { get; set; }

    public virtual DbSet<WbProductCard> WbProductCards { get; set; }

    public virtual DbSet<WbProductCardCharacteristic> WbProductCardCharacteristics { get; set; }

    public virtual DbSet<WbSize> WbSizes { get; set; }

    public virtual DbSet<external_account> external_accounts { get; set; }

    public virtual DbSet<product> products { get; set; }

    public virtual DbSet<product_attribute> product_attributes { get; set; }

    public virtual DbSet<user> users { get; set; }

    public virtual DbSet<wildberries_category> wildberries_categories { get; set; }

    public virtual DbSet<wildberries_parrent_category> wildberries_parrent_categories { get; set; }
    public virtual DbSet<BalanceUpdateRule> BalanceUpdateRules { get; set; }
    
    public virtual DbSet<MarginRule> MarginRules { get; set; }
    public virtual DbSet<OrderEntity> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pgcrypto");

        modelBuilder.Entity<WbCharacteristic>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("WbCharacteristic_pkey");

            entity.ToTable("WbCharacteristic");

            entity.Property(e => e.Value).HasColumnType("jsonb");
        });
        
        modelBuilder.Entity<MarginRule>(entity =>
        {
            entity.ToTable("margin_rules");
            
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .UseIdentityAlwaysColumn();
            
            entity.Property(e => e.PriceFrom)
                .HasColumnName("price_from")
                .HasColumnType("numeric")
                .IsRequired();

            entity.Property(e => e.PriceTo)
                .HasColumnName("price_to")
                .HasColumnType("numeric")
                .IsRequired();

            entity.Property(e => e.RatePct)
                .HasColumnName("rate_pct")
                .HasPrecision(5, 2)
                .IsRequired();
        });

        modelBuilder.Entity<WbCursor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("WbCursor_pkey");

            entity.ToTable("WbCursor");

            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone");
        });

        modelBuilder.Entity<WbDimension>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("WbDimensions_pkey");
        });
        modelBuilder.Entity<BalanceUpdateRule>().ToTable("balance_update_rules", t => t.ExcludeFromMigrations());


        modelBuilder.Entity<WbPhoto>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("WbPhoto_pkey");

            entity.ToTable("WbPhoto");

            entity.HasOne(d => d.WbProductCardNm).WithMany(p => p.WbPhotos)
                .HasForeignKey(d => d.WbProductCardNmID)
                .HasConstraintName("WbPhoto_WbProductCardNmID_fkey");
        });

        modelBuilder.Entity<WbProductCard>(entity =>
        {
            entity.HasKey(e => e.NmID).HasName("WbProductCard_pkey");

            entity.ToTable("WbProductCard");

            entity.Property(e => e.NmID).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone");

            entity.HasOne(d => d.externalaccount).WithMany(p => p.WbProductCards)
                .HasForeignKey(d => d.externalaccount_id)
                .HasConstraintName("wbproductcard_external_accounts_id_fk");

            entity.HasMany(d => d.Dimensions).WithMany(p => p.ProductNms)
                .UsingEntity<Dictionary<string, object>>(
                    "WbProductCardDimension",
                    r => r.HasOne<WbDimension>().WithMany()
                        .HasForeignKey("DimensionsId")
                        .HasConstraintName("WbProductCardDimensions_DimensionsId_fkey"),
                    l => l.HasOne<WbProductCard>().WithMany()
                        .HasForeignKey("ProductNmID")
                        .HasConstraintName("WbProductCardDimensions_ProductNmID_fkey"),
                    j =>
                    {
                        j.HasKey("ProductNmID", "DimensionsId").HasName("WbProductCardDimensions_pkey");
                        j.ToTable("WbProductCardDimensions");
                    });

            entity.HasMany(d => d.SizeChrts).WithMany(p => p.ProductNms)
                .UsingEntity<Dictionary<string, object>>(
                    "WbProductCardSize",
                    r => r.HasOne<WbSize>().WithMany()
                        .HasForeignKey("SizeChrtID")
                        .HasConstraintName("WbProductCardSizes_SizeChrtID_fkey"),
                    l => l.HasOne<WbProductCard>().WithMany()
                        .HasForeignKey("ProductNmID")
                        .HasConstraintName("WbProductCardSizes_ProductNmID_fkey"),
                    j =>
                    {
                        j.HasKey("ProductNmID", "SizeChrtID").HasName("WbProductCardSizes_pkey");
                        j.ToTable("WbProductCardSizes");
                    });
        });

        modelBuilder.Entity<WbProductCardCharacteristic>(entity =>
        {
            entity.HasKey(e => new { e.ProductNmID, e.CharacteristicId }).HasName("WbProductCardCharacteristics_pkey");

            entity.Property(e => e.Value).HasColumnType("character varying");

            entity.HasOne(d => d.Characteristic).WithMany(p => p.WbProductCardCharacteristics)
                .HasForeignKey(d => d.CharacteristicId)
                .HasConstraintName("WbProductCardCharacteristics_CharacteristicId_fkey");

            entity.HasOne(d => d.ProductNm).WithMany(p => p.WbProductCardCharacteristics)
                .HasForeignKey(d => d.ProductNmID)
                .HasConstraintName("WbProductCardCharacteristics_ProductNmID_fkey");
        });

        modelBuilder.Entity<WbSize>(entity =>
        {
            entity.HasKey(e => e.ChrtID).HasName("WbSize_pkey");

            entity.ToTable("WbSize");

            entity.Property(e => e.ChrtID).ValueGeneratedNever();
            entity.Property(e => e.Value).HasColumnType("character varying");
            entity.Property(e => e.WbSize1).HasColumnName("WbSize");
        });


        modelBuilder.Entity<BalanceUpdateRule>(entity =>
        {
            entity.ToTable("balance_update_rules"); // Название таблицы

            entity.HasKey(e => e.Id)
                .HasName("balance_update_rules_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("id");

            entity.Property(e => e.FromStock)
                .HasColumnName("from_stock");

            entity.Property(e => e.ToStock)
                .HasColumnName("to_stock");

            entity.Property(e => e.UpdateInterval)
                .HasColumnName("update_interval")
                .HasConversion(
                    v => v,          // C# TimeSpan -> БД INTERVAL
                    v => v           // БД INTERVAL -> C# TimeSpan
                );

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp without time zone")
                .HasDefaultValueSql("now()");
        });
        

        modelBuilder.Entity<external_account>(entity =>
        {
            entity.HasKey(e => e.id).HasName("external_accounts_pkey");

            entity.Property(e => e.added_at)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone");

            entity.HasOne(d => d.user).WithMany(p => p.external_accounts)
                .HasForeignKey(d => d.user_id)
                .HasConstraintName("external_accounts_user_id_fkey");
        });

        modelBuilder.Entity<product>(entity =>
        {
            entity.HasKey(e => e.sid).HasName("products_pkey");

            entity.Property(e => e.sid).ValueGeneratedNever();
            entity.Property(e => e.box_depth)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("0");
            entity.Property(e => e.box_height)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("0");
            entity.Property(e => e.box_width)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("0");
            entity.Property(e => e.depth)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("0");
            entity.Property(e => e.height)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("0");
            entity.Property(e => e.price).HasPrecision(10, 2);
            entity.Property(e => e.qty_multiplier).HasDefaultValue(1);
            entity.Property(e => e.weight)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("0");
            entity.Property(e => e.wholesale_price).HasPrecision(10, 2);
            entity.Property(e => e.width)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("0");
            entity.Property(e => e.country_name)
                .HasColumnName("country_name")
                .HasMaxLength(255)
                .IsRequired(false); 
        });

        modelBuilder.Entity<product_attribute>(entity =>
        {
            entity.HasKey(e => e.id).HasName("product_attributes_pkey");

            entity.Property(e => e.created_at)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone");

            entity.HasOne(d => d.product_s).WithMany(p => p.product_attributes)
                .HasForeignKey(d => d.product_sid)
                .HasConstraintName("product_attributes_product_sid_fkey");
        });

        modelBuilder.Entity<user>(entity =>
        {
            entity.HasKey(e => e.id).HasName("users_pkey");

            entity.HasIndex(e => e.user_name, "users_user_name_key").IsUnique();

            entity.Property(e => e.id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.created_at)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone");
        });

        modelBuilder.Entity<wildberries_category>(entity =>
        {
            entity.HasKey(e => e.id).HasName("wildberries_categories_pkey");

            entity.HasOne(d => d.parent).WithMany(p => p.wildberries_categories)
                .HasForeignKey(d => d.parent_id)
                .HasConstraintName("wildberries_categories_parent_id_fkey");
        });

        modelBuilder.Entity<wildberries_parrent_category>(entity =>
        {
            entity.HasKey(e => e.id).HasName("wildberries_parrent_categories_pkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
