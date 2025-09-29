// Services/LiteDbService.cs
using Inovesys.Retail.Entities;
using LiteDB;

namespace Inovesys.Retail.Services
{
    public class LiteDbService
    {
        private readonly LiteDatabase _database;

        // Expor o acesso ao banco para operações como BeginTrans
        public LiteDatabase Database => _database;

        public LiteDbService()
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "inovesys.db");
            _database = new LiteDatabase(dbPath);
            EnsureIndices();
        }

        public ILiteCollection<T> GetCollection<T>(string name) where T : class
        {
            return _database.GetCollection<T>(name);
        }


        private void EnsureIndices()
        {
            var invoices = _database.GetCollection<Invoice>("invoice");

            // ✅ Índice composto via campo auxiliar
            invoices.EnsureIndex(x => x.IndexKey);

            var invoicesItem = _database.GetCollection<InvoiceItem>("invoiceitem");

            // ✅ Índice composto via campo auxiliar
            invoicesItem.EnsureIndex(x => x.IndexKey);

            var InvoiceItemTax = _database.GetCollection<InvoiceItemTax>("invoiceitemtax");

            // ✅ Índice composto via campo auxiliar
            InvoiceItemTax.EnsureIndex(x => x.IndexKey);

        }


        public DateTime? GetLastSyncDate(string entityName)
        {
            var col = _database.GetCollection<SyncStatus>("sync_status");
            var entry = col.FindById(entityName);
            return entry?.LastChange;
        }

        public void UpdateLastSyncDate(string entityName)
        {
            var col = _database.GetCollection<SyncStatus>("sync_status");
            var entry = col.FindById(entityName) ?? new SyncStatus { EntityName = entityName };
            entry.LastChange = DateTime.UtcNow;
            col.Upsert(entry);
        }

        public void SaveEntities<T>(IEnumerable<T> items)
        {
            var collectionName = typeof(T).Name.ToLower();
            var col = _database.GetCollection<T>(collectionName);
            col.Upsert(items);
        }
    }
}
