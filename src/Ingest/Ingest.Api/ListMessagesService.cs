using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Ingest.Storage;

namespace Ingest.Api;

public sealed record MessageResponse(Guid MessageId, string Text, DateTimeOffset SubmittedAt);

public sealed class ListMessagesService
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;

    public ListMessagesService(IAmazonDynamoDB dynamoDb, string tableName)
    {
        _dynamoDb = dynamoDb;
        _tableName = tableName;
    }

    /// <summary>
    /// Every stored message.
    /// </summary>
    /// <remarks>
    /// A scan, because the table is keyed by message id and there is no index
    /// that groups every message together. A scan is the wrong tool at volume;
    /// at POC scale the table holds one row per message ever sent, so it costs
    /// almost nothing. Real history at volume would need a partition to query.
    ///
    /// The filter keeps the OUTBOX rows out. Both item types share a table, so
    /// an unfiltered scan would return every message twice.
    /// </remarks>
    public async Task<IReadOnlyList<MessageResponse>> ListAsync(
        CancellationToken cancellationToken)
    {
        ScanResponse response = await _dynamoDb.ScanAsync(
            new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = "#sortKey = :domainItem",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#sortKey"] = MessageItems.SortKey,
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":domainItem"] = new(MessageItems.DomainSortKey),
                },
            },
            cancellationToken);

        return response.Items
            .Select(item => new MessageResponse(
                MessageId: Guid.Parse(item[MessageItems.MessageIdAttribute].S),
                Text: item[MessageItems.TextAttribute].S,
                SubmittedAt: DateTimeOffset.Parse(item[MessageItems.SubmittedAtAttribute].S)))
            // A scan returns rows in NO defined order. The SPA appends new
            // messages to the end of its list, so history must arrive oldest
            // first or the two halves of the list disagree.
            //
            // DynamoDB Local happens to return a favourable order, so the test
            // for this cannot fail without the sort. Real DynamoDB gives no
            // such guarantee, which is why the sort is explicit.
            .OrderBy(message => message.SubmittedAt)
            .ToList();
    }
}
