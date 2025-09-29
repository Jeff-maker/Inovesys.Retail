using LiteDB;
namespace Inovesys.Retail.Entities
{
    public class Client
    {
        [BsonId]
        [BsonField("id")]
        public int Id { get; set; }

        [BsonField("description")]
        public string Description { get; set; }

        [BsonField("public_key")]
        public string PublicKey { get; set; }

        [BsonField("last_change")]
        public DateTime LastChange { get; set; }

        [BsonField("environment_sefaz")]
        public string EnvironmentSefaz { get; set; }
    }

}