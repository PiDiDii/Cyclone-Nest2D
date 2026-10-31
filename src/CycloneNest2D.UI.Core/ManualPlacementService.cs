using CycloneNest2D.Core;

namespace CycloneNest2D.UI;

/// <summary>
/// Encapsulates the validation and movement rules for manual placement adjustment.
/// </summary>
public sealed class ManualPlacementService
{
    public string? CheckMove(SheetResult sheet, SheetDefinition definition, int index, double x, double y)
    {
        if (sheet == null || definition == null) return "Không có tấm để đặt.";
        if (index < 0 || index >= sheet.Placements.Count)
            return "Không có chi tiết được chọn.";

        var candidate = PlacementOps.Moved(sheet.Placements[index], x, y);
        return Validator.CheckPlacement(sheet, index, candidate, definition);
    }

    public string? CheckMoveSelected(SheetResult sheet, SheetDefinition definition, IEnumerable<int> selectedIndices, double dx, double dy)
    {
        var selected = selectedIndices.Where(i => i >= 0 && i < sheet.Placements.Count).ToHashSet();
        if (selected.Count == 0) return "Chưa chọn chi tiết.";

        var obstacles = new SheetResult
        {
            Index = sheet.Index,
            Width = sheet.Width,
            Height = sheet.Height,
            Material = sheet.Material,
            Thickness = sheet.Thickness,
            StockName = sheet.StockName
        };

        for (int i = 0; i < sheet.Placements.Count; i++)
        {
            if (!selected.Contains(i)) obstacles.Placements.Add(sheet.Placements[i]);
        }

        foreach (int index in selected.Order())
        {
            var candidate = PlacementOps.Moved(sheet.Placements[index], sheet.Placements[index].X + dx, sheet.Placements[index].Y + dy);
            string? error = Validator.CheckPlacement(obstacles, -1, candidate, definition);
            if (error != null) return error;
            obstacles.Placements.Add(candidate);
        }

        return null;
    }

    public bool TryMoveSelected(SheetResult sheet, SheetDefinition definition, IEnumerable<int> selectedIndices, double dx, double dy)
    {
        var selected = selectedIndices.Where(i => i >= 0 && i < sheet.Placements.Count).ToArray();
        if (selected.Length == 0) return false;
        if (CheckMoveSelected(sheet, definition, selected, dx, dy) != null) return false;

        foreach (int index in selected)
        {
            var placement = sheet.Placements[index];
            sheet.Placements[index] = PlacementOps.Moved(placement, placement.X + dx, placement.Y + dy);
        }

        return true;
    }

    public bool MoveSelectedUntilBlocked(SheetResult sheet, SheetDefinition definition, IEnumerable<int> selectedIndices, double dx, double dy)
    {
        var selected = selectedIndices.Where(i => i >= 0 && i < sheet.Placements.Count).ToArray();
        if (selected.Length == 0) return false;

        bool horizontal = Math.Abs(dx) > 1e-9;
        if (horizontal == (Math.Abs(dy) > 1e-9)) return false;

        double sign = Math.Sign(horizontal ? dx : dy);
        var selectedSet = selected.ToHashSet();
        var obstacles = new SheetResult
        {
            Index = sheet.Index,
            Width = sheet.Width,
            Height = sheet.Height,
            Material = sheet.Material,
            Thickness = sheet.Thickness,
            StockName = sheet.StockName
        };

        for (int i = 0; i < sheet.Placements.Count; i++)
        {
            if (!selectedSet.Contains(i)) obstacles.Placements.Add(sheet.Placements[i]);
        }

        double limit = horizontal
            ? sign > 0
                ? sheet.Width - definition.Margin - selected.Max(i => sheet.Placements[i].X + sheet.Placements[i].Width)
                : selected.Min(i => sheet.Placements[i].X) - definition.Margin
            : sign > 0
                ? sheet.Height - definition.Margin - selected.Max(i => sheet.Placements[i].Y + sheet.Placements[i].Height)
                : selected.Min(i => sheet.Placements[i].Y) - definition.Margin;

        if (limit <= 1e-6) return false;

        double nearestObstacle = double.PositiveInfinity;
        foreach (int index in selected)
        {
            var part = sheet.Placements[index];
            foreach (var obstacle in obstacles.Placements)
            {
                bool overlapsAcrossAxis = horizontal
                    ? part.Y < obstacle.Y + obstacle.Height + definition.Spacing && obstacle.Y < part.Y + part.Height + definition.Spacing
                    : part.X < obstacle.X + obstacle.Width + definition.Spacing && obstacle.X < part.X + part.Width + definition.Spacing;
                if (!overlapsAcrossAxis) continue;

                double partStart = horizontal ? part.X : part.Y;
                double partSize = horizontal ? part.Width : part.Height;
                double obstacleStart = horizontal ? obstacle.X : obstacle.Y;
                double obstacleSize = horizontal ? obstacle.Width : obstacle.Height;
                double distance;
                if (sign > 0)
                {
                    if (obstacleStart + obstacleSize + definition.Spacing < partStart) continue;
                    distance = obstacleStart - (partStart + partSize + definition.Spacing);
                }
                else
                {
                    if (obstacleStart - definition.Spacing > partStart + partSize) continue;
                    distance = partStart - (obstacleStart + obstacleSize + definition.Spacing);
                }
                nearestObstacle = Math.Min(nearestObstacle, Math.Max(0, distance));
            }
        }

        string? CheckAt(double distance)
        {
            double tx = horizontal ? sign * distance : 0;
            double ty = horizontal ? 0 : sign * distance;
            foreach (int index in selected)
            {
                var original = sheet.Placements[index];
                var candidate = PlacementOps.Moved(original, original.X + tx, original.Y + ty);
                string? error = Validator.CheckPlacement(obstacles, -1, candidate, definition);
                if (error != null) return error;
            }
            return null;
        }

        const double probeStep = 1;
        double lastValid = 0, firstInvalid = double.NaN;
        double distanceToProbe = Math.Min(limit, double.IsFinite(nearestObstacle) ? Math.Max(probeStep, nearestObstacle) : limit);

        for (double distance = distanceToProbe; ; distance = Math.Min(distance + probeStep, limit))
        {
            if (CheckAt(distance) != null) { firstInvalid = distance; break; }
            lastValid = distance;
            if (distance >= limit - 1e-9) break;
        }

        if (!double.IsNaN(firstInvalid))
        {
            double lo = lastValid, hi = firstInvalid;
            for (int i = 0; i < 10; i++)
            {
                double mid = (lo + hi) / 2;
                if (CheckAt(mid) == null) lo = mid; else hi = mid;
            }
            lastValid = lo;
        }

        if (lastValid <= 1e-6) return false;

        double moveX = horizontal ? sign * lastValid : 0;
        double moveY = horizontal ? 0 : sign * lastValid;
        return TryMoveSelected(sheet, definition, selected, moveX, moveY);
    }

    public string? TryMove(SheetResult sheet, SheetDefinition definition, int index, double x, double y)
    {
        string? error = CheckMove(sheet, definition, index, x, y);
        if (error != null) return error;

        sheet.Placements[index] = PlacementOps.Moved(sheet.Placements[index], x, y);
        return null;
    }

    public bool MoveUntilBlocked(SheetResult sheet, SheetDefinition definition, int index, double dx, double dy)
    {
        if (index < 0 || index >= sheet.Placements.Count) return false;
        bool horizontal = Math.Abs(dx) > 1e-9;
        if (horizontal == (Math.Abs(dy) > 1e-9)) return false;

        var p = sheet.Placements[index];
        double sign = Math.Sign(horizontal ? dx : dy);
        double limit = horizontal
            ? sign > 0 ? sheet.Width - definition.Margin - p.X - p.Width : p.X - definition.Margin
            : sign > 0 ? sheet.Height - definition.Margin - p.Y - p.Height : p.Y - definition.Margin;
        if (limit <= 1e-6) return false;

        const double probeStep = 0.25;
        double lastValid = 0, firstInvalid = double.NaN;
        for (double distance = Math.Min(probeStep, limit); ; distance = Math.Min(distance + probeStep, limit))
        {
            double x = p.X + (horizontal ? sign * distance : 0);
            double y = p.Y + (horizontal ? 0 : sign * distance);
            if (CheckMove(sheet, definition, index, x, y) != null) { firstInvalid = distance; break; }
            lastValid = distance;
            if (distance >= limit - 1e-9) break;
        }

        if (!double.IsNaN(firstInvalid))
        {
            double lo = lastValid, hi = firstInvalid;
            for (int i = 0; i < 12; i++)
            {
                double mid = (lo + hi) / 2;
                double x = p.X + (horizontal ? sign * mid : 0);
                double y = p.Y + (horizontal ? 0 : sign * mid);
                if (CheckMove(sheet, definition, index, x, y) == null) lo = mid; else hi = mid;
            }
            lastValid = lo;
        }

        if (lastValid <= 1e-6) return false;

        var finalX = p.X + (horizontal ? sign * lastValid : 0);
        var finalY = p.Y + (horizontal ? 0 : sign * lastValid);
        return TryMove(sheet, definition, index, finalX, finalY) == null;
    }

    public bool TryMirror(SheetResult sheet, SheetDefinition definition, int index)
    {
        if (index < 0 || index >= sheet.Placements.Count) return false;
        var before = sheet.Placements[index];
        var candidate = before with { Shape = before.Shape == null ? null : ShapeOps.MirrorHorizontal(before.Shape) };
        if (Validator.CheckPlacement(sheet, index, candidate, definition) != null) return false;
        sheet.Placements[index] = candidate;
        return true;
    }

    public bool TryRotate(SheetResult sheet, SheetDefinition definition, int index)
    {
        if (index < 0 || index >= sheet.Placements.Count) return false;
        var candidate = PlacementOps.Rotated90(sheet.Placements[index]);
        if (Validator.CheckPlacement(sheet, index, candidate, definition) != null) return false;
        sheet.Placements[index] = candidate;
        return true;
    }
}
