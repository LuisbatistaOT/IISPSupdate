using System.ComponentModel.DataAnnotations;
using System.Text;
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
public class ProjectsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ProjectService _projectService;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(AppDbContext db, ProjectService projectService, ILogger<ProjectsController> logger)
    {
        _db = db;
        _projectService = projectService;
        _logger = logger;
    }

    // GET /api/projects
    [HttpGet]
    public async Task<IActionResult> GetProjects()
    {
        var projects = await _db.Projects
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.CreatedAtUtc,
                p.CreatedBy,
                ServerCount = p.Servers.Count,
                RunCount = p.Runs.Count
            })
            .ToListAsync();

        return Ok(projects);
    }

    // GET /api/projects/dashboard
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboardSummary()
    {
        var now = DateTime.UtcNow;
        var recentCutoff = now.AddDays(-7);

        var totalProjects = await _db.Projects.CountAsync();
        var serversUpdated = await _db.ProjectRunServers
            .CountAsync(rs => rs.Status == RunServerStatus.Success && rs.LastUpdatedUtc >= recentCutoff);

        // Pending updates should reflect "current risk", not historical scans.
        // For that reason we compute distinct KBs from only the latest scan of each server.
        var latestScanIds = _db.ServerScanResults
            .GroupBy(r => r.ProjectServerId)
            .Select(g => g.OrderByDescending(r => r.ScanTimeUtc).Select(r => r.Id).FirstOrDefault());

        var pendingUpdates = await _db.UpdateCandidates
            .Where(c => latestScanIds.Contains(c.ServerScanResultId))
            .Select(c => c.KbNumber)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct()
            .CountAsync();

        // Top KBs are based on selected KBs across projects/runs, which is typically
        // the most useful long-term operational signal for patch planning.
        var topKbs = await _db.ServerSelections
            .GroupBy(s => s.KbNumber)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new
            {
                Kb = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        return Ok(new
        {
            TotalProjects = totalProjects,
            ServersUpdated = serversUpdated,
            PendingUpdates = pendingUpdates,
            TopKbs = topKbs
        });
    }

    // GET /api/projects/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetProject(int id)
    {
        var project = await _db.Projects
            .Include(p => p.Servers)
            .Include(p => p.Runs)
            .SingleOrDefaultAsync(p => p.Id == id);

        if (project == null)
        {
            return NotFound();
        }

        return Ok(project);
    }

    // POST /api/projects
    [HttpPost]
    public async Task<IActionResult> CreateProject([FromBody] CreateProjectRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var user = User.Identity?.Name ?? "unknown";

        var project = new Project
        {
            Name = request.Name,
            Description = request.Description,
            ScanSource = request.ScanSource,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = user
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetProject), new { id = project.Id }, project);
    }

    // POST /api/projects/{id}/servers/import
    [HttpPost("{id:int}/servers/import")]
    public async Task<IActionResult> ImportServers(int id, [FromBody] ImportServersRequest request)
    {
        var project = await _db.Projects
            .Include(p => p.Servers)
            .SingleOrDefaultAsync(p => p.Id == id);

        if (project == null)
        {
            return NotFound();
        }

        var lines = (request.Content ?? string.Empty)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        if (!lines.Any())
        {
            return BadRequest("No server entries found.");
        }

        var startIndex = request.HasHeader && lines.Length > 1 ? 1 : 0;

        for (var i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Support either plain hostname or CSV: Name,Fqdn,Environment
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            var name = parts[0];
            var fqdn = parts.Length > 1 ? parts[1] : null;
            var env = parts.Length > 2 ? parts[2] : null;

            if (project.Servers.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            project.Servers.Add(new ProjectServer
            {
                Name = name,
                Fqdn = fqdn,
                Environment = env,
                PreFlightStatus = PreFlightStatus.NotStarted
            });
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            project.Id,
            ServerCount = project.Servers.Count
        });
    }

    // POST /api/projects/{id}/scan
    [HttpPost("{id:int}/scan")]
    public async Task<IActionResult> EnqueueScan(int id)
    {
        var exists = await _db.Projects.AnyAsync(p => p.Id == id);
        if (!exists)
        {
            return NotFound();
        }

        var jobId = _projectService.EnqueueScan(id);
        return Accepted(new { JobId = jobId });
    }

    // GET /api/projects/{id}/scan/results
    [HttpGet("{id:int}/scan")]
    public async Task<IActionResult> GetScanResults(int id)
    {
        var project = await _db.Projects
            .Include(p => p.Servers)
            .ThenInclude(s => s.ScanResults)
            .ThenInclude(r => r.Candidates)
            .SingleOrDefaultAsync(p => p.Id == id);

        if (project == null)
        {
            return NotFound();
        }

        var result = project.Servers.Select(s =>
        {
            var lastScan = s.ScanResults.OrderByDescending(r => r.ScanTimeUtc).FirstOrDefault();
            var kbList = lastScan?.Candidates
                .Select(c => c.KbNumber)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct()
                .OrderBy(k => k)
                .ToList() ?? new List<string>();
            var lastScanDto = lastScan == null
                ? null
                : new
                {
                    lastScan.Id,
                    lastScan.ScanTimeUtc,
                    lastScan.ScanSource,
                    Candidates = lastScan.Candidates.Select(c => new
                    {
                        c.KbNumber,
                        c.Title,
                        c.Category,
                        c.Severity
                    }).ToList()
                };

            return new
            {
                ServerId = s.Id,
                s.Name,
                s.PreFlightStatus,
                s.PreFlightError,
                LastScan = lastScanDto,
                AvailableKbCount = kbList.Count,
                AvailableKbs = kbList
            };
        });

        return Ok(result);
    }

    // GET /api/projects/{id}/scan/export
    [HttpGet("{id:int}/scan/export")]
    public async Task<IActionResult> ExportScanResults(int id)
    {
        var project = await _db.Projects
            .Include(p => p.Servers)
            .ThenInclude(s => s.ScanResults)
            .ThenInclude(r => r.Candidates)
            .SingleOrDefaultAsync(p => p.Id == id);

        if (project == null)
        {
            return NotFound();
        }

        var sb = new StringBuilder();
        sb.AppendLine("ProjectId,Server,PreFlightStatus,PreFlightError,ScanTimeUtc,KB,Title,Category,Severity");

        foreach (var server in project.Servers.OrderBy(s => s.Name))
        {
            var lastScan = server.ScanResults.OrderByDescending(r => r.ScanTimeUtc).FirstOrDefault();
            if (lastScan == null || !lastScan.Candidates.Any())
            {
                sb.AppendLine(string.Join(",",
                    id,
                    Csv(server.Name),
                    server.PreFlightStatus,
                    Csv(server.PreFlightError ?? string.Empty),
                    lastScan?.ScanTimeUtc.ToString("o") ?? string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty));
                continue;
            }

            foreach (var c in lastScan.Candidates.OrderBy(c => c.KbNumber))
            {
                sb.AppendLine(string.Join(",",
                    id,
                    Csv(server.Name),
                    server.PreFlightStatus,
                    Csv(server.PreFlightError ?? string.Empty),
                    lastScan.ScanTimeUtc.ToString("o"),
                    Csv(c.KbNumber),
                    Csv(c.Title),
                    Csv(c.Category ?? string.Empty),
                    Csv(c.Severity ?? string.Empty)));
            }
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"project-{id}-scan-report.csv");
    }

    // GET /api/projects/{id}/selections
    [HttpGet("{id:int}/selections")]
    public async Task<IActionResult> GetSelections(int id)
    {
        var selections = await _db.ServerSelections
            .Where(s => s.ProjectId == id)
            .ToListAsync();

        return Ok(selections);
    }

    // POST /api/projects/{id}/selections
    [HttpPost("{id:int}/selections")]
    public async Task<IActionResult> UpsertSelections(int id, [FromBody] List<SelectionRequest> selections)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var projectExists = await _db.Projects.AnyAsync(p => p.Id == id);
        if (!projectExists)
        {
            return NotFound();
        }

        // Replace existing selections for the project.
        var existing = await _db.ServerSelections
            .Where(s => s.ProjectId == id)
            .ToListAsync();

        _db.ServerSelections.RemoveRange(existing);

        foreach (var s in selections)
        {
            _db.ServerSelections.Add(new ServerSelection
            {
                ProjectId = id,
                ProjectServerId = s.ProjectServerId,
                KbNumber = s.KbNumber,
                Title = s.Title ?? s.KbNumber,
                Category = s.Category
            });
        }

        await _db.SaveChangesAsync();

        return Ok();
    }

    // GET /api/projects/{id}/runs
    [HttpGet("{id:int}/runs")]
    public async Task<IActionResult> GetRuns(int id)
    {
        var runs = await _db.ProjectRuns
            .Where(r => r.ProjectId == id)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => new
            {
                r.Id,
                r.CreatedAtUtc,
                r.CreatedBy,
                r.Status,
                r.Error,
                r.MaxConcurrency,
                r.AllowReboot,
                r.OnlineOnly,
                r.ErrorPolicy,
                r.ScheduledForUtc
            })
            .ToListAsync();

        return Ok(runs);
    }

    // POST /api/projects/{id}/runs
    [HttpPost("{id:int}/runs")]
    public async Task<IActionResult> CreateRun(int id, [FromBody] CreateRunRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var projectExists = await _db.Projects.AnyAsync(p => p.Id == id);
        if (!projectExists)
        {
            return NotFound();
        }

        var user = User.Identity?.Name ?? "unknown";

        var run = await _projectService.CreateAndEnqueueRunAsync(
            id,
            request.MaxConcurrency,
            request.AllowReboot,
            request.OnlineOnly,
            request.ErrorPolicy ?? "StopOnError",
            user,
            request.ScheduledForUtc,
            HttpContext.RequestAborted);

        return Accepted(new { run.Id, run.Status, run.HangfireJobId });
    }

    private static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}

public class CreateProjectRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required]
    public ScanSource ScanSource { get; set; } = ScanSource.MicrosoftUpdate;
}

public class ImportServersRequest
{
    /// <summary>
    /// Raw text or CSV of servers: one per line.
    /// Either ""servername"" or ""Name,Fqdn,Environment"".
    /// </summary>
    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Whether the first line is a header row to skip.
    /// </summary>
    public bool HasHeader { get; set; }
}

public class SelectionRequest
{
    [Required]
    public int ProjectServerId { get; set; }

    [Required]
    public string KbNumber { get; set; } = string.Empty;

    public string? Title { get; set; }
    public string? Category { get; set; }
}

public class CreateRunRequest
{
    [Range(1, 50)]
    public int MaxConcurrency { get; set; } = 50;

    public bool AllowReboot { get; set; } = true;
    public bool OnlineOnly { get; set; } = true;

    [MaxLength(100)]
    public string? ErrorPolicy { get; set; } = "StopOnError";

    /// <summary>
    /// Optional scheduled start time (UTC). If null or in the past, run begins immediately.
    /// </summary>
    public DateTime? ScheduledForUtc { get; set; }
}

