using System.Threading.RateLimiting;
using Amazon;
using Amazon.CognitoIdentityProvider;
using DentalConnect.Application.Contracts;
using DentalConnect.Infrastructure.Persistence;
using DentalConnect.Infrastructure.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var port = builder.Configuration["PORT"];
if (!string.IsNullOrWhiteSpace(port) && string.IsNullOrWhiteSpace(builder.Configuration["ASPNETCORE_URLS"]))
{
    builder.WebHost.UseUrls($"http://+:{port}");
}

var frontendOrigins = (builder.Configuration["FRONTEND_ORIGIN"] ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var rawDbUrl = builder.Configuration["DATABASE_URL"]
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=dentalconnect;Username=dentalconnect;Password=dentalconnect";

builder.Services.AddDbContext<DentalConnectDbContext>(options =>
    options.UseNpgsql(DentalConnectDbContext.NormalizeConnectionString(rawDbUrl)));

var cognitoPoolId = builder.Configuration["COGNITO_USER_POOL_ID"];
var cognitoClientId = builder.Configuration["COGNITO_CLIENT_ID"];
if (!builder.Environment.IsDevelopment() && (string.IsNullOrWhiteSpace(cognitoPoolId) || string.IsNullOrWhiteSpace(cognitoClientId)))
{
    throw new InvalidOperationException("COGNITO_USER_POOL_ID and COGNITO_CLIENT_ID are required outside Development");
}

var awsRegion = builder.Configuration["AWS_REGION"] ?? "us-east-1";
builder.Services.AddSingleton<IAmazonCognitoIdentityProvider>(_ =>
    new AmazonCognitoIdentityProviderClient(RegionEndpoint.GetBySystemName(awsRegion)));
builder.Services.AddSingleton(new CognitoOptions(cognitoPoolId ?? string.Empty, cognitoClientId ?? string.Empty));
builder.Services.AddSingleton<IIdentityProvider, CognitoIdentityProvider>();

builder.Services.AddScoped<IClinicRepository, EfClinicRepository>();
builder.Services.AddScoped<AuthService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(frontendOrigins)
            .WithMethods("GET", "POST")
            .AllowAnyHeader();
    });
});

// ponytail: fixed-window IP rate limiter; swap to Redis distributed limiter if running multi-node behind ALB
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = new { code = "RATE_LIMITED", message = "Demasiados intentos; inténtalo más tarde" }
        }, token);
    };
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "global",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0
            }));
});

builder.Services.AddOpenApi();

var app = builder.Build();

if (args.Contains("--seed"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
    await db.Database.MigrateAsync();
    await DatabaseSeeder.SeedAsync(db);
    return;
}

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (RequestValidationException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new
        {
            error = new
            {
                code = "VALIDATION_ERROR",
                message = "Datos inválidos",
                details = new
                {
                    formErrors = ex.Details.FormErrors,
                    fieldErrors = ex.Details.FieldErrors
                }
            }
        });
    }
    catch (BadHttpRequestException)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new
        {
            error = new { code = "VALIDATION_ERROR", message = "Datos inválidos" }
        });
    }
    catch (Exception ex)
    {
        if (!app.Environment.IsProduction())
        {
            Console.Error.WriteLine(ex);
        }
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new
        {
            error = new { code = "INTERNAL_ERROR", message = "Error interno del servidor" }
        });
    }
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseRateLimiter();

app.MapGet("/api/health", () => Results.Json(new { status = "ok", service = "DentalConnect API" }));

app.MapGet("/api/clinics", async (HttpContext ctx, IClinicRepository clinics, CancellationToken ct) =>
{
    var search = ctx.Request.Query.ContainsKey("search") ? ctx.Request.Query["search"].ToString() : null;
    var serviceId = ctx.Request.Query.ContainsKey("serviceId") ? ctx.Request.Query["serviceId"].ToString() : null;
    var filters = InputValidator.ValidateClinicsQuery(search, serviceId);
    var data = await clinics.ListActiveAsync(filters, ct);
    return Results.Json(new { data });
});

app.MapGet("/api/clinics/{id}", async (string id, IClinicRepository clinics, CancellationToken ct) =>
{
    var validId = InputValidator.ValidateClinicId(id);
    var clinic = await clinics.FindActiveByIdAsync(validId, ct);
    if (clinic is null)
    {
        return Results.Json(
            new { error = new { code = "CLINIC_NOT_FOUND", message = "Consultorio no encontrado" } },
            statusCode: StatusCodes.Status404NotFound);
    }
    return Results.Json(new { data = clinic });
});

static IResult RateLimited() => Results.Json(
    new { error = new { code = "RATE_LIMITED", message = "Demasiados intentos; inténtalo más tarde" } },
    statusCode: StatusCodes.Status429TooManyRequests);

var authGroup = app.MapGroup("/api/auth").RequireRateLimiting("auth");

authGroup.MapPost("/register", async (RegisterRequest? body, AuthService auth, CancellationToken ct) =>
{
    var (firstName, lastName, email, phone, password) = InputValidator.ValidateRegistration(body);
    return await auth.RegisterAsync(firstName, lastName, email, phone, password, ct) switch
    {
        IdentityOutcome.Success => Results.Json(
            new { data = new { message = "Código de verificación enviado" } },
            statusCode: StatusCodes.Status201Created),
        IdentityOutcome.EmailExists => Results.Json(
            new { error = new { code = "EMAIL_EXISTS", message = "No fue posible completar el registro" } },
            statusCode: StatusCodes.Status409Conflict),
        IdentityOutcome.InvalidPassword => throw new RequestValidationException(
            new Dictionary<string, string[]> { ["password"] = ["La contraseña no cumple los requisitos"] }),
        _ => RateLimited()
    };
});

authGroup.MapPost("/verify-email", async (VerifyEmailRequest? body, AuthService auth, CancellationToken ct) =>
{
    var (email, code) = InputValidator.ValidateVerify(body);
    return await auth.VerifyAsync(email, code, ct) switch
    {
        IdentityOutcome.Success => Results.Json(new { data = new { message = "Correo verificado" } }),
        IdentityOutcome.RateLimited => RateLimited(),
        _ => Results.Json(
            new { error = new { code = "INVALID_VERIFICATION_CODE", message = "El código no es válido o expiró" } },
            statusCode: StatusCodes.Status400BadRequest)
    };
});

authGroup.MapPost("/resend-verification-code", async (ResendCodeRequest? body, AuthService auth, CancellationToken ct) =>
{
    var email = InputValidator.ValidateEmailOnly(body);
    if (await auth.ResendAsync(email, ct) == IdentityOutcome.RateLimited)
    {
        return RateLimited();
    }
    return Results.Json(
        new { data = new { message = "Si la cuenta requiere verificación, se enviará un código" } },
        statusCode: StatusCodes.Status202Accepted);
});

app.MapFallback(() => Results.Json(
    new { error = new { code = "NOT_FOUND", message = "Ruta no encontrada" } },
    statusCode: StatusCodes.Status404NotFound));

app.Run();

public partial class Program;
