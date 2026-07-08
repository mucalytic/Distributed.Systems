using Messages;
using Soenneker.Utils.AutoBogus;

var builder = WebApplication.CreateBuilder(args);

// This endpoint only SENDS messages — it has no handlers, so it needs no input
// queue of its own. "SendOnly" makes that explicit.
var endpointConfiguration = new EndpointConfiguration("orders-api");
endpointConfiguration.SendOnly();
endpointConfiguration.UseSerialization<SystemJsonSerializer>();

var transport = new AzureServiceBusTransport(
    builder.Configuration["ServiceBus:ConnectionString"] ?? throw new InvalidOperationException("ServiceBus:ConnectionString not configured"),
    TopicTopology.Default);
var routing = endpointConfiguration.UseTransport(transport);

// Commands are point-to-point: the sender must know the destination endpoint.
routing.RouteToEndpoint(typeof(PlaceOrder), "orders-worker");

builder.Services.AddNServiceBusEndpoint(endpointConfiguration);

var app = builder.Build();

app.MapGet("/", () => "Orders API is running. POST /orders to place a fake order.");

app.MapPost("/orders", async (IMessageSession messageSession) =>
{
    var lineFaker = new AutoFaker<OrderLine>()
        .RuleFor(l => l.ProductName, f => f.Commerce.ProductName())
        .RuleFor(l => l.Quantity, f => f.Random.Int(1, 5))
        .RuleFor(l => l.UnitPrice, f => f.Finance.Amount(1, 200));

    var order = new AutoFaker<PlaceOrder>()
        .RuleFor(o => o.OrderId, _ => Guid.NewGuid())
        .RuleFor(o => o.CustomerName, f => f.Name.FullName())
        .RuleFor(o => o.CustomerEmail, f => f.Internet.Email())
        .RuleFor(o => o.Lines, f => lineFaker.Generate(f.Random.Int(1, 4)))
        .Generate();

    await messageSession.Send(order);

    // 202 Accepted, not 201 Created: the order does not exist yet anywhere.
    // We've handed a durable message to the broker and that's ALL we know.
    // This is eventual consistency made visible at the HTTP layer.
    return Results.Accepted($"/orders/{order.OrderId}", new
    {
        order.OrderId,
        order.CustomerName,
        Status = "Accepted — processing asynchronously",
    });
});

app.Run();
