using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers
{
    /// <summary>
    /// Health check endpoints for liveness/readiness probes and detailed system status.
    /// Implements caching and timeouts to avoid blocking operations on every request.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly IHealthCheckService _healthCheckService;

        public HealthController(IHealthCheckService healthCheckService)
        {
            _healthCheckService = healthCheckService;
        }

        /// <summary>
        /// Liveness probe - always fast, returns 200 OK if the API is running.
        /// Use this for Kubernetes/Docker liveness probes.
        /// </summary>
        [HttpGet]
        public IActionResult GetHealth()
        {
            return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
        }

        /// <summary>
        /// Readiness probe - checks dependencies (database, Redis) with caching.
        /// Cached for 10 seconds to avoid overwhelming dependencies.
        /// Use this for Kubernetes/Docker readiness probes.
        /// </summary>
        [HttpGet("ready")]
        public async Task<IActionResult> GetReadiness()
        {
            var health = await _healthCheckService.GetCachedHealthStatusAsync();
            var statusCode = health.IsHealthy ? 200 : 503;
            return StatusCode(statusCode, health);
        }

        /// <summary>
        /// Detailed status endpoint - for debugging and monitoring dashboards.
        /// Not cached - includes expensive operations like pending migrations check.
        /// </summary>
        [HttpGet("detailed")]
        public async Task<IActionResult> GetDetailedStatus(CancellationToken ct)
        {
            var detailed = await _healthCheckService.GetDetailedHealthStatusAsync(ct);
            return Ok(detailed);
        }
    }
}
