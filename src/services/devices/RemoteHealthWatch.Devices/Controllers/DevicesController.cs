using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Devices.Data;
using RemoteHealthWatch.Devices.Domain;
using RemoteHealthWatch.Devices.Services;
using RemoteHealthWatch.ServiceDefaults.Access;
using RemoteHealthWatch.ServiceDefaults.Auth;

namespace RemoteHealthWatch.Devices.Controllers;

public sealed record RegisterDeviceRequest(string Serial, DeviceType Type, string Firmware);
public sealed record PairDeviceRequest(Guid PatientId, int? ExpectedIntervalSeconds);
public sealed record UpdateFirmwareRequest(string Firmware);

public sealed record DeviceDto(
    Guid Id, string Serial, DeviceType Type, string Firmware, DeviceStatus Status, Guid? PatientId, DateTimeOffset? PairedAt,
    int ExpectedIntervalSeconds, string KeyPrefix, DateTimeOffset KeyIssuedAt, int? BatteryPercent, DateTimeOffset? LastSeenAt,
    DateTimeOffset? SilentSince, DateTimeOffset RegisteredAt);

public sealed record DeviceWithKey(DeviceDto Device, string ApiKey);

public sealed class RegisterDeviceValidator : AbstractValidator<RegisterDeviceRequest>
{
    public RegisterDeviceValidator()
    {
        RuleFor(x => x.Serial).NotEmpty().MaximumLength(40).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Firmware).NotEmpty().MaximumLength(30);
    }
}

public sealed class PairDeviceValidator : AbstractValidator<PairDeviceRequest>
{
    public PairDeviceValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.ExpectedIntervalSeconds).InclusiveBetween(5, 3600).When(x => x.ExpectedIntervalSeconds is not null);
    }
}

[ApiController]
[Route("api/devices")]
[Authorize(Policy = Policies.Staff)]
public sealed class DevicesController(DevicesDb db, DeviceService devices, PatientsClient patients) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.ManagePatients)]
    public async Task<ActionResult<IReadOnlyList<DeviceDto>>> List([FromQuery] DeviceStatus? status, [FromQuery] bool? silent, CancellationToken ct)
    {
        var query = db.Devices.AsNoTracking();
        if (status is not null) query = query.Where(d => d.Status == status);
        if (silent == true) query = query.Where(d => d.SilentSince != null);
        var list = await query.OrderBy(d => d.Serial).Take(2000).ToListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ManagePatients)]
    public async Task<ActionResult<DeviceDto>> Get(Guid id, CancellationToken ct)
    {
        var device = await devices.FindAsync(id, ct);
        return device is null ? NotFound() : ToDto(device);
    }

    [HttpGet("patient/{patientId:guid}")]
    [PatientData("devices")]
    public async Task<ActionResult<IReadOnlyList<DeviceDto>>> ForPatient(Guid patientId, CancellationToken ct)
    {
        var list = await db.Devices.AsNoTracking().Where(d => d.PatientId == patientId).ToListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    [HttpPost]
    [Authorize(Policy = Policies.ManagePatients)]
    public async Task<ActionResult<DeviceWithKey>> Register(RegisterDeviceRequest request, CancellationToken ct)
    {
        if (await db.Devices.AnyAsync(d => d.Serial == request.Serial.ToUpper(), ct))
            return Problem("Serial already registered", statusCode: StatusCodes.Status409Conflict);
        var (device, key) = await devices.RegisterAsync(request.Serial, request.Type, request.Firmware, ct);
        return CreatedAtAction(nameof(Get), new { id = device.Id }, new DeviceWithKey(ToDto(device), key));
    }

    [HttpPost("{id:guid}/pair")]
    [Authorize(Policy = Policies.ManagePatients)]
    public async Task<ActionResult<DeviceDto>> Pair(Guid id, PairDeviceRequest request, CancellationToken ct)
    {
        var device = await devices.FindAsync(id, ct);
        if (device is null) return NotFound();

        var eligibility = await patients.CheckEligibilityAsync(request.PatientId, ct);
        if (!eligibility.Eligible)
            return Problem($"Patient can't be paired: {eligibility.Reason}", statusCode: StatusCodes.Status409Conflict);

        var result = devices.TryPair(device, request.PatientId, request.ExpectedIntervalSeconds ?? device.ExpectedIntervalSeconds);
        if (!result.Ok) return Problem($"Device can't be paired: {result.Error}", statusCode: StatusCodes.Status409Conflict);
        await devices.SaveAsync(device, ct);
        return ToDto(device);
    }

    [HttpPost("{id:guid}/unpair")]
    [Authorize(Policy = Policies.ManagePatients)]
    public async Task<ActionResult<DeviceDto>> Unpair(Guid id, CancellationToken ct)
    {
        var device = await devices.FindAsync(id, ct);
        if (device is null) return NotFound();
        if (device.Status != DeviceStatus.Paired) return Problem("Device is not paired", statusCode: StatusCodes.Status409Conflict);
        devices.Unpair(device, "unpaired-by-staff");
        await devices.SaveAsync(device, ct);
        return ToDto(device);
    }

    [HttpPost("{id:guid}/rotate-key")]
    [Authorize(Policy = Policies.ManagePatients)]
    public async Task<ActionResult<DeviceWithKey>> RotateKey(Guid id, CancellationToken ct)
    {
        var device = await devices.FindAsync(id, ct);
        if (device is null) return NotFound();
        if (device.Status == DeviceStatus.Revoked) return Problem("Device is revoked", statusCode: StatusCodes.Status409Conflict);
        var key = await devices.RotateKeyAsync(device, ct);
        return new DeviceWithKey(ToDto(device), key);
    }

    [HttpPost("{id:guid}/revoke")]
    [Authorize(Policy = Policies.ManagePatients)]
    public async Task<ActionResult<DeviceDto>> Revoke(Guid id, CancellationToken ct)
    {
        var device = await devices.FindAsync(id, ct);
        if (device is null) return NotFound();
        await devices.RevokeAsync(device, ct);
        return ToDto(device);
    }

    [HttpPut("{id:guid}/firmware")]
    [Authorize(Policy = Policies.ManagePatients)]
    public async Task<ActionResult<DeviceDto>> Firmware(Guid id, UpdateFirmwareRequest request, CancellationToken ct)
    {
        var device = await devices.FindAsync(id, ct);
        if (device is null) return NotFound();
        device.Firmware = request.Firmware;
        device.Version = Guid.NewGuid();
        await devices.SaveAsync(device, ct);
        return ToDto(device);
    }

    internal static DeviceDto ToDto(Device d) => new(d.Id, d.Serial, d.Type, d.Firmware, d.Status, d.PatientId, d.PairedAt,
        d.ExpectedIntervalSeconds, d.ApiKeyPrefix, d.KeyIssuedAt, d.BatteryPercent, d.LastSeenAt, d.SilentSince, d.RegisteredAt);
}
