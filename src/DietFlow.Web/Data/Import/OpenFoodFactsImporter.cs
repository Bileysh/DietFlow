using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using Npgsql;
using NpgsqlTypes;

namespace DietFlow.Web.Data.Import;

public class OpenFoodFactsImporter(string connectionString, ILogger logger)
{
    public const string SourceUrl = "https://openfoodfacts-ds.s3.eu-west-3.amazonaws.com/en.openfoodfacts.org.products.csv.gz";
    private const int BatchSize = 50_000;

    private static readonly Dictionary<string, string> Groups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cereals and potatoes"] = "Крупи та картопля",
        ["Milk and dairy products"] = "Молочні продукти",
        ["Fish Meat Eggs"] = "М'ясо, риба, яйця",
        ["Fruits and vegetables"] = "Фрукти та овочі",
        ["Fat and sauces"] = "Жири та соуси",
        ["Sugary snacks"] = "Солодощі",
        ["Salty snacks"] = "Солоні снеки",
        ["Beverages"] = "Напої",
        ["Composite foods"] = "Готові страви",
    };

    private record Row(string Name, string? Category, decimal Kcal, decimal Protein, decimal Fat, decimal Carbs, string Barcode);

    public async Task RunAsync(int? limit, CancellationToken ct = default)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        await Exec(conn, """
            CREATE TEMP TABLE off_staging (
                name text, category text, kcal numeric, protein numeric, fat numeric, carbs numeric, barcode text)
            """, ct);

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DietFlow-student-project/1.0 (+https://github.com/Bileysh/DietFlow)");

        logger.LogInformation("Downloading {Url} (streaming, not saved to disk)…", SourceUrl);
        using var response = await http.GetAsync(SourceUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength ?? 0;

        await using var network = await response.Content.ReadAsStreamAsync(ct);
        await using var counting = new CountingStream(network);
        await using var gzip = new GZipStream(counting, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1 << 16);

        var header = (await reader.ReadLineAsync(ct))?.Split('\t') ?? throw new InvalidDataException("Empty file");
        int Col(string name) => Array.IndexOf(header, name) is var i and >= 0 ? i : throw new InvalidDataException($"Column {name} not found");
        var (cCode, cName, cGroup, cKcal, cProt, cFat, cCarb) =
            (Col("code"), Col("product_name"), Col("food_groups_en"), Col("energy-kcal_100g"), Col("proteins_100g"), Col("fat_100g"), Col("carbohydrates_100g"));
        var maxCol = new[] { cCode, cName, cGroup, cKcal, cProt, cFat, cCarb }.Max();

        var batch = new List<Row>(BatchSize);
        long read = 0, accepted = 0, inserted = 0;
        var sw = Stopwatch.StartNew();

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            read++;
            var f = line.Split('\t');
            if (f.Length > maxCol && TryParse(f, cCode, cName, cGroup, cKcal, cProt, cFat, cCarb) is { } row)
            {
                batch.Add(row);
                accepted++;
            }

            if (batch.Count >= BatchSize)
            {
                inserted += await FlushAsync(conn, batch, ct);
                logger.LogInformation("{Read:N0} rows read · {Accepted:N0} valid · {Inserted:N0} inserted · {Mb:N0}/{TotalMb:N0} MB · {Elapsed}",
                    read, accepted, inserted, counting.BytesRead / 1048576, totalBytes / 1048576, sw.Elapsed.ToString(@"mm\:ss"));
            }
            if (limit is { } l && accepted >= l) break;
        }

        inserted += await FlushAsync(conn, batch, ct);
        await Exec(conn, "ANALYZE \"Products\"", ct); 
        logger.LogInformation("Done: {Read:N0} rows read, {Accepted:N0} valid, {Inserted:N0} new products in {Elapsed}.",
            read, accepted, inserted, sw.Elapsed.ToString(@"hh\:mm\:ss"));
    }

    private static Row? TryParse(string[] f, int cCode, int cName, int cGroup, int cKcal, int cProt, int cFat, int cCarb)
    {
        var name = f[cName].Trim();
        var code = f[cCode].Trim();
        if (name.Length is < 2 or > 200 || code.Length is 0 or > 50) return null;

        if (!Dec(f[cKcal], out var kcal) || !Dec(f[cProt], out var p) || !Dec(f[cFat], out var fat) || !Dec(f[cCarb], out var c))
            return null;
        if (kcal is < 0 or > 900 || p is < 0 or > 100 || fat is < 0 or > 100 || c is < 0 or > 100 || p + fat + c > 100)
            return null;

        var group = f[cGroup].Split(',', 2)[0].Trim();
        return new Row(name, Groups.GetValueOrDefault(group, "Інше"), kcal, p, fat, c, code);
    }

    private static bool Dec(string s, out decimal value)
    {
        if (!decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
        value = Math.Round(value, 2); 
        return true;
    }

    private static async Task<long> FlushAsync(NpgsqlConnection conn, List<Row> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return 0;

        await using (var copy = await conn.BeginBinaryImportAsync(
                         "COPY off_staging (name, category, kcal, protein, fat, carbs, barcode) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var r in batch)
            {
                await copy.StartRowAsync(ct);
                await copy.WriteAsync(r.Name, NpgsqlDbType.Text, ct);
                await copy.WriteAsync(r.Category, NpgsqlDbType.Text, ct);
                await copy.WriteAsync(r.Kcal, NpgsqlDbType.Numeric, ct);
                await copy.WriteAsync(r.Protein, NpgsqlDbType.Numeric, ct);
                await copy.WriteAsync(r.Fat, NpgsqlDbType.Numeric, ct);
                await copy.WriteAsync(r.Carbs, NpgsqlDbType.Numeric, ct);
                await copy.WriteAsync(r.Barcode, NpgsqlDbType.Text, ct);
            }
            await copy.CompleteAsync(ct);
        }
        batch.Clear();

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO "Products" ("Name", "Category", "KcalPer100g", "ProteinPer100g", "FatPer100g", "CarbsPer100g", "Barcode")
            SELECT name, category, kcal, protein, fat, carbs, barcode FROM off_staging
            ON CONFLICT ("Barcode") WHERE "Barcode" IS NOT NULL DO NOTHING;
            """, conn);
        var inserted = await cmd.ExecuteNonQueryAsync(ct);
        await Exec(conn, "TRUNCATE off_staging", ct);
        return inserted;
    }

    private static async Task Exec(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) { var n = inner.Read(buffer, offset, count); BytesRead += n; return n; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { var n = await inner.ReadAsync(buffer, ct); BytesRead += n; return n; }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
