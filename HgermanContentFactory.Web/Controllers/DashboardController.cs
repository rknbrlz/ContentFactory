using HgermanContentFactory.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace HgermanContentFactory.Web.Controllers;

public class DashboardController : Controller
{
    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard) => _dashboard = dashboard;

    public async Task<IActionResult> Index()
    {
        var stats = await _dashboard.GetStatsAsync();
        return View(stats);
    }

    [HttpGet]
    public async Task<IActionResult> Stats() =>
        Json(await _dashboard.GetStatsAsync());
}
