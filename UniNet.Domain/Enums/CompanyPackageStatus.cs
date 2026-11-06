namespace UniNet.Domain.Enums;

public enum CompanyPackageStatus : short
{
    Available = 0, // Khả dụng (chưa sử dụng)
    InUse = 1,     // Đang được gán cho một chiến dịch (chờ duyệt)
    Used = 2,      // Đã được duyệt và đang/đã chạy
    Expired = 3    // Hết hạn
}