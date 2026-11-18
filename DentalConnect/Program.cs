using System.Threading.RateLimiting;
    if (!string.IsNullOrWhiteSpace(cognitoClientId)) options.Audience = cognitoClientId;
    options.TokenValidationParameters.RoleClaimType = "cognito:groups";
});
builder.Services.AddAuthorization();
builder.Services.AddScoped<IClinicRepository, EfClinicRepository>();
builder.Services.AddScoped<IProposalExpirationProcessor, ProposalExpirationProcessor>();
builder.Services.AddOptions<ProposalExpirationOptions>()
    .Bind(builder.Configuration.GetSection(ProposalExpirationOptions.SectionName))
    .Validate(options => options.PollIntervalSeconds > 0, "ProposalExpiration:PollIntervalSeconds must be positive")
    .Validate(options => options.BatchSize > 0, "ProposalExpiration:BatchSize must be positive")
    .ValidateOnStart();
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<ProposalExpirationWorker>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(frontendOrigins)
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS").AllowAnyHeader()));


builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = new { code = "RATE_LIMITED", message = "Demasiados intentos; inténtalo más tarde" } }, token);
    };
    options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "global", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 20, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
});
builder.Services.AddOpenApi();
var app = builder.Build();


if (args.Contains("--seed"))
{
    using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
    await db.Database.MigrateAsync(); await DatabaseSeeder.SeedAsync(db); return;
}
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
    await db.Database.MigrateAsync(); return;
}
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (RequestValidationException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = new { code = "VALIDATION_ERROR", message = "Datos inválidos", details = new { formErrors = ex.Details.FormErrors, fieldErrors = ex.Details.FieldErrors } } });
    }
    catch (BadHttpRequestException)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = new { code = "VALIDATION_ERROR", message = "Datos inválidos" } });
    }
    catch (Exception ex)
    {
        if (!app.Environment.IsProduction()) Console.Error.WriteLine(ex);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = new { code = "INTERNAL_ERROR", message = "Error interno del servidor" } });
    }
});
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseCors();
app.UseAuthentication();
app.UseClinicMembershipClaims();
app.UseAuthorization();
app.UseRateLimiter();
app.MapGet("/api/health", () => Results.Json(new { status = "ok", service = "DentalConnect API" }));
app.MapGet("/api/clinics", async (HttpContext ctx, IClinicRepository clinics, CancellationToken ct) =>
{
    var search = ctx.Request.Query.ContainsKey("search") ? ctx.Request.Query["search"].ToString() : null;
    var serviceId = ctx.Request.Query.ContainsKey("serviceId") ? ctx.Request.Query["serviceId"].ToString() : null;
    var filters = InputValidator.ValidateClinicsQuery(search, serviceId); var data = await clinics.ListActiveAsync(filters, ct); return Results.Json(new { data });
});
app.MapGet("/api/clinics/{id}", async (string id, IClinicRepository clinics, CancellationToken ct) =>
{
    var validId = InputValidator.ValidateClinicId(id); var clinic = await clinics.FindActiveByIdAsync(validId, ct);
