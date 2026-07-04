using System.Globalization;
using System.Runtime.InteropServices;
using XLSight.Analysis;

namespace XLSight.Internal.Sinks;

internal static class SheetLayoutInference
{
    public static SheetLayoutInfo Infer(LayoutCellStore cells)
    {
        if (cells.Count == 0)
        {
            return SheetLayoutInfo.Empty;
        }

        var sheet = SheetFacts.From(cells);
        var occupied = new List<ExcelRange>();

        List<FieldCandidate> matrices = FindMatrixFields(sheet);
        AddMatrixZones(matrices, occupied);

        var dense = new List<FieldCandidate>();
        foreach (var candidate in FindDenseFields(sheet, matrices))
        {
            if (!OverlapsAny(candidate.Range, occupied))
            {
                dense.Add(candidate);
                occupied.Add(candidate.Range);
            }
        }

        // Spacer columns inside one wide table (a missing header cell, or a sparse separator)
        // split it into co-extensive side-by-side fields. Re-join them across data-bearing gaps;
        // a fully empty gap column is a real boundary and is left alone.
        dense = MergeColumnAdjacentFields(dense, sheet);
        occupied.Clear();
        AddMatrixZones(matrices, occupied);
        occupied.AddRange(dense.Select(static candidate => candidate.Range));

        var vector = CoalesceVectorFields(FindVectorFields(sheet, occupied)).ToList();

        var fieldRanges = new List<ExcelRange>(dense.Count + matrices.Count + vector.Count);
        fieldRanges.AddRange(dense.Select(static candidate => candidate.Range));
        fieldRanges.AddRange(matrices.Select(static candidate => candidate.Range));
        fieldRanges.AddRange(vector.Select(static candidate => candidate.Range));
        var axisBuilder = new AxisBuilder(sheet, fieldRanges);

        var candidates = new List<FieldCandidate>(fieldRanges.Count);
        candidates.AddRange(dense);
        candidates.AddRange(matrices);
        candidates.AddRange(vector);
        return BuildLayout(sheet, axisBuilder, candidates);
    }

    private static SheetLayoutInfo BuildLayout(SheetFacts sheet, AxisBuilder axisBuilder, List<FieldCandidate> candidates)
    {
        var axesPerField = new List<List<LayoutAxis>>(candidates.Count);
        foreach (var candidate in candidates)
        {
            axesPerField.Add(axisBuilder.GetAxes(candidate));
        }

        InheritHorizontalContext(candidates, axesPerField, axisBuilder);

        var fields = new List<MeasureFieldInfo>(candidates.Count);
        var fieldAxes = new List<IReadOnlyList<LayoutAxis>>(candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
        {
            fields.Add(CreateField(i + 1, candidates[i].Range, axesPerField[i], sheet));
            fieldAxes.Add(axesPerField[i]);
        }

        return new SheetLayoutInfo
        {
            Axes = axisBuilder.Axes,
            MeasureFields = fields,
            Groups = BuildGroups(fields, fieldAxes),
        };
    }

    // A matrix occupies its data block plus its two coordinate runs: the header row above and
    // the column just left. All three must be off-limits to dense/vector candidate detection.
    private static void AddMatrixZones(List<FieldCandidate> matrices, List<ExcelRange> occupied)
    {
        foreach (var matrix in matrices)
        {
            occupied.Add(matrix.Range);
            occupied.Add(new ExcelRange(
                new ExcelAddress(matrix.HeaderStartCol, matrix.HeaderRow),
                new ExcelAddress(matrix.HeaderEndCol, matrix.HeaderRow)));
            occupied.Add(new ExcelRange(
                new ExcelAddress(matrix.Range.TopLeft.Column - 1, matrix.Range.TopLeft.Row),
                new ExcelAddress(matrix.Range.TopLeft.Column - 1, matrix.Range.BottomRight.Row)));
        }
    }

    // A sensitivity-style matrix announces itself with numeric coordinates instead of text
    // headers: a strictly monotonic, uniform-step run of plain numerics across a row (e.g.
    // growth rates 0.5%..2.5%), a matching monotonic run down the column just left of it
    // (e.g. WACC 3.2%..7.2%), and a dense data block at their intersection. Year/date runs
    // never qualify (they are not measure cells), so ordinary year headers cannot seed one.
    private static List<FieldCandidate> FindMatrixFields(SheetFacts sheet)
    {
        var matrices = new List<FieldCandidate>();
        var claimed = new List<ExcelRange>();
        for (int rowIndex = 0; rowIndex < sheet.Rows.Count; rowIndex++)
        {
            (int lo, int hi) = sheet.RowSegment(rowIndex);
            int runStart = lo;
            while (runStart < hi)
            {
                int runEnd = ExtendUniformRun(sheet, runStart, hi);
                if (runEnd - runStart >= 3 &&
                    TryCreateMatrix(sheet, sheet.Rows[rowIndex], runStart, runEnd, claimed) is { } matrix)
                {
                    matrices.Add(matrix);
                    claimed.Add(matrix.Range);
                    runStart = runEnd;
                }
                else
                {
                    // Advance one cell, not to runEnd: a failed run's tail may start the real one
                    // (e.g. a base value in the corner directly before the coordinate run).
                    runStart++;
                }
            }
        }

        return matrices;
    }

    // Coordinate runs are hand-typed sequences with an exact constant step (0.5%, 1.0%, ...);
    // near-exact tolerance keeps computed data rows (which are never uniform) from seeding.
    private static bool IsUniformStep(double delta, double reference) =>
        Math.Sign(delta) == Math.Sign(reference) &&
        Math.Abs(delta - reference) <= Math.Max(Math.Abs(reference) * 0.05, 1e-9);

    // Exclusive end of the run from start of column-consecutive measure cells advancing by a
    // constant step.
    private static int ExtendUniformRun(SheetFacts sheet, int start, int hi)
    {
        if (!SheetFacts.IsMeasureCell(sheet.CellAt(start)))
        {
            return start;
        }

        int end = start + 1;
        double? firstDelta = null;
        while (end < hi)
        {
            LayoutCellFact previous = sheet.CellAt(end - 1);
            LayoutCellFact current = sheet.CellAt(end);
            if (current.Column != previous.Column + 1 || !SheetFacts.IsMeasureCell(current))
            {
                break;
            }

            double delta = current.NumericValue - previous.NumericValue;
            if (firstDelta is { } reference)
            {
                if (!IsUniformStep(delta, reference))
                {
                    break;
                }
            }
            else if (delta == 0)
            {
                break;
            }
            else
            {
                firstDelta = delta;
            }

            end++;
        }

        return end;
    }

    private static FieldCandidate? TryCreateMatrix(SheetFacts sheet, int headerRow, int runStart, int runEnd, List<ExcelRange> claimed)
    {
        int startCol = sheet.CellAt(runStart).Column;
        int endCol = sheet.CellAt(runEnd - 1).Column;
        int coordinateCol = startCol - 1;

        // The row coordinate column starts within a few rows below the header run (a text
        // label like "Return Rate" may sit between) and must itself be monotonic-uniform.
        int firstRow = 0;
        for (int row = headerRow + 1; row <= headerRow + 3; row++)
        {
            if (sheet.TryGetCell(row, coordinateCol, out var cell) && SheetFacts.IsMeasureCell(cell))
            {
                firstRow = row;
                break;
            }
        }

        if (firstRow == 0)
        {
            return null;
        }

        int lastRow = ExtendUniformColumnRun(sheet, coordinateCol, firstRow);
        if (lastRow - firstRow + 1 < 3)
        {
            return null;
        }

        var range = new ExcelRange(new ExcelAddress(startCol, firstRow), new ExcelAddress(endCol, lastRow));
        int area = range.Width * range.Height;
        if (OverlapsAny(range, claimed) || sheet.CountNumericCells(firstRow, lastRow, startCol, endCol) * 2 < area)
        {
            return null;
        }

        return new FieldCandidate(range, headerRow, startCol, endCol);
    }

    private static int ExtendUniformColumnRun(SheetFacts sheet, int col, int firstRow)
    {
        int lastRow = firstRow;
        double? firstDelta = null;
        while (sheet.TryGetCell(lastRow + 1, col, out var next) && SheetFacts.IsMeasureCell(next))
        {
            sheet.TryGetCell(lastRow, col, out var current);
            double delta = next.NumericValue - current.NumericValue;
            if (delta == 0 || (firstDelta is { } reference && !IsUniformStep(delta, reference)))
            {
                break;
            }

            firstDelta ??= delta;
            lastRow++;
        }

        return lastRow;
    }

    private static IEnumerable<FieldCandidate> FindDenseFields(SheetFacts sheet, List<FieldCandidate> matrices)
    {
        var rawCandidates = new List<FieldCandidate>();
        var claimed = new List<ExcelRange>();
        foreach (int headerRow in sheet.Rows)
        {
            foreach ((int startCol, int endCol) in sheet.GetHeaderRuns(headerRow))
            {
                int width = endCol - startCol + 1;
                if (width < 2)
                {
                    continue;
                }

                // A header-like run strictly inside an already-claimed field is table content
                // (labels, dates), not a new table's header; probing every such row would be
                // quadratic on tall tables.
                if (IsInsideClaimedField(headerRow, startCol, endCol, claimed))
                {
                    continue;
                }

                if (TryCreateDenseCandidate(sheet, headerRow, startCol, endCol, matrices) is not { } candidate)
                {
                    continue;
                }

                rawCandidates.Add(candidate);

                // Claim the untrimmed run width so label columns trimmed off the measure
                // range still suppress interior runs of the same table.
                claimed.Add(new ExcelRange(
                    new ExcelAddress(startCol, candidate.Range.TopLeft.Row),
                    new ExcelAddress(endCol, candidate.Range.BottomRight.Row)));
            }
        }

        return NormalizeSiblingRows(rawCandidates, sheet)
            .OrderBy(static candidate => candidate.Range.TopLeft.Row)
            .ThenBy(static candidate => candidate.Range.TopLeft.Column);
    }

    private static FieldCandidate? TryCreateDenseCandidate(SheetFacts sheet, int headerRow, int startCol, int endCol, List<FieldCandidate> matrices)
    {
        int startRow = FindDenseStartRow(sheet, headerRow, startCol, endCol);
        if (startRow == 0)
        {
            return null;
        }

        int endRow = FindDenseEndRow(sheet, headerRow, startRow, startCol, endCol, matrices);
        if (endRow < startRow)
        {
            return null;
        }

        (int trimmedStartCol, int trimmedEndCol) = TrimMeasureColumns(sheet, startRow, endRow, startCol, endCol);

        // A leading column of years/dates is the table's row coordinate (e.g. a "Year" column),
        // not measure data; peeling it here lets it attach as a vertical context axis instead.
        while (trimmedStartCol < trimmedEndCol && sheet.IsYearLikeColumn(startRow, endRow, trimmedStartCol))
        {
            trimmedStartCol++;
        }

        int trimmedWidth = trimmedEndCol - trimmedStartCol + 1;
        if (trimmedWidth < 2)
        {
            return null;
        }

        int numericCells = sheet.CountNumericCells(startRow, endRow, trimmedStartCol, trimmedEndCol);
        if (numericCells < Math.Min(4, trimmedWidth))
        {
            return null;
        }

        return new FieldCandidate(
            new ExcelRange(new ExcelAddress(trimmedStartCol, startRow), new ExcelAddress(trimmedEndCol, endRow)),
            headerRow,
            trimmedStartCol,
            trimmedEndCol);
    }

    private static bool IsInsideClaimedField(int headerRow, int startCol, int endCol, List<ExcelRange> claimed)
    {
        foreach (var range in claimed)
        {
            // Strictly above the bottom row: a run in a field's last row may legitimately
            // head a new table right below it, so it stays a candidate.
            if (headerRow >= range.TopLeft.Row && headerRow < range.BottomRight.Row &&
                startCol >= range.TopLeft.Column && endCol <= range.BottomRight.Column)
            {
                return true;
            }
        }

        return false;
    }

    private static List<FieldCandidate> NormalizeSiblingRows(List<FieldCandidate> rawCandidates, SheetFacts sheet)
    {
        var candidates = new List<FieldCandidate>(rawCandidates.Count);
        foreach (var group in rawCandidates.GroupBy(static candidate => candidate.HeaderRow))
        {
            var orderedGroup = group.OrderBy(static candidate => candidate.Range.TopLeft.Column).ToList();
            int groupStartRow = orderedGroup.Min(static candidate => candidate.Range.TopLeft.Row);
            int groupEndRow = orderedGroup[0].Range.BottomRight.Row;
            foreach (var candidate in orderedGroup)
            {
                candidates.Add(candidate with
                {
                    Range = new ExcelRange(
                        new ExcelAddress(candidate.Range.TopLeft.Column, groupStartRow),
                        new ExcelAddress(candidate.Range.BottomRight.Column, groupEndRow)),
                });
            }

            AddMissingSiblingCandidates(sheet, orderedGroup, group.Key, groupStartRow, groupEndRow, candidates);
        }

        return candidates;
    }

    private static void AddMissingSiblingCandidates(
        SheetFacts sheet,
        List<FieldCandidate> existing,
        int headerRow,
        int groupStartRow,
        int groupEndRow,
        List<FieldCandidate> candidates)
    {
        int minCol = existing[0].Range.TopLeft.Column;
        int maxCol = existing[^1].Range.BottomRight.Column;
        foreach ((int startCol, int endCol) in sheet.GetHeaderRuns(headerRow))
        {
            if (startCol < minCol || endCol > maxCol || HeaderRunOverlapsExisting(startCol, endCol, existing))
            {
                continue;
            }

            int numericCells = sheet.CountNumericCells(groupStartRow, groupEndRow, startCol, endCol);
            if (numericCells < 2)
            {
                continue;
            }

            candidates.Add(new FieldCandidate(
                new ExcelRange(new ExcelAddress(startCol, groupStartRow), new ExcelAddress(endCol, groupEndRow)),
                headerRow,
                startCol,
                endCol));
        }
    }

    private static bool HeaderRunOverlapsExisting(int startCol, int endCol, List<FieldCandidate> existing)
    {
        foreach (var candidate in existing)
        {
            if (Math.Min(endCol, candidate.Range.BottomRight.Column) >= Math.Max(startCol, candidate.Range.TopLeft.Column))
            {
                return true;
            }
        }

        return false;
    }

    private static (int StartCol, int EndCol) TrimMeasureColumns(SheetFacts sheet, int startRow, int endRow, int startCol, int endCol)
    {
        while (startCol <= endCol && sheet.CountNumericCells(startRow, endRow, startCol, startCol) < 2)
        {
            startCol++;
        }

        while (endCol >= startCol && sheet.CountNumericCells(startRow, endRow, endCol, endCol) < 2)
        {
            endCol--;
        }

        return (startCol, endCol);
    }

    private static int FindDenseStartRow(SheetFacts sheet, int headerRow, int startCol, int endCol)
    {
        int maxProbeRow = Math.Min(sheet.MaxRow, headerRow + 6);
        for (int row = headerRow + 1; row <= maxProbeRow; row++)
        {
            if (sheet.CountNumericCells(row, row, startCol, endCol) > 0)
            {
                return row;
            }
        }

        return 0;
    }

    private static int FindDenseEndRow(SheetFacts sheet, int headerRow, int startRow, int startCol, int endCol, List<FieldCandidate> matrices)
    {
        int lastSubstantiveRow = startRow;
        int emptyRows = 0;
        for (int row = startRow; row <= sheet.MaxRow; row++)
        {
            // A repeated header band after a blank gap starts a new section (e.g. the year
            // row reprinted above each of Income Statement / Balance Sheet / Cash Flow).
            if (row > startRow && emptyRows > 0 && sheet.IsHeaderRowForRun(row, startCol, endCol))
            {
                break;
            }

            // Matrix territory (numeric coordinate headers carry no header-like cells, so the
            // check above cannot see them) ends the table just like a reprinted header band.
            if (row > startRow && EntersMatrixZone(row, startCol, endCol, matrices))
            {
                break;
            }

            int numericCount = sheet.CountNumericCells(row, row, startCol, endCol);
            if (numericCount > 0)
            {
                int width = endCol - startCol + 1;
                if (emptyRows > 0 && width >= 4 && numericCount < Math.Max(2, width / 4))
                {
                    break;
                }

                // An all-zero numeric row keeps the table contiguous but is not a real end:
                // remember only the last row carrying a non-zero value so trailing zero-only
                // placeholder rows are trimmed while interior zero rows (e.g. a financial
                // statement's "Exceptional income = 0" line) stay inside the field.
                if (numericCount > sheet.CountZeroNumericCells(row, row, startCol, endCol))
                {
                    lastSubstantiveRow = row;
                }

                emptyRows = 0;
                continue;
            }

            emptyRows++;
            if (emptyRows >= 4)
            {
                break;
            }
        }

        return lastSubstantiveRow;
    }

    // Header-less fields sitting under another table's column header still answer to it: a block
    // spanning most of a year header's columns (e.g. per-year assumption rows far below the year
    // row) inherits it as Context, and a fragment abutting such a block's left edge inherits its
    // host's horizontals. Context-role copies keep the tables in separate groups.
    private static void InheritHorizontalContext(List<FieldCandidate> candidates, List<List<LayoutAxis>> axesPerField, AxisBuilder axisBuilder)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (axesPerField[i].Any(static axis => axis.Orientation == LayoutAxisOrientation.Horizontal))
            {
                continue;
            }

            ExcelRange range = candidates[i].Range;
            LayoutAxis? inherited = axisBuilder.Axes
                .Where(axis => axis.Orientation == LayoutAxisOrientation.Horizontal &&
                    axis.Role == LayoutAxisRole.Primary &&
                    axis.Range.TopLeft.Row < range.TopLeft.Row &&
                    axis.Range.TopLeft.Column <= range.TopLeft.Column &&
                    axis.Range.BottomRight.Column >= range.BottomRight.Column &&
                    range.Width * 2 >= axis.Range.Width)
                .MaxBy(static axis => axis.Range.TopLeft.Row);
            if (inherited is not null)
            {
                axesPerField[i].Add(axisBuilder.CreateContextCopy(inherited));
            }
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            if (axesPerField[i].Any(static axis => axis.Orientation == LayoutAxisOrientation.Horizontal))
            {
                continue;
            }

            InheritFromHost(candidates, axesPerField, axisBuilder, i);
        }
    }

    private static void InheritFromHost(List<FieldCandidate> candidates, List<List<LayoutAxis>> axesPerField, AxisBuilder axisBuilder, int fragmentIndex)
    {
        ExcelRange fragment = candidates[fragmentIndex].Range;
        for (int host = 0; host < candidates.Count; host++)
        {
            ExcelRange hostRange = candidates[host].Range;
            if (host == fragmentIndex ||
                fragment.BottomRight.Column != hostRange.TopLeft.Column - 1 ||
                fragment.TopLeft.Row < hostRange.TopLeft.Row ||
                fragment.BottomRight.Row > hostRange.BottomRight.Row)
            {
                continue;
            }

            foreach (var axis in axesPerField[host])
            {
                if (axis.Orientation == LayoutAxisOrientation.Horizontal &&
                    axis.Range.TopLeft.Column <= fragment.TopLeft.Column &&
                    axis.Range.BottomRight.Column >= fragment.BottomRight.Column)
                {
                    axesPerField[fragmentIndex].Add(axisBuilder.CreateContextCopy(axis));
                    return;
                }
            }
        }
    }

    private static bool EntersMatrixZone(int row, int startCol, int endCol, List<FieldCandidate> matrices)
    {
        foreach (var matrix in matrices)
        {
            if (row >= matrix.HeaderRow && row <= matrix.Range.BottomRight.Row &&
                startCol <= matrix.HeaderEndCol && endCol >= matrix.Range.TopLeft.Column - 1)
            {
                return true;
            }
        }

        return false;
    }

    private static List<FieldCandidate> MergeColumnAdjacentFields(List<FieldCandidate> fields, SheetFacts sheet)
    {
        var ordered = fields
            .OrderBy(static candidate => candidate.Range.TopLeft.Row)
            .ThenBy(static candidate => candidate.Range.TopLeft.Column)
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            FieldCandidate current = ordered[i];
            for (int j = i + 1; j < ordered.Count;)
            {
                FieldCandidate other = ordered[j];
                if (current.Range.TopLeft.Row == other.Range.TopLeft.Row &&
                    current.Range.BottomRight.Row == other.Range.BottomRight.Row &&
                    GapIsBridgeable(sheet, current.Range, other.Range))
                {
                    current = current with
                    {
                        Range = new ExcelRange(current.Range.TopLeft, new ExcelAddress(other.Range.BottomRight.Column, current.Range.BottomRight.Row)),
                        HeaderEndCol = Math.Max(current.HeaderEndCol, other.HeaderEndCol),
                    };
                    ordered.RemoveAt(j);
                }
                else
                {
                    j++;
                }
            }

            ordered[i] = current;
        }

        return ordered;
    }

    // Two co-extensive fields belong to one table when the columns between them all carry data
    // (a sparse internal separator); a fully empty gap column is a genuine table boundary.
    private static bool GapIsBridgeable(SheetFacts sheet, ExcelRange left, ExcelRange right)
    {
        if (right.TopLeft.Column <= left.BottomRight.Column + 1)
        {
            return right.TopLeft.Column > left.BottomRight.Column;
        }

        for (int col = left.BottomRight.Column + 1; col < right.TopLeft.Column; col++)
        {
            if (sheet.CountNumericCells(left.TopLeft.Row, left.BottomRight.Row, col, col) == 0)
            {
                return false;
            }
        }

        return true;
    }

    // Headerless blocks (e.g. stacked assumption tables) yield one single-column vector per column.
    // Merge column-adjacent vectors whose row spans overlap back into a single block so a label
    // column anchors one table instead of N disjoint columns.
    private static IEnumerable<FieldCandidate> CoalesceVectorFields(IEnumerable<FieldCandidate> vectors)
    {
        var pending = vectors
            .OrderBy(static candidate => candidate.Range.TopLeft.Row)
            .ThenBy(static candidate => candidate.Range.TopLeft.Column)
            .ToList();

        for (int i = 0; i < pending.Count; i++)
        {
            ExcelRange range = pending[i].Range;
            for (int j = i + 1; j < pending.Count;)
            {
                ExcelRange other = pending[j].Range;
                bool columnAdjacent = other.TopLeft.Column == range.BottomRight.Column + 1;
                bool rowsOverlap = other.TopLeft.Row <= range.BottomRight.Row && other.BottomRight.Row >= range.TopLeft.Row;
                if (columnAdjacent && rowsOverlap)
                {
                    range = new ExcelRange(
                        new ExcelAddress(range.TopLeft.Column, Math.Min(range.TopLeft.Row, other.TopLeft.Row)),
                        new ExcelAddress(other.BottomRight.Column, Math.Max(range.BottomRight.Row, other.BottomRight.Row)));
                    pending.RemoveAt(j);
                }
                else
                {
                    j++;
                }
            }

            yield return pending[i] with { Range = range };
        }
    }

    // One row-major pass with per-column run state: measure cells in a column chain into one
    // vector while at most one non-measure row separates them.
    private static IEnumerable<FieldCandidate> FindVectorFields(SheetFacts sheet, IReadOnlyList<ExcelRange> occupied)
    {
        var candidates = new List<FieldCandidate>();
        int width = sheet.MaxCol - sheet.MinCol + 1;
        int[] startRows = new int[width];
        int[] lastRows = new int[width];

        for (int i = 0; i < sheet.CellCount; i++)
        {
            LayoutCellFact cell = sheet.CellAt(i);
            if (!SheetFacts.IsMeasureCell(cell) || IsCellInRanges(cell.Row, cell.Column, occupied))
            {
                continue;
            }

            int col = cell.Column - sheet.MinCol;
            if (lastRows[col] != 0 && cell.Row - lastRows[col] <= 2)
            {
                lastRows[col] = cell.Row;
                continue;
            }

            AddVectorCandidate(sheet, occupied, candidates, cell.Column, startRows[col], lastRows[col]);
            startRows[col] = cell.Row;
            lastRows[col] = cell.Row;
        }

        for (int col = 0; col < width; col++)
        {
            AddVectorCandidate(sheet, occupied, candidates, sheet.MinCol + col, startRows[col], lastRows[col]);
        }

        return candidates
            .OrderBy(static candidate => candidate.Range.TopLeft.Row)
            .ThenBy(static candidate => candidate.Range.TopLeft.Column);
    }

    private static void AddVectorCandidate(
        SheetFacts sheet,
        IReadOnlyList<ExcelRange> occupied,
        List<FieldCandidate> candidates,
        int col,
        int startRow,
        int lastRow)
    {
        if (startRow == 0 || lastRow - startRow + 1 < 2)
        {
            return;
        }

        var range = new ExcelRange(new ExcelAddress(col, startRow), new ExcelAddress(col, lastRow));
        if (!OverlapsAny(range, occupied) && HasLeftAxis(sheet, range))
        {
            candidates.Add(new FieldCandidate(range, 0, col, col));
        }
    }

    private static bool HasLeftAxis(SheetFacts sheet, ExcelRange range)
    {
        int leftLimit = Math.Max(sheet.MinCol, range.TopLeft.Column - 4);
        for (int col = range.TopLeft.Column - 1; col >= leftLimit; col--)
        {
            if (sheet.CountAxisLikeCells(range.TopLeft.Row, range.BottomRight.Row, col) >= 1)
            {
                return true;
            }
        }

        return false;
    }

    private static MeasureFieldInfo CreateField(int id, ExcelRange range, IReadOnlyList<LayoutAxis> axes, SheetFacts sheet)
    {
        string[] axisIds = [.. axes.Select(static axis => axis.Id)];
        return new MeasureFieldInfo
        {
            Id = $"field{id}",
            Range = range,
            AxisIds = axisIds,
            Rank = axisIds.Length,
            Profile = sheet.BuildProfile(range),
        };
    }

    private static List<LayoutGroupInfo> BuildGroups(List<MeasureFieldInfo> fields, List<IReadOnlyList<LayoutAxis>> fieldAxes)
    {
        List<List<int>> clusters = ClusterFieldsBySharedAxis(fields.Count, fieldAxes);

        var groups = new List<LayoutGroupInfo>(clusters.Count);
        for (int g = 0; g < clusters.Count; g++)
        {
            groups.Add(CreateGroup(g + 1, clusters[g], fields, fieldAxes));
        }

        return groups;
    }

    // Fields that share at least one axis id belong to one group; union-find over field indices
    // finds those clusters transitively (field A sharing with B, and B with C, joins A and C too).
    private static List<List<int>> ClusterFieldsBySharedAxis(int fieldCount, List<IReadOnlyList<LayoutAxis>> fieldAxes)
    {
        int[] parent = [.. Enumerable.Range(0, fieldCount)];

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        var axisFirstField = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < fieldCount; i++)
        {
            foreach (var axis in fieldAxes[i])
            {
                if (axisFirstField.TryGetValue(axis.Id, out int firstIndex))
                {
                    int rootA = Find(i);
                    int rootB = Find(firstIndex);
                    if (rootA != rootB)
                    {
                        parent[rootA] = rootB;
                    }
                }
                else
                {
                    axisFirstField[axis.Id] = i;
                }
            }
        }

        var clustersByRoot = new Dictionary<int, List<int>>();
        var clusterOrder = new List<List<int>>();
        for (int i = 0; i < fieldCount; i++)
        {
            int root = Find(i);
            if (!clustersByRoot.TryGetValue(root, out var cluster))
            {
                cluster = [];
                clustersByRoot[root] = cluster;
                clusterOrder.Add(cluster);
            }

            cluster.Add(i);
        }

        return clusterOrder;
    }

    private static LayoutGroupInfo CreateGroup(int id, List<int> members, List<MeasureFieldInfo> fields, List<IReadOnlyList<LayoutAxis>> fieldAxes)
    {
        ExcelRange range = fields[members[0]].Range;
        var axisIds = new List<string>();
        var seenAxisIds = new HashSet<string>(StringComparer.Ordinal);
        var measureFieldIds = new List<string>(members.Count);
        foreach (int index in members)
        {
            range = Union(range, fields[index].Range);
            foreach (var axis in fieldAxes[index])
            {
                range = Union(range, axis.Range);
                if (seenAxisIds.Add(axis.Id))
                {
                    axisIds.Add(axis.Id);
                }
            }

            measureFieldIds.Add(fields[index].Id);
        }

        return new LayoutGroupInfo
        {
            Id = $"group{id}",
            Range = range,
            AxisIds = axisIds,
            MeasureFieldIds = measureFieldIds,
        };
    }

    private static bool OverlapsAny(ExcelRange range, IReadOnlyList<ExcelRange> ranges)
    {
        foreach (var existing in ranges)
        {
            if (Overlaps(range, existing))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Overlaps(ExcelRange left, ExcelRange right) =>
        left.TopLeft.Row <= right.BottomRight.Row &&
        left.BottomRight.Row >= right.TopLeft.Row &&
        left.TopLeft.Column <= right.BottomRight.Column &&
        left.BottomRight.Column >= right.TopLeft.Column;

    private static bool IsCellInRanges(int row, int col, IReadOnlyList<ExcelRange> ranges)
    {
        var address = new ExcelAddress(col, row);
        foreach (var range in ranges)
        {
            if (range.Contains(address))
            {
                return true;
            }
        }

        return false;
    }

    private static ExcelRange Union(ExcelRange left, ExcelRange right) =>
        new(
            new ExcelAddress(Math.Min(left.TopLeft.Column, right.TopLeft.Column), Math.Min(left.TopLeft.Row, right.TopLeft.Row)),
            new ExcelAddress(Math.Max(left.BottomRight.Column, right.BottomRight.Column), Math.Max(left.BottomRight.Row, right.BottomRight.Row)));

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct FieldCandidate(ExcelRange Range, int HeaderRow, int HeaderStartCol, int HeaderEndCol);

    private sealed class AxisBuilder(SheetFacts sheet, List<ExcelRange> fieldRanges)
    {
        private readonly Dictionary<AxisKey, LayoutAxis> _axesByKey = [];
        private int _nextAxisId;

        public IReadOnlyList<LayoutAxis> Axes => [.. _axesByKey.Values.OrderBy(static axis => axis.Range.TopLeft.Row).ThenBy(static axis => axis.Range.TopLeft.Column)];

        public List<LayoutAxis> GetAxes(FieldCandidate field)
        {
            var axes = new List<LayoutAxis>(3);
            var range = field.Range;
            int? mainVerticalColumn = FindMainVerticalAxis(range);
            if (mainVerticalColumn is { } mainCol)
            {
                axes.Add(GetOrCreate(LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary, mainCol, range.TopLeft.Row, mainCol, range.BottomRight.Row));

                for (int col = mainCol + 1; col < range.TopLeft.Column; col++)
                {
                    // Year-like/date numerics are coordinates, not data, so they don't disqualify
                    // a context column (e.g. a peeled "Year" column beside the label column).
                    int axisLikeCount = sheet.CountAxisLikeCells(range.TopLeft.Row, range.BottomRight.Row, col);
                    int measureCount = sheet.CountMeasureCells(range.TopLeft.Row, range.BottomRight.Row, col);
                    int minContextCount = Math.Max(2, range.Height / 3);
                    if (axisLikeCount >= minContextCount && measureCount == 0)
                    {
                        axes.Add(GetOrCreate(LayoutAxisOrientation.Vertical, LayoutAxisRole.Context, col, range.TopLeft.Row, col, range.BottomRight.Row));
                    }
                }
            }

            if (field.HeaderRow > 0)
            {
                axes.Add(GetOrCreate(
                    LayoutAxisOrientation.Horizontal,
                    LayoutAxisRole.Primary,
                    field.HeaderStartCol,
                    field.HeaderRow,
                    field.HeaderEndCol,
                    field.HeaderRow));
            }

            return axes;
        }

        // Label columns are searched leftward, but another field's columns are data, not labels:
        // a co-extensive sibling (identical row span, e.g. a CAGR block beside its statement) may
        // be crossed to reach a shared label column — unless a nearer candidate already scored —
        // while a field with a different row span marks unrelated table territory and stops the
        // scan outright.
        private int? FindMainVerticalAxis(ExcelRange range)
        {
            int bestCol = 0;
            int bestScore = 0;
            for (int col = range.TopLeft.Column - 1; col >= sheet.MinCol; col--)
            {
                switch (ClassifyColumnZone(col, range))
                {
                    case ColumnZone.OtherTable:
                        return bestCol == 0 ? null : bestCol;
                    case ColumnZone.Sibling when bestScore > 0:
                        return bestCol;
                    case ColumnZone.Sibling:
                        continue;
                }

                int score = sheet.GetAxisScore(range.TopLeft.Row, range.BottomRight.Row, col);
                if (score == 0)
                {
                    continue;
                }

                if (score > bestScore || (score == bestScore && col < bestCol))
                {
                    bestCol = col;
                    bestScore = score;
                }
            }

            return bestCol == 0 ? null : bestCol;
        }

        private ColumnZone ClassifyColumnZone(int col, ExcelRange fieldRange)
        {
            foreach (var other in fieldRanges)
            {
                if (other == fieldRange ||
                    col < other.TopLeft.Column || col > other.BottomRight.Column ||
                    other.TopLeft.Row > fieldRange.BottomRight.Row || other.BottomRight.Row < fieldRange.TopLeft.Row)
                {
                    continue;
                }

                bool sameRows = other.TopLeft.Row == fieldRange.TopLeft.Row &&
                    other.BottomRight.Row == fieldRange.BottomRight.Row;

                // A field abutting this field's left edge with a contained row span is a fragment
                // of the same table (a sliver column the segmenter split off), not foreign territory.
                bool adjacentFragment = other.BottomRight.Column == fieldRange.TopLeft.Column - 1 &&
                    other.TopLeft.Row >= fieldRange.TopLeft.Row &&
                    other.BottomRight.Row <= fieldRange.BottomRight.Row;

                return sameRows || adjacentFragment ? ColumnZone.Sibling : ColumnZone.OtherTable;
            }

            return ColumnZone.None;
        }

        /// <summary>A Context-role axis over the same range as <paramref name="source"/>, for fields inheriting it.</summary>
        public LayoutAxis CreateContextCopy(LayoutAxis source) =>
            GetOrCreate(
                source.Orientation,
                LayoutAxisRole.Context,
                source.Range.TopLeft.Column,
                source.Range.TopLeft.Row,
                source.Range.BottomRight.Column,
                source.Range.BottomRight.Row);

        private LayoutAxis GetOrCreate(
            LayoutAxisOrientation orientation,
            LayoutAxisRole role,
            int startCol,
            int startRow,
            int endCol,
            int endRow)
        {
            var key = new AxisKey(orientation, role, startCol, startRow, endCol, endRow);
            if (_axesByKey.TryGetValue(key, out var axis))
            {
                return axis;
            }

            var range = new ExcelRange(new ExcelAddress(startCol, startRow), new ExcelAddress(endCol, endRow));
            axis = new LayoutAxis
            {
                Id = $"axis{++_nextAxisId}",
                Orientation = orientation,
                Role = role,
                ValueKind = sheet.GetAxisKind(range),
                Range = range,
                Coverage = orientation == LayoutAxisOrientation.Vertical ? range.Height : range.Width,
                Samples = sheet.GetSamples(range),
            };
            _axesByKey.Add(key, axis);
            return axis;
        }
    }

    private enum ColumnZone
    {
        None,
        Sibling,
        OtherTable,
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct AxisKey(
        LayoutAxisOrientation Orientation,
        LayoutAxisRole Role,
        int StartCol,
        int StartRow,
        int EndCol,
        int EndRow);

    // Query layer over the chunked LayoutCellStore built during the scan: the store's CSR row
    // index and numeric prefix counts make rectangle and point queries a binary search per row.
    private sealed class SheetFacts
    {
        private readonly LayoutCellStore _store;
        private readonly List<int> _rowNumbers;

        private SheetFacts(LayoutCellStore store)
        {
            _store = store;
            _rowNumbers = store.RowNumbers;
        }

        public List<int> Rows => _rowNumbers;
        public int MaxRow => _rowNumbers[^1];
        public int MinCol => _store.MinCol;
        public int MaxCol => _store.MaxCol;
        public int CellCount => _store.Count;

        public LayoutCellFact CellAt(int index) => _store[index];

        public static SheetFacts From(LayoutCellStore cells) =>
            new(cells.IsSorted ? cells : cells.Rebuilt());

        /// <summary>Cell-index bounds [Lo, Hi) of the row at <paramref name="rowIndex"/> into <see cref="Rows"/>.</summary>
        public (int Lo, int Hi) RowSegment(int rowIndex) =>
            (_store.RowStartAt(rowIndex), _store.RowStartAt(rowIndex + 1));

        public bool TryGetCell(int row, int col, out LayoutCellFact cell)
        {
            int rowIndex = _rowNumbers.BinarySearch(row);
            if (rowIndex >= 0)
            {
                int rowEnd = _store.RowStartAt(rowIndex + 1);
                int i = ColumnLowerBound(_store.RowStartAt(rowIndex), rowEnd, col);
                if (i < rowEnd && _store[i].Column == col)
                {
                    cell = _store[i];
                    return true;
                }
            }

            cell = default;
            return false;
        }

        // Index into _rowNumbers of the first row >= row, or _rowNumbers.Count.
        private int FirstRowIndexAtOrAfter(int row)
        {
            int index = _rowNumbers.BinarySearch(row);
            return index >= 0 ? index : ~index;
        }

        // First cell index in [lo, hi) whose column is >= col, or hi.
        private int ColumnLowerBound(int lo, int hi, int col)
        {
            while (lo < hi)
            {
                int mid = (lo + hi) >>> 1;
                if (_store[mid].Column < col)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }

        public static bool IsMeasureCell(LayoutCellFact cell) =>
            cell.HasNumericValue &&
            (cell.KindMask & (LayoutKindMask.YearLikeNumber | LayoutKindMask.Date)) == LayoutKindMask.None;

        /// <summary>Whether the column's numerics in the row span are exclusively year-like or date values (at least two).</summary>
        public bool IsYearLikeColumn(int startRow, int endRow, int col)
        {
            int yearLike = 0;
            for (int r = FirstRowIndexAtOrAfter(startRow); r < _rowNumbers.Count && _rowNumbers[r] <= endRow; r++)
            {
                int rowEnd = _store.RowStartAt(r + 1);
                int i = ColumnLowerBound(_store.RowStartAt(r), rowEnd, col);
                if (i >= rowEnd || _store[i].Column != col)
                {
                    continue;
                }

                if (IsMeasureCell(_store[i]))
                {
                    return false;
                }

                if (_store[i].HasNumericValue)
                {
                    yearLike++;
                }
            }

            return yearLike >= 2;
        }

        public int CountMeasureCells(int startRow, int endRow, int col)
        {
            int count = 0;
            for (int r = FirstRowIndexAtOrAfter(startRow); r < _rowNumbers.Count && _rowNumbers[r] <= endRow; r++)
            {
                int rowEnd = _store.RowStartAt(r + 1);
                int i = ColumnLowerBound(_store.RowStartAt(r), rowEnd, col);
                if (i < rowEnd && _store[i].Column == col && IsMeasureCell(_store[i]))
                {
                    count++;
                }
            }

            return count;
        }

        public IEnumerable<(int StartCol, int EndCol)> GetHeaderRuns(int row)
        {
            int rowIndex = _rowNumbers.BinarySearch(row);
            if (rowIndex < 0)
            {
                yield break;
            }

            int start = 0;
            int end = 0;
            for (int i = _store.RowStartAt(rowIndex); i < _store.RowStartAt(rowIndex + 1); i++)
            {
                LayoutCellFact cell = _store[i];
                if (!cell.IsHeaderLike)
                {
                    if (start != 0)
                    {
                        yield return (start, end);
                        start = 0;
                    }

                    continue;
                }

                if (start == 0 || cell.Column > end + 1)
                {
                    if (start != 0)
                    {
                        yield return (start, end);
                    }

                    start = cell.Column;
                }

                end = cell.Column;
            }

            if (start != 0)
            {
                yield return (start, end);
            }
        }

        // A section boundary is a reprinted header band that spans a substantial part of the field
        // width (e.g. the year row above each statement), not a stray 2-cell run from one label cell
        // plus a fresh-coordinate blip inside a sub-table.
        public bool IsHeaderRowForRun(int row, int startCol, int endCol)
        {
            int width = endCol - startCol + 1;
            int required = Math.Max(2, width / 2);
            foreach ((int start, int end) in GetHeaderRuns(row))
            {
                if (Math.Min(end, endCol) - Math.Max(start, startCol) + 1 >= required)
                {
                    return true;
                }
            }

            return false;
        }

        public int CountNumericCells(int startRow, int endRow, int startCol, int endCol)
        {
            int count = 0;
            for (int r = FirstRowIndexAtOrAfter(startRow); r < _rowNumbers.Count && _rowNumbers[r] <= endRow; r++)
            {
                int rowEnd = _store.RowStartAt(r + 1);
                int lo = ColumnLowerBound(_store.RowStartAt(r), rowEnd, startCol);
                int hi = ColumnLowerBound(lo, rowEnd, endCol + 1);
                count += _store.NumericCountBefore(hi) - _store.NumericCountBefore(lo);
            }

            return count;
        }

        public int CountZeroNumericCells(int startRow, int endRow, int startCol, int endCol)
        {
            int count = 0;
            for (int r = FirstRowIndexAtOrAfter(startRow); r < _rowNumbers.Count && _rowNumbers[r] <= endRow; r++)
            {
                int rowStart = _store.RowStartAt(r);
                int hi = ColumnLowerBound(rowStart, _store.RowStartAt(r + 1), endCol + 1);
                for (int i = ColumnLowerBound(rowStart, hi, startCol); i < hi; i++)
                {
                    LayoutCellFact cell = _store[i];
                    if (cell.HasNumericValue && cell.NumericValue == 0)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        public int CountAxisLikeCells(int startRow, int endRow, int col)
        {
            int count = 0;
            for (int r = FirstRowIndexAtOrAfter(startRow); r < _rowNumbers.Count && _rowNumbers[r] <= endRow; r++)
            {
                int rowEnd = _store.RowStartAt(r + 1);
                int i = ColumnLowerBound(_store.RowStartAt(r), rowEnd, col);
                if (i < rowEnd && _store[i].Column == col &&
                    (_store[i].KindMask & (LayoutKindMask.Text | LayoutKindMask.Number | LayoutKindMask.Date | LayoutKindMask.YearLikeNumber)) != LayoutKindMask.None)
                {
                    count++;
                }
            }

            return count;
        }

        public int GetAxisScore(int startRow, int endRow, int col)
        {
            int score = 0;
            for (int r = FirstRowIndexAtOrAfter(startRow); r < _rowNumbers.Count && _rowNumbers[r] <= endRow; r++)
            {
                int rowEnd = _store.RowStartAt(r + 1);
                int i = ColumnLowerBound(_store.RowStartAt(r), rowEnd, col);
                if (i >= rowEnd || _store[i].Column != col)
                {
                    continue;
                }

                LayoutCellFact cell = _store[i];
                if ((cell.KindMask & LayoutKindMask.Text) != LayoutKindMask.None)
                {
                    score += 3;
                }
                else if ((cell.KindMask & (LayoutKindMask.Number | LayoutKindMask.Date | LayoutKindMask.YearLikeNumber)) != LayoutKindMask.None)
                {
                    score++;
                }
            }

            return score;
        }

        // Cell-index segments (one per occupied row) covering the given rectangle.
        private IEnumerable<(int Lo, int Hi)> GetSegments(ExcelRange range)
        {
            for (int r = FirstRowIndexAtOrAfter(range.TopLeft.Row); r < _rowNumbers.Count && _rowNumbers[r] <= range.BottomRight.Row; r++)
            {
                int rowEnd = _store.RowStartAt(r + 1);
                int lo = ColumnLowerBound(_store.RowStartAt(r), rowEnd, range.TopLeft.Column);
                int hi = ColumnLowerBound(lo, rowEnd, range.BottomRight.Column + 1);
                if (lo < hi)
                {
                    yield return (lo, hi);
                }
            }
        }

        public MeasureFieldProfile BuildProfile(ExcelRange range)
        {
            var profile = new ProfileAccumulator();
            foreach ((int lo, int hi) in GetSegments(range))
            {
                for (int i = lo; i < hi; i++)
                {
                    profile.Add(_store[i]);
                }
            }

            return new MeasureFieldProfile
            {
                CellCount = profile.CellCount,
                NumericCount = profile.NumericCount,
                TextCount = profile.TextCount,
                FormulaCount = profile.FormulaCount,
                MinNumeric = profile.HasNumeric ? profile.Min : null,
                MaxNumeric = profile.HasNumeric ? profile.Max : null,
            };
        }

        public LayoutAxisValueKind GetAxisKind(ExcelRange range)
        {
            int text = 0;
            int number = 0;
            int date = 0;
            foreach ((int lo, int hi) in GetSegments(range))
            {
                for (int i = lo; i < hi; i++)
                {
                    LayoutCellFact cell = _store[i];
                    if ((cell.KindMask & LayoutKindMask.Date) != LayoutKindMask.None)
                    {
                        date++;
                    }
                    else if ((cell.KindMask & (LayoutKindMask.Number | LayoutKindMask.YearLikeNumber)) != LayoutKindMask.None)
                    {
                        number++;
                    }
                    else if ((cell.KindMask & LayoutKindMask.Text) != LayoutKindMask.None)
                    {
                        text++;
                    }
                }
            }

            if (date > text && date >= number)
            {
                return LayoutAxisValueKind.Date;
            }

            if (number > text && number > 0)
            {
                return LayoutAxisValueKind.Numeric;
            }

            return text > 0 && number > 0 ? LayoutAxisValueKind.Mixed : LayoutAxisValueKind.Text;
        }

        public List<string> GetSamples(ExcelRange range)
        {
            var samples = new List<string>(3);
            foreach ((int lo, int hi) in GetSegments(range))
            {
                for (int i = lo; i < hi; i++)
                {
                    LayoutCellFact cell = _store[i];
                    if (cell.Text is { } text)
                    {
                        samples.Add(text);
                    }
                    else if ((cell.KindMask & LayoutKindMask.Date) != LayoutKindMask.None)
                    {
                        samples.Add(DateTime.FromOADate(cell.NumericValue).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    }
                    else if (cell.HasNumericValue)
                    {
                        samples.Add(cell.NumericValue.ToString("G", CultureInfo.InvariantCulture));
                    }

                    if (samples.Count >= 3)
                    {
                        return samples;
                    }
                }
            }

            return samples;
        }

        [StructLayout(LayoutKind.Auto)]
        private struct ProfileAccumulator
        {
            public int CellCount;
            public int NumericCount;
            public int TextCount;
            public int FormulaCount;
            public double Min;
            public double Max;
            public bool HasNumeric;

            public void Add(LayoutCellFact cell)
            {
                CellCount++;
                if ((cell.KindMask & LayoutKindMask.Text) != LayoutKindMask.None)
                {
                    TextCount++;
                }

                if ((cell.KindMask & LayoutKindMask.Formula) != LayoutKindMask.None)
                {
                    FormulaCount++;
                }

                if (cell.HasNumericValue)
                {
                    AddNumeric(cell.NumericValue);
                }
            }

            private void AddNumeric(double value)
            {
                NumericCount++;
                if (!HasNumeric)
                {
                    Min = value;
                    Max = value;
                    HasNumeric = true;
                    return;
                }

                Min = Math.Min(Min, value);
                Max = Math.Max(Max, value);
            }
        }
    }
}
