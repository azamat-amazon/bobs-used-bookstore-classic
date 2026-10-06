using System;
using System.Threading.Tasks;
using Bookstore.Domain.Addresses;
using Bookstore.Domain.Customers;
using Bookstore.Domain.Offers;
using Xunit;

namespace Bookstore.Domain.Tests
{
    public class CustomerServiceTests
    {
        private readonly FakeCustomerRepository customers = new FakeCustomerRepository();
        private readonly CustomerService service;

        public CustomerServiceTests()
        {
            service = new CustomerService(customers);
        }

        [Fact]
        public async Task CreateOrUpdate_AddsANewCustomer()
        {
            await service.CreateOrUpdateCustomerAsync(new CreateOrUpdateCustomerDto("sub-1", "bookstoreuser", "Bookstore", "User"));

            var customer = Assert.Single(customers.Added);
            Assert.Equal("sub-1", customer.Sub);
            Assert.Equal("bookstoreuser", customer.Username);
            Assert.Equal("Bookstore User", customer.FullName);
            Assert.Equal(1, customers.SaveChangesCalls);
        }

        [Fact]
        public async Task CreateOrUpdate_UpdatesAnExistingCustomerInPlace()
        {
            var existing = new Customer { Id = 3, Sub = "sub-1", Username = "old", FirstName = "Old", LastName = "Name" };
            customers.Customers.Add(existing);

            await service.CreateOrUpdateCustomerAsync(new CreateOrUpdateCustomerDto("sub-1", "bookstoreuser", "Bookstore", "User"));

            Assert.Empty(customers.Added);
            Assert.Equal("bookstoreuser", existing.Username);
            Assert.Equal("Bookstore User", existing.FullName);
            Assert.Equal(1, customers.SaveChangesCalls);
        }
    }

    public class AddressServiceTests
    {
        private readonly FakeAddressRepository addresses = new FakeAddressRepository();
        private readonly FakeCustomerRepository customers = new FakeCustomerRepository();
        private readonly AddressService service;

        public AddressServiceTests()
        {
            service = new AddressService(addresses, customers);
            customers.Customers.Add(TestData.NewCustomer(42));
        }

        [Fact]
        public async Task CreateAddress_BindsTheAddressToTheCustomer()
        {
            await service.CreateAddressAsync(new CreateAddressDto("1 Main St", "Unit 2", "Toronto", "ON", "Canada", "M5V 1A1", TestData.LocalUserSub));

            var address = Assert.Single(addresses.Added);
            Assert.Equal(42, address.CustomerId);
            Assert.Equal(42, address.Customer.Id);
            Assert.Equal("1 Main St", address.AddressLine1);
            Assert.Equal("Unit 2", address.AddressLine2);
            Assert.Equal("Toronto", address.City);
            Assert.Equal("ON", address.State);
            Assert.Equal("Canada", address.Country);
            Assert.Equal("M5V 1A1", address.ZipCode);
            Assert.True(address.IsActive);
            Assert.Equal(1, addresses.SaveChangesCalls);
        }

        [Fact]
        public async Task UpdateAddress_ChangesTheCustomersOwnAddress()
        {
            var address = new Address(TestData.NewCustomer(42), "1 Main St", "Unit 2", "Toronto", "ON", "Canada", "M5V 1A1") { Id = 3 };
            addresses.Addresses.Add(address);

            await service.UpdateAddressAsync(new UpdateAddressDto(3, "9 Side Rd", null, "Ottawa", "ON", "Canada", "K1A 0B1", TestData.LocalUserSub));

            Assert.Equal("9 Side Rd", address.AddressLine1);
            Assert.Null(address.AddressLine2);
            Assert.Equal("Ottawa", address.City);
            Assert.Equal("K1A 0B1", address.ZipCode);
            Assert.Empty(addresses.Added);
            Assert.Equal(1, addresses.SaveChangesCalls);
        }

        [Fact]
        public async Task DeleteAddress_AsksTheRepositoryToDeleteItForThatCustomer()
        {
            await service.DeleteAddressAsync(new DeleteAddressDto(3, TestData.LocalUserSub));

            var deleted = Assert.Single(addresses.Deleted);
            Assert.Equal(TestData.LocalUserSub, deleted.Sub);
            Assert.Equal(3, deleted.Id);
            Assert.Equal(1, addresses.SaveChangesCalls);
        }
    }

    public class OfferServiceTests
    {
        private readonly FakeOfferRepository offers = new FakeOfferRepository();
        private readonly FakeCustomerRepository customers = new FakeCustomerRepository();
        private readonly OfferService service;

        public OfferServiceTests()
        {
            service = new OfferService(offers, customers);
            customers.Customers.Add(TestData.NewCustomer(42));
        }

        [Fact]
        public async Task CreateOffer_RecordsAPendingOfferForTheCustomer()
        {
            await service.CreateOfferAsync(new CreateOfferDto(TestData.LocalUserSub, "Leagues Of Smoke", "Pat Candella", "4556789542", 2, 7, 11, 18, 3M));

            var offer = Assert.Single(offers.Added);
            Assert.Equal(42, offer.CustomerId);
            Assert.Equal("Leagues Of Smoke", offer.BookName);
            Assert.Equal(3M, offer.BookPrice);
            Assert.Equal(OfferStatus.PendingApproval, offer.OfferStatus);
            Assert.Equal(1, offers.SaveChangesCalls);
        }

        [Fact]
        public async Task UpdateOfferStatus_MovesTheOfferOnAndSaves()
        {
            var offer = new Offer(42, "Leagues Of Smoke", "Pat Candella", "4556789542", 2, 7, 11, 18, 3M) { Id = 9, UpdatedOn = new DateTime(2020, 1, 1) };
            offers.Offers[9] = offer;

            await service.UpdateOfferStatusAsync(new UpdateOfferStatusDto(9, OfferStatus.Approved));

            Assert.Equal(OfferStatus.Approved, offer.OfferStatus);
            Assert.True(offer.UpdatedOn > new DateTime(2020, 1, 1));
            Assert.Equal(1, offers.SaveChangesCalls);
        }
    }
}
