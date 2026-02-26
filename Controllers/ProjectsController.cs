using System.ComponentModel.DataAnnotations;
using System.Reflection;
using IISPSupdate.Data;
using IISPSupdate.Models;
using IISPSupdate.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IISPSupdate.Controllers;

[Authorize]
public class ProjectsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ProjectService _projectService;

    public ProjectsController(AppDbContext db, ProjectService projectService)
    {
        _db = db;
        _projectService = projectService;
    }

    public async Task<IActionResult> Index()
    {
        var projects = await _db.Projects
            .Include(p => p.Servers)
            .Include(p => p.Runs)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync();

        var list = projects.Select(p =>
        {
            var serverCount = p.Servers.Count;
            var allPreflightDone = serverCount == 0 || p.Servers.All(s =>
                s.PreFlightStatus == PreFlightStatus.Success || s.PreFlightStatus == PreFlightStatus.Failed);
            var latestRun = p.Runs.OrderByDescending(r => r.CreatedAtUtc).FirstOrDefault();
            var runCompleted = latestRun != null && (
                latestRun.Status == RunStatus.Completed || latestRun.Status == RunStatus.Failed ||
                latestRun.Status == RunStatus.Cancelled);
            var runInProgress = latestRun != null && (
                latestRun.Status == RunStatus.Queued || latestRun.Status == RunStatus.Running);

            string preflightStatus = serverCount == 0 ? "—" : allPreflightDone ? "Completed" : "In progress";
            string updateStatus = latestRun == null ? "Not started" :
                runInProgress ? "In progress" : runCompleted ? "Completed" : "Not started";

            return new ProjectListItem
            {
                Id = p.Id,
                Name = p.Name,
                ScanSource = p.ScanSource,
                ScanSourceDisplay = GetScanSourceDisplayName(p.ScanSource),
                CreatedAtUtc = p.CreatedAtUtc,
                CreatedBy = p.CreatedBy,
                PreflightStatus = preflightStatus,
                UpdateStatus = updateStatus
            };
        }).ToList();

        return View(list);
    }

    private static string GetScanSourceDisplayName(ScanSource source)
    {
        var member = typeof(ScanSource).GetMember(source.ToString()).FirstOrDefault();
        var attr = member?.GetCustomAttributes(typeof(DisplayAttribute), false).OfType<DisplayAttribute>().FirstOrDefault();
        return attr?.Name ?? source.ToString();
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new Project());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Project project)
    {
        if (!ModelState.IsValid)
        {
            return View(project);
        }

        project.CreatedAtUtc = DateTime.UtcNow;
        project.CreatedBy = User.Identity?.Name ?? "unknown";

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Wizard), new { id = project.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Wizard(int id)
    {
        var project = await _db.Projects
            .Include(p => p.Servers)
            .SingleOrDefaultAsync(p => p.Id == id);

        if (project == null)
        {
            return NotFound();
        }

        ViewBag.ScanSourceDisplay = GetScanSourceDisplayName(project.ScanSource);
        return View(project);
    }

    [HttpGet]
    public async Task<IActionResult> Open(int id)
    {
        var exists = await _db.Projects.AnyAsync(p => p.Id == id);
        if (!exists)
        {
            return RedirectToAction(nameof(Index));
        }

        return RedirectToAction(nameof(Wizard), new { id });
    }

    /// <summary>
    /// Applies the same KB selection to all servers in the project.
    /// This is a convenience endpoint for the wizard UI.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplySelection(int id, [FromForm] string kbList)
    {
        var project = await _db.Projects
            .Include(p => p.Servers)
            .SingleOrDefaultAsync(p => p.Id == id);

        if (project == null)
        {
            return NotFound();
        }

        var kbTokens = (kbList ?? string.Empty)
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim().ToUpperInvariant())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct()
            .ToList();

        // Clear existing selections for this project.
        var existing = await _db.ServerSelections
            .Where(s => s.ProjectId == id)
            .ToListAsync();
        _db.ServerSelections.RemoveRange(existing);

        foreach (var server in project.Servers)
        {
            foreach (var kb in kbTokens)
            {
                _db.ServerSelections.Add(new ServerSelection
                {
                    ProjectId = id,
                    ProjectServerId = server.Id,
                    KbNumber = kb,
                    Title = kb
                });
            }
        }

        await _db.SaveChangesAsync();

        TempData["SelectionMessage"] = $"Applied {kbTokens.Count} KBs to {project.Servers.Count} servers.";
        return RedirectToAction(nameof(Wizard), new { id });
    }

    /// <summary>
    /// Creates a run with options from the wizard UI and enqueues it.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartRun(int id, int maxConcurrency, bool allowReboot, bool onlineOnly, string errorPolicy, DateTime? scheduledForUtc)
    {
        var user = User.Identity?.Name ?? "unknown";

        var run = await _projectService.CreateAndEnqueueRunAsync(
            id,
            maxConcurrency,
            allowReboot,
            onlineOnly,
            string.IsNullOrWhiteSpace(errorPolicy) ? "StopOnError" : errorPolicy,
            user,
            scheduledForUtc,
            HttpContext.RequestAborted);

        return RedirectToAction("Details", "Runs", new { id = run.Id });
    }
}

public class ProjectListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ScanSource ScanSource { get; set; }
    public string ScanSourceDisplay { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string PreflightStatus { get; set; } = string.Empty;
    public string UpdateStatus { get; set; } = string.Empty;
}

