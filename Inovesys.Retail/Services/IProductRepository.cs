using Inovesys.Retail.Entities;
using Inovesys.Retail.Models;
using LiteDB;
using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace Inovesys.Retail.Services
{

    public static class TextFold
    {
        public static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            var norm = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(norm.Length);
            foreach (var ch in norm)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat != UnicodeCategory.NonSpacingMark) sb.Append(ch);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        }
    }


    public interface IProductRepository
    {
        Task<IReadOnlyList<ProductSuggestion>> FindAsync(
            string term,
            int limit,
            bool preferCode,
            int clientId,
            CancellationToken ct);
    }


    public class ProductRepositoryLiteDb : IProductRepository
    {
        private readonly LiteDbService _db;

        // Helper p/ montar expressão REPLACE...UPPER(...) só uma vez
        private static readonly string NameFoldExpr = BuildFoldExpr("$.name");

        private static string BuildFoldExpr(string field)
        {
            var pairs = new (string fromC, string toC)[] {
                        ("Á","A"),("À","A"),("Â","A"),("Ã","A"),("Ä","A"),
                        ("á","a"),("à","a"),("â","a"),("ã","a"),("ä","a"),
                        ("É","E"),("È","E"),("Ê","E"),("Ë","E"),
                        ("é","e"),("è","e"),("ê","e"),("ë","e"),
                        ("Í","I"),("Ì","I"),("Î","I"),("Ï","I"),
                        ("í","i"),("ì","i"),("î","i"),("ï","i"),
                        ("Ó","O"),("Ò","O"),("Ô","O"),("Õ","O"),("Ö","O"),
                        ("ó","o"),("ò","o"),("ô","o"),("õ","o"),("ö","o"),
                        ("Ú","U"),("Ù","U"),("Û","U"),("Ü","U"),
                        ("ú","u"),("ù","u"),("û","u"),("ü","u"),
                    };

            var expr = field;
            foreach (var (fromC, toC) in pairs)
                expr = $"REPLACE({expr}, '{fromC}', '{toC}')";

            return $"UPPER({expr})";
        }

        public ProductRepositoryLiteDb(LiteDbService db)
        {
            _db = db;

            var col = _db.GetCollection<Material>("material");
            // Índices idempotentes
            col.EnsureIndex(x => x.Id, unique: false);
            col.EnsureIndex(x => x.Ean13, unique: false);
            col.EnsureIndex(x => x.Name, unique: false);
            col.EnsureIndex(x => x.ClientId, unique: false);


            var priceCol = _db.GetCollection<MaterialPrice>("materialprice");
            priceCol.EnsureIndex(x => x.ClientId, unique: false);
            priceCol.EnsureIndex(x => x.MaterialId, unique: false);
            priceCol.EnsureIndex(x => x.MaterialUnit, unique: false);
            priceCol.EnsureIndex(x => x.StartDate, unique: false);
            priceCol.EnsureIndex(x => x.EndDate, unique: false);

        }

        public async Task<IReadOnlyList<ProductSuggestion>> FindAsync(
                              string term,
                              int limit,
                              bool preferCode,
                              int clientId,
                              CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                var col = _db.GetCollection<Material>("material");
                var materialsFound = new List<Material>(limit);

                term = (term ?? "").Trim();
                if (string.IsNullOrEmpty(term)) return Array.Empty<ProductSuggestion>();

                // Preparos para LIKE (case-insensitive via UPPER)
                string like = "%" + term.ToUpperInvariant() + "%";

                // Helper interno para adicionar materiais evitando duplicados e respeitando o limit
                void AddRangeNoDup(IEnumerable<Material> items)
                {
                    var seen = new HashSet<string>(
                        materialsFound.Select(x => x.Id),
                        StringComparer.OrdinalIgnoreCase
                    );

                    foreach (var m in items)
                    {
                        if (materialsFound.Count >= limit) break;
                        if (seen.Add(m.Id))
                            materialsFound.Add(m);
                    }
                }

                if (preferCode)
                {
                    // 1) EAN13 exato (se só dígitos com 8+)
                    if (term.All(char.IsDigit) && term.Length >= 8 && materialsFound.Count < limit)
                    {
                        var byEanExact = col.Query()
                            .Where("($.client_id = @0) AND ($.ean_13 = @1)", clientId, term)
                            .OrderBy("$.ean_13")
                            .Limit(limit)
                            .ToList();
                        AddRangeNoDup(byEanExact);
                    }

                    // 2) EAN prefixo (LIKE + "%")
                    if (materialsFound.Count < limit)
                    {
                        var remain = limit - materialsFound.Count;
                        var termUpper = term.ToUpperInvariant();

                        var byEanPrefix = col.Query()
                            .Where("( $.client_id = @0 ) AND ( UPPER($.ean_13) LIKE @1 )", clientId, termUpper + "%")
                            .OrderBy("$.ean_13")
                            .Limit(remain)
                            .ToList();

                        AddRangeNoDup(byEanPrefix);
                    }

                    // 3) ID prefixo (LIKE + "%")
                    if (materialsFound.Count < limit)
                    {
                        var remain = limit - materialsFound.Count;
                        var termUpper = term.ToUpperInvariant();

                        var byIdPrefix = col.Query()
                            .Where("( $.client_id = @0 ) AND ( UPPER($.id) LIKE @1 )", clientId, termUpper + "%")
                            .OrderBy("$.id")
                            .Limit(remain)
                            .ToList();

                        AddRangeNoDup(byIdPrefix);
                    }

                    // 4) Nome contém
                    if (materialsFound.Count < limit)
                    {
                        var remain = limit - materialsFound.Count;

                        var byName = col.Query()
                            .Where("(UPPER($.name) LIKE @0) AND ($.client_id = @1)", like, clientId)
                            .OrderBy("$.name")
                            .Limit(remain)
                            .ToList();

                        AddRangeNoDup(byName);
                    }
                }
                else
                {
                    // Busca por nome tokenizada e "folded"
                    var tokens = TextFold.Fold(term)
                        .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                    var likeInOrder = "%" + string.Join("%", tokens) + "%";  // "CAFE 3" -> "%CAFE%3%"

                    var byName = col.Query()
                        .Where($@"($.client_id = @0) AND (
                            {BuildFoldExpr("$.name")} LIKE @1
                          )",
                               new BsonValue(clientId), new BsonValue(likeInOrder))
                        .OrderBy("$.name")
                        .Limit(limit)
                        .ToList();

                    AddRangeNoDup(byName);

                    // Completa com ID prefixo
                    if (materialsFound.Count < limit)
                    {
                        var remain = limit - materialsFound.Count;
                        var termUpper = term.ToUpperInvariant();

                        var byIdPrefix = col.Query()
                            .Where("( $.client_id = @0 ) AND ( UPPER($.id) LIKE @1 )", clientId, termUpper + "%")
                            .OrderBy("$.id")
                            .Limit(remain)
                            .ToList();

                        AddRangeNoDup(byIdPrefix);
                    }

                    // Completa com EAN prefixo (se quiser manter)
                    if (materialsFound.Count < limit)
                    {
                        var remain = limit - materialsFound.Count;
                        var termUpper = term.ToUpperInvariant();

                        var byEanPrefix = col.Query()
                            .Where("( $.client_id = @0 ) AND ( UPPER($.ean_13) LIKE @1 )", clientId, termUpper + "%")
                            .OrderBy("$.ean_13")
                            .Limit(remain)
                            .ToList();

                        AddRangeNoDup(byEanPrefix);
                    }
                }


                var dtoList = new List<ProductSuggestion>(materialsFound.Count);
                foreach (var m in materialsFound)
                {
                    var baseUnit = m.BasicUnit;
                    var (price, unit) = GetCurrentPriceForMaterialUnit(clientId, m.Id, baseUnit);

                    if (price is not null)
                    {
                        dtoList.Add(new ProductSuggestion
                        {
                            Id = m.Id,
                            Name = m.Name,
                            Price = price,
                            // se não houver preço vigente, usa a unidade base do material como fallback visual
                            PriceUnit = unit ?? baseUnit
                        });

                    }
                    
                }

                return (IReadOnlyList<ProductSuggestion>)dtoList;
            }, ct);
        }



        private (decimal? price, string unit) GetCurrentPriceForMaterialUnit(
                                int clientId,
                                string materialId,
                                string baseUnit)
        {
            if (string.IsNullOrWhiteSpace(materialId))
                return (null, null);

            var priceCol = _db.GetCollection<MaterialPrice>("materialprice");

            // DateTime em UTC, no mesmo "tipo" do que está no banco
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

            // 1) Tenta vigente COM unidade básica igual
            if (!string.IsNullOrWhiteSpace(baseUnit))
            {
                var valid = priceCol.Find(x =>
                          x.ClientId == clientId &&
                          x.MaterialId == materialId &&
                          x.StartDate <= now &&
                          x.EndDate >= now &&
                          x.By == 1
                          )
                      .OrderByDescending(x => x.StartDate)
                      .Take(1)
                      .ToList();

                // log só pra você ver o valor
                System.Diagnostics.Debug.WriteLine($"now = {now:O} (Kind={now.Kind})");

                if (valid.Count > 0)
                    return (valid[0].Price, valid[0].MaterialUnit);
            }

            ////// 2) Se não achou vigente na unidade básica, tenta vigente em qualquer unidade
            ////var anyValid = priceCol.Query()
            ////    .Where(@"($.client_id = @0)
            ////     AND ($.material_id = @1)
            ////     AND ($.start_date <= @2)
            ////     AND ($.end_date >= @2)",
            ////           clientId, materialId, now)
            ////    .OrderByDescending("$.start_date")
            ////    .Limit(1)
            ////    .ToList();

            ////if (anyValid.Count > 0)
            ////    return (anyValid[0].Price, anyValid[0].MaterialUnit);

            //// 3) Fallback: último preço (mais recente) na unidade básica
            //if (!string.IsNullOrWhiteSpace(baseUnit))
            //{
            //    var latestSameUnit = priceCol.Query()
            //        .Where(@"($.client_id = @0)
            //         AND ($.material_id = @1)
            //         AND ($.material_unit = @2)",
            //               clientId, materialId, baseUnit)
            //        .OrderByDescending("$.start_date")
            //        .Limit(1)
            //        .ToList();

            //    if (latestSameUnit.Count > 0)
            //        return (latestSameUnit[0].Price, latestSameUnit[0].MaterialUnit);
            //}

            //// 4) Último preço em qualquer unidade (como último recurso)
            //var latestAny = priceCol.Query()
            //    .Where(@"($.client_id = @0)
            //     AND ($.material_id = @1)",
            //           clientId, materialId)
            //    .OrderByDescending("$.start_date")
            //    .Limit(1)
            //    .ToList();

            //if (latestAny.Count > 0)
            //    return (latestAny[0].Price, latestAny[0].MaterialUnit);

            return (null, null);
        }

    }




}
