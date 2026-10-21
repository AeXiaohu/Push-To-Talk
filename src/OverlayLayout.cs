using System;
using System.Collections.Generic;
using System.Drawing;

namespace PushToTalk
{
    // Pure geometry keeps saved overlays visible after monitor or resolution changes.
    internal static class OverlayLayout
    {
        public const int MinimumSize = 48;
        public const int MaximumSize = 256;
        public const int DefaultSize = 80;
        public const int DefaultMargin = 24;
        public const int MinimumOpacity = 20;
        public const int MaximumOpacity = 100;
        public const int DefaultOpacity = 100;

        public static int NormalizeSize(int size)
        {
            return Math.Max(MinimumSize, Math.Min(MaximumSize, size));
        }

        public static int NormalizeOpacity(int opacity)
        {
            return Math.Max(MinimumOpacity, Math.Min(MaximumOpacity, opacity));
        }

        // Put the primary working area first to choose the initial location.
        public static Rectangle GetBounds(int x, int y, int size, Rectangle[] workingAreas)
        {
            List<Rectangle> areas = GetValidAreas(workingAreas);
            size = NormalizeSize(size);

            if (x == int.MinValue || y == int.MinValue)
            {
                Rectangle primary = areas[0];
                int firstSize = FitSize(size, primary);
                long left = Right(primary) - firstSize - DefaultMargin;
                long top = (long)primary.Top + DefaultMargin;
                return ClampBounds(left, top, firstSize, primary);
            }

            // Keep the user's placement exactly when it is still fully visible.
            foreach (Rectangle area in areas)
            {
                if (x >= area.Left && y >= area.Top &&
                    (long)x + size <= Right(area) && (long)y + size <= Bottom(area))
                    return new Rectangle(x, y, size, size);
            }

            Rectangle chosen = areas[0];
            long greatestIntersection = -1;
            double shortestDistance = double.MaxValue;
            foreach (Rectangle area in areas)
            {
                long width = Math.Max(0L, Math.Min((long)x + size, Right(area)) - Math.Max((long)x, area.Left));
                long height = Math.Max(0L, Math.Min((long)y + size, Bottom(area)) - Math.Max((long)y, area.Top));
                long intersection = width * height;
                // Distance from the saved square's center to the nearest point in this area.
                double centerX = (double)x + size / 2.0;
                double centerY = (double)y + size / 2.0;
                double dx = Math.Max(Math.Max(area.Left - centerX, centerX - Right(area)), 0.0);
                double dy = Math.Max(Math.Max(area.Top - centerY, centerY - Bottom(area)), 0.0);
                double distance = dx * dx + dy * dy;
                if (intersection > greatestIntersection ||
                    (intersection == greatestIntersection && distance < shortestDistance))
                {
                    chosen = area;
                    greatestIntersection = intersection;
                    shortestDistance = distance;
                }
            }
            return ClampBounds(x, y, FitSize(size, chosen), chosen);
        }

        private static int FitSize(int size, Rectangle area)
        {
            // Very small work areas may require a smaller square to remain visible.
            return Math.Min(size, Math.Min(area.Width, area.Height));
        }

        private static Rectangle ClampBounds(long x, long y, int size, Rectangle area)
        {
            long left = Math.Max((long)area.Left, Math.Min(Right(area) - size, x));
            long top = Math.Max((long)area.Top, Math.Min(Bottom(area) - size, y));
            return new Rectangle((int)left, (int)top, size, size);
        }

        private static List<Rectangle> GetValidAreas(Rectangle[] workingAreas)
        {
            List<Rectangle> areas = new List<Rectangle>();
            if (workingAreas != null)
            {
                foreach (Rectangle area in workingAreas)
                {
                    if (area.Width <= 0 || area.Height <= 0) continue;
                    // Normalize pathological rectangles without overflowing Right/Bottom.
                    long right = Math.Min((long)int.MaxValue, Right(area));
                    long bottom = Math.Min((long)int.MaxValue, Bottom(area));
                    long width = right - area.Left;
                    long height = bottom - area.Top;
                    if (width > 0 && height > 0)
                        areas.Add(new Rectangle(area.Left, area.Top, (int)width, (int)height));
                }
            }
            if (areas.Count == 0) areas.Add(new Rectangle(0, 0, 1920, 1080));
            return areas;
        }

        private static long Right(Rectangle area) { return (long)area.Left + area.Width; }
        private static long Bottom(Rectangle area) { return (long)area.Top + area.Height; }
    }
}
