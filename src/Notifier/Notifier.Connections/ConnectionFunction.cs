using Amazon.Lambda.APIGatewayEvents;

namespace Notifier.Connections;

public sealed class ConnectionFunction
{
    private readonly ConnectionRegistry _registry;

    public ConnectionFunction(ConnectionRegistry registry) => _registry = registry;

    public const string ConnectRoute = "$connect";
    public const string DisconnectRoute = "$disconnect";

    public async Task<APIGatewayProxyResponse> HandleAsync(APIGatewayProxyRequest request)
    {
        string connectionId = request.RequestContext.ConnectionId;

        // One Lambda serves both routes. API Gateway sets the route key.
        switch (request.RequestContext.RouteKey)
        {
            case ConnectRoute:
                await _registry.AddAsync(connectionId, CancellationToken.None);
                break;

            case DisconnectRoute:
                await _registry.RemoveAsync(connectionId, CancellationToken.None);
                break;
        }

        return Ok();
    }

    /// <summary>
    /// A non-200 from $connect makes API Gateway refuse the handshake, so the
    /// browser never connects. A non-200 from $disconnect is logged and
    /// otherwise ignored, because the socket has already closed.
    /// </summary>
    private static APIGatewayProxyResponse Ok() => new() { StatusCode = 200 };
}
