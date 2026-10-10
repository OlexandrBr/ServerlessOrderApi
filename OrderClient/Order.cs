namespace OrderClient;

public class Order
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = "";
    public string Product { get; set; } = "";
    public int Quantity { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
