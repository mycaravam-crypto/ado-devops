namespace AdoCli.Cli;

using System.Diagnostics;

/// <summary>Prints the already generated standalone HTML to PDF using a locally installed Chromium browser.</summary>
public static class TestClosurePdf
{
    public static async Task WriteAsync(string html, string outputPath)
    {
        var browser = FindBrowser() ?? throw AdoException.Usage(
            "PDF export requires Chromium or Google Chrome installed (or ADO_PDF_BROWSER pointing to its executable).");
        var target = Path.GetFullPath(outputPath);
        var scratch = Path.Combine(Path.GetTempPath(), "ado-closure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var source = Path.Combine(scratch, "report.html");
            File.WriteAllText(source, html);
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(browser)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add("--headless");
            process.StartInfo.ArgumentList.Add("--disable-gpu");
            process.StartInfo.ArgumentList.Add("--no-first-run");
            process.StartInfo.ArgumentList.Add("--disable-extensions");
            process.StartInfo.ArgumentList.Add("--no-pdf-header-footer");
            var generated = Path.Combine(scratch, "report.pdf");
            process.StartInfo.ArgumentList.Add("--print-to-pdf=" + generated);
            process.StartInfo.ArgumentList.Add(new Uri(source).AbsoluteUri);
            process.Start();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new AdoException("PDF browser timed out.");
            }
            var errors = await stderr;
            if (process.ExitCode != 0 || !File.Exists(generated) || new FileInfo(generated).Length == 0)
                throw new AdoException($"PDF browser failed (exit {process.ExitCode}): {errors}");
            File.Move(generated, target, overwrite: true);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static string? FindBrowser()
    {
        var configured = Environment.GetEnvironmentVariable("ADO_PDF_BROWSER");
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) ? configured : null;
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "chrome.exe", "msedge.exe", "chromium.exe" }
            : new[] { "chromium", "chromium-browser", "google-chrome", "google-chrome-stable", "microsoft-edge" };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                var path = Path.Combine(directory, candidate);
                if (File.Exists(path)) return path;
            }
        return null;
    }
}
