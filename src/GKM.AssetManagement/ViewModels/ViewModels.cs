using System.ComponentModel.DataAnnotations;
using GKM.AssetManagement.Models;

namespace GKM.AssetManagement.ViewModels;

public class LoginViewModel
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

/// <summary>Editable subset of Asset (prevents over-posting of status, depreciation and approval fields).</summary>
public class AssetForm
{
    public int Id { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    [MaxLength(1000)] public string? Description { get; set; }
    [Display(Name = "Category")] public int CategoryId { get; set; }
    [Display(Name = "Plant")] public int PlantId { get; set; }
    [Display(Name = "Location")] public int? LocationId { get; set; }
    [Display(Name = "Cost centre")] public int CostCenterId { get; set; }
    public Criticality Criticality { get; set; } = Criticality.Medium;
    public AssetCondition Condition { get; set; } = AssetCondition.Good;
    public string? Manufacturer { get; set; }
    [Display(Name = "Model")] public string? ModelNumber { get; set; }
    [Display(Name = "Serial number")] public string? SerialNumber { get; set; }
    public string? Vendor { get; set; }
    [Display(Name = "PO number")] public string? PurchaseOrderNumber { get; set; }
    public string? Custodian { get; set; }
    [DataType(DataType.Date), Display(Name = "Acquisition date")] public DateTime AcquisitionDate { get; set; } = DateTime.Today;
    [Range(0.01, 1_000_000_000), Display(Name = "Acquisition cost")] public decimal AcquisitionCost { get; set; }
    [Range(0, 1_000_000_000), Display(Name = "Salvage value")] public decimal? SalvageValue { get; set; }
    [Range(1, 1200), Display(Name = "Useful life (months)")] public int? UsefulLifeMonths { get; set; }
    [Display(Name = "Depreciation method")] public DepreciationMethod? Method { get; set; }
    [DataType(DataType.Date), Display(Name = "Warranty expiry")] public DateTime? WarrantyExpiry { get; set; }
}

public class MoveForm
{
    [Required] public int AssetId { get; set; }
    [Required, Display(Name = "To plant")] public int ToPlantId { get; set; }
    [Display(Name = "To location")] public int? ToLocationId { get; set; }
    [Required, Display(Name = "To cost centre")] public int ToCostCenterId { get; set; }
    [Display(Name = "New custodian")] public string? ToCustodian { get; set; }
    [Required, MaxLength(300)] public string Reason { get; set; } = "";
}

public class WorkOrderForm
{
    [Required, Display(Name = "Asset")] public int AssetId { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(2000)] public string? Description { get; set; }
    public WorkOrderType Type { get; set; } = WorkOrderType.Corrective;
    public Priority Priority { get; set; } = Priority.Medium;
    [Display(Name = "Assigned to")] public string? AssignedTo { get; set; }
    [DataType(DataType.Date), Display(Name = "Scheduled date")] public DateTime? ScheduledDate { get; set; }
}

public class CompleteWorkOrderForm
{
    public int Id { get; set; }
    [Range(0, 100_000_000), Display(Name = "Labour cost")] public decimal LaborCost { get; set; }
    [Range(0, 100_000_000), Display(Name = "Parts cost")] public decimal PartsCost { get; set; }
    [Range(0, 100_000_000), Display(Name = "External / vendor cost")] public decimal ExternalCost { get; set; }
    [Required, MaxLength(1000), Display(Name = "Resolution notes")] public string ResolutionNotes { get; set; } = "";
    [Display(Name = "Asset condition after work")] public AssetCondition Condition { get; set; } = AssetCondition.Good;
}

public class DashboardViewModel
{
    public int TotalAssets { get; set; }
    public decimal GrossCost { get; set; }
    public decimal AccumulatedDepreciation { get; set; }
    public decimal NetBookValue { get; set; }
    public int PendingApprovals { get; set; }
    public int OpenWorkOrders { get; set; }
    public int OverduePlans { get; set; }
    public decimal MaintenanceCostYtd { get; set; }
    public int UnderMaintenance { get; set; }
    public List<(string Label, decimal Value)> NbvByClass { get; set; } = new();
    public List<(string Label, decimal Value)> CountByPlant { get; set; } = new();
    public List<(string Label, decimal Value)> CountByStatus { get; set; } = new();
    public List<(string Label, decimal Value)> MaintenanceByMonth { get; set; } = new();
    public List<(string Label, decimal Value)> DepreciationByMonth { get; set; } = new();
    public List<AssetEvent> RecentEvents { get; set; } = new();
}

public class CostCenterSummaryRow
{
    public string CostCenter { get; set; } = "";
    public string Plant { get; set; } = "";
    public int AssetCount { get; set; }
    public decimal Cost { get; set; }
    public decimal AccumulatedDepreciation { get; set; }
    public decimal NetBookValue => Cost - AccumulatedDepreciation;
    public decimal MaintenanceCost { get; set; }
}

public class GlJournalRow
{
    public string DebitAccount { get; set; } = "";
    public string CreditAccount { get; set; } = "";
    public string Description { get; set; } = "";
    public string CostCenter { get; set; } = "";
    public decimal Amount { get; set; }
}
