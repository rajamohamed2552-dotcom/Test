using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace GKM.AssetManagement.Models;

public static class Roles
{
    public const string Admin = "Admin";
    public const string FinanceManager = "FinanceManager";
    public const string AssetAccountant = "AssetAccountant";
    public const string PlantManager = "PlantManager";
    public const string MaintenanceEngineer = "MaintenanceEngineer";
    public const string Auditor = "Auditor";

    public static readonly string[] All = { Admin, FinanceManager, AssetAccountant, PlantManager, MaintenanceEngineer, Auditor };

    // Role groups used by [Authorize(Roles = ...)]
    public const string AssetEditors = Admin + "," + FinanceManager + "," + AssetAccountant + "," + PlantManager;
    public const string Approvers = Admin + "," + FinanceManager;
    public const string MaintenanceUsers = Admin + "," + FinanceManager + "," + PlantManager + "," + MaintenanceEngineer;
    public const string AuditUsers = Admin + "," + FinanceManager + "," + Auditor;
}

public class ApplicationUser : IdentityUser
{
    [MaxLength(120)] public string FullName { get; set; } = "";
    public int? PlantId { get; set; }
    public Plant? Plant { get; set; }
}

public enum DepreciationMethod { StraightLine = 1, DecliningBalance = 2, NoDepreciation = 3 }
public enum AssetStatus { Draft = 1, PendingApproval = 2, Active = 3, UnderMaintenance = 4, Rejected = 5, Disposed = 6 }
public enum Criticality { Low = 1, Medium = 2, High = 3, Critical = 4 }
public enum AssetCondition { Good = 1, Fair = 2, Poor = 3, NonFunctional = 4 }

public class Plant
{
    public int Id { get; set; }
    [Required, MaxLength(10)] public string Code { get; set; } = "";
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(100)] public string? City { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Location> Locations { get; set; } = new();
    public List<CostCenter> CostCenters { get; set; } = new();
}

public class Location
{
    public int Id { get; set; }
    public int PlantId { get; set; }
    public Plant? Plant { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";   // e.g. "Body Shop - Line 2"
    [MaxLength(200)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string FullName => Plant == null ? Name : $"{Plant.Code} / {Name}";
}

public class CostCenter
{
    public int Id { get; set; }
    [Required, MaxLength(20)] public string Code { get; set; } = "";
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    public int? PlantId { get; set; }
    public Plant? Plant { get; set; }
    [MaxLength(100)] public string? Owner { get; set; }
    public bool IsActive { get; set; } = true;
    public string Display => $"{Code} - {Name}";
}

public enum GlAccountType { Asset = 1, AccumulatedDepreciation = 2, DepreciationExpense = 3, MaintenanceExpense = 4, GainLossOnDisposal = 5 }

public class GlAccount
{
    public int Id { get; set; }
    [Required, MaxLength(20)] public string Code { get; set; } = "";
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public GlAccountType Type { get; set; }
    public string Display => $"{Code} - {Name}";
}

public class AssetCategory
{
    public int Id { get; set; }
    [Required, MaxLength(10)] public string Code { get; set; } = "";       // used in the asset tag
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(60)] public string AssetClass { get; set; } = "Plant & Machinery"; // top-level classification
    [MaxLength(300)] public string? Description { get; set; }
    public DepreciationMethod DefaultMethod { get; set; } = DepreciationMethod.StraightLine;
    [Range(1, 1200)] public int DefaultUsefulLifeMonths { get; set; } = 120;
    [Range(0, 100)] public decimal DefaultSalvagePercent { get; set; } = 5;
    public decimal CapitalizationThreshold { get; set; } = 0;     // below this -> expensed, not capitalised
    public int? AssetGlAccountId { get; set; }
    public GlAccount? AssetGlAccount { get; set; }
    public int? AccumDepGlAccountId { get; set; }
    public GlAccount? AccumDepGlAccount { get; set; }
    public int? DepExpenseGlAccountId { get; set; }
    public GlAccount? DepExpenseGlAccount { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Asset
{
    public int Id { get; set; }
    [MaxLength(30)] public string AssetTag { get; set; } = "";
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    [MaxLength(1000)] public string? Description { get; set; }
    public int CategoryId { get; set; }
    public AssetCategory? Category { get; set; }
    public int PlantId { get; set; }
    public Plant? Plant { get; set; }
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
    public int CostCenterId { get; set; }
    public CostCenter? CostCenter { get; set; }

    public AssetStatus Status { get; set; } = AssetStatus.Draft;
    public Criticality Criticality { get; set; } = Criticality.Medium;
    public AssetCondition Condition { get; set; } = AssetCondition.Good;

    [MaxLength(80)] public string? Manufacturer { get; set; }
    [MaxLength(80)] public string? ModelNumber { get; set; }
    [MaxLength(80)] public string? SerialNumber { get; set; }
    [MaxLength(120)] public string? Vendor { get; set; }
    [MaxLength(60)] public string? PurchaseOrderNumber { get; set; }
    [MaxLength(100)] public string? Custodian { get; set; }

    [DataType(DataType.Date)] public DateTime AcquisitionDate { get; set; } = DateTime.Today;
    [Range(0, 1_000_000_000)] public decimal AcquisitionCost { get; set; }
    [Range(0, 1_000_000_000)] public decimal SalvageValue { get; set; }
    [Range(1, 1200)] public int UsefulLifeMonths { get; set; } = 120;
    public DepreciationMethod Method { get; set; } = DepreciationMethod.StraightLine;
    [DataType(DataType.Date)] public DateTime? CapitalizationDate { get; set; }
    public decimal AccumulatedDepreciation { get; set; }
    public DateTime? WarrantyExpiry { get; set; }

    // Onboarding workflow
    [MaxLength(100)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    [MaxLength(100)] public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    [MaxLength(500)] public string? RejectionReason { get; set; }

    // Disposal
    public DateTime? DisposalDate { get; set; }
    public decimal? DisposalProceeds { get; set; }
    [MaxLength(300)] public string? DisposalReason { get; set; }

    public decimal BookValue => AcquisitionCost - AccumulatedDepreciation;

    public List<AssetMovement> Movements { get; set; } = new();
    public List<WorkOrder> WorkOrders { get; set; } = new();
    public List<AssetEvent> Events { get; set; } = new();
    public List<DepreciationEntry> DepreciationEntries { get; set; } = new();
}

public class AssetMovement
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public Asset? Asset { get; set; }
    public int? FromPlantId { get; set; }
    public Plant? FromPlant { get; set; }
    public int? FromLocationId { get; set; }
    public Location? FromLocation { get; set; }
    public int? FromCostCenterId { get; set; }
    public CostCenter? FromCostCenter { get; set; }
    [MaxLength(100)] public string? FromCustodian { get; set; }
    public int ToPlantId { get; set; }
    public Plant? ToPlant { get; set; }
    public int? ToLocationId { get; set; }
    public Location? ToLocation { get; set; }
    public int ToCostCenterId { get; set; }
    public CostCenter? ToCostCenter { get; set; }
    [MaxLength(100)] public string? ToCustodian { get; set; }
    [MaxLength(300)] public string? Reason { get; set; }
    public DateTime MovedOn { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string? MovedBy { get; set; }
}

public enum WorkOrderType { Preventive = 1, Corrective = 2, Inspection = 3, Calibration = 4 }
public enum WorkOrderStatus { Open = 1, InProgress = 2, Completed = 3, Cancelled = 4 }
public enum Priority { Low = 1, Medium = 2, High = 3, Urgent = 4 }

public class WorkOrder
{
    public int Id { get; set; }
    [MaxLength(20)] public string Number { get; set; } = "";
    public int AssetId { get; set; }
    public Asset? Asset { get; set; }
    public int? MaintenancePlanId { get; set; }
    public MaintenancePlan? MaintenancePlan { get; set; }
    public WorkOrderType Type { get; set; } = WorkOrderType.Corrective;
    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Open;
    public Priority Priority { get; set; } = Priority.Medium;
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(2000)] public string? Description { get; set; }
    [MaxLength(100)] public string? AssignedTo { get; set; }
    [MaxLength(100)] public string? RequestedBy { get; set; }
    public DateTime RequestedOn { get; set; } = DateTime.UtcNow;
    [DataType(DataType.Date)] public DateTime? ScheduledDate { get; set; }
    public DateTime? StartedOn { get; set; }
    public DateTime? CompletedOn { get; set; }
    public decimal LaborCost { get; set; }
    public decimal PartsCost { get; set; }
    public decimal ExternalCost { get; set; }
    [MaxLength(1000)] public string? ResolutionNotes { get; set; }
    public decimal TotalCost => LaborCost + PartsCost + ExternalCost;
}

public class MaintenancePlan
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public Asset? Asset { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    [MaxLength(1000)] public string? Instructions { get; set; }
    [Range(1, 3650)] public int FrequencyDays { get; set; } = 90;
    [DataType(DataType.Date)] public DateTime NextDueDate { get; set; } = DateTime.Today.AddDays(90);
    public DateTime? LastPerformedOn { get; set; }
    public decimal EstimatedCost { get; set; }
    public bool IsActive { get; set; } = true;
}

public class DepreciationRun
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime RunOn { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string? RunBy { get; set; }
    public int AssetsProcessed { get; set; }
    public decimal TotalDepreciation { get; set; }
    public List<DepreciationEntry> Entries { get; set; } = new();
    public string Period => new DateTime(Year, Month, 1).ToString("MMM yyyy");
}

public class DepreciationEntry
{
    public int Id { get; set; }
    public int DepreciationRunId { get; set; }
    public DepreciationRun? Run { get; set; }
    public int AssetId { get; set; }
    public Asset? Asset { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public decimal AccumulatedAfter { get; set; }
    public decimal BookValueAfter { get; set; }
    public int CostCenterId { get; set; }          // snapshot for cost-center reporting
    public int? DepExpenseGlAccountId { get; set; } // snapshot for GL posting
}

public enum AuditCampaignStatus { Planned = 1, InProgress = 2, Closed = 3 }
public enum AuditResult { Pending = 0, Verified = 1, Missing = 2, LocationMismatch = 3, ConditionIssue = 4 }

public class AuditCampaign
{
    public int Id { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    public int PlantId { get; set; }
    public Plant? Plant { get; set; }
    [DataType(DataType.Date)] public DateTime StartDate { get; set; } = DateTime.Today;
    [DataType(DataType.Date)] public DateTime EndDate { get; set; } = DateTime.Today.AddDays(14);
    public AuditCampaignStatus Status { get; set; } = AuditCampaignStatus.Planned;
    [MaxLength(100)] public string? CreatedBy { get; set; }
    public DateTime? ClosedOn { get; set; }
    [MaxLength(1000)] public string? Summary { get; set; }
    public List<AuditItem> Items { get; set; } = new();
}

public class AuditItem
{
    public int Id { get; set; }
    public int AuditCampaignId { get; set; }
    public AuditCampaign? Campaign { get; set; }
    public int AssetId { get; set; }
    public Asset? Asset { get; set; }
    public int? ExpectedLocationId { get; set; }
    public Location? ExpectedLocation { get; set; }
    public AuditResult Result { get; set; } = AuditResult.Pending;
    public int? FoundLocationId { get; set; }
    public Location? FoundLocation { get; set; }
    public AssetCondition? FoundCondition { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
    [MaxLength(100)] public string? VerifiedBy { get; set; }
    public DateTime? VerifiedOn { get; set; }
}

/// <summary>Immutable audit trail: every significant change to an asset is logged here.</summary>
public class AssetEvent
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public Asset? Asset { get; set; }
    [MaxLength(40)] public string EventType { get; set; } = "";
    [MaxLength(500)] public string Details { get; set; } = "";
    [MaxLength(100)] public string? PerformedBy { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}
