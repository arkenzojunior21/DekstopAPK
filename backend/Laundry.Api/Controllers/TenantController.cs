using System.Security.Claims;
using Laundry.Application.Core;
using Laundry.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laundry.Api.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize(Policy = "RequirePlatformAdmin")]
public sealed class TenantController(ITenantService tenantService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TenantDto>>> List(CancellationToken ct)
        => Ok(await tenantService.ListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TenantDto>> Get(Guid id, CancellationToken ct)
    {
        try { return Ok(await tenantService.GetAsync(id, ct)); }
        catch (KeyNotFoundException e) { return NotFound(e.Message); }
    }

    [HttpPost]
    public async Task<ActionResult<TenantDto>> Create(CreateTenantRequest req, CancellationToken ct)
    {
        try { return Ok(await tenantService.CreateAsync(req, ct)); }
        catch (InvalidOperationException e) { return Conflict(e.Message); }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TenantDto>> Update(Guid id, UpdateTenantRequest req, CancellationToken ct)
    {
        try { return Ok(await tenantService.UpdateAsync(id, req, ct)); }
        catch (KeyNotFoundException e) { return NotFound(e.Message); }
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<TenantDto>> SetStatus(Guid id, [FromBody] SetTenantStatusRequest req, CancellationToken ct)
    {
        try { return Ok(await tenantService.SetStatusAsync(id, req.Status, ct)); }
        catch (KeyNotFoundException e) { return NotFound(e.Message); }
    }
}

public record SetTenantStatusRequest(TenantStatus Status);