using Microsoft.AspNetCore.Mvc;
using System; 

[ApiController]
[Route("[controller]")] // Define a rota base como /time
public class TimeController : ControllerBase
{
    // Define o endpoint para requisições GET em /time/now
    [HttpGet("now")]
    public IActionResult GetCurrentTime()
    {
        var currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        
        return Ok(new { 
            current_time = currentTime 
        });
    }
}