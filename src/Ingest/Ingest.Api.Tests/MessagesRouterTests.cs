using System.Net;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using AwesomeAssertions;
using Ingest.Api;
using Ingest.Storage;
using Microsoft.Extensions.Time.Testing;

namespace Ingest.Api.Tests;

/// <summary>
/// Handler contract tests for the router. One Lambda serves every route on
/// the messages resource, so it must dispatch on the method and the path.
/// </summary>
public class MessagesRouterTests : IClassFixture<DynamoDbFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Midday =
        new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly DynamoDbFixture _table;
    private readonly MessagesRouter _router;

    public MessagesRouterTests(DynamoDbFixture table)
    {
        _table = table;

        FakeTimeProvider timeProvider = new();
        timeProvider.SetUtcNow(Midday);

        _router = new MessagesRouter(
            new SubmitMessageFunction(table.Client, DynamoDbFixture.TableName, timeProvider),
            new ListMessagesService(table.Client, DynamoDbFixture.TableName));
    }

    public Task InitializeAsync() => _table.ClearAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static APIGatewayHttpApiV2ProxyRequest Request(
        string method,
        string path,
        string? body = null) =>
        new()
        {
            Body = body,
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription
                {
                    Method = method,
                    Path = path,
                },
            },
        };

    [Fact]
    public async Task Get_messages_returns_what_was_submitted()
    {
        await _router.HandleAsync(
            Request("POST", "/messages", """{"text":"stored earlier"}"""));

        APIGatewayHttpApiV2ProxyResponse response =
            await _router.HandleAsync(Request("GET", "/messages"));

        response.StatusCode.Should().Be((int)HttpStatusCode.OK);

        MessageResponse[] messages =
            JsonSerializer.Deserialize<MessageResponse[]>(
                response.Body, SubmitMessageFunction.JsonOptions)!;

        messages.Should().ContainSingle()
            .Which.Text.Should().Be("stored earlier");
    }
}
