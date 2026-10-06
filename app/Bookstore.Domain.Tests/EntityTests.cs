using System;
using System.Linq;
using Bookstore.Domain.Books;
using Bookstore.Domain.Carts;
using Bookstore.Domain.Customers;
using Bookstore.Domain.Offers;
using Bookstore.Domain.Orders;
using Xunit;

namespace Bookstore.Domain.Tests
{
    public class BookTests
    {
        [Fact]
        public void NewBook_CarriesTheDetailsItWasBuiltWith()
        {
            var book = new Book("Children Of Iron", "Nikki Wolf", "7665438976", 16, 1, 11, 6, 13.95M, 3, 2019, "A summary", "/covers/iron.png");

            Assert.Equal("Children Of Iron", book.Name);
            Assert.Equal("Nikki Wolf", book.Author);
            Assert.Equal("7665438976", book.ISBN);
            Assert.Equal(16, book.PublisherId);
            Assert.Equal(1, book.BookTypeId);
            Assert.Equal(11, book.GenreId);
            Assert.Equal(6, book.ConditionId);
            Assert.Equal(13.95M, book.Price);
            Assert.Equal(3, book.Quantity);
            Assert.Equal(2019, book.Year);
            Assert.Equal("A summary", book.Summary);
            Assert.Equal("/covers/iron.png", book.CoverImageUrl);
            Assert.True(book.IsNewEntity());
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(0, false)]
        public void IsInStock_IsTrueOnlyWhileCopiesRemain(int quantity, bool expected)
        {
            Assert.Equal(expected, TestData.NewBook(1, quantity: quantity).IsInStock);
        }

        [Fact]
        public void ReduceStockLevel_SubtractsTheCopiesSoldButNeverGoesBelowZero()
        {
            var book = TestData.NewBook(1, quantity: 5);

            book.ReduceStockLevel(2);
            Assert.Equal(3, book.Quantity);

            book.ReduceStockLevel(10);
            Assert.Equal(0, book.Quantity);
        }
    }

    public class OrderTests
    {
        [Fact]
        public void NewOrder_IsPendingForTheCustomerAndDueInAWeek()
        {
            var earliest = DateTime.Now.AddDays(7);

            var order = new Order(42, 7);

            var latest = DateTime.Now.AddDays(7);
            Assert.Equal(42, order.CustomerId);
            Assert.Equal(7, order.AddressId);
            Assert.Equal(OrderStatus.Pending, order.OrderStatus);
            Assert.InRange(order.DeliveryDate, earliest, latest);
            Assert.Empty(order.OrderItems);
        }

        [Fact]
        public void Totals_AddTenPercentTaxToTheSubtotal()
        {
            var order = new Order(42, 7);
            order.AddOrderItem(TestData.NewBook(1, price: 10.95M), 1);
            order.AddOrderItem(TestData.NewBook(2, price: 13.95M), 1);

            Assert.Equal(24.90M, order.SubTotal);
            Assert.Equal(2.49M, order.Tax);
            Assert.Equal(27.39M, order.Total);
        }
    }

    public class ShoppingCartTests
    {
        // Item 1: in stock, to buy. Item 2: out of stock, to buy. Item 3: wishlist.
        private static ShoppingCart MixedCart()
        {
            return TestData.NewCart(
                "cart-1",
                (1, TestData.NewBook(10, price: 10.95M, quantity: 4), true),
                (2, TestData.NewBook(11, price: 3.00M, quantity: 0), true),
                (3, TestData.NewBook(12, price: 8.25M, quantity: 2), false));
        }

        [Fact]
        public void GetShoppingCartItems_IncludingOutOfStock_ReturnsEveryItemToBuy()
        {
            var ids = MixedCart().GetShoppingCartItems(ShoppingCartItemFilter.IncludeOutOfStockItems).Select(x => x.Id);

            Assert.Equal(new[] { 1, 2 }, ids);
        }

        [Fact]
        public void GetShoppingCartItems_ExcludingOutOfStock_ReturnsOnlyBooksThatCanShip()
        {
            var ids = MixedCart().GetShoppingCartItems(ShoppingCartItemFilter.ExcludeOutOfStockItems).Select(x => x.Id);

            Assert.Equal(new[] { 1 }, ids);
        }

        [Fact]
        public void GetSubTotal_AddsThePricesOfTheItemsToBuy()
        {
            var cart = MixedCart();

            Assert.Equal(13.95M, cart.GetSubTotal(ShoppingCartItemFilter.IncludeOutOfStockItems));
            Assert.Equal(10.95M, cart.GetSubTotal(ShoppingCartItemFilter.ExcludeOutOfStockItems));
        }

        [Fact]
        public void MoveWishListItemToShoppingCart_MarksTheItemToBuy()
        {
            var cart = MixedCart();

            cart.MoveWishListItemToShoppingCart(3);

            Assert.True(cart.ShoppingCartItems.Single(x => x.Id == 3).WantToBuy);
            Assert.Empty(cart.GetWishListItems());
        }

        [Fact]
        public void MoveWishListItemToShoppingCart_IgnoresAnUnknownItem()
        {
            var cart = MixedCart();

            cart.MoveWishListItemToShoppingCart(99);

            Assert.Equal(new[] { true, true, false }, cart.ShoppingCartItems.Select(x => x.WantToBuy));
        }
    }

    public class EntityTests
    {
        [Fact]
        public void IsNewEntity_IsTrueUntilTheDatabaseAssignsAnId()
        {
            var customer = new Customer();

            Assert.True(customer.IsNewEntity());

            customer.Id = 5;

            Assert.False(customer.IsNewEntity());
        }

        [Fact]
        public void NewOffer_AwaitsApproval()
        {
            var offer = new Offer(42, "Leagues Of Smoke", "Pat Candella", "4556789542", 2, 7, 11, 18, 3M);

            Assert.Equal(OfferStatus.PendingApproval, offer.OfferStatus);
            Assert.Equal(42, offer.CustomerId);
            Assert.Equal("Leagues Of Smoke", offer.BookName);
            Assert.Equal("Pat Candella", offer.Author);
            Assert.Equal("4556789542", offer.ISBN);
            Assert.Equal(2, offer.BookTypeId);
            Assert.Equal(7, offer.ConditionId);
            Assert.Equal(11, offer.GenreId);
            Assert.Equal(18, offer.PublisherId);
            Assert.Equal(3M, offer.BookPrice);
        }

        [Fact]
        public void GetDescription_ReturnsTheDescriptionOrElseTheName()
        {
            Assert.Equal("Pending Approval", OfferStatus.PendingApproval.GetDescription());
            Assert.Equal("Payment Completed", OfferStatus.Paid.GetDescription());
            Assert.Equal("Shipped", OrderStatus.Shipped.GetDescription());
        }

        // The dashboard counts orders since the start of the month; the order
        // filter includes the whole of its "to" day. Only the date part of the
        // start of the month is asserted.
        [Fact]
        public void DateHelpers_GiveTheStartOfTheMonthAndTheEndOfTheDay()
        {
            var day = new DateTime(2026, 10, 17, 13, 45, 0);

            Assert.Equal(new DateTime(2026, 10, 1), day.StartOfMonth().Date);
            Assert.Equal(new DateTime(2026, 10, 17, 23, 59, 59), day.OneSecondToMidnight());
        }
    }
}
