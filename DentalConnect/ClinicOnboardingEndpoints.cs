using System.Security.Claims;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using DentalConnect.Domain.Entities;
using DentalConnect.Infrastructure.Persistence;
using DentalConnect.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DentalConnect;

public sealed record ClinicApplicationRequest(string? ClinicName, string? Phone, string? Address, string? Municipality, string? State, string? PostalCode);
public sealed record ClinicReviewRequest(string? Reason);

public static class ClinicOnboardingEndpoints
{
    public static IEndpointRouteBuilder MapClinicOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/clinic-applications", SubmitAsync).RequireAuthorization();
        app.MapGet("/api/clinic-applications/mine", MineAsync).RequireAuthorization();
        app.MapGet("/api/platform/clinic-applications", ListPendingAsync).RequireAuthorization();
        app.MapPost("/api/platform/clinic-applications/{id}/approve", ApproveAsync).RequireAuthorization();
        app.MapPost("/api/platform/clinic-applications/{id}/reject", RejectAsync).RequireAuthorization();
        return app;
    }

    public static WebApplication UseClinicMembershipClaims(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var groups = context.User.FindAll("cognito:groups").SelectMany(x => x.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            var sub = context.User.FindFirst("sub")?.Value;
            if (sub is not null && groups.Contains("CLINIC_ADMIN", StringComparer.Ordinal) && context.User.FindFirst("clinicId") is null && context.User.FindFirst("custom:clinicId") is null)
            {
                using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
                var connection = (NpgsqlConnection)db.Database.GetDbConnection(); await connection.OpenAsync(context.RequestAborted);
                await using var command = new NpgsqlCommand("""SELECT "clinicId" FROM "ClinicMembership" WHERE "userSubject" = @subject LIMIT 1""", connection);
                command.Parameters.AddWithValue("subject", sub); var clinicId = (string?)await command.ExecuteScalarAsync(context.RequestAborted);
                if (!string.IsNullOrWhiteSpace(clinicId) && context.User.Identity is ClaimsIdentity identity)
                { identity.AddClaim(new Claim("clinicId", clinicId)); identity.AddClaim(new Claim("custom:clinicId", clinicId)); identity.AddClaim(new Claim("clinic_id", clinicId)); identity.AddClaim(new Claim("custom:clinic_id", clinicId)); }
            }
            await next();
        });
        return app;
    }

    private static bool IsPlatformAdmin(HttpContext ctx) => ctx.User.Identity?.IsAuthenticated == true && ctx.User.FindAll("cognito:groups").SelectMany(x => x.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Contains("PLATFORM_ADMIN", StringComparer.Ordinal);

    private static async Task<IResult> SubmitAsync(HttpContext ctx, ClinicApplicationRequest? body, DentalConnectDbContext db, CancellationToken ct)
    {
        var subject=ctx.User.FindFirst("sub")?.Value; var email=ctx.User.FindFirst("email")?.Value?.Trim().ToLowerInvariant();
        if(string.IsNullOrWhiteSpace(subject)||string.IsNullOrWhiteSpace(email)||ctx.User.FindFirst("email_verified")?.Value?.Equals("true",StringComparison.OrdinalIgnoreCase)!=true) return Results.Unauthorized();
        var name=body?.ClinicName?.Trim()??""; var phone=body?.Phone?.Trim()??""; var address=body?.Address?.Trim()??""; var municipality=body?.Municipality?.Trim()??""; var state=body?.State?.Trim()??""; var postal=body?.PostalCode?.Trim()??"";
        if(name.Length is <3 or >120||phone.Length is <8 or >24||address.Length is <5 or >200||municipality.Length is <2 or >100||state.Length is <2 or >100||postal.Length is <4 or >12) return Results.BadRequest(new { error=new { code="VALIDATION_ERROR",message="Revisa nombre, teléfono y domicilio del consultorio" } });
        var conn=(NpgsqlConnection)db.Database.GetDbConnection(); await conn.OpenAsync(ct);
        await using(var check=new NpgsqlCommand("""SELECT 1 FROM "ClinicMembership" WHERE "userSubject"=@s UNION ALL SELECT 1 FROM "ClinicApplication" WHERE "ownerSubject"=@s AND "status" IN ('PENDING_REVIEW','PROVISIONING') LIMIT 1""",conn)) { check.Parameters.AddWithValue("s",subject); if(await check.ExecuteScalarAsync(ct)is not null) return Results.Conflict(new { error=new { code="APPLICATION_EXISTS",message="Ya tienes una solicitud pendiente o un consultorio asociado" } }); }
        var id=CuidGenerator.NewId();
        await using var insert=new NpgsqlCommand("""INSERT INTO "ClinicApplication" ("id","ownerSubject","ownerEmail","clinicName","phone","address","municipality","state","postalCode","status","createdAt") VALUES (@id,@s,@e,@n,@p,@a,@m,@st,@pc,'PENDING_REVIEW',CURRENT_TIMESTAMP)""",conn);
        insert.Parameters.AddWithValue("id",id); insert.Parameters.AddWithValue("s",subject); insert.Parameters.AddWithValue("e",email); insert.Parameters.AddWithValue("n",name); insert.Parameters.AddWithValue("p",phone); insert.Parameters.AddWithValue("a",address); insert.Parameters.AddWithValue("m",municipality); insert.Parameters.AddWithValue("st",state); insert.Parameters.AddWithValue("pc",postal); await insert.ExecuteNonQueryAsync(ct);
        return Results.Created("/api/clinic-applications/mine",new { data=new { id,status="PENDING_REVIEW" } });
    }

    private static async Task<IResult> MineAsync(HttpContext ctx, DentalConnectDbContext db, CancellationToken ct)
    {
        var subject=ctx.User.FindFirst("sub")?.Value; if(subject is null) return Results.Unauthorized(); var conn=(NpgsqlConnection)db.Database.GetDbConnection(); await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("""SELECT "id","clinicName","status","reviewNote","createdAt","reviewedAt" FROM "ClinicApplication" WHERE "ownerSubject"=@s ORDER BY "createdAt" DESC LIMIT 1""",conn); cmd.Parameters.AddWithValue("s",subject);
        await using var reader=await cmd.ExecuteReaderAsync(ct); if(!await reader.ReadAsync(ct)) return Results.Ok(new { data=(object?)null });
        return Results.Ok(new { data=new { id=reader.GetString(0),clinicName=reader.GetString(1),status=reader.GetString(2),reviewNote=reader.IsDBNull(3)?null:reader.GetString(3),createdAt=reader.GetDateTime(4),reviewedAt=reader.IsDBNull(5)?null:(DateTime?)reader.GetDateTime(5) } });
    }

    private static async Task<IResult> ListPendingAsync(HttpContext ctx, DentalConnectDbContext db, CancellationToken ct)
    {
        if(!IsPlatformAdmin(ctx)) return Results.Forbid(); var conn=(NpgsqlConnection)db.Database.GetDbConnection(); await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("""SELECT "id","ownerEmail","clinicName","phone","address","municipality","state","postalCode","status","createdAt" FROM "ClinicApplication" WHERE "status" IN ('PENDING_REVIEW','PROVISIONING') ORDER BY "createdAt" ASC""",conn);
        await using var reader=await cmd.ExecuteReaderAsync(ct); var rows=new List<object>(); while(await reader.ReadAsync(ct)) rows.Add(new { id=reader.GetString(0),ownerEmail=reader.GetString(1),clinicName=reader.GetString(2),phone=reader.GetString(3),address=reader.GetString(4),municipality=reader.GetString(5),state=reader.GetString(6),postalCode=reader.GetString(7),status=reader.GetString(8),createdAt=reader.GetDateTime(9) });
        return Results.Ok(new { data=rows });
    }

    private static async Task<IResult> ApproveAsync(string id, HttpContext ctx, DentalConnectDbContext db, IAmazonCognitoIdentityProvider cognito, CognitoOptions options, CancellationToken ct)
    {
        if(!IsPlatformAdmin(ctx)) return Results.Forbid(); if(!CuidGenerator.IsValid(id)) return Results.NotFound(); var reviewer=ctx.User.FindFirst("sub")?.Value??""; var conn=(NpgsqlConnection)db.Database.GetDbConnection(); await conn.OpenAsync(ct);
        string subject,email,clinicName,phone,address,municipality,state,postal,clinicId;
        await using(var tx=await conn.BeginTransactionAsync(ct))
        {
            await using var read=new NpgsqlCommand("""SELECT "ownerSubject","ownerEmail","clinicName","phone","address","municipality","state","postalCode","status","clinicId" FROM "ClinicApplication" WHERE "id"=@id FOR UPDATE""",conn,tx); read.Parameters.AddWithValue("id",id);
            await using var r=await read.ExecuteReaderAsync(ct); if(!await r.ReadAsync(ct)) return Results.NotFound();
            subject=r.GetString(0); email=r.GetString(1); clinicName=r.GetString(2); phone=r.GetString(3); address=r.GetString(4); municipality=r.GetString(5); state=r.GetString(6); postal=r.GetString(7); var status=r.GetString(8); clinicId=r.IsDBNull(9)?CuidGenerator.NewId():r.GetString(9); await r.CloseAsync();
            if(status=="PENDING_REVIEW")
            {
                await using var clinic=new NpgsqlCommand("""INSERT INTO "Clinic" ("id","name","description","phone","email","address","municipality","state","postalCode","isActive","createdAt","updatedAt") VALUES (@id,@name,'Consultorio registrado en DentalConnect',@phone,@email,@address,@city,@state,@postal,false,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)""",conn,tx);
                clinic.Parameters.AddWithValue("id",clinicId); clinic.Parameters.AddWithValue("name",clinicName); clinic.Parameters.AddWithValue("phone",phone); clinic.Parameters.AddWithValue("email",email); clinic.Parameters.AddWithValue("address",address); clinic.Parameters.AddWithValue("city",municipality); clinic.Parameters.AddWithValue("state",state); clinic.Parameters.AddWithValue("postal",postal); await clinic.ExecuteNonQueryAsync(ct);
                await using var member=new NpgsqlCommand("""INSERT INTO "ClinicMembership" ("id","userSubject","clinicId","role","createdAt") VALUES (@id,@s,@c,'CLINIC_ADMIN',CURRENT_TIMESTAMP)""",conn,tx); member.Parameters.AddWithValue("id",CuidGenerator.NewId()); member.Parameters.AddWithValue("s",subject); member.Parameters.AddWithValue("c",clinicId); await member.ExecuteNonQueryAsync(ct);
                await using var update=new NpgsqlCommand("""UPDATE "ClinicApplication" SET "status"='PROVISIONING',"clinicId"=@c,"reviewedBy"=@r,"reviewedAt"=CURRENT_TIMESTAMP WHERE "id"=@id""",conn,tx); update.Parameters.AddWithValue("c",clinicId); update.Parameters.AddWithValue("r",reviewer); update.Parameters.AddWithValue("id",id); await update.ExecuteNonQueryAsync(ct);
            }
            else if(status!="PROVISIONING"&&status!="APPROVED") return Results.Conflict(new { error=new { code="INVALID_TRANSITION",message="La solicitud ya fue revisada" } });
            await tx.CommitAsync(ct);
        }
        try { await cognito.AdminAddUserToGroupAsync(new AdminAddUserToGroupRequest { UserPoolId=options.UserPoolId,Username=email,GroupName="CLINIC_ADMIN" },ct); }
        catch(AmazonCognitoIdentityProviderException) { return Results.Json(new { error=new { code="ROLE_ASSIGNMENT_PENDING",message="La solicitud está en aprovisionamiento; reintenta la aprobación cuando Cognito esté disponible" } },statusCode:503); }
        await using(var finish=new NpgsqlCommand("""UPDATE "Clinic" SET "isActive"=true,"updatedAt"=CURRENT_TIMESTAMP WHERE "id"=@c; UPDATE "ClinicApplication" SET "status"='APPROVED',"reviewedAt"=CURRENT_TIMESTAMP WHERE "id"=@id""",conn)) { finish.Parameters.AddWithValue("c",clinicId); finish.Parameters.AddWithValue("id",id); await finish.ExecuteNonQueryAsync(ct); }
        return Results.Ok(new { data=new { id,clinicId,status="APPROVED" } });
    }

    private static async Task<IResult> RejectAsync(string id, HttpContext ctx, ClinicReviewRequest? body, DentalConnectDbContext db, CancellationToken ct)
    {
        if(!IsPlatformAdmin(ctx)) return Results.Forbid(); var reason=body?.Reason?.Trim(); if(string.IsNullOrWhiteSpace(reason)||reason.Length>250) return Results.BadRequest(new { error=new { code="VALIDATION_ERROR",message="Indica un motivo de rechazo de hasta 250 caracteres" } });
        var conn=(NpgsqlConnection)db.Database.GetDbConnection(); await conn.OpenAsync(ct); await using var cmd=new NpgsqlCommand("""UPDATE "ClinicApplication" SET "status"='REJECTED',"reviewNote"=@reason,"reviewedBy"=@r,"reviewedAt"=CURRENT_TIMESTAMP WHERE "id"=@id AND "status"='PENDING_REVIEW'""",conn); cmd.Parameters.AddWithValue("reason",reason); cmd.Parameters.AddWithValue("r",ctx.User.FindFirst("sub")?.Value??""); cmd.Parameters.AddWithValue("id",id);
        return await cmd.ExecuteNonQueryAsync(ct)==1?Results.Ok(new { data=new { id,status="REJECTED" } }):Results.Conflict(new { error=new { code="INVALID_TRANSITION",message="La solicitud ya no está pendiente" } });
    }
}
