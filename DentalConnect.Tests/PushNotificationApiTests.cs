using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DentalConnect.Tests;

[Collection("Postgres API")]
public sealed class PushNotificationApiTests(PostgresApiFixture fixture)
{
    private readonly PostgresApiFixture _fixture = fixture;

    [PostgresFact]
    public async Task Subscription_CanBeRegisteredCheckedAndRemovedByItsOwner()
    {
        if (!_fixture.Available) return;
        var subject = "push-patient-" + Guid.NewGuid().ToString("N");
        var endpoint = "https://fcm.googleapis.com/fcm/send/" + Guid.NewGuid().ToString("N");
        using var client = NewClient(subject);
        try
        {
            using var save = await client.PutAsJsonAsync("/api/push/subscriptions", new
            {
                endpoint,
                keys = new { p256dh = "test-public-key", auth = "test-auth-key" }
            });
            Assert.Equal(HttpStatusCode.OK, save.StatusCode);

            using var status = await client.GetAsync("/api/push/subscriptions/me");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            using var statusJson = await JsonDocument.ParseAsync(await status.Content.ReadAsStreamAsync());
            Assert.True(statusJson.RootElement.GetProperty("data").GetProperty("enabled").GetBoolean());
            Assert.Equal(1, statusJson.RootElement.GetProperty("data").GetProperty("devices").GetInt32());

            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/push/subscriptions")
            {
                Content = JsonContent.Create(new { endpoint })
            };
            using var delete = await client.SendAsync(deleteRequest);
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

            using var finalStatus = await client.GetAsync("/api/push/subscriptions/me");
            using var finalJson = await JsonDocument.ParseAsync(await finalStatus.Content.ReadAsStreamAsync());
            Assert.False(finalJson.RootElement.GetProperty("data").GetProperty("enabled").GetBoolean());
        }
        finally
        {
            using var scope = _fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
            await db.WebPushSubscriptions.Where(s => s.RecipientKey == subject).ExecuteDeleteAsync();
        }
    }

    [PostgresFact]
    public async Task Subscription_RejectsEndpointsOutsideSupportedPushProviders()
    {
        if (!_fixture.Available) return;
        using var client = NewClient("push-patient-" + Guid.NewGuid().ToString("N"));
        using var response = await client.PutAsJsonAsync("/api/push/subscriptions", new
        {
            endpoint = "https://attacker.example.test/collect",
            keys = new { p256dh = "test-public-key", auth = "test-auth-key" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [PostgresFact]
    public async Task SubscriptionEndpoints_RequireAuthentication()
    {
        if (!_fixture.Available) return;
        using var client = _fixture.Factory!.CreateClient();
        using var response = await client.GetAsync("/api/push/subscriptions/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient NewClient(string subject)
    {
        var client = _fixture.Factory!.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, subject);
        return client;
    }
}
