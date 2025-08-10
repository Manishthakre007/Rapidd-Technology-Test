using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.IO;
using System.Threading.Tasks;

class Program
{
    public class TimeEntry
    {
        public string EmployeeName { get; set; }
        public DateTime StarTimeUtc { get; set; }
        public DateTime EndTimeUtc { get; set; }
    }

    static async Task Main()
    {
        const string apiUrl = "https://rc-vault-fap-live-1.azurewebsites.net/api/gettimeentries?code=v017RnE8vuzXzPJo5eaLLjXjmRW07law99QTD90zat9FfOQJKKUcgQ==";

        using var client = new HttpClient();
        var json = await client.GetStringAsync(apiUrl);
        var entries = JsonSerializer.Deserialize<List<TimeEntry>>(json);

        var employeeHours = entries
            .Where(e => !string.IsNullOrEmpty(e.EmployeeName))
            .GroupBy(e => e.EmployeeName)
            .Select(g => new {
                Name = g.Key,
                TotalHours = g.Sum(e => (e.EndTimeUtc - e.StarTimeUtc).TotalHours)
            })
            .OrderByDescending(e => e.TotalHours)
            .ToList();

        string html = $@"
        <html>
        <head>
            <title>Employee Hours</title>
            <style>
                table {{ border-collapse: collapse; width: 100%; }}
                th, td {{ border: 1px solid #ddd; padding: 8px; text-align: left; }}
                tr:nth-child(even) {{ background-color: #f2f2f2; }}
                .low-hours {{ background-color: #ffcccc; }}
            </style>
        </head>
        <body>
            <h1>Employee Work Hours</h1>
            <table>
                <thead>
                    <tr><th>Name</th><th>Total Hours</th></tr>
                </thead>
                <tbody>
        {string.Join("\n", employeeHours.Select(e => 
            $@"<tr class='{(e.TotalHours < 100 ? "low-hours" : "")}'>
                <td>{e.Name}</td>
                <td>{e.TotalHours:F2}</td>
            </tr>"
        ))}
                </tbody>
            </table>
        </body>
        </html>";

        File.WriteAllText("employees.html", html);
        Console.WriteLine("HTML file generated: employees.html");
    }
}
