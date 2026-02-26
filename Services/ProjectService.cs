using Hangfire;
using Hangfire.States;
using IISPSupdate.Data;
using IISPSupdate.Models;
using Microsoft.EntityFrameworkCore;

namespace IISPSupdate.Services;

/// <summary>
/// Orchestrates project runs and delegates actual scan/install/verify work to Hangfire jobs.
/// </summary>
public class ProjectService
{
    private readonly AppDbContext _db;
    private readonly PowerShellUpdateService _ps;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly ILogger<ProjectService> _logger;

    public ProjectService(
        AppDbContext db,
        PowerShellUpdateService ps,
        IBackgroundJobClient backgroundJobs,
        ILogger<ProjectService> logger)
    {
        _db = db;
        _ps = ps;
        _backgroundJobs = backgroundJobs;
        _logger = logger;
    }

    /// <summary>
    /// Enqueues a project-wide scan job on the 'scan' queue.
    /// </summary>
    public string EnqueueScan(int projectId)
    {
        var jobId = _backgroundJobs.Create<ProjectService>(
            x => x.ExecuteScanAsync(projectId, CancellationToken.None),
            new EnqueuedState("scan"));

        _logger.LogInformation("Enqueued scan job {JobId} for project {ProjectId}", jobId, projectId);
        return jobId;
    }

    /// <summary>
    /// Hangfire job: performs DNS/WinRM/admin pre-flight and then scans all servers.
    /// </summary>
    public async Task ExecuteScanAsync(int projectId, CancellationToken cancellationToken)
    {
        var project = await _db.Projects
            .Include(p => p.Servers)
            .SingleAsync(p => p.Id == projectId, cancellationToken);

        foreach (var server in project.Servers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await RunPreFlightAsync(server, cancellationToken);
                await _ps.ScanAsync(projectId, server.Id, cancellationToken);
                server.PreFlightStatus = PreFlightStatus.Success;
                server.PreFlightError = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scan failed for server {Server}", server.Name);
                server.PreFlightStatus = PreFlightStatus.Failed;
                server.PreFlightError = ex.Message;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Creates a run entity for the project and schedules per-server install jobs.
    /// </summary>
    public async Task<ProjectRun> CreateAndEnqueueRunAsync(
        int projectId,
        int maxConcurrency,
        bool allowReboot,
        bool onlineOnly,
        string errorPolicy,
        string createdBy,
        DateTime? scheduledForUtc = null,
        CancellationToken cancellationToken = default)
    {
        var project = await _db.Projects
            .Include(p => p.Servers)
            .SingleAsync(p => p.Id == projectId, cancellationToken);

        var selections = await _db.ServerSelections
            .Where(s => s.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        var run = new ProjectRun
        {
            ProjectId = projectId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = createdBy,
            MaxConcurrency = Math.Min(maxConcurrency, 50),
            AllowReboot = allowReboot,
            OnlineOnly = onlineOnly,
            ErrorPolicy = errorPolicy,
            ScheduledForUtc = scheduledForUtc,
            Status = RunStatus.Pending
        };

        foreach (var server in project.Servers)
        {
            run.Servers.Add(new ProjectRunServer
            {
                ProjectServerId = server.Id,
                Status = RunServerStatus.Queued
            });
        }

        _db.ProjectRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);

        string jobId;
        if (scheduledForUtc.HasValue && scheduledForUtc.Value > DateTime.UtcNow.AddMinutes(1))
        {
            var delay = scheduledForUtc.Value - DateTime.UtcNow;
            jobId = _backgroundJobs.Schedule<ProjectService>(
                x => x.ExecuteRunAsync(run.Id, CancellationToken.None),
                delay);
        }
        else
        {
            jobId = _backgroundJobs.Create<ProjectService>(
                x => x.ExecuteRunAsync(run.Id, CancellationToken.None),
                new EnqueuedState("install"));
        }

        run.HangfireJobId = jobId;
        run.Status = RunStatus.Queued;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created run {RunId} with job {JobId} for project {ProjectId}", run.Id, jobId, projectId);

        return run;
    }

    /// <summary>
    /// Hangfire job entry point for executing an entire run.
    /// </summary>
    public async Task ExecuteRunAsync(int runId, CancellationToken cancellationToken)
    {
        var run = await _db.ProjectRuns
            .Include(r => r.Servers)
            .ThenInclude(rs => rs.ProjectServer)
            .SingleAsync(r => r.Id == runId, cancellationToken);

        run.Status = RunStatus.Running;
        await _db.SaveChangesAsync(cancellationToken);

        var selections = await _db.ServerSelections
            .Where(s => s.ProjectId == run.ProjectId)
            .ToListAsync(cancellationToken);

        // Enqueue per-server install + verify jobs.
        foreach (var runServer in run.Servers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var serverSelections = selections
                .Where(s => s.ProjectServerId == runServer.ProjectServerId)
                .ToList();

            if (!serverSelections.Any())
            {
                runServer.Status = RunServerStatus.Success;
                continue;
            }

            var rsId = runServer.Id;
            var kbList = serverSelections.Select(s => s.KbNumber).Distinct().ToArray();

            // Install job on 'install' queue
            var installJobId = _backgroundJobs.Create<ProjectService>(
                x => x.ExecuteServerInstallAsync(runId, rsId, kbList, CancellationToken.None),
                new EnqueuedState("install"));

            // Verify job chained on 'verify' queue
            _backgroundJobs.ContinueJobWith<ProjectService>(
                installJobId,
                x => x.ExecuteServerVerifyAsync(runId, rsId, kbList, CancellationToken.None),
                new EnqueuedState("verify"));
        }
    }

    /// <summary>
    /// Hangfire job: performs an install for a single server within a run.
    /// </summary>
    public async Task ExecuteServerInstallAsync(int runId, int runServerId, string[] kbNumbers, CancellationToken cancellationToken)
    {
        var run = await _db.ProjectRuns.FindAsync(new object[] { runId }, cancellationToken)
                  ?? throw new InvalidOperationException($"Run {runId} not found.");

        var runServer = await _db.ProjectRunServers
            .Include(rs => rs.ProjectServer)
            .SingleAsync(rs => rs.Id == runServerId && rs.ProjectRunId == runId, cancellationToken);

        var selections = await _db.ServerSelections
            .Where(s => s.ProjectId == run.ProjectId && s.ProjectServerId == runServer.ProjectServerId && kbNumbers.Contains(s.KbNumber))
            .ToListAsync(cancellationToken);

        runServer.Status = RunServerStatus.Installing;
        runServer.LastUpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            await _ps.InstallAsync(run, runServer, selections, cancellationToken);
            runServer.Status = RunServerStatus.RebootPending;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Install failed for runServer {RunServerId}", runServerId);
            runServer.Status = RunServerStatus.Failed;
            runServer.LastError = ex.Message;
        }

        runServer.LastUpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Hangfire job: verifies installation for a single server and updates status.
    /// </summary>
    public async Task ExecuteServerVerifyAsync(int runId, int runServerId, string[] kbNumbers, CancellationToken cancellationToken)
    {
        var runServer = await _db.ProjectRunServers
            .Include(rs => rs.ProjectServer)
            .SingleAsync(rs => rs.Id == runServerId && rs.ProjectRunId == runId, cancellationToken);

        runServer.Status = RunServerStatus.Verifying;
        runServer.LastUpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            var success = await _ps.VerifyAsync(runServer, kbNumbers, cancellationToken);
            runServer.Status = success ? RunServerStatus.Success : RunServerStatus.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Verification failed for runServer {RunServerId}", runServerId);
            runServer.Status = RunServerStatus.Failed;
            runServer.LastError = ex.Message;
        }

        runServer.LastUpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        // If all servers finished, update the parent run status.
        var run = await _db.ProjectRuns
            .Include(r => r.Servers)
            .SingleAsync(r => r.Id == runId, cancellationToken);

        if (run.Servers.All(rs => rs.Status == RunServerStatus.Success || rs.Status == RunServerStatus.Failed))
        {
            run.Status = run.Servers.All(rs => rs.Status == RunServerStatus.Success)
                ? RunStatus.Completed
                : RunStatus.Failed;

            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Bulk retry for failed servers in a run.
    /// </summary>
    public async Task BulkRetryFailedAsync(int runId, CancellationToken cancellationToken = default)
    {
        var run = await _db.ProjectRuns
            .Include(r => r.Servers)
            .SingleAsync(r => r.Id == runId, cancellationToken);

        var failed = run.Servers.Where(rs => rs.Status == RunServerStatus.Failed).ToList();
        if (!failed.Any())
        {
            return;
        }

        var selections = await _db.ServerSelections
            .Where(s => s.ProjectId == run.ProjectId)
            .ToListAsync(cancellationToken);

        foreach (var runServer in failed)
        {
            var kbList = selections
                .Where(s => s.ProjectServerId == runServer.ProjectServerId)
                .Select(s => s.KbNumber)
                .Distinct()
                .ToArray();

            if (!kbList.Any())
            {
                continue;
            }

            runServer.Status = RunServerStatus.Queued;
            runServer.LastError = null;

            var rsId = runServer.Id;

            var installJobId = _backgroundJobs.Create<ProjectService>(
                x => x.ExecuteServerInstallAsync(runId, rsId, kbList, CancellationToken.None),
                new EnqueuedState("install"));

            _backgroundJobs.ContinueJobWith<ProjectService>(
                installJobId,
                x => x.ExecuteServerVerifyAsync(runId, rsId, kbList, CancellationToken.None),
                new EnqueuedState("verify"));
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RunPreFlightAsync(ProjectServer server, CancellationToken cancellationToken)
    {
        server.PreFlightStatus = PreFlightStatus.Pending;
        server.PreFlightError = null;
        await _db.SaveChangesAsync(cancellationToken);

        // Simple DNS + WinRM reachability pre-flight using Test-NetConnection and Test-WSMan.
        var script = $@"
Write-Verbose ""Pre-flight checks for {server.Name}"" -Verbose

$dns = Test-NetConnection -ComputerName '{server.Name}' -WarningAction SilentlyContinue
if (-not $dns.PingSucceeded) {{
    throw ""DNS/ICMP failed for {server.Name}""
}}

try {{
    Test-WSMan -ComputerName '{server.Name}' -ErrorAction Stop | Out-Null
}} catch {{
    throw ""WinRM connection failed for {server.Name}. Ensure Enable-PSRemoting has been run and firewall rules allow WinRM.""
}}
";

        // Use PowerShell on the local machine; failure bubbles up to caller.
        using var ps = System.Management.Automation.PowerShell.Create();
        ps.AddScript(script);

        var results = ps.Invoke();
        if (ps.HadErrors)
        {
            var errors = string.Join(Environment.NewLine, ps.Streams.Error.Select(e => e.ToString()));
            throw new InvalidOperationException(errors);
        }
    }
}

