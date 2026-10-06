using System;
using System.Linq;
using System.Threading.Tasks;
using Bookstore.Domain.Carts;
using Bookstore.Domain.Orders;
using Xunit;

namespace Bookstore.Domain.Tests
{
    public class OrderServiceTests
    {
        private readonly FakeOrderRepository orders = new FakeOrderRepository();
        private readonly FakeShoppingCartRepository carts = new FakeShoppingCartRepository();
        private readonly FakeCustomerRepository customers = new FakeCustomerRepository();
        private readonly OrderService service;

        public OrderServiceTests()
        {
            service = new OrderService(orders, carts, customers);
            customers.Customers.Add(TestData.NewCustomer(42));
        }

        [Fact]
        public async Task CreateOrder_OrdersTheInStockItemsReducesStockAndClearsThemFromTheCart()
        {
            var inStock = TestData.NewBook(10, price: 10.95M, quantity: 4);
            var soldOut = TestData.NewBook(11, price: 3M, quantity: 0);
            var wished = TestData.NewBook(12, price: 8.25M, quantity: 2);
            var cart = TestData.NewCart("cart-1", (1, inStock, true), (2, soldOut, true), (3, wished, false));
            carts.Carts["cart-1"] = cart;

            var orderId = await service.CreateOrderAsync(new CreateOrderDto(TestData.LocalUserSub, "cart-1", 7));

            var order = Assert.Single(orders.Added);
            Assert.Equal(1001, orderId);
            Assert.Equal(orderId, order.Id);
            Assert.Equal(42, order.CustomerId);
            Assert.Equal(7, order.AddressId);
            Assert.Equal(OrderStatus.Pending, order.OrderStatus);
            var item = Assert.Single(order.OrderItems);
            Assert.Same(inStock, item.Book);
            Assert.Equal(1, item.Quantity);
            Assert.Equal(10.95M, order.SubTotal);
            Assert.Equal(3, inStock.Quantity);
            Assert.Equal(2, wished.Quantity);
            // The out-of-stock item and the wishlist item stay in the cart.
            Assert.Equal(new[] { 2, 3 }, cart.ShoppingCartItems.Select(x => x.Id));
            // One unit of work: the order, the stock levels and the cart in one save.
            Assert.Equal(1, orders.SaveChangesCalls);
        }

        [Fact]
        public async Task UpdateOrderStatus_SetsTheStatusAndSaves()
        {
            var order = new Order(42, 7) { Id = 5, UpdatedOn = new DateTime(2020, 1, 1) };
            orders.Orders[5] = order;

            await service.UpdateOrderStatusAsync(new UpdateOrderStatusDto(5, OrderStatus.Shipped));

            Assert.Equal(OrderStatus.Shipped, order.OrderStatus);
            Assert.True(order.UpdatedOn > new DateTime(2020, 1, 1));
            Assert.Equal(1, orders.SaveChangesCalls);
        }

        [Fact]
        public async Task CancelOrder_CancelsTheCustomersOwnOrder()
        {
            var order = new Order(42, 7) { Id = 5, Customer = TestData.NewCustomer(42) };
            orders.Orders[5] = order;

            await service.CancelOrderAsync(new CancelOrderDto(TestData.LocalUserSub, 5));

            Assert.Equal(OrderStatus.Cancelled, order.OrderStatus);
            Assert.Equal(1, orders.SaveChangesCalls);
        }

        [Fact]
        public async Task CancelOrder_LeavesAnotherCustomersOrderAlone()
        {
            var order = new Order(43, 8) { Id = 5, Customer = TestData.NewCustomer(43, "someone-else") };
            orders.Orders[5] = order;

            await service.CancelOrderAsync(new CancelOrderDto(TestData.LocalUserSub, 5));

            Assert.Equal(OrderStatus.Pending, order.OrderStatus);
            Assert.Equal(0, orders.SaveChangesCalls);
        }
    }

    public class ShoppingCartServiceTests
    {
        private readonly FakeShoppingCartRepository carts = new FakeShoppingCartRepository();
        private readonly ShoppingCartService service;

        public ShoppingCartServiceTests()
        {
            service = new ShoppingCartService(carts);
        }

        [Fact]
        public async Task AddToShoppingCart_CreatesTheCartOnFirstUse()
        {
            await service.AddToShoppingCartAsync(new AddToShoppingCartDto("cart-1", 7, 1));

            var cart = Assert.Single(carts.Added);
            Assert.Equal("cart-1", cart.CorrelationId);
            var item = Assert.Single(cart.ShoppingCartItems);
            Assert.Equal(7, item.BookId);
            Assert.True(item.WantToBuy);
            Assert.Equal(1, carts.SaveChangesCalls);
        }

        [Fact]
        public async Task AddToShoppingCart_ReusesTheExistingCart()
        {
            var existing = TestData.NewCart("cart-1", (1, TestData.NewBook(10), true));
            carts.Carts["cart-1"] = existing;

            await service.AddToShoppingCartAsync(new AddToShoppingCartDto("cart-1", 11, 1));

            Assert.Empty(carts.Added);
            Assert.Equal(new[] { 10, 11 }, existing.ShoppingCartItems.Select(x => x.BookId));
            Assert.Equal(1, carts.SaveChangesCalls);
        }

        [Fact]
        public async Task AddToWishlist_AddsAnItemNotToBuyYet()
        {
            await service.AddToWishlistAsync(new AddToWishlistDto("cart-1", 7));

            var item = Assert.Single(Assert.Single(carts.Added).ShoppingCartItems);
            Assert.False(item.WantToBuy);
            Assert.Equal(1, item.Quantity);
        }

        [Fact]
        public async Task MoveWishlistItemToShoppingCart_MovesThatItemAndSaves()
        {
            var cart = TestData.NewCart("cart-1", (1, TestData.NewBook(10), false), (2, TestData.NewBook(11), false));
            carts.Carts["cart-1"] = cart;

            await service.MoveWishlistItemToShoppingCartAsync(new MoveWishlistItemToShoppingCartDto("cart-1", 2));

            Assert.Equal(new[] { 1 }, cart.GetWishListItems().Select(x => x.Id));
            Assert.Equal(1, carts.SaveChangesCalls);
        }

        [Fact]
        public async Task MoveAllWishlistItemsToShoppingCart_MovesEveryWishlistItem()
        {
            var cart = TestData.NewCart(
                "cart-1",
                (1, TestData.NewBook(10), false),
                (2, TestData.NewBook(11), true),
                (3, TestData.NewBook(12), false));
            carts.Carts["cart-1"] = cart;

            await service.MoveAllWishlistItemsToShoppingCartAsync(new MoveAllWishlistItemsToShoppingCartDto("cart-1"));

            Assert.Empty(cart.GetWishListItems());
            Assert.Equal(3, cart.GetShoppingCartItems(ShoppingCartItemFilter.IncludeOutOfStockItems).Count());
            Assert.Equal(1, carts.SaveChangesCalls);
        }

        [Fact]
        public async Task MoveAllWishlistItemsToShoppingCart_DoesNothingWithoutACart()
        {
            await service.MoveAllWishlistItemsToShoppingCartAsync(new MoveAllWishlistItemsToShoppingCartDto("no-such-cart"));

            Assert.Equal(0, carts.SaveChangesCalls);
        }

        [Fact]
        public async Task DeleteShoppingCartItem_RemovesTheItemAndSaves()
        {
            var cart = TestData.NewCart("cart-1", (1, TestData.NewBook(10), true), (2, TestData.NewBook(11), true));
            carts.Carts["cart-1"] = cart;

            await service.DeleteShoppingCartItemAsync(new DeleteShoppingCartItemDto("cart-1", 1));

            Assert.Equal(new[] { 2 }, cart.ShoppingCartItems.Select(x => x.Id));
            Assert.Equal(1, carts.SaveChangesCalls);
        }
    }
}
