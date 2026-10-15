using System;

namespace UniNet.Domain.Entities;

public sealed class AdvertisementTransaction
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public Guid CampaignId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation Properties
    public Transaction Transaction { get; set; } = null!;
    public AdvertisementCampaign Campaign { get; set; } = null!;
}