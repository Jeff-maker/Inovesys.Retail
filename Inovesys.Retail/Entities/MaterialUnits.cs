using LiteDB;
using System;

namespace Inovesys.Retail.Entities
{
    public class MaterialUnits
    {
        [BsonId]
        public string CompositeKey => client_id + "-" + material_id + "-" + unit_id;
        [BsonField("client_id")]
        public int client_id { get; set; }
        [BsonField("material_id")]
        public string material_id { get; set; }
        [BsonField("unit_id")]
        public string unit_id { get; set; }
        [BsonField("numerator_conversion_rate")]
        public decimal numerator_conversion_rate { get; set; }
        [BsonField("denominator_conversion_rate")]
        public decimal denominator_conversion_rate { get; set; }
        [BsonField("weight_unit_id")]
        public string weight_unit_id { get; set; }
        [BsonField("net_weight")]
        public decimal net_weight { get; set; }
        [BsonField("gross_weight")]
        public decimal gross_weight { get; set; }
        [BsonField("ean")]
        public string ean { get; set; }
        [BsonField("last_change")]
        public DateTime last_change { get; set; }
    }
}