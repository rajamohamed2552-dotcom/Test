using GKM.AssetManagement.Models;

namespace GKM.AssetManagement;

/// <summary>Small presentation helpers shared by the Razor views.</summary>
public static class Ui
{
    public static string Money(decimal v) => v.ToString("N2");
    public static string Money0(decimal v) => v.ToString("N0");

    public static string Badge(AssetStatus s) => s switch
    {
        AssetStatus.Active => "success", AssetStatus.PendingApproval => "warning", AssetStatus.UnderMaintenance => "info",
        AssetStatus.Rejected => "danger", AssetStatus.Disposed => "dark", _ => "secondary"
    };

    public static string Badge(WorkOrderStatus s) => s switch
    {
        WorkOrderStatus.Open => "warning", WorkOrderStatus.InProgress => "info", WorkOrderStatus.Completed => "success", _ => "secondary"
    };

    public static string Badge(Priority p) => p switch { Priority.Urgent => "danger", Priority.High => "warning", Priority.Medium => "primary", _ => "secondary" };

    public static string Badge(AuditResult r) => r switch
    {
        AuditResult.Verified => "success", AuditResult.Missing => "danger", AuditResult.LocationMismatch => "warning", AuditResult.ConditionIssue => "warning", _ => "secondary"
    };

    public static string Spaced(object? e) => System.Text.RegularExpressions.Regex.Replace(e?.ToString() ?? "", "(?<=[a-z])(?=[A-Z])", " ");
}
