using Inovesys.Retail.Entities;
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
        Task<IReadOnlyList<ProductDto>> FindAsync(
            string term,
            int limit,
            bool preferCode,
            int clientId,
            CancellationToken ct);
    }

    public class ProductDto
    {
        public string Id { get; set; } = "";  // mapeia Material.Id
        public string Name { get; set; } = "";  // mapeia Material.Name
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
        }

        public async Task<IReadOnlyList<ProductDto>> FindAsync(
            string term,
            int limit,
            bool preferCode,
            int clientId,
            CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                var col = _db.GetCollection<Material>("material");
                var list = new List<ProductDto>(limit);

                term = (term ?? "").Trim();
                if (string.IsNullOrEmpty(term)) return (IReadOnlyList<ProductDto>)list;

                // Preparos para LIKE (case-insensitive via UPPER)
                string like = "%" + term.ToUpperInvariant() + "%";

                // Helper de mapeamento
                static ProductDto Map(Material m) => new() { Id = m.Id, Name = m.Name };

                // Quando preferCode = true, prioriza EAN/ID prefixo e completa por nome
                if (preferCode)
                {
                    // 1) EAN13 exato (se só dígitos com 8+)
                    if (term.All(char.IsDigit) && term.Length >= 8)
                    {
                        var byEanExact = col.Query()
                            .Where("($.client_id = @0) AND ($.ean_13 = @1)", clientId, term)
                            .OrderBy("$.ean_13")
                            .Limit(limit)
                            .ToList();
                        list.AddRange(byEanExact.Select(Map));
                    }

                    // 2) EAN prefixo (LiteDB não suporta STARTSWITH, usa LIKE)
                    if (list.Count < limit)
                    {
                        var remain = limit - list.Count;
                        var termUpper = term.ToUpperInvariant();

                        var byEanPrefix = col.Query()
                            // LIKE + "%" → busca prefixo; UPPER para case-insensitive
                            .Where("( $.client_id = @0 ) AND ( UPPER($.ean_13) LIKE @1 )", clientId, termUpper + "%")
                            .OrderBy("$.ean_13")
                            .Limit(remain)
                            .ToList();

                        // evita duplicar registros já adicionados
                        var seen = new HashSet<string>(
                            list.Select(x => x.Id),
                            StringComparer.OrdinalIgnoreCase
                        );

                        foreach (var m in byEanPrefix)
                        {
                            if (seen.Add(m.Id))
                                list.Add(Map(m));
                        }
                    }

                    // 3) ID prefixo (LiteDB não suporta STARTSWITH, usa LIKE)
                    if (list.Count < limit)
                    {
                        var remain = limit - list.Count;
                        var termUpper = term.ToUpperInvariant();

                        var byIdPrefix = col.Query()
                            .Where("( $.client_id = @0 ) AND ( UPPER($.id) LIKE @1 )", clientId, termUpper + "%")
                            .OrderBy("$.id")
                            .Limit(remain)
                            .ToList();

                        // evita duplicar registros já adicionados
                        var seen = new HashSet<string>(
                            list.Select(x => x.Id),
                            StringComparer.OrdinalIgnoreCase
                        );

                        foreach (var m in byIdPrefix)
                        {
                            if (seen.Add(m.Id))
                                list.Add(Map(m));
                        }
                    }

                    // 4) Nome contém (case-insensitive via UPPER)
                    if (list.Count < limit)
                    {

                        var remain = limit - list.Count;

                        var byName = col.Query()
                            .Where("(UPPER($.name) LIKE @0) AND ($.client_id = @1)", like, clientId)
                            .OrderBy("$.name")
                            .Limit(remain)
                            .ToList();
                        var seen = new HashSet<string>(list.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
                        foreach (var m in byName) if (seen.Add(m.Id)) list.Add(Map(m));
                    }
                }
                else
                {
         
                    var tokens = TextFold.Fold(term)
                        .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                    var likeInOrder = "%" + string.Join("%", tokens) + "%";  // "CAFE 3" -> "%CAFE%3%"

                    var byName = col.Query()
                        .Where($@"($.client_id = @0) AND (
                                    {BuildFoldExpr("$.name")} LIKE @1
                                  )", new BsonValue(clientId), new BsonValue(likeInOrder))
                            .OrderBy("$.name")
                            .Limit(limit)
                            .ToList();

                    list.AddRange(byName.Select(Map));

                    // Completa com ID prefixo (case-insensitive; LiteDB não tem STARTSWITH)
                    if (list.Count < limit)
                    {
                        var remain = limit - list.Count;
                        var termUpper = term.ToUpperInvariant();

                        var byIdPrefix = col.Query()
                            .Where("( $.client_id = @0 ) AND ( UPPER($.id) LIKE @1 )", clientId, termUpper + "%")
                            .OrderBy("$.id")
                            .Limit(remain)
                            .ToList();

                        // OBS: se seu DTO usa Code em vez de Id, troque x.Id por x.Code abaixo
                        var seen = new HashSet<string>(list.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
                        foreach (var m in byIdPrefix)
                            if (seen.Add(m.Id))
                                list.Add(Map(m));
                    }

                    // E completa com EAN prefixo
                    // Completa com ID prefixo (case-insensitive; LiteDB não tem STARTSWITH)
                    if (list.Count < limit)
                    {
                        var remain = limit - list.Count;
                        var termUpper = term.ToUpperInvariant();

                        var byIdPrefix = col.Query()
                            // UPPER para tornar a busca case-insensitive
                            .Where("( $.client_id = @0 ) AND ( UPPER($.id) LIKE @1 )", clientId, termUpper + "%")
                            .OrderBy("$.id")
                            .Limit(remain)
                            .ToList();

                        // Evita duplicados já adicionados na lista anterior
                        var seen = new HashSet<string>(
                            list.Select(x => x.Id),
                            StringComparer.OrdinalIgnoreCase
                        );

                        foreach (var m in byIdPrefix)
                        {
                            if (seen.Add(m.Id))
                                list.Add(Map(m));
                        }
                    }
                }

                return (IReadOnlyList<ProductDto>)list;
            }, ct);
        }

        static string FoldExpr(string field)
        {
            // mapeamento mínimo PT-BR; amplie se precisar
            var pairs = new (string fromChar, string toChar)[] {
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
                            ("Ç","C"),("ç","c")
             };

            var expr = field;
            foreach (var p in pairs) expr = $"REPLACE({expr}, '{p.fromChar}', '{p.toChar}')";
            return $"UPPER({expr})";
        }
    }




}
