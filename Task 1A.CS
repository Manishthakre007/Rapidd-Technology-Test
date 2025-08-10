using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using SkiaSharp;

namespace TimeVisualizer;

public class Program
{
    private const string Endpoint = "https://rc-vault-fap-live-1.azurewebsites.net/api/gettimeentries?code=vO17RnE8vuzXzPJo5eaLLjXjmRW07law99QTD90zat9FfOQJKKUcgQ==";

    public static async Task Main()
    {
        try
        {
            Console.WriteLine("Fetching time entries...");
            var entries = await FetchTimeEntries();
            if (entries.Count == 0)
            {
                Console.WriteLine("No entries found.");
                return;
            }

            var totals = AggregateHoursByEmployee(entries);
            var ordered = totals.OrderByDescending(kvp => kvp.Value).ToList();

            var htmlPath = Path.GetFullPath("employees.html");
            var pngPath = Path.GetFullPath("employees_pie.png");

            Console.WriteLine($"Writing HTML table -> {htmlPath}");
            File.WriteAllText(htmlPath, GenerateHtml(ordered));

            Console.WriteLine($"Generating pie chart -> {pngPath}");
            GeneratePieChart(ordered, pngPath);

            Console.WriteLine("Done.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}\n{ex}");
        }
    }

    private static async Task<List<TimeEntry>> FetchTimeEntries()
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var contentStream = await response.Content.ReadAsStreamAsync();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var data = await JsonSerializer.DeserializeAsync<List<TimeEntry>>(contentStream, options);
        return data ?? new List<TimeEntry>();
    }

    private static Dictionary<string, double> AggregateHoursByEmployee(IEnumerable<TimeEntry> entries)
    {
        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.EmployeeName)) continue;
            var hours = CalculateHours(e);
            if (hours <= 0) continue;
            totals[e.EmployeeName] = totals.GetValueOrDefault(e.EmployeeName) + hours;
        }
        return totals;
    }

    private static double CalculateHours(TimeEntry e)
    {
        // Some records can be malformed; handle defensively
        if (!DateTime.TryParse(e.StarTimeUtc, out var startUtc)) return 0;
        if (!DateTime.TryParse(e.EndTimeUtc, out var endUtc)) return 0;
        if (endUtc <= startUtc) return 0;
        var duration = endUtc - startUtc;
        return duration.TotalHours;
    }

    private static string GenerateHtml(List<KeyValuePair<string, double>> ordered)
    {
        var rows = string.Join("\n", ordered.Select((kvp, idx) =>
        {
            var name = WebUtility.HtmlEncode(kvp.Key);
            var total = kvp.Value;
            var hoursText = total.ToString("0.##");
            var highlight = total < 100.0 ? " class=\"low\"" : string.Empty;
            return $"<tr{highlight}><td>{idx + 1}</td><td>{name}</td><td>{hoursText}</td></tr>";
        }));

        var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>Employee Time Worked</title>
  <style>
    body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; padding: 24px; color: #1f2937; }}
    h1 {{ margin: 0 0 16px; font-size: 24px; }}
    table {{ border-collapse: collapse; width: 100%; max-width: 900px; }}
    th, td {{ padding: 10px 12px; border-bottom: 1px solid #e5e7eb; text-align: left; }}
    th {{ background: #f9fafb; font-weight: 600; }}
    tr.low {{ background: #fee2e2; }}
    caption {{ text-align: left; margin-bottom: 8px; color: #6b7280; }}
  </style>
</head>
<body>
  <h1>Employee Total Hours</h1>
  <table>
    <caption>Rows highlighted when total hours &lt; 100</caption>
    <thead>
      <tr><th>#</th><th>Name</th><th>Total Hours</th></tr>
    </thead>
    <tbody>
      {rows}
    </tbody>
  </table>
</body>
</html>";
        return html;
    }

    private static void GeneratePieChart(List<KeyValuePair<string, double>> ordered, string outputPath)
    {
        if (ordered.Count == 0) return;

        int width = 1000;
        int height = 700;
        int legendWidth = 380;
        int chartDiameter = Math.Min(width - legendWidth - 60, height - 80);
        int centerX = 40 + chartDiameter / 2;
        int centerY = 40 + chartDiameter / 2;

        double total = ordered.Sum(k => k.Value);
        if (total <= 0) return;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var titlePaint = new SKPaint
        {
            Color = new SKColor(31, 41, 55),
            TextSize = 28,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };
        canvas.DrawText("Share of Total Hours by Employee", 20, 36, titlePaint);

        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            Color = new SKColor(229, 231, 235),
            IsAntialias = true
        };

        var rect = new SKRect(40, 60, 40 + chartDiameter, 60 + chartDiameter);
        canvas.DrawOval(rect, borderPaint);

        var palette = GeneratePalette(ordered.Count);
        float startAngle = -90f; // start at top

        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        for (int i = 0; i < ordered.Count; i++)
        {
            var value = ordered[i].Value;
            if (value <= 0) continue;
            float sweepAngle = (float)(360.0 * (value / total));
            fillPaint.Color = palette[i];
            using var path = new SKPath();
            path.MoveTo(rect.MidX, rect.MidY);
            path.ArcTo(rect, startAngle, sweepAngle, false);
            path.Close();
            canvas.DrawPath(path, fillPaint);
            startAngle += sweepAngle;
        }

        // Legend
        float legendX = 40 + chartDiameter + 30;
        float legendY = 80;
        using var legendText = new SKPaint { Color = new SKColor(55, 65, 81), TextSize = 18, IsAntialias = true };
        using var legendTitle = new SKPaint { Color = new SKColor(31, 41, 55), TextSize = 22, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) };
        canvas.DrawText("Legend", legendX, legendY - 12, legendTitle);
        legendY += 12;

        for (int i = 0; i < ordered.Count; i++)
        {
            var (name, value) = (ordered[i].Key, ordered[i].Value);
            var percent = value / total * 100.0;
            var label = $"{name} — {percent:0.##}% ({value:0.##}h)";
            using var swatchPaint = new SKPaint { Color = palette[i], IsAntialias = true, Style = SKPaintStyle.Fill };
            var swatchRect = new SKRect(legendX, legendY + i * 28, legendX + 20, legendY + 20 + i * 28);
            canvas.DrawRect(swatchRect, swatchPaint);
            canvas.DrawText(label, legendX + 28, legendY + 16 + i * 28, legendText);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(outputPath);
        data.SaveTo(stream);
    }

    private static List<SKColor> GeneratePalette(int count)
    {
        var colors = new List<SKColor>();
        // Generate distinct hues
        for (int i = 0; i < count; i++)
        {
            float hue = (i * 360f / Math.Max(1, count));
            colors.Add(HslToColor(hue, 0.6f, 0.55f));
        }
        return colors;
    }

    private static SKColor HslToColor(float h, float s, float l)
    {
        // Convert HSL to RGB
        // Source: standard algorithm
        h = (h % 360 + 360) % 360;
        float c = (1 - MathF.Abs(2 * l - 1)) * s;
        float x = c * (1 - MathF.Abs((h / 60f) % 2 - 1));
        float m = l - c / 2f;
        float r1, g1, b1;
        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }
        byte R = (byte)Math.Clamp((int)((r1 + m) * 255), 0, 255);
        byte G = (byte)Math.Clamp((int)((g1 + m) * 255), 0, 255);
        byte B = (byte)Math.Clamp((int)((b1 + m) * 255), 0, 255);
        return new SKColor(R, G, B);
    }
}

public sealed class TimeEntry
{
    public string? Id { get; set; }
    public string? EmployeeName { get; set; }
    public string? StarTimeUtc { get; set; }
    public string? EndTimeUtc { get; set; }
    public string? EntryNotes { get; set; }
    public string? DeletedOn { get; set; }
}