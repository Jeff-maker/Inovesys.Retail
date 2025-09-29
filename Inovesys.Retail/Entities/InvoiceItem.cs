using LiteDB;

namespace Inovesys.Retail.Entities
{
    public class InvoiceItem
    {
        [BsonId]
        public string CompositeKey => ClientId + "-" + InvoiceId + "-" + ItemNumber;

        [BsonField("client_id")]
        public int ClientId { get; set; }

        [BsonField("invoice_id")]
        public int InvoiceId { get; set; }

        [BsonField("item_number")]
        public int ItemNumber { get; set; }

        [BsonField("material_id")]
        public string MaterialId { get; set; }


        [BsonField("material_name")]
        public string MaterialName { get; set; }


        [BsonField("ncm")]
        public string NCM { get; set; }

        [BsonField("quantity")]
        public decimal Quantity { get; set; }

        [BsonField("unit_price")]
        public decimal UnitPrice { get; set; }

        [BsonField("discount_amount")]
        public decimal DiscountAmount { get; set; }

        [BsonField("total_amount")]
        public decimal TotalAmount { get; set; }

        [BsonField("unit_id")]
        public string UnitId { get; set; }

        [BsonField("cfop_id")]
        public string CfopId { get; set; }

        [BsonField("cst_icms_id")]
        public string CstIcmsId { get; set; }

        [BsonField("cst_pis_id")]
        public string CstPisId { get; set; }

        [BsonField("cst_cofins_id")]
        public string CstCofinsId { get; set; }

        public string IndexKey => $"{ClientId}|{InvoiceId}";

        [BsonIgnore]
        public List<InvoiceItemTax> Taxes { get; set; } = new();


    }
}
