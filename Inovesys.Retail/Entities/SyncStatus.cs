using LiteDB;

namespace Inovesys.Retail.Entities
{
    public class SyncStatus
    {
        [BsonId]
        public string EntityName { get; set; } = null!;
        public DateTime LastChange { get; set; }
    }
}
