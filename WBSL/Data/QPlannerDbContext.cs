using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using WBSL.Models;

namespace WBSL.Data;

public partial class QPlannerDbContext : DbContext
{
    public QPlannerDbContext(DbContextOptions<QPlannerDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<external_account> external_accounts { get; set; }

    public virtual DbSet<product> products { get; set; }

    public virtual DbSet<user> users { get; set; }

    public virtual DbSet<wbcharacteristic> wbcharacteristics { get; set; }

    public virtual DbSet<wbcursor> wbcursors { get; set; }

    public virtual DbSet<wbdimension> wbdimensions { get; set; }

    public virtual DbSet<wbphoto> wbphotos { get; set; }

    public virtual DbSet<wbproductcard> wbproductcards { get; set; }

    public virtual DbSet<wbsize> wbsizes { get; set; }

    public virtual DbSet<wbsku> wbskus { get; set; }

    public virtual DbSet<wildberries_category> wildberries_categories { get; set; }

    public virtual DbSet<wildberries_parrent_category> wildberries_parrent_categories { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pgcrypto");

        modelBuilder.Entity<external_account>(entity =>
        {
            entity.HasKey(e => e.id).HasName("external_accounts_pkey");

            entity.Property(e => e.added_at)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone");

            entity.HasOne(d => d.user).WithMany(p => p.external_accounts)
                .HasForeignKey(d => d.user_id)
                .HasConstraintName("fk_user");
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

        modelBuilder.Entity<wbcharacteristic>(entity =>
        {
            entity.HasKey(e => e.id).HasName("wbcharacteristic_pkey");

            entity.ToTable("wbcharacteristic");

            entity.Property(e => e.id).ValueGeneratedNever();
            entity.Property(e => e.value).HasColumnType("jsonb");

            entity.HasOne(d => d.wbproductcardnm).WithMany(p => p.wbcharacteristics)
                .HasForeignKey(d => d.wbproductcardnmid)
                .HasConstraintName("wbcharacteristic_wbproductcardnmid_fkey");
        });

        modelBuilder.Entity<wbcursor>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("wbcursor");

            entity.Property(e => e.updatedat).HasColumnType("timestamp without time zone");
        });

        modelBuilder.Entity<wbdimension>(entity =>
        {
            entity.HasKey(e => e.wbproductcardnmid).HasName("wbdimensions_pkey");

            entity.Property(e => e.wbproductcardnmid).ValueGeneratedNever();

            entity.HasOne(d => d.wbproductcardnm).WithOne(p => p.wbdimension)
                .HasForeignKey<wbdimension>(d => d.wbproductcardnmid)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("wbdimensions_wbproductcardnmid_fkey");
        });

        modelBuilder.Entity<wbphoto>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("wbphoto");

            entity.HasOne(d => d.wbproductcardnm).WithMany()
                .HasForeignKey(d => d.wbproductcardnmid)
                .HasConstraintName("wbphoto_wbproductcardnmid_fkey");
        });

        modelBuilder.Entity<wbproductcard>(entity =>
        {
            entity.HasKey(e => e.nmid).HasName("wbproductcard_pkey");

            entity.ToTable("wbproductcard");

            entity.Property(e => e.nmid).ValueGeneratedNever();
            entity.Property(e => e.createdat).HasColumnType("timestamp without time zone");
            entity.Property(e => e.updatedat).HasColumnType("timestamp without time zone");
        });

        modelBuilder.Entity<wbsize>(entity =>
        {
            entity.HasKey(e => e.chrtid).HasName("wbsize_pkey");

            entity.ToTable("wbsize");

            entity.Property(e => e.chrtid).ValueGeneratedNever();
            entity.Property(e => e.wbsize1).HasColumnName("wbsize");

            entity.HasOne(d => d.wbproductcardnm).WithMany(p => p.wbsizes)
                .HasForeignKey(d => d.wbproductcardnmid)
                .HasConstraintName("wbsize_wbproductcardnmid_fkey");
        });

        modelBuilder.Entity<wbsku>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("wbsku");

            entity.HasOne(d => d.wbsizechrt).WithMany()
                .HasForeignKey(d => d.wbsizechrtid)
                .HasConstraintName("wbsku_wbsizechrtid_fkey");
        });

        modelBuilder.Entity<wildberries_category>(entity =>
        {
            entity.HasKey(e => e.id).HasName("wildberries_categories_pkey");

            entity.HasOne(d => d.parent).WithMany(p => p.wildberries_categories)
                .HasForeignKey(d => d.parent_id)
                .HasConstraintName("fk_parent_category");
        });

        modelBuilder.Entity<wildberries_parrent_category>(entity =>
        {
            entity.HasKey(e => e.id).HasName("wildberries_parrent_categories_pkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
