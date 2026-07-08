namespace Messages;

/// <summary>
/// A command: an instruction to a single, known logical endpoint ("do this").
/// Commands are SENT point-to-point via a queue.
/// </summary>
public class PlaceOrder : ICommand
{
    public Guid OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public List<OrderLine> Lines { get; set; } = [];
}

public class OrderLine
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
