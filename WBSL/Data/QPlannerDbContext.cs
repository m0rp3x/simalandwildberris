using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
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

    public virtual DbSet<_lock> _locks { get; set; }

    public virtual DbSet<aggregatedcounter> aggregatedcounters { get; set; }

    public virtual DbSet<balance_update_rule> balance_update_rules { get; set; }

    public virtual DbSet<counter> counters { get; set; }

    public virtual DbSet<external_account> external_accounts { get; set; }

    public virtual DbSet<hash> hashes { get; set; }

    public virtual DbSet<job> jobs { get; set; }

    public virtual DbSet<jobparameter> jobparameters { get; set; }

    public virtual DbSet<jobqueue> jobqueues { get; set; }

    public virtual DbSet<list> lists { get; set; }

    public virtual DbSet<product> products { get; set; }

    public virtual DbSet<product_attribute> product_attributes { get; set; }

    public virtual DbSet<schema> schemas { get; set; }

    public virtual DbSet<server> servers { get; set; }

    public virtual DbSet<set> sets { get; set; }

    public virtual DbSet<state> states { get; set; }

    public virtual DbSet<user> users { get; set; }

    public virtual DbSet<wildberries_category> wildberries_categories { get; set; }

    public virtual DbSet<wildberries_parrent_category> wildberries_parrent_categories { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pgcrypto");

        modelBuilder.Entity<WbCharacteristic>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("WbCharacteristic_pkey");

            entity.ToTable("WbCharacteristic");

            entity.Property(e => e.Value).HasColumnType("jsonb");
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

        modelBuilder.Entity<_lock>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("lock", "hangfire");

            entity.HasIndex(e => e.resource, "lock_resource_key").IsUnique();

            entity.Property(e => e.updatecount).HasDefaultValue(0);
        });

        modelBuilder.Entity<aggregatedcounter>(entity =>
        {
            entity.HasKey(e => e.id).HasName("aggregatedcounter_pkey");

            entity.ToTable("aggregatedcounter", "hangfire");

            entity.HasIndex(e => e.key, "aggregatedcounter_key_key").IsUnique();
        });

        modelBuilder.Entity<balance_update_rule>(entity =>
        {
            entity.HasKey(e => e.id).HasName("balance_update_rules_pkey");

            entity.Property(e => e.created_at)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone");
        });

        modelBuilder.Entity<counter>(entity =>
        {
            entity.HasKey(e => e.id).HasName("counter_pkey");

            entity.ToTable("counter", "hangfire");

            entity.HasIndex(e => e.expireat, "ix_hangfire_counter_expireat");

            entity.HasIndex(e => e.key, "ix_hangfire_counter_key");
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

        modelBuilder.Entity<hash>(entity =>
        {
            entity.HasKey(e => e.id).HasName("hash_pkey");

            entity.ToTable("hash", "hangfire");

            entity.HasIndex(e => new { e.key, e.field }, "hash_key_field_key").IsUnique();

            entity.HasIndex(e => e.expireat, "ix_hangfire_hash_expireat");

            entity.Property(e => e.updatecount).HasDefaultValue(0);
        });

        modelBuilder.Entity<job>(entity =>
        {
            entity.HasKey(e => e.id).HasName("job_pkey");

            entity.ToTable("job", "hangfire");

            entity.HasIndex(e => e.expireat, "ix_hangfire_job_expireat");

            entity.HasIndex(e => e.statename, "ix_hangfire_job_statename");

            entity.HasIndex(e => e.statename, "ix_hangfire_job_statename_is_not_null").HasFilter("(statename IS NOT NULL)");

            entity.Property(e => e.arguments).HasColumnType("jsonb");
            entity.Property(e => e.invocationdata).HasColumnType("jsonb");
            entity.Property(e => e.updatecount).HasDefaultValue(0);
        });

        modelBuilder.Entity<jobparameter>(entity =>
        {
            entity.HasKey(e => e.id).HasName("jobparameter_pkey");

            entity.ToTable("jobparameter", "hangfire");

            entity.HasIndex(e => new { e.jobid, e.name }, "ix_hangfire_jobparameter_jobidandname");

            entity.Property(e => e.updatecount).HasDefaultValue(0);

            entity.HasOne(d => d.job).WithMany(p => p.jobparameters)
                .HasForeignKey(d => d.jobid)
                .HasConstraintName("jobparameter_jobid_fkey");
        });

        modelBuilder.Entity<jobqueue>(entity =>
        {
            entity.HasKey(e => e.id).HasName("jobqueue_pkey");

            entity.ToTable("jobqueue", "hangfire");

            entity.HasIndex(e => new { e.fetchedat, e.queue, e.jobid }, "ix_hangfire_jobqueue_fetchedat_queue_jobid").HasNullSortOrder(new[] { NullSortOrder.NullsFirst, NullSortOrder.NullsLast, NullSortOrder.NullsLast });

            entity.HasIndex(e => new { e.jobid, e.queue }, "ix_hangfire_jobqueue_jobidandqueue");

            entity.HasIndex(e => new { e.queue, e.fetchedat }, "ix_hangfire_jobqueue_queueandfetchedat");

            entity.Property(e => e.updatecount).HasDefaultValue(0);
        });

        modelBuilder.Entity<list>(entity =>
        {
            entity.HasKey(e => e.id).HasName("list_pkey");

            entity.ToTable("list", "hangfire");

            entity.HasIndex(e => e.expireat, "ix_hangfire_list_expireat");

            entity.Property(e => e.updatecount).HasDefaultValue(0);
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

        modelBuilder.Entity<schema>(entity =>
        {
            entity.HasKey(e => e.version).HasName("schema_pkey");

            entity.ToTable("schema", "hangfire");

            entity.Property(e => e.version).ValueGeneratedNever();
        });

        modelBuilder.Entity<server>(entity =>
        {
            entity.HasKey(e => e.id).HasName("server_pkey");

            entity.ToTable("server", "hangfire");

            entity.Property(e => e.data).HasColumnType("jsonb");
            entity.Property(e => e.updatecount).HasDefaultValue(0);
        });

        modelBuilder.Entity<set>(entity =>
        {
            entity.HasKey(e => e.id).HasName("set_pkey");

            entity.ToTable("set", "hangfire");

            entity.HasIndex(e => e.expireat, "ix_hangfire_set_expireat");

            entity.HasIndex(e => new { e.key, e.score }, "ix_hangfire_set_key_score");

            entity.HasIndex(e => new { e.key, e.value }, "set_key_value_key").IsUnique();

            entity.Property(e => e.updatecount).HasDefaultValue(0);
        });

        modelBuilder.Entity<state>(entity =>
        {
            entity.HasKey(e => e.id).HasName("state_pkey");

            entity.ToTable("state", "hangfire");

            entity.HasIndex(e => e.jobid, "ix_hangfire_state_jobid");

            entity.Property(e => e.data).HasColumnType("jsonb");
            entity.Property(e => e.updatecount).HasDefaultValue(0);

            entity.HasOne(d => d.job).WithMany(p => p.states)
                .HasForeignKey(d => d.jobid)
                .HasConstraintName("state_jobid_fkey");
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
