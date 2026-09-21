using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Notifier.Connections;

public sealed class ConnectionRegistry
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;

    public ConnectionRegistry(IAmazonDynamoDB dynamoDb, string tableName, TimeProvider timeProvider)
    {
        _dynamoDb = dynamoDb;
        _tableName = tableName;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// API Gateway closes a WebSocket connection after two hours, so a row
    /// older than that is certainly dead. The TTL removes any row whose
    /// $disconnect never arrived.
    /// </summary>
    public static readonly TimeSpan ConnectionLifetime = TimeSpan.FromHours(2);

    public Task AddAsync(string connectionId, CancellationToken cancellationToken) =>
        _dynamoDb.PutItemAsync(
            _tableName,
            new Dictionary<string, AttributeValue>
            {
                [ConnectionItem.PartitionKey] = new(connectionId),
                [ConnectionItem.ExpiresAtAttribute] = new AttributeValue
                {
                    // Epoch SECONDS. DynamoDB ignores milliseconds in silence.
                    N = _timeProvider.GetUtcNow().Add(ConnectionLifetime)
                        .ToUnixTimeSeconds().ToString(),
                },
            },
            cancellationToken);

    public Task RemoveAsync(string connectionId, CancellationToken cancellationToken) =>
        _dynamoDb.DeleteItemAsync(
            _tableName,
            new Dictionary<string, AttributeValue>
            {
                [ConnectionItem.PartitionKey] = new(connectionId),
            },
            cancellationToken);
}
