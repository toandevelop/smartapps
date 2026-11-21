using Microsoft.AspNetCore.Mvc;
using SmartApps.Api.Services;

namespace SmartApps.Api.Controllers;

[ApiController]
[Route("api/ai/copilot")]
public class AiCopilotController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly AiCopilotService _copilot;

    public AiCopilotController(AuthService auth, AiCopilotService copilot)
    {
        _auth = auth;
        _copilot = copilot;
    }

    [HttpGet("tools")]
    public async Task<IActionResult> GetTools()
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Vui lòng đăng nhập." });

        var data = await _copilot.GetCapabilitiesAsync(user);
        return Ok(new { success = true, data });
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AiCopilotAskRequest req, CancellationToken ct)
    {
        var user = await _auth.ResolveRequestUserAsync(Request);
        if (user == null) return Unauthorized(new { success = false, message = "Vui lòng đăng nhập." });

        var result = await _copilot.AskAsync(req ?? new AiCopilotAskRequest(), user, ct);
        return Ok(new { success = true, data = result });
    }
}
