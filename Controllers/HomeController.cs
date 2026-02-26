using IISPSupdate.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IISPSupdate.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;
        var recentCutoff = now.AddDays(-7);

        var totalProjects = await _db.Projects.CountAsync();
        var serversUpdatedRecently = await _db.ProjectRunServers
            .CountAsync(rs => rs.Status == Models.RunServerStatus.Success && rs.LastUpdatedUtc >= recentCutoff);

        var topKbs = await _db.ServerSelections
            .GroupBy(s => s.KbNumber)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new TopKb
            {
                KbNumber = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        var model = new HomeViewModel
        {
            TotalProjects = totalProjects,
            ServersUpdatedRecently = serversUpdatedRecently,
            TopKbs = topKbs
        };

        return View(model);
    }
}

public class HomeViewModel
{
    public int TotalProjects { get; set; }
    public int ServersUpdatedRecently { get; set; }
    public List<TopKb> TopKbs { get; set; } = new();
}

public class TopKb
{
    public string KbNumber { get; set; } = string.Empty;
    public int Count { get; set; }
}

