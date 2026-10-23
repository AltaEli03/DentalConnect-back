using DentalConnect.Application.Contracts;
using DentalConnect.Domain.Entities;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DentalConnect.Tests;

public class ClinicProfileModelTests
{
    [Fact]
    public void ClinicProfileColumns_AreMapped()
    {
        var options = new DbContextOptionsBuilder<DentalConnectDbContext>()
            .UseNpgsql("Host=localhost;Database=dentalconnect_test;Username=test;Password=test")
            .Options;
        using var db = new DentalConnectDbContext(options);

        var clinic = db.Model.FindEntityType(typeof(Clinic));
        Assert.NotNull(clinic);
        Assert.Equal("city", clinic!.FindProperty(nameof(Clinic.City))?.GetColumnName());
        Assert.Equal("neighborhood", clinic.FindProperty(nameof(Clinic.Neighborhood))?.GetColumnName());
        Assert.Equal("websiteUrl", clinic.FindProperty(nameof(Clinic.WebsiteUrl))?.GetColumnName());
        Assert.Equal("facebookUrl", clinic.FindProperty(nameof(Clinic.FacebookUrl))?.GetColumnName());
        Assert.Equal("instagramUrl", clinic.FindProperty(nameof(Clinic.InstagramUrl))?.GetColumnName());
    }
}

public class ClinicProfileValidatorTests
{
    private static UpdateClinicProfileRequest ValidProfile() => new(
        Name: "Sonrisa del Centro",
        Description: "Consultorio ficticio.",
        Phone: "2225550101",
        Email: "centro@ejemplo.dental",
        Address: "Av. Ejemplo 101, Col. Centro",
        City: "Puebla",
        Municipality: "Puebla",
        Neighborhood: "Centro",
        State: "Puebla",
        PostalCode: "72000",
        Latitude: 19.0433,
        Longitude: -98.2019,
        WebsiteUrl: "https://sonrisa.example",
        FacebookUrl: null,
        InstagramUrl: null
    );

    [Fact]
    public void ValidProfile_NormalizesOptionalFields()
    {
        var update = InputValidator.ValidateClinicProfileUpdate(ValidProfile() with
        {
            Name = "  Clínica  ",
            Description = null,
            City = " ",
            WebsiteUrl = " https://sonrisa.example "
        });

        Assert.Equal("Clínica", update.Name);
        Assert.Equal(string.Empty, update.Description);
        Assert.Equal(string.Empty, update.City);
        Assert.Equal("https://sonrisa.example", update.WebsiteUrl);
    }

    [Fact]
    public void MissingRequiredFields_ReportsEachField()
    {
        var req = ValidProfile() with { Name = null, Address = null, Phone = null, Email = null };

        var ex = Assert.Throws<RequestValidationException>(() => InputValidator.ValidateClinicProfileUpdate(req));

        Assert.Contains("name", ex.Details.FieldErrors.Keys);
        Assert.Contains("address", ex.Details.FieldErrors.Keys);
        Assert.Contains("phone", ex.Details.FieldErrors.Keys);
        Assert.Contains("email", ex.Details.FieldErrors.Keys);
    }

    [Fact]
    public void InvalidOptionalValues_ReportsFields()
    {
        var req = ValidProfile() with
        {
            PostalCode = "72A00",
            WebsiteUrl = "http://sonrisa.example",
            FacebookUrl = "facebook.com/sonrisa",
            InstagramUrl = "https://"
        };

        var ex = Assert.Throws<RequestValidationException>(() => InputValidator.ValidateClinicProfileUpdate(req));

        Assert.Contains("postalCode", ex.Details.FieldErrors.Keys);
        Assert.Contains("websiteUrl", ex.Details.FieldErrors.Keys);
        Assert.Contains("facebookUrl", ex.Details.FieldErrors.Keys);
        Assert.Contains("instagramUrl", ex.Details.FieldErrors.Keys);
    }

    [Fact]
    public void Coordinates_MustComeInPairsAndStayInRange()
    {
        var pairError = Assert.Throws<RequestValidationException>(() =>
            InputValidator.ValidateClinicProfileUpdate(ValidProfile() with { Longitude = null }));
        Assert.Contains("latitude", pairError.Details.FieldErrors.Keys);
        Assert.Contains("longitude", pairError.Details.FieldErrors.Keys);

        var rangeError = Assert.Throws<RequestValidationException>(() =>
            InputValidator.ValidateClinicProfileUpdate(ValidProfile() with { Latitude = 91, Longitude = -181 }));
        Assert.Contains("latitude", rangeError.Details.FieldErrors.Keys);
        Assert.Contains("longitude", rangeError.Details.FieldErrors.Keys);
    }

    [Fact]
    public void OptionalFields_CanBeEmpty()
    {
        var update = InputValidator.ValidateClinicProfileUpdate(ValidProfile() with
        {
            Description = "",
            City = "",
            Municipality = "",
            Neighborhood = "",
            State = "",
            PostalCode = "",
            Latitude = null,
            Longitude = null,
            WebsiteUrl = "",
            FacebookUrl = "",
            InstagramUrl = ""
        });

        Assert.Equal(string.Empty, update.Description);
        Assert.Equal(string.Empty, update.PostalCode);
        Assert.Null(update.Latitude);
        Assert.Null(update.WebsiteUrl);
        Assert.Null(update.FacebookUrl);
        Assert.Null(update.InstagramUrl);
    }
}
