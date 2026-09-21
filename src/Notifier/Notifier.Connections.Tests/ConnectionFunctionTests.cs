using Notifier.TestSupport;
using Notifier.Storage;
using System.Net;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Notifier.Connections;

namespace Notifier.Connections.Tests;

/// <summary>
/// Handler contract tests: build the real API Gateway event, invoke the real
/// entry point, assert the real response. There is no ASP.NET pipeline in
/// front of this in production (design D13).
/// </summary>
public class ConnectionFunctionTests : IClassFixture<ConnectionsTableFixture>
{
    private readonly ConnectionsTableFixture _table;
    private readonly ConnectionFunction _function;

    public ConnectionFunctionTests(ConnectionsTableFixture table)
    {
        _table = table;

        FakeTimeProvider timeProvider = new();
        timeProvider.SetUtcNow(new DateTimeOffset(2026, 9, 18, 9, 0, 0, TimeSpan.Zero));

        _function = new ConnectionFunction(
            new ConnectionRegistry(
                table.Client,
                ConnectionsTableFixture.TableName,
                timeProvider));
    }

    private static APIGatewayProxyRequest SocketRequest(string routeKey, string connectionId) =>
        new()
        {
            RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
            {
                RouteKey = routeKey,
                ConnectionId = connectionId,
            },
        };

    private async Task<bool> ExistsAsync(string connectionId)
    {
        GetItemResponse response = await _table.Client.GetItemAsync(new GetItemRequest
        {
            TableName = ConnectionsTableFixture.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [ConnectionItem.PartitionKey] = new(connectionId),
            },
        });

        return response.IsItemSet;
    }

    [Fact]
    public async Task Connect_stores_the_connection_and_returns_200()
    {
        APIGatewayProxyResponse response =
            await _function.HandleAsync(SocketRequest("$connect", "socket-one"));

        // A non-200 from $connect makes API Gateway refuse the handshake, so
        // the browser never connects at all.
        response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        (await ExistsAsync("socket-one")).Should().BeTrue();
    }

    [Fact]
    public async Task Disconnect_removes_the_connection_and_returns_200()
    {
        await _function.HandleAsync(SocketRequest("$connect", "socket-two"));

        // One Lambda serves both routes, so it must branch on the route key.
        // Without the branch, $disconnect re-adds the row it should remove.
        APIGatewayProxyResponse response =
            await _function.HandleAsync(SocketRequest("$disconnect", "socket-two"));

        response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        (await ExistsAsync("socket-two")).Should().BeFalse();
    }
}
