using IISPSupdate.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IISPSupdate.Controllers;

[Authorize]
public class RunsController : Controller
{
    private readonly AppDbContext _db;

    public RunsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var run = await _db.ProjectRuns
            .Include(r => r.Project)
            .SingleOrDefaultAsync(r => r.Id == id);

        if (run == null)
        {
            return NotFound();
        }

        return View(run);
    }
}

