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
        PendingPayment = 1,     // Chờ thanh toán
        PendingModeration = 2,  // Đã thanh toán, chờ Moderator duyệt
        Active = 3,             // Đang chạy
        Completed = 4,          // Đã hoàn thành / Hết hạn
        Rejected = 5            // Bị từ chối
    }
}
