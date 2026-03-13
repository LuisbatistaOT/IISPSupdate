using System.Text;
using Hangfire;
using IISPSupdate.Data;
using IISPSupdate.Models;
using IISPSupdate.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IISPSupdate.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RunsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ProjectService _projectService;

    public RunsController(AppDbContext db, ProjectService projectService)
    {
        _db = db;
        _projectService = projectService;
    }

    // GET /api/runs/{runId}
    [HttpGet("{runId:int}")]
    public async Task<IActionResult> GetRun(int runId)
    {
        var run = await _db.ProjectRuns
            .Include(r => r.Servers)
            .ThenInclude(rs => rs.ProjectServer)
            .SingleOrDefaultAsync(r => r.Id == runId);

        if (run == null)
        {
            return NotFound();
        }

        var dto = new
        {
            run.Id,
            run.ProjectId,
            run.CreatedAtUtc,
            run.CreatedBy,
            run.Status,
            run.Error,
            run.MaxConcurrency,
            run.AllowReboot,
            run.OnlineOnly,
            run.ErrorPolicy,
            run.ScheduledForUtc,
            Servers = run.Servers.Select(rs => new
            {
                rs.Id,
                rs.ProjectServerId,
                ServerName = rs.ProjectServer!.Name,
                rs.Status,
                rs.LastUpdatedUtc,
                rs.LastError,
                rs.LogTail
            })
        };

        return Ok(dto);
    }

    // GET /api/runs/{runId}/logs?serverId=123
    [HttpGet("{runId:int}/logs")]
    public async Task<IActionResult> GetLogs(int runId, [FromQuery] int? serverId)
    {
        var query = _db.ProjectRunServers
            .Include(rs => rs.ProjectServer)
            .Where(rs => rs.ProjectRunId == runId);

        if (serverId.HasValue)
        {
            query = query.Where(rs => rs.ProjectServerId == serverId.Value);
        }

        var items = await query
            .Select(rs => new
            {
                rs.Id,
                rs.ProjectServerId,
                ServerName = rs.ProjectServer!.Name,
                rs.Status,
                rs.LogTail,
                rs.LastError,
                rs.LogPath,
                rs.LastUpdatedUtc
            })
            .ToListAsync();

        return Ok(items);
    }

    // POST /api/runs/{runId}/retry-failed
    [HttpPost("{runId:int}/retry-failed")]
    public async Task<IActionResult> RetryFailed(int runId)
    {
        var runExists = await _db.ProjectRuns.AnyAsync(r => r.Id == runId);
        if (!runExists)
        {
            return NotFound();
        }

        await _projectService.BulkRetryFailedAsync(runId, HttpContext.RequestAborted);
        return Accepted();
    }

    // POST /api/runs/{runId}/cancel-scheduled
    [HttpPost("{runId:int}/cancel-scheduled")]
    public async Task<IActionResult> CancelScheduled(int runId)
    {
        var run = await _db.ProjectRuns
            .Include(r => r.Servers)
            .SingleOrDefaultAsync(r => r.Id == runId);

        if (run == null)
        {
            return NotFound();
        }

        var isScheduledFuture = run.Status == RunStatus.Queued
            && run.ScheduledForUtc.HasValue
            && run.ScheduledForUtc.Value > DateTime.UtcNow;

        if (!isScheduledFuture)
        {
            return BadRequest("Only runs that are still scheduled for future execution can be cancelled.");
        }

        if (string.IsNullOrWhiteSpace(run.HangfireJobId))
        {
            return BadRequest("Run has no scheduled Hangfire job id.");
        }

        // Delete returns false when the job is not found (already dequeued/started/deleted).
        // In that case we refuse to mark the run as cancelled to avoid inconsistent state.
        var removed = BackgroundJob.Delete(run.HangfireJobId);
        if (!removed)
        {
            return Conflict("Could not cancel the Hangfire job. It may have already started.");
        }

        // Keep application-level state aligned with Hangfire-level cancellation so
        // run details and reports are accurate for operators and auditors.
        run.Status = RunStatus.Cancelled;
        run.Error = "Cancelled by user before scheduled execution.";
        foreach (var rs in run.Servers.Where(s => s.Status == RunServerStatus.Queued))
        {
            rs.LastError = "Run cancelled before execution.";
            rs.LastUpdatedUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok(new { run.Id, run.Status });
    }

    // GET /api/runs/{runId}/export
    [HttpGet("{runId:int}/export")]
    public async Task<IActionResult> Export(int runId)
    {
        var run = await _db.ProjectRuns
            .Include(r => r.Servers)
            .ThenInclude(rs => rs.ProjectServer)
            .SingleOrDefaultAsync(r => r.Id == runId);

        if (run == null)
        {
            return NotFound();
        }

        var sb = new StringBuilder();
        sb.AppendLine("RunId,ProjectId,ServerName,Status,LastUpdatedUtc,LastError");

        foreach (var rs in run.Servers)
        {
            sb.AppendLine(string.Join(",",
                run.Id,
                run.ProjectId,
                Quote(rs.ProjectServer!.Name),
                rs.Status,
                rs.LastUpdatedUtc?.ToString("o") ?? string.Empty,
                Quote(rs.LastError ?? string.Empty)));
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = $"run-{runId}-results.csv";

        return File(bytes, "text/csv", fileName);
    }

    private static string Quote(string value)
    {
        if (value.Contains(',') || value.Contains('"'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}

