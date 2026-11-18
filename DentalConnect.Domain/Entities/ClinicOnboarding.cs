namespace DentalConnect.Domain.Entities;

/// <summary>A verified user's request to register and manage a clinic.</summary>
public sealed class ClinicApplication
{
    public string Id { get; set; } = CuidGenerator.NewId();
    public string OwnerSubject { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string ClinicName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Municipality { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING_REVIEW";
    public string? ReviewNote { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
}

/// <summary>Server-side mapping prevents trusting client-supplied clinic IDs.</summary>
public sealed class ClinicMembership
{
    public string Id { get; set; } = CuidGenerator.NewId();
    public string UserSubject { get; set; } = string.Empty;
    public string ClinicId { get; set; } = string.Empty;
    public string Role { get; set; } = "CLINIC_ADMIN";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Clinic Clinic { get; set; } = null!;
}
