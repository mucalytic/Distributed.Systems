using Messages;
using Microsoft.Azure.Cosmos;

namespace Orders.Worker;

public class PlaceOrderHandler(CosmosClient cosmos, ILogger<PlaceOrderHandler> logger)
    : IHandleMessages<PlaceOrder>
{
    public async Task Handle(PlaceOrder message, IMessageHandlerContext context)
    {
        logger.LogInformation("Processing PlaceOrder {OrderId} for {Customer}",
            message.OrderId, message.CustomerName);

        var total = message.Lines.Sum(l => l.Quantity * l.UnitPrice);

        // Azure Service Bus gives AT-LEAST-ONCE delivery: this handler may run
        // more than once for the same message (retry after a crash, lock lost,
        // etc.). We make that safe with NATURAL IDEMPOTENCY: the document id is
        // derived from the message (OrderId), and Upsert means "create or
        // overwrite with the same data" — running twice converges to the same
        // state instead of creating a duplicate order.
        var document = new OrderDocument(
            id: message.OrderId.ToString(),
            customerName: message.CustomerName,
            customerEmail: message.CustomerEmail,
            lines: message.Lines,
            total: total,
            status: "Placed");

        var container = cosmos.GetContainer("ordersdb", "orders");
        await container.UpsertItemAsync(document, new PartitionKey(document.id),
            cancellationToken: context.CancellationToken);

        logger.LogInformation("Order {OrderId} persisted, total {Total:C}. Publishing OrderPlaced.",
            message.OrderId, total);

        // Publish the EVENT. Anyone subscribed (today: Billing) reacts.
        // Tomorrow a Shipping or Loyalty service could subscribe without this
        // line — or this service — changing at all. That's the decoupling win.
        await context.Publish(new OrderPlaced
        {
            OrderId = message.OrderId,
            CustomerEmail = message.CustomerEmail,
            TotalAmount = total,
        });
    }
}

public record OrderDocument(
    string id,
    string customerName,
    string customerEmail,
    List<OrderLine> lines,
    decimal total,
    string status);
