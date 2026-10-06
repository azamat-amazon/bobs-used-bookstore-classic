using System;
using System.IO;
using System.Threading.Tasks;
using Bookstore.Domain.Books;
using Bookstore.Domain.ReferenceData;
using Xunit;

namespace Bookstore.Domain.Tests
{
    public class BookServiceTests
    {
        private readonly FakeImageResizeService resize = new FakeImageResizeService();
        private readonly FakeImageValidationService validation = new FakeImageValidationService();
        private readonly FakeFileService files = new FakeFileService();
        private readonly FakeBookRepository books = new FakeBookRepository();
        private readonly BookService service;

        public BookServiceTests()
        {
            service = new BookService(resize, validation, files, books, new FakeOrderRepository());
        }

        private static CreateBookDto NewBookDto(Stream cover = null, string coverName = null)
        {
            return new CreateBookDto("Children Of Iron", "Nikki Wolf", 1, 6, 11, 16, 2019, "7665438976", "A summary", 13.95M, 3, cover, coverName);
        }

        [Fact]
        public async Task Add_WithoutACoverImage_AddsTheBookAndSaves()
        {
            var result = await service.AddAsync(NewBookDto());

            Assert.True(result.IsSuccess);
            Assert.Null(result.ErrorMessage);
            var book = Assert.Single(books.Added);
            Assert.Equal("Children Of Iron", book.Name);
            Assert.Equal("Nikki Wolf", book.Author);
            Assert.Equal("7665438976", book.ISBN);
            Assert.Equal(16, book.PublisherId);
            Assert.Equal(1, book.BookTypeId);
            Assert.Equal(11, book.GenreId);
            Assert.Equal(6, book.ConditionId);
            Assert.Equal(13.95M, book.Price);
            Assert.Equal(3, book.Quantity);
            Assert.Null(book.CoverImageUrl);
            Assert.Empty(resize.Resized);
            Assert.Equal(1, books.SaveChangesCalls);
        }

        [Fact]
        public async Task Add_WithAnUnsafeCoverImage_FailsWithoutSaving()
        {
            validation.IsSafe = false;

            var result = await service.AddAsync(NewBookDto(new MemoryStream(new byte[] { 9 }), "cover.png"));

            Assert.False(result.IsSuccess);
            Assert.Contains("safety check", result.ErrorMessage);
            Assert.Equal(0, books.SaveChangesCalls);
            Assert.DoesNotContain(files.Saved, x => x.Contents != null);
        }

        [Fact]
        public async Task Update_WithANewCoverImage_StoresTheResizedImageAndReplacesTheOldOne()
        {
            var existing = TestData.NewBook(7, price: 5M, quantity: 1);
            existing.CoverImageUrl = "/Content/Images/coverimages/old.png";
            existing.UpdatedOn = new DateTime(2020, 1, 1);
            books.Books[7] = existing;
            files.SavedUrl = "/Content/Images/coverimages/new.png";
            var upload = new MemoryStream(new byte[] { 7, 7, 7 });

            var result = await service.UpdateAsync(new UpdateBookDto(7, "Renamed", "New Author", 2, 4, 9, 20, 2021, "123", "New summary", 9.99M, 6, upload, "new.png"));

            Assert.True(result.IsSuccess);
            Assert.Same(upload, Assert.Single(resize.Resized));
            var saved = Assert.Single(files.Saved);
            Assert.Same(resize.Output, saved.Contents);
            Assert.Equal("new.png", saved.FileName);
            Assert.Equal(new[] { "/Content/Images/coverimages/old.png" }, files.Deleted);
            Assert.Equal("/Content/Images/coverimages/new.png", existing.CoverImageUrl);
            Assert.Equal("Renamed", existing.Name);
            Assert.Equal(9.99M, existing.Price);
            Assert.Equal(6, existing.Quantity);
            Assert.True(existing.UpdatedOn > new DateTime(2020, 1, 1));
            Assert.Same(existing, Assert.Single(books.Updated));
            Assert.Equal(1, books.SaveChangesCalls);
        }

        [Fact]
        public async Task Update_WithoutANewCoverImage_KeepsTheExistingOne()
        {
            var existing = TestData.NewBook(7, price: 5M, quantity: 1);
            existing.CoverImageUrl = "/Content/Images/coverimages/old.png";
            books.Books[7] = existing;

            var result = await service.UpdateAsync(new UpdateBookDto(7, "Renamed", "New Author", 2, 4, 9, 20, 2021, "123", "New summary", 9.99M, 6, null, null));

            Assert.True(result.IsSuccess);
            Assert.Equal("/Content/Images/coverimages/old.png", existing.CoverImageUrl);
            Assert.Empty(files.Deleted);
            Assert.Empty(resize.Resized);
            Assert.Equal("Renamed", existing.Name);
            Assert.Equal(9.99M, existing.Price);
            Assert.Equal(6, existing.Quantity);
            Assert.Equal(1, books.SaveChangesCalls);
        }
    }

    public class ReferenceDataServiceTests
    {
        private readonly FakeReferenceDataRepository items = new FakeReferenceDataRepository();
        private readonly ReferenceDataService service;

        public ReferenceDataServiceTests()
        {
            service = new ReferenceDataService(items);
        }

        [Fact]
        public async Task Create_AddsTheItemAndSaves()
        {
            await service.CreateAsync(new CreateReferenceDataItemDto(ReferenceDataType.Genre, "Poetry"));

            var item = Assert.Single(items.Added);
            Assert.Equal(ReferenceDataType.Genre, item.DataType);
            Assert.Equal("Poetry", item.Text);
            Assert.Equal(1, items.SaveChangesCalls);
        }

        [Fact]
        public async Task Update_ChangesTheExistingItem()
        {
            var item = new ReferenceDataItem(ReferenceDataType.Genre, "Poetry") { Id = 30 };
            items.Items[30] = item;

            await service.UpdateAsync(new UpdateReferenceDataItemDto(30, ReferenceDataType.Publisher, "Poetry Press"));

            Assert.Equal(ReferenceDataType.Publisher, item.DataType);
            Assert.Equal("Poetry Press", item.Text);
            Assert.Equal(1, items.SaveChangesCalls);
        }
    }
}
