using Messages;
using Microsoft.Azure.Cosmos;

namespace Billing.Worker;

/// <summary>
/// Billing knows nothing about the Orders service — it only knows the
/// OrderPlaced event contract. NServiceBus created a subscription on the
/// topic for this endpoint automatically (because this handler exists).
/// </summary>
public class OrderPlacedHandler(CosmosClient cosmos, ILogger<OrderPlacedHandler> logger)
    : IHandleMessages<OrderPlaced>
{
    public async Task Handle(OrderPlaced message, IMessageHandlerContext context)
    {
        logger.LogInformation("OrderPlaced received for {OrderId}, amount {Amount:C}",
            message.OrderId, message.TotalAmount);

        // Same idempotency trick: the invoice id is DERIVED from the order id,
        // so redelivery of this event can never produce two invoices.
        var invoice = new InvoiceDocument(
            id: $"inv-{message.OrderId}",
            orderId: message.OrderId.ToString(),
            customerEmail: message.CustomerEmail,
            amount: message.TotalAmount,
            status: "Charged");

        var container = cosmos.GetContainer("ordersdb", "invoices");
        await container.UpsertItemAsync(invoice, new PartitionKey(invoice.id),
            cancellationToken: context.CancellationToken);

        logger.LogInformation("Invoice {InvoiceId} written for order {OrderId}",
            invoice.id, message.OrderId);
    }
}

public record InvoiceDocument(
    string id,
    string orderId,
    string customerEmail,
    decimal amount,
    string status);
