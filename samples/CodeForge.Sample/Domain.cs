using System.ComponentModel;
using CodeForge;

[assembly: ForbidDependency("Sample.Domain", "Sample.Infrastructure")]
[assembly: ForbidDependency("Sample.Application", "Sample.Infrastructure")]

namespace Sample.Domain
{
    using Sample.Contracts;

    [StronglyTypedId]
    public readonly partial struct OrderId;

    [StronglyTypedId(typeof(int))]
    public readonly partial struct LineNumber;

    [EnumExtensions]
    public enum OrderStatus
    {
        [Description("Waiting for payment")] Pending = 1,
        Paid = 2,
        [Description("On its way")] Shipped = 3,
    }

    [MapTo(typeof(OrderLineDto))]
    public class OrderLine
    {
        public LineNumber Number { get; init; }

        public string Sku { get; init; } = string.Empty;

        public int Quantity { get; init; }
    }

    [MapTo(typeof(OrderDto))]
    public class Order
    {
        public OrderId Id { get; init; }

        public string Customer { get; init; } = string.Empty;

        public OrderStatus Status { get; init; }

        public List<OrderLine> Lines { get; init; } = new();

        [MapIgnore]
        public string InternalNote { get; init; } = string.Empty;
    }
}

namespace Sample.Contracts
{
    using Sample.Domain;

    public record OrderLineDto(LineNumber Number, string Sku, long Quantity);

    public record OrderDto(OrderId Id, string Customer, OrderStatus Status)
    {
        public List<OrderLineDto> Lines { get; init; } = new();
    }
}

namespace Sample.Infrastructure
{
    using Sample.Domain;

    public class InMemoryOrders
    {
        private readonly Dictionary<OrderId, Order> _orders = new();

        public void Save(Order order) => _orders[order.Id] = order;

        public Order? Find(OrderId id) => _orders.GetValueOrDefault(id);
    }
}
