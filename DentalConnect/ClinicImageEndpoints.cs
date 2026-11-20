using System.Security.Claims;
using Amazon.S3;
using Amazon.S3.Model;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DentalConnect;

public static class ClinicImageEndpoints
{
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private const long MaxRequestBytes = MaxImageBytes + 64 * 1024;

    public static IEndpointRouteBuilder MapClinicImageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Public profiles render through the API; the S3 bucket remains private.
        endpoints.MapGet("/api/images/{**key}", GetImageAsync);
        endpoints.MapPost("/api/clinics/{clinicId}/management/image", UploadClinicAsync)
            .RequireAuthorization().DisableAntiforgery();
        endpoints.MapPost("/api/clinics/{clinicId}/dentists/{dentistId}/image", UploadDentistAsync)
            .RequireAuthorization().DisableAntiforgery();
        return endpoints;
    }

    private static bool CanManage(HttpContext context, string clinicId)
    {
        var groups = context.User.FindAll("cognito:groups")
            .SelectMany(c => c.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        var clinicClaim = context.User.FindFirst("custom:clinicId")?.Value ?? context.User.FindFirst("clinicId")?.Value;
        return context.User.Identity?.IsAuthenticated == true &&
            (groups.Contains("CLINIC_ADMIN", StringComparer.Ordinal) || context.User.FindFirst(ClaimTypes.Role)?.Value == "CLINIC_ADMIN") &&
            string.Equals(clinicClaim, clinicId, StringComparison.Ordinal);
    }

    private static IResult Denied() => Results.Json(new
    {
        error = new { code = "CLINIC_ACCESS_DENIED", message = "No tienes permiso para administrar este consultorio" }
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult StorageUnavailable() => Results.Json(new
    {
        error = new { code = "IMAGE_STORAGE_UNAVAILABLE", message = "El almacenamiento de imágenes no está configurado" }
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static async Task<IResult> GetImageAsync(string? key, HttpContext context, IAmazonS3 s3,
        IConfiguration configuration, CancellationToken ct)
    {
        var bucket = configuration["IMAGE_BUCKET_NAME"];
        if (string.IsNullOrWhiteSpace(bucket)) return StorageUnavailable();
        if (!IsSafeImageKey(key)) return Results.NotFound();

        try
        {
            using var image = await s3.GetObjectAsync(bucket, key!, ct);
            await using var buffer = new MemoryStream();
            await image.ResponseStream.CopyToAsync(buffer, ct);
            context.Response.Headers.CacheControl = "public,max-age=86400,immutable";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.Bytes(buffer.ToArray(), image.Headers.ContentType ?? "application/octet-stream");
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound || ex.ErrorCode == "NoSuchKey")
        {
            return Results.NotFound();
        }
        catch (AmazonS3Exception)
        {
            return Results.Json(new { error = new { code = "IMAGE_READ_FAILED", message = "No se pudo cargar la imagen" } },
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static bool IsSafeImageKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 512 || key.Contains('\\') || key.Contains('%')) return false;
        var segments = key.Split('/');
        var clinicImage = segments.Length == 3 && segments[0] == "clinic-images";
        var dentistImage = segments.Length == 4 && segments[0] == "dentist-images";
        if (!clinicImage && !dentistImage) return false;
        foreach (var segment in segments.Skip(1).Take(segments.Length - 2))
            if (segment.Length == 0 || segment.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) return false;
        var file = segments[^1];
        var extension = Path.GetExtension(file);
        return extension is ".png" or ".jpg" or ".webp" &&
            Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _);
    }

    private static async Task<IResult> UploadClinicAsync(string clinicId, HttpContext context,
        DentalConnectDbContext db, IAmazonS3 s3, IConfiguration configuration, CancellationToken ct)
    {
        if (!CanManage(context, clinicId)) return Denied();
        var clinic = await db.Clinics.SingleOrDefaultAsync(c => c.Id == clinicId, ct);
        if (clinic is null) return Results.NotFound(new { error = new { code = "CLINIC_NOT_FOUND", message = "Consultorio no encontrado" } });
        return await UploadAsync(context.Request, s3, configuration, $"clinic-images/{clinicId}", async imageUrl =>
        {
            clinic.ImageUrl = imageUrl;
            clinic.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    private static async Task<IResult> UploadDentistAsync(string clinicId, string dentistId, HttpContext context,
        DentalConnectDbContext db, IAmazonS3 s3, IConfiguration configuration, CancellationToken ct)
    {
        if (!CanManage(context, clinicId)) return Denied();
        var dentist = await db.Dentists.SingleOrDefaultAsync(d => d.Id == dentistId && d.ClinicId == clinicId, ct);
        if (dentist is null) return Results.NotFound(new { error = new { code = "DENTIST_NOT_FOUND", message = "Dentista no encontrado" } });
        return await UploadAsync(context.Request, s3, configuration, $"dentist-images/{clinicId}/{dentistId}", async imageUrl =>
        {
            dentist.ImageUrl = imageUrl;
            dentist.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    private static async Task<IResult> UploadAsync(HttpRequest request, IAmazonS3 s3, IConfiguration configuration,
        string prefix, Func<string, Task> saveImageUrl, CancellationToken ct)
    {
        var bucket = configuration["IMAGE_BUCKET_NAME"];
        var publicBase = configuration["IMAGE_PUBLIC_BASE_URL"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(bucket) || !Uri.TryCreate(publicBase, UriKind.Absolute, out var publicUri) || publicUri.Scheme != Uri.UriSchemeHttps)
            return StorageUnavailable();
        if (request.ContentLength is > MaxRequestBytes) return TooLarge();

        IFormFile? file;
        try { file = (await request.ReadFormAsync(ct)).Files.GetFile("file"); }
        catch (InvalidDataException) { return InvalidImage(); }
        if (file is null || file.Length == 0) return InvalidImage();
        if (file.Length > MaxImageBytes) return TooLarge();

        await using var stream = file.OpenReadStream();
        var signature = new byte[12];
        var count = await stream.ReadAsync(signature.AsMemory(), ct);
        var detected = DetectImage(signature, count);
        if (detected is null) return InvalidImage();
        stream.Position = 0;

        var key = $"{prefix}/{Guid.NewGuid():N}{detected.Value.Extension}";
        try
        {
            await s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = stream,
                ContentType = detected.Value.ContentType
            }, ct);
            var imageUrl = publicBase + "/" + string.Join('/', key.Split('/').Select(Uri.EscapeDataString));
            await saveImageUrl(imageUrl);
            return Results.Ok(new { data = new { imageUrl } });
        }
        catch (AmazonS3Exception)
        {
            return Results.Json(new { error = new { code = "IMAGE_UPLOAD_FAILED", message = "No se pudo guardar la imagen" } }, statusCode: 502);
        }
    }

    private static (string ContentType, string Extension)? DetectImage(byte[] bytes, int count)
    {
        if (count >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ("image/png", ".png");
        if (count >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return ("image/jpeg", ".jpg");
        if (count >= 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' &&
            bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P') return ("image/webp", ".webp");
        return null;
    }

    private static IResult TooLarge() => Results.Json(new
    {
        error = new { code = "IMAGE_TOO_LARGE", message = "La imagen debe pesar como máximo 5 MB" }
    }, statusCode: StatusCodes.Status413PayloadTooLarge);

    private static IResult InvalidImage() => Results.BadRequest(new
    {
        error = new { code = "INVALID_IMAGE", message = "Selecciona una imagen PNG, JPEG o WebP válida" }
    });
}
