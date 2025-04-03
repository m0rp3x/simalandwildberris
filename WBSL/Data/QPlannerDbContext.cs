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
    public virtual DbSet<WildberriesParrentCategories> wildberries_parrent_categories { get; set; }
    public virtual DbSet<WildberriesCategories> wildberries_categories { get; set; }

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
