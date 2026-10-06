using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bookstore.Domain.Addresses;
using Bookstore.Domain.Books;
using Bookstore.Domain.Carts;
using Bookstore.Domain.Customers;
using Bookstore.Domain.Offers;
using Bookstore.Domain.Orders;
using Bookstore.Domain.ReferenceData;

namespace Bookstore.Domain.Tests
{
    // Builders for the domain objects the tests need. Ids are set explicitly,
    // as the database would, because the domain looks items up by Id.
    internal static class TestData
    {
        // Stands in for the identity provider's subject id of the signed-in customer.
        public const string LocalUserSub = "test-user-sub";

        public static Book NewBook(int id, decimal price = 10m, int quantity = 5, string name = null)
        {
            return new Book(
                name ?? "Book " + id,
                "Author " + id,
                "ISBN-" + id,
                15, 1, 13, 5,
                price,
                quantity,
                2020,
                "Summary " + id,
                "/Content/Images/coverimages/book" + id + ".png")
            {
                Id = id,
                Publisher = new ReferenceDataItem(ReferenceDataType.Publisher, "Arcadia Books") { Id = 15 },
                BookType = new ReferenceDataItem(ReferenceDataType.BookType, "Hardcover") { Id = 1 },
                Genre = new ReferenceDataItem(ReferenceDataType.Genre, "Science Fiction & Fantasy") { Id = 13 },
                Condition = new ReferenceDataItem(ReferenceDataType.Condition, "Like New") { Id = 5 },
            };
        }

        public static Customer NewCustomer(int id, string sub = LocalUserSub)
        {
            return new Customer { Id = id, Sub = sub, Username = "bookstoreuser", FirstName = "Bookstore", LastName = "User" };
        }

        // A cart whose items have database Ids and loaded books.
        public static ShoppingCart NewCart(string correlationId, params (int ItemId, Book Book, bool WantToBuy)[] items)
        {
            var cart = new ShoppingCart(correlationId) { Id = 1 };
            foreach (var (itemId, book, wantToBuy) in items)
            {
                if (wantToBuy)
                {
                    cart.AddItemToShoppingCart(book.Id, 1);
                }
                else
                {
                    cart.AddItemToWishlist(book.Id);
                }
                var item = cart.ShoppingCartItems.Last();
                item.Id = itemId;
                item.Book = book;
            }
            return cart;
        }
    }

    // Hand-written in-memory repositories. Each records what the service asked
    // it to add and how often it saved, which is what the services' behavior is.
    // Members the tests never reach throw, so an unexpected call is visible.

    internal sealed class FakeBookRepository : IBookRepository
    {
        public Dictionary<int, Book> Books { get; } = new Dictionary<int, Book>();
        public List<Book> Added { get; } = new List<Book>();
        public List<Book> Updated { get; } = new List<Book>();
        public int SaveChangesCalls { get; private set; }
        public BookStatistics Statistics { get; set; }

        public Task<Book> GetAsync(int id) => Task.FromResult(Books.TryGetValue(id, out var book) ? book : null);

        public Task<IPaginatedList<Book>> ListAsync(BookFilters filters, int pageIndex, int pageSize) =>
            throw new NotSupportedException();

        public Task<IPaginatedList<Book>> ListAsync(string searchString, string sortBy, int pageIndex, int pageSize) =>
            throw new NotSupportedException();

        public Task AddAsync(Book book)
        {
            Added.Add(book);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Book book)
        {
            Updated.Add(book);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync()
        {
            SaveChangesCalls++;
            return Task.CompletedTask;
        }

        public Task<BookStatistics> GetStatisticsAsync() => Task.FromResult(Statistics);
    }

    internal sealed class FakeOrderRepository : IOrderRepository
    {
        public Dictionary<int, Order> Orders { get; } = new Dictionary<int, Order>();
        public List<Order> Added { get; } = new List<Order>();
        public int SaveChangesCalls { get; private set; }
        public OrderStatistics Statistics { get; set; }
        public List<Book> BestSellers { get; } = new List<Book>();

        // Ids the database would assign to added orders on save.
        public int NextId { get; set; } = 1001;

        public Task<Order> GetAsync(int id) => Task.FromResult(Orders.TryGetValue(id, out var order) ? order : null);

        public Task<Order> GetAsync(int id, string sub) =>
            Task.FromResult(Orders.TryGetValue(id, out var order) && order.Customer?.Sub == sub ? order : null);

        public Task<IEnumerable<Book>> ListBestSellingBooksAsync(int count) =>
            Task.FromResult<IEnumerable<Book>>(BestSellers.Take(count).ToList());

        public Task<IPaginatedList<Order>> ListAsync(OrderFilters filters, int pageIndex = 1, int pageSize = 10) =>
            throw new NotSupportedException();

        public Task<IEnumerable<Order>> ListAsync(string sub) => throw new NotSupportedException();

        public Task AddAsync(Order order)
        {
            Added.Add(order);
            return Task.CompletedTask;
        }

        public Task<OrderStatistics> GetStatisticsAsync() => Task.FromResult(Statistics);

        public Task SaveChangesAsync()
        {
            SaveChangesCalls++;
            foreach (var order in Added.Where(x => x.IsNewEntity()))
            {
                order.Id = NextId++;
            }
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeOfferRepository : IOfferRepository
    {
        public Dictionary<int, Offer> Offers { get; } = new Dictionary<int, Offer>();
        public List<Offer> Added { get; } = new List<Offer>();
        public int SaveChangesCalls { get; private set; }
        public OfferStatistics Statistics { get; set; }

        public Task<IPaginatedList<Offer>> ListAsync(OfferFilters filters, int pageIndex, int pageSize) =>
            throw new NotSupportedException();

        public Task<IEnumerable<Offer>> ListAsync(string sub) => throw new NotSupportedException();

        public Task<Offer> GetAsync(int id) => Task.FromResult(Offers.TryGetValue(id, out var offer) ? offer : null);

        public Task AddAsync(Offer offer)
        {
            Added.Add(offer);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync()
        {
            SaveChangesCalls++;
            return Task.CompletedTask;
        }

        public Task<OfferStatistics> GetStatisticsAsync() => Task.FromResult(Statistics);
    }

    internal sealed class FakeCustomerRepository : ICustomerRepository
    {
        public List<Customer> Customers { get; } = new List<Customer>();
        public List<Customer> Added { get; } = new List<Customer>();
        public int SaveChangesCalls { get; private set; }

        public Task<Customer> GetAsync(int id) => Task.FromResult(Customers.SingleOrDefault(x => x.Id == id));

        public Task<Customer> GetAsync(string sub) => Task.FromResult(Customers.SingleOrDefault(x => x.Sub == sub));

        public Task AddAsync(Customer customer)
        {
            Added.Add(customer);
            Customers.Add(customer);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync()
        {
            SaveChangesCalls++;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeShoppingCartRepository : IShoppingCartRepository
    {
        public Dictionary<string, ShoppingCart> Carts { get; } = new Dictionary<string, ShoppingCart>();
        public List<ShoppingCart> Added { get; } = new List<ShoppingCart>();
        public int SaveChangesCalls { get; private set; }

        public Task AddAsync(ShoppingCart shoppingCart)
        {
            Added.Add(shoppingCart);
            Carts[shoppingCart.CorrelationId] = shoppingCart;
            return Task.CompletedTask;
        }

        public Task<ShoppingCart> GetAsync(string correlationId) =>
            Task.FromResult(Carts.TryGetValue(correlationId, out var cart) ? cart : null);

        public Task SaveChangesAsync()
        {
            SaveChangesCalls++;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeReferenceDataRepository : IReferenceDataRepository
    {
        public Dictionary<int, ReferenceDataItem> Items { get; } = new Dictionary<int, ReferenceDataItem>();
        public List<ReferenceDataItem> Added { get; } = new List<ReferenceDataItem>();
        public int SaveChangesCalls { get; private set; }

        public Task<IEnumerable<ReferenceDataItem>> FullListAsync() =>
            Task.FromResult<IEnumerable<ReferenceDataItem>>(Items.Values.ToList());

        public Task<IPaginatedList<ReferenceDataItem>> ListAsync(ReferenceDataFilters filters, int pageIndex, int pageSize) =>
            throw new NotSupportedException();

        public Task<ReferenceDataItem> GetAsync(int id) => Task.FromResult(Items.TryGetValue(id, out var item) ? item : null);

        public Task AddAsync(ReferenceDataItem item)
        {
            Added.Add(item);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync()
        {
            SaveChangesCalls++;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeAddressRepository : IAddressRepository
    {
        public List<Address> Addresses { get; } = new List<Address>();
        public List<Address> Added { get; } = new List<Address>();
        public List<(string Sub, int Id)> Deleted { get; } = new List<(string Sub, int Id)>();
        public int SaveChangesCalls { get; private set; }

        public Task<Address> GetAsync(string sub, int id) =>
            Task.FromResult(Addresses.SingleOrDefault(x => x.Id == id && x.Customer?.Sub == sub));

        public Task<IEnumerable<Address>> ListAsync(string sub) =>
            Task.FromResult<IEnumerable<Address>>(Addresses.Where(x => x.Customer?.Sub == sub).ToList());

        public Task AddAsync(Address address)
        {
            Added.Add(address);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string sub, int id)
        {
            Deleted.Add((sub, id));
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync()
        {
            SaveChangesCalls++;
            return Task.CompletedTask;
        }
    }

    // Stands in for the local or S3 file store.
    internal sealed class FakeFileService : IFileService
    {
        public List<(Stream Contents, string FileName)> Saved { get; } = new List<(Stream Contents, string FileName)>();
        public List<string> Deleted { get; } = new List<string>();

        // The URL the store hands back for a saved file.
        public string SavedUrl { get; set; } = "/Content/Images/coverimages/saved.png";

        public Task<string> SaveAsync(Stream contents, string filename)
        {
            Saved.Add((contents, filename));
            return Task.FromResult(contents == null ? null : SavedUrl);
        }

        public Task DeleteAsync(string filePath)
        {
            Deleted.Add(filePath);
            return Task.CompletedTask;
        }
    }

    // Returns a distinct stream, so a test can tell the resized image from the upload.
    internal sealed class FakeImageResizeService : IImageResizeService
    {
        public List<Stream> Resized { get; } = new List<Stream>();
        public Stream Output { get; } = new MemoryStream(new byte[] { 1, 2, 3 });

        public Task<Stream> ResizeImageAsync(Stream image)
        {
            Resized.Add(image);
            return Task.FromResult(Output);
        }
    }

    internal sealed class FakeImageValidationService : IImageValidationService
    {
        public bool IsSafe { get; set; } = true;
        public int Calls { get; private set; }

        public Task<bool> IsSafeAsync(Stream image)
        {
            Calls++;
            return Task.FromResult(IsSafe);
        }
    }
}
