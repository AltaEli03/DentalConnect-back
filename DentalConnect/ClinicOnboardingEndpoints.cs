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
        app.MapPost("/api/clinic-applications/mine/publish", PublishMineAsync).RequireAuthorization();
        app.MapGet("/api/platform/clinic-applications", ListPendingAsync).RequireAuthorization();
        app.MapPost("/api/platform/clinic-applications/{id}/approve", ApproveAsync).RequireAuthorization();
        app.MapPost("/api/platform/clinic-applications/{id}/reject", RejectAsync).RequireAuthorization();
        return app;
    }

    public static WebApplication UseClinicMembershipClaims(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var sub = context.User.FindFirst("sub")?.Value;
            if (sub is not null && context.User.Identity is ClaimsIdentity identity &&
                !identity.HasClaim(claim => claim.Type == "clinicId"))
            {
                using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
                var connection = (NpgsqlConnection)db.Database.GetDbConnection(); await connection.OpenAsync(context.RequestAborted);
                await using var command = new NpgsqlCommand("""SELECT "clinicId" FROM "ClinicMembership" WHERE "userSubject" = @subject LIMIT 1""", connection);
                command.Parameters.AddWithValue("subject", sub); var clinicId = (string?)await command.ExecuteScalarAsync(context.RequestAborted);
                if (!string.IsNullOrWhiteSpace(clinicId))
                {
                    identity.AddClaim(new Claim("clinicId", clinicId)); identity.AddClaim(new Claim("custom:clinicId", clinicId));
                    identity.AddClaim(new Claim("clinic_id", clinicId)); identity.AddClaim(new Claim("custom:clinic_id", clinicId));
                    identity.AddClaim(new Claim("cognito:groups", "CLINIC_ADMIN"));
                }
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
        var id=CuidGenerator.NewId(); var clinicId=CuidGenerator.NewId();
        await using var tx=await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var userLock=new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@subject, 0))",conn,tx))
            { userLock.Parameters.AddWithValue("subject",subject); await userLock.ExecuteNonQueryAsync(ct); }
            await using(var check=new NpgsqlCommand("""SELECT 1 FROM "ClinicMembership" WHERE "userSubject"=@s UNION ALL SELECT 1 FROM "ClinicApplication" WHERE "ownerSubject"=@s AND "status" IN ('PENDING_REVIEW','PROVISIONING','APPROVED') LIMIT 1""",conn,tx))
            { check.Parameters.AddWithValue("s",subject); if(await check.ExecuteScalarAsync(ct)is not null) return Results.Conflict(new { error=new { code="APPLICATION_EXISTS",message="Tu cuenta ya tiene un consultorio registrado" } }); }
            await using var clinic=new NpgsqlCommand("""INSERT INTO "Clinic" ("id","name","description","phone","email","address","municipality","state","postalCode","isActive","createdAt","updatedAt") VALUES (@id,@name,'Consultorio registrado en DentalConnect',@phone,@email,@address,@city,@state,@postal,true,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)""",conn,tx);
            clinic.Parameters.AddWithValue("id",clinicId); clinic.Parameters.AddWithValue("name",name); clinic.Parameters.AddWithValue("phone",phone); clinic.Parameters.AddWithValue("email",email); clinic.Parameters.AddWithValue("address",address); clinic.Parameters.AddWithValue("city",municipality); clinic.Parameters.AddWithValue("state",state); clinic.Parameters.AddWithValue("postal",postal); await clinic.ExecuteNonQueryAsync(ct);
            await using var member=new NpgsqlCommand("""INSERT INTO "ClinicMembership" ("id","userSubject","clinicId","role","createdAt") VALUES (@id,@s,@c,'CLINIC_ADMIN',CURRENT_TIMESTAMP)""",conn,tx);
            member.Parameters.AddWithValue("id",CuidGenerator.NewId()); member.Parameters.AddWithValue("s",subject); member.Parameters.AddWithValue("c",clinicId); await member.ExecuteNonQueryAsync(ct);
            await using var insert=new NpgsqlCommand("""INSERT INTO "ClinicApplication" ("id","ownerSubject","ownerEmail","clinicName","phone","address","municipality","state","postalCode","status","clinicId","reviewedAt","createdAt") VALUES (@id,@s,@e,@n,@p,@a,@m,@st,@pc,'APPROVED',@c,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)""",conn,tx);
            insert.Parameters.AddWithValue("id",id); insert.Parameters.AddWithValue("s",subject); insert.Parameters.AddWithValue("e",email); insert.Parameters.AddWithValue("n",name); insert.Parameters.AddWithValue("p",phone); insert.Parameters.AddWithValue("a",address); insert.Parameters.AddWithValue("m",municipality); insert.Parameters.AddWithValue("st",state); insert.Parameters.AddWithValue("pc",postal); insert.Parameters.AddWithValue("c",clinicId); await insert.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState is "23505" or "40001")
        { await tx.RollbackAsync(ct); return Results.Conflict(new { error=new { code="APPLICATION_EXISTS",message="Tu cuenta ya tiene un consultorio registrado" } }); }
        return Results.Created("/api/clinic-applications/mine",new { data=new { id,clinicId,status="APPROVED" } });
    }

    private static async Task<IResult> MineAsync(HttpContext ctx, DentalConnectDbContext db, CancellationToken ct)
    {
        var subject=ctx.User.FindFirst("sub")?.Value; if(subject is null) return Results.Unauthorized(); var conn=(NpgsqlConnection)db.Database.GetDbConnection(); await conn.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("""SELECT "id","clinicId","clinicName","status","reviewNote","createdAt","reviewedAt" FROM "ClinicApplication" WHERE "ownerSubject"=@s ORDER BY "createdAt" DESC LIMIT 1""",conn); cmd.Parameters.AddWithValue("s",subject);
        await using var reader=await cmd.ExecuteReaderAsync(ct); if(!await reader.ReadAsync(ct)) return Results.Ok(new { data=(object?)null });
        return Results.Ok(new { data=new { id=reader.GetString(0),clinicId=reader.IsDBNull(1)?null:reader.GetString(1),clinicName=reader.GetString(2),status=reader.GetString(3),reviewNote=reader.IsDBNull(4)?null:reader.GetString(4),createdAt=reader.GetDateTime(5),reviewedAt=reader.IsDBNull(6)?null:(DateTime?)reader.GetDateTime(6) } });
    }

    private static async Task<IResult> PublishMineAsync(HttpContext ctx, DentalConnectDbContext db, CancellationToken ct)
    {
        var subject=ctx.User.FindFirst("sub")?.Value;
        var email=ctx.User.FindFirst("email")?.Value?.Trim().ToLowerInvariant();
        if(string.IsNullOrWhiteSpace(subject)||ctx.User.FindFirst("email_verified")?.Value?.Equals("true",StringComparison.OrdinalIgnoreCase)!=true) return Results.Unauthorized();
        var conn=(NpgsqlConnection)db.Database.GetDbConnection(); await conn.OpenAsync(ct);
        await using var tx=await conn.BeginTransactionAsync(ct);
        string applicationId,ownerEmail,name,phone,address,municipality,state,postal,status;
        string? clinicId;
        await using(var read=new NpgsqlCommand("""SELECT "id","ownerEmail","clinicName","phone","address","municipality","state","postalCode","status","clinicId" FROM "ClinicApplication" WHERE "ownerSubject"=@subject ORDER BY "createdAt" DESC LIMIT 1 FOR UPDATE""",conn,tx))
        {
            read.Parameters.AddWithValue("subject",subject); await using var row=await read.ExecuteReaderAsync(ct);
            if(!await row.ReadAsync(ct)) return Results.NotFound(new { error=new { code="APPLICATION_NOT_FOUND",message="No encontramos un registro de consultorio para esta cuenta" } });
            applicationId=row.GetString(0); ownerEmail=row.GetString(1); name=row.GetString(2); phone=row.GetString(3); address=row.GetString(4); municipality=row.GetString(5); state=row.GetString(6); postal=row.GetString(7); status=row.GetString(8); clinicId=row.IsDBNull(9)?null:row.GetString(9);
        }
        if(status=="REJECTED") return Results.Conflict(new { error=new { code="APPLICATION_REJECTED",message="El registro fue rechazado; contacta al soporte para solicitar una corrección" } });
        if(status is not ("PENDING_REVIEW" or "PROVISIONING" or "APPROVED")) return Results.Conflict(new { error=new { code="INVALID_TRANSITION",message="No es posible publicar este registro" } });
        clinicId ??= CuidGenerator.NewId();
        await using(var existingMembership=new NpgsqlCommand("SELECT \"clinicId\" FROM \"ClinicMembership\" WHERE \"userSubject\"=@subject LIMIT 1",conn,tx))
        {
            existingMembership.Parameters.AddWithValue("subject",subject);
            var currentClinic=await existingMembership.ExecuteScalarAsync(ct) as string;
            if(currentClinic is not null && currentClinic!=clinicId) return Results.Conflict(new { error=new { code="CLINIC_ALREADY_LINKED",message="Esta cuenta ya administra otro consultorio" } });
        }
        var ownerEmailForClinic=string.IsNullOrWhiteSpace(ownerEmail)?email!:ownerEmail;
        await using(var clinic=new NpgsqlCommand("""INSERT INTO "Clinic" ("id","name","description","phone","email","address","municipality","state","postalCode","isActive","createdAt","updatedAt") VALUES (@id,@name,'Consultorio registrado en DentalConnect',@phone,@email,@address,@city,@state,@postal,true,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP) ON CONFLICT ("id") DO UPDATE SET "isActive"=true,"updatedAt"=CURRENT_TIMESTAMP""",conn,tx))
        { clinic.Parameters.AddWithValue("id",clinicId); clinic.Parameters.AddWithValue("name",name); clinic.Parameters.AddWithValue("phone",phone); clinic.Parameters.AddWithValue("email",ownerEmailForClinic); clinic.Parameters.AddWithValue("address",address); clinic.Parameters.AddWithValue("city",municipality); clinic.Parameters.AddWithValue("state",state); clinic.Parameters.AddWithValue("postal",postal); await clinic.ExecuteNonQueryAsync(ct); }
        await using(var membership=new NpgsqlCommand("""INSERT INTO "ClinicMembership" ("id","userSubject","clinicId","role","createdAt") VALUES (@id,@subject,@clinicId,'CLINIC_ADMIN',CURRENT_TIMESTAMP) ON CONFLICT ("userSubject") DO NOTHING""",conn,tx))
        { membership.Parameters.AddWithValue("id",CuidGenerator.NewId()); membership.Parameters.AddWithValue("subject",subject); membership.Parameters.AddWithValue("clinicId",clinicId); await membership.ExecuteNonQueryAsync(ct); }
        await using(var update=new NpgsqlCommand("""UPDATE "ClinicApplication" SET "status"='APPROVED',"clinicId"=@clinicId,"reviewedAt"=CURRENT_TIMESTAMP WHERE "id"=@id""",conn,tx))
        { update.Parameters.AddWithValue("clinicId",clinicId); update.Parameters.AddWithValue("id",applicationId); await update.ExecuteNonQueryAsync(ct); }
        await tx.CommitAsync(ct);
        return Results.Ok(new { data=new { id=applicationId,clinicId,status="APPROVED" } });
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
