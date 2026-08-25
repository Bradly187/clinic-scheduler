using System.Security.Claims;
using System.Text.Json.Nodes;

namespace ClinicScheduler.Web.Services.Skills;

/// <summary>Parsing helpers for the loosely-typed JSON arguments the model supplies to a skill.</summary>
public static class SkillArgs
{
    /// <summary>Reads an integer that the model may have sent as a JSON number or a string.</summary>
    public static int? ParseOptionalInt(JsonNode? node)
    {
        if (node == null) return null;
        return node.GetValueKind() == System.Text.Json.JsonValueKind.Number
            ? node.GetValue<int>()
            : int.TryParse(node.GetValue<string>(), out var n) ? n : null;
    }

    /// <summary>Reads a boolean that the model may have sent as a JSON bool or a string.</summary>
    public static bool ParseBool(JsonNode? node)
    {
        if (node == null) return false;
        return node.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.String => bool.TryParse(node.GetValue<string>(), out var b) && b,
            _ => false
        };
    }
}

/// <summary>Shared role checks used by skills to gate staff/admin-only operations.</summary>
public static class SkillPrincipalExtensions
{
    /// <summary>True for any non-patient operational role (Admin, ClinicManager, Staff, Therapist).</summary>
    public static bool IsStaffOrAbove(this ClaimsPrincipal user)
        => user.IsInRole(RoleNames.Admin) || user.IsInRole(RoleNames.ClinicManager)
        || user.IsInRole(RoleNames.Staff) || user.IsInRole(RoleNames.Therapist);
}
