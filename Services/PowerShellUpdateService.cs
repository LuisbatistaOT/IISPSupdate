using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Text.RegularExpressions;
using IISPSupdate.Data;
using IISPSupdate.Models;
using Microsoft.EntityFrameworkCore;

namespace IISPSupdate.Services;

/// <summary>
/// Encapsulates all interaction with PSWindowsUpdate via System.Management.Automation.
/// - Scan: uses Get-WindowsUpdate against WSUS/MU/offline as configured.
/// - Install: uses Invoke-WUJob so work always runs locally on the target (scheduled task as SYSTEM).
/// - Verify: uses Get-WUHistory / Get-WULastResults, with Get-HotFix as a fallback.
/// </summary>
public class PowerShellUpdateService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PowerShellUpdateService> _logger;

    private static readonly Regex KbRegex = new(@"KB\d{4,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public PowerShellUpdateService(AppDbContext db, ILogger<PowerShellUpdateService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Runs a PSWindowsUpdate scan against the specified server and stores results.
    /// </summary>
    public async Task<ServerScanResult> ScanAsync(int projectId, int projectServerId, CancellationToken cancellationToken = default)
    {
        var server = await _db.ProjectServers
            .Include(s => s.Project)
            .SingleAsync(s => s.Id == projectServerId && s.ProjectId == projectId, cancellationToken);

        var project = server.Project!;

        var psScript = BuildScanScript(server.Name, project.ScanSource);

        _logger.LogInformation("Starting update scan for server {Server} in project {ProjectId}", server.Name, projectId);

        var output = await InvokePowerShellAsync(psScript, cancellationToken);

        var scanResult = new ServerScanResult
        {
            ProjectId = projectId,
            ProjectServerId = server.Id,
            ScanSource = project.ScanSource,
            ScanTimeUtc = DateTime.UtcNow,
            RawOutput = output
        };

        var candidates = ParseScanOutputToCandidates(output);
        foreach (var c in candidates)
        {
            scanResult.Candidates.Add(c);
        }

        server.LastScanAtUtc = DateTime.UtcNow;

        _db.ServerScanResults.Add(scanResult);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Completed update scan for server {Server} with {Count} candidates", server.Name, candidates.Count);

        return scanResult;
    }

    /// <summary>
    /// Uses Invoke-WUJob to schedule installation of selected KBs on the target server as SYSTEM.
    /// </summary>
    public async Task InstallAsync(ProjectRun run, ProjectRunServer runServer, IEnumerable<ServerSelection> selections, CancellationToken cancellationToken = default)
    {
        var server = await _db.ProjectServers.FindAsync(new object[] { runServer.ProjectServerId }, cancellationToken);
        if (server == null)
        {
            throw new InvalidOperationException($"Server {runServer.ProjectServerId} not found.");
        }

        var kbList = selections.Select(s => s.KbNumber).Distinct().ToArray();
        if (kbList.Length == 0)
        {
            _logger.LogWarning("No KB selections found for server {Server} in run {RunId}", server.Name, run.Id);
            return;
        }

        var psScript = BuildInstallScript(server.Name, kbList, run.AllowReboot);

        _logger.LogInformation("Starting Invoke-WUJob install for server {Server} in run {RunId}", server.Name, run.Id);

        var output = await InvokePowerShellAsync(psScript, cancellationToken);

        runServer.LogTail = TruncateLog(output, 4000);
        runServer.LogPath ??= @"C:\Windows\WindowsUpdate.log"; // PSWindowsUpdate logs here by default on many builds
        runServer.LastUpdatedUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Verifies that all selected KBs are installed by querying PSWindowsUpdate history and Get-HotFix.
    /// </summary>
    public async Task<bool> VerifyAsync(ProjectRunServer runServer, IEnumerable<string> kbNumbers, CancellationToken cancellationToken = default)
    {
        var server = await _db.ProjectServers.FindAsync(new object[] { runServer.ProjectServerId }, cancellationToken);
        if (server == null)
        {
            throw new InvalidOperationException($"Server {runServer.ProjectServerId} not found.");
        }

        var kbArray = kbNumbers.Distinct().ToArray();
        if (kbArray.Length == 0)
        {
            return true;
        }

        var psScript = BuildVerifyScript(server.Name, kbArray);

        _logger.LogInformation("Starting verification for server {Server} with {Count} KBs", server.Name, kbArray.Length);

        var output = await InvokePowerShellAsync(psScript, cancellationToken);

        runServer.LogTail = TruncateLog(output, 4000);
        runServer.LastUpdatedUtc = DateTime.UtcNow;

        // The script returns a simple "True"/"False" string at the end; treat anything else as failure.
        var lastLine = output.Trim().Split(Environment.NewLine)
            .LastOrDefault(l => !string.IsNullOrWhiteSpace(l))
            ?.Trim();

        var success = string.Equals(lastLine, "True", StringComparison.OrdinalIgnoreCase);
        if (!success)
        {
            runServer.LastError = "Verification script reported one or more KBs missing. See log tail for details.";
        }

        await _db.SaveChangesAsync(cancellationToken);

        return success;
    }

    private static string BuildScanScript(string computerName, ScanSource scanSource)
    {
        var sourceSwitch = scanSource switch
        {
            ScanSource.Wsus => "-MicrosoftUpdate:$false",
            ScanSource.MicrosoftUpdate => "-MicrosoftUpdate",
            _ => "-MicrosoftUpdate" // Offline catalog could be added here with -Catalog parameter
        };

        return $@"
Import-Module PSWindowsUpdate -ErrorAction Stop

Write-Verbose ""Scanning updates on {computerName}"" -Verbose
$updates = Get-WindowsUpdate -ComputerName '{computerName}' {sourceSwitch} -IgnoreReboot -ErrorAction Stop

$updates | Select-Object KB, Title, Category, MsrcSeverity | Format-Table -AutoSize | Out-String
";
    }

    private static string BuildInstallScript(string computerName, string[] kbNumbers, bool allowReboot)
    {
        var kbFilter = string.Join(",", kbNumbers.Select(k => $"'{k}'"));
        var rebootSwitch = allowReboot ? "-AutoReboot" : "-IgnoreReboot";

        return $@"
Import-Module PSWindowsUpdate -ErrorAction Stop

Invoke-WUJob -ComputerName '{computerName}' -Script {{
    Import-Module PSWindowsUpdate -ErrorAction Stop
    $targetKbs = @({kbFilter})
    Install-WindowsUpdate -KBArticleID $targetKbs -AcceptAll {rebootSwitch} -Verbose -ErrorAction Continue
}} -RunNow -Confirm:$false -Verbose -ErrorAction Stop | Out-String
";
    }

    private static string BuildVerifyScript(string computerName, string[] kbNumbers)
    {
        var kbFilter = string.Join(",", kbNumbers.Select(k => $"'{k}'"));

        return $@"
Import-Module PSWindowsUpdate -ErrorAction SilentlyContinue

$kbs = @({kbFilter})
$missing = @()

foreach ($kb in $kbs) {{
    $found = $false

    try {{
        $hist = Get-WUHistory -ComputerName '{computerName}' -ErrorAction SilentlyContinue
        if ($hist -and ($hist | Where-Object {{ $_.KB -eq $kb }})) {{
            $found = $true
        }}
    }} catch {{ }}

    if (-not $found) {{
        try {{
            $hf = Get-HotFix -ComputerName '{computerName}' -Id $kb -ErrorAction SilentlyContinue
            if ($hf) {{
                $found = $true
            }}
        }} catch {{ }}
    }}

    if (-not $found) {{
        $missing += $kb
        Write-Output ""Missing KB: $kb""
    }}
}}

if ($missing.Count -eq 0) {{
    Write-Output ""True""
}} else {{
    Write-Output ""False""
}}
";
    }

    private static List<UpdateCandidate> ParseScanOutputToCandidates(string output)
    {
        // Basic parsing: extract lines containing KBxxxx and split on whitespace.
        var candidates = new List<UpdateCandidate>();

        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var kbMatch = KbRegex.Match(line);
            if (!kbMatch.Success)
            {
                continue;
            }

            var kb = kbMatch.Value.ToUpperInvariant();
            var title = line.Trim();

            candidates.Add(new UpdateCandidate
            {
                KbNumber = kb,
                Title = title,
                Category = null,
                Severity = null
            });
        }

        return candidates;
    }

    private async Task<string> InvokePowerShellAsync(string script, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            using var ps = PowerShell.Create();
            ps.AddScript(script);
            ps.AddCommand("Out-String");

            Collection<PSObject> results;
            try
            {
                results = ps.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PowerShell invocation failed.");
                throw;
            }

            if (ps.HadErrors)
            {
                var errors = string.Join(Environment.NewLine, ps.Streams.Error.Select(e => e.ToString()));
                _logger.LogWarning("PowerShell reported errors: {Errors}", errors);
            }

            var combined = string.Join(Environment.NewLine, results.Select(r => r.ToString()));
            return combined;
        }, cancellationToken);
    }

    private static string TruncateLog(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[^maxLength..];
    }
}

