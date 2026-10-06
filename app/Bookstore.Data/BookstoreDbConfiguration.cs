using System.Data.Entity;
using System.Data.Entity.SqlServer;

namespace Bookstore.Data
{
    /// <summary>
    /// Programmatic EF6 DbConfiguration that registers the SQL Server provider.
    /// On .NET 10 the XML-based entityFramework config section in App.config is not
    /// reliably loaded, so provider registration must happen in code.
    /// EF6 discovers this class automatically because it lives in the same assembly
    /// as <see cref="ApplicationDbContext"/>.
    /// </summary>
    public class BookstoreDbConfiguration : DbConfiguration
    {
        public BookstoreDbConfiguration()
        {
            SetProviderServices("System.Data.SqlClient", SqlProviderServices.Instance);
        }
    }
}
