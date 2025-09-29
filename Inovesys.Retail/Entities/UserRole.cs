using LiteDB;
using System;

namespace Inovesys.Retail.Entities
{
    public class UserRole
    {
        [BsonId]
        public string CompositeKey => client_id + "-" + user_id + "-" + role_id;
        [BsonField("client_id")]
        public int client_id { get; set; }
        [BsonField("user_id")]
        public string user_id { get; set; }
        [BsonField("role_id")]
        public string role_id { get; set; }
        [BsonField("last_change")]
        public DateTime last_change { get; set; }
    }
}