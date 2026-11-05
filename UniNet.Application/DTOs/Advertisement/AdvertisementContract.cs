using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using UniNet.Domain.Enums;

namespace UniNet.Application.DTOs.Advertisement;

public sealed record CreateCampaignRequest(
    string Title,
    string Content,
    string DestinationUrl,
    IFormFile? MediaFile);
public sealed record AssignPackageRequest(
    Guid PackageId);
public sealed record UpdateCampaignRequest(
    string Title,
    string Content,
    string DestinationUrl,
    string? MediaUrl);

public sealed record CampaignResponse(
    Guid Id,
    Guid CompanyUserId,
    string CompanyName,
    Guid? PackageId,
    string? PackageName,
    string Title,
    string Content,
    string DestinationUrl,
    string? MediaUrl,
    AdCampaignStatus Status,
    decimal BudgetCap,
    string? RejectionReason,
    long ImpressionCount,
    long ClickCount,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ExpiredAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CampaignsResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<CampaignResponse> Campaigns);

public sealed record ModerateCampaignRequest(
    bool IsApproved,
    string? Reason);