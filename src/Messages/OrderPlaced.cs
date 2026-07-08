namespace Messages;

/// <summary>
/// An event: a statement of fact about something that already happened ("this occurred").
/// Events are PUBLISHED via a topic; zero or more subscribers may react.
/// The publisher doesn't know or care who is listening.
/// </summary>
public class OrderPlaced : IEvent
{
    public Guid OrderId { get; set; }
    public string CustomerEmail { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
}
