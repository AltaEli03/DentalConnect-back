using DentalConnect.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DentalConnect.Infrastructure.Persistence;

public class DentalConnectDbContext(DbContextOptions<DentalConnectDbContext> options) : DbContext(options)
{
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<DentalService> DentalServices => Set<DentalService>();
    public DbSet<ClinicService> ClinicServices => Set<ClinicService>();
    public DbSet<Dentist> Dentists => Set<Dentist>();
    public DbSet<DentistService> DentistServices => Set<DentistService>();
    public DbSet<DentistWeeklySchedule> DentistWeeklySchedules => Set<DentistWeeklySchedule>();
    public DbSet<DentistScheduleBlock> DentistScheduleBlocks => Set<DentistScheduleBlock>(); => Set<Dentist>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Clinic>(entity =>
        {
            entity.ToTable("Clinic");
            entity.HasKey(e => e.Id).HasName("Clinic_pkey");
            entity.Property(e => e.Id).HasColumnName("id").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").IsRequired();
            entity.Property(e => e.Phone).HasColumnName("phone").IsRequired();
            entity.Property(e => e.Email).HasColumnName("email").IsRequired();
            entity.Property(e => e.Address).HasColumnName("address").IsRequired();
            entity.Property(e => e.Municipality).HasColumnName("municipality").IsRequired();
            entity.Property(e => e.State).HasColumnName("state").IsRequired();
            entity.Property(e => e.PostalCode).HasColumnName("postalCode").IsRequired();
            entity.Property(e => e.Latitude).HasColumnName("latitude");
            entity.Property(e => e.Longitude).HasColumnName("longitude");
            entity.Property(e => e.ImageUrl).HasColumnName("imageUrl");
            entity.Property(e => e.IsActive).HasColumnName("isActive").HasDefaultValue(true).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("createdAt").HasDefaultValueSql("CURRENT_TIMESTAMP").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updatedAt").IsRequired();
        });

        modelBuilder.Entity<DentalService>(entity =>
        {
            entity.ToTable("DentalService");
            entity.HasKey(e => e.Id).HasName("DentalService_pkey");
            entity.Property(e => e.Id).HasColumnName("id").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").IsRequired();
            entity.Property(e => e.EstimatedDurationMinutes).HasColumnName("estimatedDurationMinutes").IsRequired();
            entity.Property(e => e.IsActive).HasColumnName("isActive").HasDefaultValue(true).IsRequired();
        });

        modelBuilder.Entity<ClinicService>(entity =>
        {
            entity.ToTable("ClinicService");
            entity.HasKey(e => e.Id).HasName("ClinicService_pkey");
            entity.Property(e => e.Id).HasColumnName("id").IsRequired();
            entity.Property(e => e.ClinicId).HasColumnName("clinicId").IsRequired();
            entity.Property(e => e.DentalServiceId).HasColumnName("dentalServiceId").IsRequired();
            entity.Property(e => e.PriceFrom).HasColumnName("priceFrom").HasPrecision(10, 2).IsRequired();
            entity.Property(e => e.IsActive).HasColumnName("isActive").HasDefaultValue(true).IsRequired();

            entity.HasIndex(e => new { e.ClinicId, e.DentalServiceId })
                .IsUnique()
                .HasDatabaseName("ClinicService_clinicId_dentalServiceId_key");

            entity.HasOne(e => e.Clinic)
                .WithMany(c => c.ClinicServices)
                .HasForeignKey(e => e.ClinicId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ClinicService_clinicId_fkey");

            entity.HasOne(e => e.DentalService)
                .WithMany(d => d.ClinicServices)
                .HasForeignKey(e => e.DentalServiceId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("ClinicService_dentalServiceId_fkey");
        });

        modelBuilder.Entity<Dentist>(entity =>
        {
            entity.ToTable("Dentist");
            entity.HasKey(e => e.Id).HasName("Dentist_pkey");
            entity.Property(e => e.Id).HasColumnName("id").IsRequired();
            entity.Property(e => e.ClinicId).HasColumnName("clinicId").IsRequired();
            entity.Property(e => e.FirstName).HasColumnName("firstName").IsRequired();
            entity.Property(e => e.LastName).HasColumnName("lastName").IsRequired();
            entity.Property(e => e.ProfessionalLicense).HasColumnName("professionalLicense").IsRequired();
            entity.Property(e => e.Specialty).HasColumnName("specialty").IsRequired();
            entity.Property(e => e.Biography).HasColumnName("biography").IsRequired();
            entity.Property(e => e.ImageUrl).HasColumnName("imageUrl");
            entity.Property(e => e.IsActive).HasColumnName("isActive").HasDefaultValue(true).IsRequired();

            entity.HasOne(e => e.Clinic)
                .WithMany(c => c.Dentists)
                .HasForeignKey(e => e.ClinicId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("Dentist_clinicId_fkey");
        });
    }

    public static string NormalizeConnectionString(string rawConnectionString)
    {
        if (string.IsNullOrWhiteSpace(rawConnectionString))
            return string.Empty;

        var trimmed = rawConnectionString.Trim().Trim('"');
        if (!trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var uri = new Uri(trimmed);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.Trim('/'),
            Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty,
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty
        };

        if (!string.IsNullOrWhiteSpace(uri.Query))
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals("schema", StringComparison.OrdinalIgnoreCase))
                {
                    builder.SearchPath = Uri.UnescapeDataString(kv[1]);
                }
            }
        }

        return builder.ConnectionString;
    }

    public static string BuildConnectionString(
        string? rawConnectionString,
        string? host,
        string? database,
        string? username,
        string? password,
        string? port)
    {
        if (!string.IsNullOrWhiteSpace(rawConnectionString))
            return NormalizeConnectionString(rawConnectionString);

        if (string.IsNullOrWhiteSpace(host))
            return NormalizeConnectionString(
                "Host=localhost;Port=5432;Database=dentalconnect;Username=dentalconnect;Password=dentalconnect");

        if (string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(username) || password is null)
            throw new InvalidOperationException(
                "DB_NAME, DB_USERNAME, and DB_PASSWORD are required when DB_HOST is configured.");

        var parsedPort = 5432;
        if (!string.IsNullOrWhiteSpace(port) && !int.TryParse(port, out parsedPort))
            throw new InvalidOperationException("DB_PORT must be a valid TCP port.");

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = parsedPort,
            Database = database,
            Username = username,
            Password = password,
            SslMode = SslMode.VerifyFull
        }.ConnectionString;
    }
}
