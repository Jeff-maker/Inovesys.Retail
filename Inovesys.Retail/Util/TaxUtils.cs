using Inovesys.Retail.Entities;
using Inovesys.Retail.Services;
using System.Globalization;

public static class TaxUtils
{
    public sealed record ItemTax(string MaterialId, string? NcmId, decimal Base, decimal PercentFederal, decimal PercentState, decimal PercentMunicipal, decimal AmountFederal, decimal AmountState, decimal AmountMunicipal)
    {
        public decimal PercentTotal => PercentFederal + PercentState + PercentMunicipal;
        public decimal AmountTotal => AmountFederal + AmountState + AmountMunicipal;
    }

    public sealed record ApproxTaxesResult(decimal TotalFederal, decimal TotalState, decimal TotalMunicipal, List<ItemTax> Items)
    {
        public decimal Total => TotalFederal + TotalState + TotalMunicipal;
    }

    /// <param name="assumeImported">Se true usa a alíquota "Importado" (IBPT) no lugar da "Nacional".</param>
    public static ApproxTaxesResult CalculateApproximateTaxes(Invoice inv, LiteDbService db, bool assumeImported = false)
    {
        var ncmCol = db.GetCollection<Ncm>("ncm"); // ajuste o nome da coleção se for diferente
        var items = new List<ItemTax>();

        decimal totalFed = 0, totalUf = 0, totalMun = 0;

        foreach (var it in inv.Items ?? Enumerable.Empty<InvoiceItem>())
        {
            var qty = it.Quantity <= 0 ? 1 : it.Quantity;
            var unitPrice = it.UnitPrice > 0 ? it.UnitPrice : (it.TotalAmount > 0 ? it.TotalAmount / qty : 0m);
            var baseLine = unitPrice * qty;

            var ncm = !string.IsNullOrWhiteSpace(it.NCM)
                ? ncmCol.FindById(it.NCM)
                : null;

            decimal pFed, pUf, pMun;
            if (ncm is null)
            {
                pFed = pUf = pMun = 0m;
            }
            else
            {
                // IBPT: para produto nacional, usa Nacional+Estadual+Municipal
                // para importado, usa Importado+Estadual+Municipal
                var federalPercent = assumeImported
                    ? (ncm.ImportedTax)
                    : (ncm.NationalTax);

                pFed = SafePercent(federalPercent);
                pUf = SafePercent(ncm.StateTax);
                pMun = SafePercent(ncm.MunicipalTax );
            }

            var aFed = Round2(baseLine * pFed / 100m);
            var aUf = Round2(baseLine * pUf / 100m);
            var aMun = Round2(baseLine * pMun / 100m);

            totalFed += aFed;
            totalUf += aUf;
            totalMun += aMun;

            items.Add(new ItemTax(
                MaterialId: it.MaterialId,
                NcmId: it.NCM,
                Base: baseLine,
                PercentFederal: pFed, PercentState: pUf, PercentMunicipal: pMun,
                AmountFederal: aFed, AmountState: aUf, AmountMunicipal: aMun
            ));
        }

        return new ApproxTaxesResult(Round2(totalFed), Round2(totalUf), Round2(totalMun), items);
    }

    public static string BuildIbptObservation(ApproxTaxesResult r)
    {
        // Ex.: "Tributos aprox. Lei 12.741/2012: R$ 12,34 (Fed: R$ 8,22; Est: R$ 3,45; Mun: R$ 0,67). Fonte: IBPT."
        var ci = new CultureInfo("pt-BR");
        return $"Tributos aproximados (Lei 12.741/2012): R$ {r.Total.ToString("N2", ci)} (Federal: R$ {r.TotalFederal.ToString("N2", ci)}; Estadual: R$ {r.TotalState.ToString("N2", ci)}; Municipal: R$ {r.TotalMunicipal.ToString("N2", ci)}). Fonte: IBPT.";
    }

    private static decimal SafePercent(decimal v) => v < 0 ? 0 : v;
    private static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
