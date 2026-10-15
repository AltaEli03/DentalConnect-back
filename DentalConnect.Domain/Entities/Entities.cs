using System.Security.Cryptography;

namespace DentalConnect.Domain.Entities;

public static class CuidGenerator
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    public static string NewId()
    {
        Span<char> buffer = stackalloc char[25];
        buffer[0] = 'c';
        long ticks = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 8; i >= 1; i--)
        {
            buffer[i] = Alphabet[(int)(ticks % 36)];
            ticks /= 36;
        }
        for (int i = 9; i < 25; i++)
        {
            buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return new string(buffer);
    }

    public static bool IsValid(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length < 10 || id.Length > 36 || id[0] != 'c')
            return false;
        for (int i = 1; i < id.Length; i++)
        {
            char c = id[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z')))
                return false;
        }
        return true;
    }
}

public class Clinic
{
    public string Id { get; set; } = CuidGenerator.NewId();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Municipality { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ClinicService> ClinicServices { get; set; } = new List<ClinicService>();
    public ICollection<Dentist> Dentists { get; set; } = new List<Dentist>();
}

public class DentalService
{
    public string Id { get; set; } = CuidGenerator.NewId();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int EstimatedDurationMinutes { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ClinicService> ClinicServices { get; set; } = new List<ClinicService>();
}

public class ClinicService
{
    public string Id { get; set; } = CuidGenerator.NewId();
    public string ClinicId { get; set; } = string.Empty;
    public string DentalServiceId { get; set; } = string.Empty;
    public decimal PriceFrom { get; set; }
    public bool IsActive { get; set; } = true;

    public Clinic Clinic { get; set; } = null!;
    public DentalService DentalService { get; set; } = null!;
}

public class Dentist
{
    public string Id { get; set; } = CuidGenerator.NewId();
    public string ClinicId { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string ProfessionalLicense { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string Biography { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;

    public Clinic Clinic { get; set; } = null!;
}
