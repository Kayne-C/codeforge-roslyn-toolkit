using System.Text.Json;
using Sample.Contracts;
using Sample.Domain;
using Sample.Infrastructure;

var orders = new InMemoryOrders();
var order = new Order
{
    Id = OrderId.New(),
    Customer = "Acme",
    Status = OrderStatus.Paid,
    Lines =
    {
        new OrderLine { Number = LineNumber.From(1), Sku = "LAMP-01", Quantity = 2 },
        new OrderLine { Number = LineNumber.From(2), Sku = "DESK-07", Quantity = 1 },
    },
};
orders.Save(order);

var dto = orders.Find(order.Id)!.ToOrderDto();
Console.WriteLine($"order {dto.Id} for {dto.Customer}: {dto.Status.ToDescription()} ({dto.Status.ToStringFast()})");
foreach (var line in dto.Lines)
{
    Console.WriteLine($"  line {line.Number}: {line.Sku} x{line.Quantity}");
}

var json = JsonSerializer.Serialize(dto);
Console.WriteLine(json);
var back = JsonSerializer.Deserialize<OrderDto>(json)!;
Console.WriteLine($"round trip keeps the id: {back.Id == dto.Id}");
Console.WriteLine($"known statuses: {string.Join(", ", OrderStatusExtensions.GetNames())}");
