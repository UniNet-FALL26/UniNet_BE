namespace UniNet.Application.Common;

public static class PortfolioInterestCatalog
{
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[]
    {
        "Đọc sách", "Nhiếp ảnh", "Du lịch", "Cầu lông", "Âm nhạc", "Nấu ăn",
        "Chạy bộ", "Cà phê", "Hội họa", "Làm vườn", "Điện ảnh", "Đạp xe",
        "Bóng đá", "Bóng rổ", "Bóng chuyền", "Bóng bàn", "Tennis", "Bơi lội",
        "Yoga", "Gym", "Leo núi", "Đi bộ", "Cắm trại", "Câu cá",
        "Ca hát", "Guitar", "Piano", "Nhảy múa", "Viết lách", "Thủ công",
        "Thiết kế", "Thời trang", "Lập trình", "Robot", "Thiên văn", "Khoa học",
        "Học ngoại ngữ", "Chơi game", "Cờ vua", "Board game", "Podcast", "Anime",
        "Thú cưng", "Thiện nguyện", "Ẩm thực", "Làm bánh", "Sưu tầm", "Thiền",
    });
}
