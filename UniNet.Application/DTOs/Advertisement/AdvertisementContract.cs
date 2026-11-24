using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application.DTOs.Advertisement;
public sealed record ReviewRefundRequest(
    bool IsApproved,
    string? ManualBankReference, // Bắt buộc khi IsApproved = true (mã giao dịch ngân hàng)
    string? ReviewReason);        // Bắt buộc khi IsApproved = false (lý do từ chối)

public sealed record RefundTransactionsResponse(
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    IReadOnlyList<RefundTransactionResponse> Items);
public sealed record RequestRefundRequest(
    string Reason,
    string ManualPayingAccount,
    Guid? RequestKey = null);

public sealed record RefundTransactionResponse(
    Guid Id,
    Guid TransactionId,
    Guid? RequestedByUserId,
    decimal Amount,
    string PaymentProvider,
    RefundApproval Approval,
    PaymentStatus Status,
    string Reason,
    string ManualPayingAccount,
    string? ReviewReason,
    string? ManualBankReference,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset CreatedAt);
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
    IFormFile? MediaFile);

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