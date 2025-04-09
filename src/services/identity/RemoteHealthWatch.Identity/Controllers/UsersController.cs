using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Identity.Domain;
using RemoteHealthWatch.ServiceDefaults.Auth;

namespace RemoteHealthWatch.Identity.Controllers;

public sealed record CreateUserRequest(string Email, string DisplayName, string Role, string Password, bool IsOnCall = false);
public sealed record UpdateUserRequest(string DisplayName, bool IsOnCall, bool IsActive);

public sealed class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Role).Must(r => Roles.Staff.Contains(r)).WithMessage("Unknown role");
        RuleFor(x => x.Password).MinimumLength(8);
    }
}

[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.Staff)]
public sealed class UsersController(UserManager<AppUser> users) : ControllerBase
{
    // coordinators need the staff directory to build care teams
    [HttpGet]
    [Authorize(Policy = Policies.ManagePatients)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> List([FromQuery] string? role, CancellationToken ct)
    {
        var query = users.Users.Where(u => u.IsActive);
        if (!string.IsNullOrWhiteSpace(role)) query = query.Where(u => u.Role == role);
        var list = await query.OrderBy(u => u.Role).ThenBy(u => u.DisplayName).ToListAsync(ct);
        return list.Select(AuthController.ToDto).ToList();
    }

    [HttpGet("on-call")]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> OnCall(CancellationToken ct)
    {
        var list = await users.Users.Where(u => u.IsActive && u.IsOnCall).ToListAsync(ct);
        return list.Select(AuthController.ToDto).ToList();
    }

    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            DisplayName = request.DisplayName,
            Role = request.Role,
            IsOnCall = request.IsOnCall,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return ValidationProblem(new ValidationProblemDetails(result.Errors.GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray())));
        await users.AddToRoleAsync(user, request.Role);
        return CreatedAtAction(nameof(List), new { role = user.Role }, AuthController.ToDto(user));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<UserDto>> Update(Guid id, UpdateUserRequest request)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();
        user.DisplayName = request.DisplayName;
        user.IsOnCall = request.IsOnCall;
        user.IsActive = request.IsActive;
        await users.UpdateAsync(user);
        return AuthController.ToDto(user);
    }
}
