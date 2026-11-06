using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UniNet.Domain.Enums
{
    public enum AdCampaignStatus : short
    {
        Draft = 0,              // Nháp
        PendingModeration = 1,  // Chờ AI Review hoặc Admin Review
        PendingPayment = 2,     // Đã duyệt (bởi AI/Admin), chờ Partner thanh toán
        Active = 3,             // Thanh toán thành công -> Đã xuất bản (Publish Ad)
        Completed = 4,          // Hết hạn thời gian chạy
        Rejected = 5            // Bị từ chối (bởi AI hoặc Admin)
    }
}
