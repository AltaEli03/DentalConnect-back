using DentalConnect.Application.Contracts;
using DentalConnect.Domain.Entities;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DentalConnect.Tests;

public class ClinicServiceModelTests
{
    [Fact]
    public void DentalServiceBelongsToClinic_AndPriceIsOptional()
    {
        var options = new DbContextOptionsBuilder<DentalConnectDbContext>()
            .UseNpgsql("Host=localhost;Database=dentalconnect_test;Username=test;Password=test")
            .Options;
        using var db = new DentalConnectDbContext(options);

        var clinicId = db.Model.FindEntityType(typeof(DentalService))!.FindProperty(nameof(DentalService.ClinicId))!;
        Assert.Equal("clinicId", clinicId.GetColumnName());
        Assert.False(clinicId.IsNullable);

        var price = db.Model.FindEntityType(typeof(ClinicService))!.FindProperty(nameof(ClinicService.PriceFrom))!;
        Assert.True(price.IsNullable);

        var service = db.Model.FindEntityType(typeof(DentalService))!;
        Assert.Contains(service.GetForeignKeys(), fk => fk.GetConstraintName() == "DentalService_clinicId_fkey");
        Assert.Contains(service.GetIndexes(), index => index.GetDatabaseName() == "DentalService_clinicId_idx");
    }
}

public class ClinicServiceValidatorTests
{
    private static ClinicServiceRequest ValidService() => new("Limpieza dental", "Profilaxis ficticia.", 45);

    [Fact]
    public void ValidService_TrimsNameAndDescription()
    {
        var data = InputValidator.ValidateClinicService(ValidService() with { Name = "  Limpieza  ", Description = " Profilaxis " });

        Assert.Equal("Limpieza", data.Name);
        Assert.Equal("Profilaxis", data.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(null)]
    public void NonPositiveOrMissingDuration_ReportsField(int? minutes)
    {
        var ex = Assert.Throws<RequestValidationException>(() =>
            InputValidator.ValidateClinicService(ValidService() with { EstimatedDurationMinutes = minutes }));

        Assert.Contains("estimatedDurationMinutes", ex.Details.FieldErrors.Keys);
    }

    [Fact]
    public void EmptyNameAndLongDescription_ReportBothFields()
    {
        var ex = Assert.Throws<RequestValidationException>(() =>
            InputValidator.ValidateClinicService(ValidService() with { Name = "  ", Description = new string('a', 1001) }));

        Assert.Contains("name", ex.Details.FieldErrors.Keys);
        Assert.Contains("description", ex.Details.FieldErrors.Keys);
    }

    [Fact]
    public void StatusWithoutValue_ReportsField()
    {
        var ex = Assert.Throws<RequestValidationException>(() =>
            InputValidator.ValidateClinicServiceStatus(new ClinicServiceStatusRequest(null)));

        Assert.Contains("isActive", ex.Details.FieldErrors.Keys);
    }
}
