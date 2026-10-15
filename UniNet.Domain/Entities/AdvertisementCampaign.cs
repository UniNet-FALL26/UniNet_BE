using System;
using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class AdvertisementCampaign
{
    public Guid Id { get; set; }
    public Guid CompanyUserId { get; set; } // Foreign Key -> UserProfile (Id của Doanh nghiệp)
    public Guid PackageId { get; set; }     // Foreign Key -> Package
    public string Title { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string DestinationUrl { get; set; } = null!;
    public string? MediaUrl { get; set; }
    public AdCampaignStatus Status { get; set; } = AdCampaignStatus.Draft;
    public decimal BudgetCap { get; set; }
    public long ImpressionCount { get; set; } = 0;
    public long ClickCount { get; set; } = 0;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? ExpiredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation Properties
    public UserProfile CompanyUser { get; set; } = null!;
    public Package Package { get; set; } = null!;
    public List<AdvertisementTransaction> AdvertisementTransactions { get; set; } = [];
}