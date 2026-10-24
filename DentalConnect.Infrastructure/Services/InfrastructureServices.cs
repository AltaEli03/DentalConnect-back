using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using DentalConnect.Application.Contracts;
using DentalConnect.Domain.Entities;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DentalConnect.Infrastructure.Services;

public class EfClinicRepository(DentalConnectDbContext db) : IClinicRepository
{
    public async Task<IReadOnlyList<ClinicSummaryDto>> ListActiveAsync(ClinicSearchFilter filters, CancellationToken ct = default)
    {
        IQueryable<Clinic> query = db.Clinics
            .AsNoTracking()
            .Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var term = filters.Search.Trim().ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(term) ||
                c.Municipality.ToLower().Contains(term) ||
                c.State.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(filters.ServiceId))
        {
            var serviceId = filters.ServiceId;
            query = query.Where(c => c.ClinicServices.Any(cs =>
                cs.IsActive &&
                cs.DentalServiceId == serviceId &&
                cs.DentalService.IsActive));
        }

        var clinics = await query
            .Include(c => c.ClinicServices.Where(cs => cs.IsActive && cs.DentalService.IsActive))
                .ThenInclude(cs => cs.DentalService)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        return clinics.Select(c => new ClinicSummaryDto(
            c.Id,
            c.Name,
            c.Description,
            c.Municipality,
            c.State,
            c.Address,
            c.ImageUrl,
            c.ClinicServices
                .Where(cs => cs.IsActive && cs.DentalService.IsActive)
                .Select(cs => new ClinicServiceSummaryDto(cs.DentalService.Id, cs.DentalService.Name))
                .ToList()
        )).ToList();
    }

    public async Task<ClinicDetailDto?> FindActiveByIdAsync(string id, CancellationToken ct = default)
    {
        var clinic = await db.Clinics
            .AsNoTracking()
            .Include(c => c.ClinicServices.Where(cs => cs.IsActive && cs.DentalService.IsActive))
                .ThenInclude(cs => cs.DentalService)
            .Include(c => c.Dentists.Where(d => d.IsActive))
                .ThenInclude(d => d.DentistServices.Where(ds => ds.DentalService.IsActive))
                    .ThenInclude(ds => ds.DentalService)
            .FirstOrDefaultAsync(c => c.Id == id && c.IsActive, ct);

        if (clinic is null)
            return null;

        return ToDetail(clinic);
    }

    public async Task<ClinicDetailDto?> UpdateProfileAsync(string id, ClinicProfileUpdate data, CancellationToken ct = default)
    {
        var clinic = await db.Clinics
            .Include(c => c.ClinicServices.Where(cs => cs.IsActive && cs.DentalService.IsActive))
                .ThenInclude(cs => cs.DentalService)
            .Include(c => c.Dentists.Where(d => d.IsActive))
                .ThenInclude(d => d.DentistServices.Where(ds => ds.DentalService.IsActive))
                    .ThenInclude(ds => ds.DentalService)
            .FirstOrDefaultAsync(c => c.Id == id && c.IsActive, ct);

        if (clinic is null)
            return null;

        clinic.Name = data.Name;
        clinic.Description = data.Description;
        clinic.Phone = data.Phone;
        clinic.Email = data.Email;
        clinic.Address = data.Address;
        clinic.City = data.City;
        clinic.Municipality = data.Municipality;
        clinic.Neighborhood = data.Neighborhood;
        clinic.State = data.State;
        clinic.PostalCode = data.PostalCode;
        clinic.Latitude = data.Latitude;
        clinic.Longitude = data.Longitude;
        clinic.WebsiteUrl = data.WebsiteUrl;
        clinic.FacebookUrl = data.FacebookUrl;
        clinic.InstagramUrl = data.InstagramUrl;
        clinic.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return ToDetail(clinic);
    }

    public async Task<IReadOnlyList<ClinicServiceManagementDto>?> ListClinicServicesAsync(string clinicId, CancellationToken ct = default)
    {
        if (!await db.Clinics.AnyAsync(c => c.Id == clinicId && c.IsActive, ct))
            return null;

        return await db.ClinicServices
            .AsNoTracking()
            .Where(cs => cs.ClinicId == clinicId)
            .OrderBy(cs => cs.DentalService.Name)
            .Select(cs => new ClinicServiceManagementDto(
                cs.DentalServiceId,
                cs.DentalService.Name,
                cs.DentalService.Description,
                cs.DentalService.EstimatedDurationMinutes,
                cs.IsActive && cs.DentalService.IsActive,
                cs.PriceFrom))
            .ToListAsync(ct);
    }

    public async Task<ClinicServiceManagementDto?> CreateClinicServiceAsync(string clinicId, ClinicServiceData data, CancellationToken ct = default)
    {
        if (!await db.Clinics.AnyAsync(c => c.Id == clinicId && c.IsActive, ct))
            return null;

        var offer = new ClinicService
        {
            ClinicId = clinicId,
            DentalService = new DentalService
            {
                ClinicId = clinicId,
                Name = data.Name,
                Description = data.Description,
                EstimatedDurationMinutes = data.EstimatedDurationMinutes
            }
        };
        db.ClinicServices.Add(offer);
        await db.SaveChangesAsync(ct);
        return ToManagementDto(offer);
    }

    public async Task<ClinicServiceManagementDto?> UpdateClinicServiceAsync(string clinicId, string serviceId, ClinicServiceData data, CancellationToken ct = default)
    {
        var offer = await FindOwnedOfferAsync(clinicId, serviceId, ct);
        if (offer is null)
            return null;

        offer.DentalService.Name = data.Name;
        offer.DentalService.Description = data.Description;
        offer.DentalService.EstimatedDurationMinutes = data.EstimatedDurationMinutes;
        await db.SaveChangesAsync(ct);
        return ToManagementDto(offer);
    }

    public async Task<ClinicServiceManagementDto?> SetClinicServiceStatusAsync(string clinicId, string serviceId, bool isActive, CancellationToken ct = default)
    {
        var offer = await FindOwnedOfferAsync(clinicId, serviceId, ct);
        if (offer is null)
            return null;

        offer.IsActive = isActive;
        offer.DentalService.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return ToManagementDto(offer);
    }

    private Task<ClinicService?> FindOwnedOfferAsync(string clinicId, string serviceId, CancellationToken ct) =>
        db.ClinicServices
            .Include(cs => cs.DentalService)
            .FirstOrDefaultAsync(cs =>
                cs.ClinicId == clinicId &&
                cs.DentalServiceId == serviceId &&
                cs.DentalService.ClinicId == clinicId &&
                cs.Clinic.IsActive, ct);

    private static ClinicServiceManagementDto ToManagementDto(ClinicService offer) => new(
        offer.DentalService.Id,
        offer.DentalService.Name,
        offer.DentalService.Description,
        offer.DentalService.EstimatedDurationMinutes,
        offer.IsActive && offer.DentalService.IsActive,
        offer.PriceFrom);

    private static ClinicDetailDto ToDetail(Clinic clinic) => new(
        clinic.Id,
        clinic.Name,
        clinic.Description,
        clinic.Phone,
        clinic.Email,
        clinic.Address,
        clinic.City,
        clinic.Neighborhood,
        clinic.Municipality,
        clinic.State,
        clinic.PostalCode,
        clinic.Latitude,
        clinic.Longitude,
        clinic.ImageUrl,
        clinic.WebsiteUrl,
        clinic.FacebookUrl,
        clinic.InstagramUrl,
        clinic.ClinicServices
            .Where(cs => cs.IsActive && cs.DentalService.IsActive)
            .Select(cs => new ClinicServiceDetailDto(
                cs.DentalService.Id,
                cs.DentalService.Name,
                cs.DentalService.Description,
                cs.DentalService.EstimatedDurationMinutes,
                cs.PriceFrom
            ))
            .ToList(),
        clinic.Dentists
            .Where(d => d.IsActive)
            .OrderBy(d => d.LastName)
            .ThenBy(d => d.FirstName)
            .Select(d => new DentistDto(
                d.Id,
                d.FirstName,
                d.LastName,
                d.ProfessionalLicense,
                d.Specialty,
                d.Biography,
                d.ImageUrl,
                d.DentistServices.Where(ds => ds.DentalService.IsActive)
                    .Select(ds => new DentistServiceDto(ds.DentalService.Id, ds.DentalService.Name, ds.DentalService.Description, ds.DentalService.EstimatedDurationMinutes))
                    .ToList()
            ))
            .ToList()
    );
}

// ponytail: UserPoolId unused by SignUp/ConfirmSignUp/ResendConfirmationCode; use it as JwtBearer authority when a protected endpoint is added.
public record CognitoOptions(string UserPoolId, string ClientId);

public class CognitoIdentityProvider(IAmazonCognitoIdentityProvider cognito, CognitoOptions options) : IIdentityProvider
{
    public async Task<IdentityOutcome> SignUpAsync(string email, string password, string firstName, string lastName, string phone, CancellationToken ct = default)
    {
        try
        {
            await cognito.SignUpAsync(new SignUpRequest
            {
                ClientId = options.ClientId,
                Username = email,
                Password = password,
                UserAttributes =
                [
                    new AttributeType { Name = "email", Value = email },
                    new AttributeType { Name = "given_name", Value = firstName },
                    new AttributeType { Name = "family_name", Value = lastName },
                    new AttributeType { Name = "phone_number", Value = phone }
                ]
            }, ct);
            return IdentityOutcome.Success;
        }
        catch (UsernameExistsException) { return IdentityOutcome.EmailExists; }
        catch (InvalidPasswordException) { return IdentityOutcome.InvalidPassword; }
        catch (LimitExceededException) { return IdentityOutcome.RateLimited; }
        catch (TooManyRequestsException) { return IdentityOutcome.RateLimited; }
    }

    public async Task<IdentityOutcome> ConfirmSignUpAsync(string email, string code, CancellationToken ct = default)
    {
        try
        {
            await cognito.ConfirmSignUpAsync(new ConfirmSignUpRequest
            {
                ClientId = options.ClientId,
                Username = email,
                ConfirmationCode = code
            }, ct);
            return IdentityOutcome.Success;
        }
        catch (CodeMismatchException) { return IdentityOutcome.InvalidCode; }
        catch (ExpiredCodeException) { return IdentityOutcome.InvalidCode; }
        catch (UserNotFoundException) { return IdentityOutcome.InvalidCode; }
        catch (NotAuthorizedException) { return IdentityOutcome.InvalidCode; }
        catch (LimitExceededException) { return IdentityOutcome.RateLimited; }
        catch (TooManyRequestsException) { return IdentityOutcome.RateLimited; }
    }

    public async Task<IdentityOutcome> ResendCodeAsync(string email, CancellationToken ct = default)
    {
        try
        {
            await cognito.ResendConfirmationCodeAsync(new ResendConfirmationCodeRequest
            {
                ClientId = options.ClientId,
                Username = email
            }, ct);
            return IdentityOutcome.Success;
        }
        catch (UserNotFoundException) { return IdentityOutcome.NotPending; }
        catch (InvalidParameterException) { return IdentityOutcome.NotPending; }
        catch (LimitExceededException) { return IdentityOutcome.RateLimited; }
        catch (TooManyRequestsException) { return IdentityOutcome.RateLimited; }
    }
}

public static class DatabaseSeeder
{
    public static async Task SeedAsync(DentalConnectDbContext db, CancellationToken ct = default)
    {
        // Deployment may run this command more than once. Never replace or delete
        // the production catalog; only add the demo catalog to a new empty database.
        if (await db.Clinics.AnyAsync(ct))
            return;

        var catalog = new List<DentalService>
        {
            new() { Name = "Consulta de valoración", Description = "Evaluación inicial ficticia de salud bucal.", EstimatedDurationMinutes = 30 },
            new() { Name = "Limpieza dental", Description = "Profilaxis preventiva ficticia.", EstimatedDurationMinutes = 45 },
            new() { Name = "Ortodoncia", Description = "Valoración ficticia para alineación dental.", EstimatedDurationMinutes = 50 },
            new() { Name = "Endodoncia", Description = "Atención ficticia de conductos radiculares.", EstimatedDurationMinutes = 90 },
            new() { Name = "Odontopediatría", Description = "Consulta ficticia para niñas y niños.", EstimatedDurationMinutes = 40 },
            new() { Name = "Blanqueamiento dental", Description = "Procedimiento estético ficticio.", EstimatedDurationMinutes = 60 }
        };
        var byName = catalog.ToDictionary(s => s.Name, s => s);

        var clinics = new List<Clinic>
        {
            new()
            {
                Name = "Sonrisa del Centro",
                Description = "Consultorio ficticio de atención dental general en el centro de Puebla.",
                Phone = "222-555-0101",
                Email = "centro@ejemplo.dental",
                Address = "Av. Ejemplo 101, Col. Centro",
                Municipality = "Puebla",
                State = "Puebla",
                PostalCode = "72000",
                Latitude = 19.0433,
                Longitude = -98.2019,
                ImageUrl = "https://images.unsplash.com/photo-1629909613654-28e377c37b09?auto=format&fit=crop&w=1200&q=80"
            },
            new()
            {
                Name = "Clínica Horizonte Dental",
                Description = "Consultorio ficticio enfocado en prevención y estética dental.",
                Phone = "55-5555-0102",
                Email = "horizonte@ejemplo.dental",
                Address = "Calle Horizonte 220, Col. Del Valle",
                Municipality = "Benito Juárez",
                State = "Ciudad de México",
                PostalCode = "03100",
                Latitude = 19.387,
                Longitude = -99.162,
                ImageUrl = "https://images.unsplash.com/photo-1606811971618-4486d14f3f99?auto=format&fit=crop&w=1200&q=80"
            },
            new()
            {
                Name = "Dental Norte Familiar",
                Description = "Consultorio ficticio para familias y atención odontopediátrica.",
                Phone = "81-5555-0103",
                Email = "norte@ejemplo.dental",
                Address = "Av. Norte 450, Col. Cumbres",
                Municipality = "Monterrey",
                State = "Nuevo León",
                PostalCode = "64610",
                Latitude = 25.714,
                Longitude = -100.367,
                ImageUrl = "https://images.unsplash.com/photo-1588776814546-daab30f31062?auto=format&fit=crop&w=1200&q=80"
            }
        };
        db.Clinics.AddRange(clinics);

        db.ClinicServices.AddRange(
            Offer(clinics[0], byName["Consulta de valoración"], 450m),
            Offer(clinics[0], byName["Limpieza dental"], 700m),
            Offer(clinics[0], byName["Blanqueamiento dental"], 2600m),
            Offer(clinics[1], byName["Consulta de valoración"], 500m),
            Offer(clinics[1], byName["Ortodoncia"], 650m),
            Offer(clinics[1], byName["Endodoncia"], 3200m),
            Offer(clinics[2], byName["Consulta de valoración"], 400m),
            Offer(clinics[2], byName["Odontopediatría"], 600m),
            Offer(clinics[2], byName["Limpieza dental"], 650m)
        );

        db.Dentists.AddRange(
            new Dentist
            {
                ClinicId = clinics[0].Id,
                FirstName = "Valeria",
                LastName = "Mendoza",
                ProfessionalLicense = "CED-FICT-1001",
                Specialty = "Odontología general",
                Biography = "Profesional ficticia enfocada en prevención y atención integral.",
                ImageUrl = "https://images.unsplash.com/photo-1559839734-2b71ea197ec2?auto=format&fit=crop&w=480&q=80"
            },
            new Dentist
            {
                ClinicId = clinics[0].Id,
                FirstName = "Jorge",
                LastName = "Paredes",
                ProfessionalLicense = "CED-FICT-1002",
                Specialty = "Estética dental",
                Biography = "Profesional ficticio con interés en rehabilitación estética.",
                ImageUrl = null
            },
            new Dentist
            {
                ClinicId = clinics[1].Id,
                FirstName = "Camila",
                LastName = "Ríos",
                ProfessionalLicense = "CED-FICT-1003",
                Specialty = "Ortodoncia",
                Biography = "Profesional ficticia dedicada a tratamientos de alineación dental.",
                ImageUrl = "https://images.unsplash.com/photo-1594824476967-48c8b964273f?auto=format&fit=crop&w=480&q=80"
            },
            new Dentist
            {
                ClinicId = clinics[2].Id,
                FirstName = "Mateo",
                LastName = "León",
                ProfessionalLicense = "CED-FICT-1004",
                Specialty = "Odontopediatría",
                Biography = "Profesional ficticio enfocado en una experiencia amable para infancias.",
                ImageUrl = null
            }
        );

        await db.SaveChangesAsync(ct);
    }

    private static ClinicService Offer(Clinic clinic, DentalService template, decimal priceFrom) => new()
    {
        ClinicId = clinic.Id,
        PriceFrom = priceFrom,
        DentalService = new DentalService
        {
            ClinicId = clinic.Id,
            Name = template.Name,
            Description = template.Description,
            EstimatedDurationMinutes = template.EstimatedDurationMinutes
        }
    };
}
