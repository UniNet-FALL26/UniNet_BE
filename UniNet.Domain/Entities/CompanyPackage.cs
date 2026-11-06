using System;
using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class CompanyPackage
{
    public Guid Id { get; set; }
    public Guid CompanyUserId { get; set; } // FK -> UserProfile (Doanh nghiệp sở hữu)
    public Guid PackageId { get; set; }     // FK -> Package gốc
    public CompanyPackageStatus Status { get; set; } = CompanyPackageStatus.Available;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation Properties
    public UserProfile CompanyUser { get; set; } = null!;
    public Package Package { get; set; } = null!;
    public AdvertisementCampaign? Campaign { get; set; }
}