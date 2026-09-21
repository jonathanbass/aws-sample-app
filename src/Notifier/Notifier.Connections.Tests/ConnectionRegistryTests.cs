using Amazon.DynamoDBv2.Model;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Notifier.Connections;

namespace Notifier.Connections.Tests;

public class ConnectionRegistryTests : IClassFixture<ConnectionsTableFixture>
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly ConnectionsTableFixture _table;
    private readonly ConnectionRegistry _registry;

    public ConnectionRegistryTests(ConnectionsTableFixture table)
    {
        _table = table;

        FakeTimeProvider timeProvider = new();
        timeProvider.SetUtcNow(Now);

        _registry = new ConnectionRegistry(
            table.Client,
            ConnectionsTableFixture.TableName,
            timeProvider);
    }

    private async Task<Dictionary<string, AttributeValue>?> ReadAsync(string connectionId)
    {
        GetItemResponse response = await _table.Client.GetItemAsync(new GetItemRequest
        {
            TableName = ConnectionsTableFixture.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [ConnectionItem.PartitionKey] = new(connectionId),
            },
        });

        return response.IsItemSet ? response.Item : null;
    }

    [Fact]
    public async Task AddAsync_stores_the_connection()
    {
        await _registry.AddAsync("connection-one", CancellationToken.None);

        Dictionary<string, AttributeValue>? stored = await ReadAsync("connection-one");

        stored.Should().NotBeNull();
        stored![ConnectionItem.PartitionKey].S.Should().Be("connection-one");
    }

    [Fact]
    public async Task AddAsync_expires_the_connection_after_two_hours()
    {
        await _registry.AddAsync("connection-two", CancellationToken.None);

        Dictionary<string, AttributeValue>? stored = await ReadAsync("connection-two");

        // API Gateway closes a WebSocket connection after two hours, so a row
        // older than that is certainly dead. Without the TTL, every connection
        // whose $disconnect never fired stays in the table for ever, and the
        // Phase 5 broadcast tries to push to all of them.
        //
        // DynamoDB TTL reads epoch SECONDS. It ignores milliseconds in silence
        // and the row then never expires.
        long expected = Now.AddHours(2).ToUnixTimeSeconds();

        stored![ConnectionItem.ExpiresAtAttribute].N.Should().Be(expected.ToString());
    }

    [Fact]
    public async Task RemoveAsync_deletes_the_connection()
    {
        await _registry.AddAsync("connection-three", CancellationToken.None);

        await _registry.RemoveAsync("connection-three", CancellationToken.None);

        (await ReadAsync("connection-three")).Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_accepts_a_connection_that_is_not_there()
    {
        // $disconnect can arrive for a connection the table never held, or
        // arrive twice. A throw here fails the Lambda and API Gateway retries,
        // which achieves nothing.
        Func<Task> remove = () => _registry.RemoveAsync("never-stored", CancellationToken.None);

        await remove.Should().NotThrowAsync();
    }
}
