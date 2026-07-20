namespace RevitAPP.Core.Services;

/// <summary>Đóng gói các cặp viewport thành hai hàng, có kiểm tra tràn và chồng lấn.</summary>
public static class FootingSheetLayoutCalculator
{
    public static IReadOnlyList<FootingSheetLayoutSlot> Pack(
        double minX, double minY, double maxX, double maxY,
        IReadOnlyList<FootingViewportPairSize> pairs,
        double titleBlockReserveRatio = 0.22,
        double horizontalGap = 0.08,
        double verticalGap = 0.08)
    {
        if (pairs.Count == 0) return Array.Empty<FootingSheetLayoutSlot>();
        if (maxX <= minX || maxY <= minY) throw new ArgumentException("Kích thước sheet không hợp lệ.");
        if (titleBlockReserveRatio is < 0 or >= 0.5)
            throw new ArgumentOutOfRangeException(nameof(titleBlockReserveRatio));
        if (pairs.Any(pair => pair.PlanWidth <= 0 || pair.PlanHeight <= 0 ||
                              pair.SectionWidth <= 0 || pair.SectionHeight <= 0))
            throw new ArgumentException("Kích thước viewport phải lớn hơn 0.");

        var sheetWidth = maxX - minX;
        var sheetHeight = maxY - minY;
        var marginX = sheetWidth * 0.02;
        var marginY = sheetHeight * 0.12;
        var availableLeft = minX + marginX;
        var availableRight = maxX - sheetWidth * titleBlockReserveRatio - marginX;
        var columnWidths = pairs.Select(pair => Math.Max(pair.PlanWidth, pair.SectionWidth)).ToArray();
        var requiredWidth = columnWidths.Sum() + horizontalGap * Math.Max(0, pairs.Count - 1);
        if (requiredWidth > availableRight - availableLeft)
            throw new InvalidOperationException("Không đủ chiều ngang sheet để xếp các cặp viewport mà không chồng nhau.");

        var maxPlanHeight = pairs.Max(pair => pair.PlanHeight);
        var maxSectionHeight = pairs.Max(pair => pair.SectionHeight);
        if (maxPlanHeight + maxSectionHeight + verticalGap > sheetHeight - marginY * 2)
            throw new InvalidOperationException("Không đủ chiều cao sheet để đặt mặt bằng trên và mặt cắt dưới.");

        var extra = (availableRight - availableLeft - requiredWidth) / 2;
        var cursor = availableLeft + extra;
        var planCenterY = maxY - marginY - maxPlanHeight / 2;
        var sectionCenterY = minY + marginY + maxSectionHeight / 2;
        var slots = new List<FootingSheetLayoutSlot>(pairs.Count);
        for (var index = 0; index < pairs.Count; index++)
        {
            var x = cursor + columnWidths[index] / 2;
            slots.Add(new FootingSheetLayoutSlot(index, x, planCenterY, sectionCenterY));
            cursor += columnWidths[index] + horizontalGap;
        }
        return slots;
    }
}

public sealed record FootingViewportPairSize(double PlanWidth, double PlanHeight, double SectionWidth, double SectionHeight);
public sealed record FootingSheetLayoutSlot(int Column, double X, double PlanY, double SectionY);
