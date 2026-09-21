using System.Text.Json;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Contracts;
using Notifier.Storage;

namespace Notifier.Consumer;

public sealed class BroadcastService
{
    private readonly IAmazonApiGatewayManagementApi _socketApi;
    private readonly ConnectionRegistry _registry;

    public BroadcastService(IAmazonApiGatewayManagementApi socketApi, ConnectionRegistry registry)
    {
        _socketApi = socketApi;
        _registry = registry;
    }

    public async Task BroadcastAsync(TextSubmitted submitted, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> connections = await _registry.ListAsync(cancellationToken);

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(submitted, EventJson.Options);

        foreach (string connectionId in connections)
        {
            try
            {
                await _socketApi.PostToConnectionAsync(
                    new PostToConnectionRequest
                    {
                        ConnectionId = connectionId,

                        // A new stream per connection. PostToConnectionAsync
                        // reads the stream to its end, so one shared stream
                        // would be empty for every connection after the first.
                        Data = new MemoryStream(payload),
                    },
                    cancellationToken);
            }
            catch (GoneException)
            {
                // The browser closed without a clean $disconnect. Delete the
                // row and carry on: one dead connection must not stop the
                // live ones receiving the event.
                await _registry.RemoveAsync(connectionId, cancellationToken);
            }
        }
    }
}
