using System.Globalization;
using System.Runtime.InteropServices;
using XLSight.Analysis;

namespace XLSight.Internal.Sinks;

internal static class SheetLayoutInference
{
    public static SheetLayoutInfo Infer(LayoutCellStore cells, ISharedStringSource sharedStrings)
    {
        if (cells.Count == 0)
        {
            return SheetLayoutInfo.Empty;
        }

        var sheet = SheetFacts.From(cells, sharedStrings);
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
        List<ExcelRange> denseRanges = [.. dense.Select(static candidate => candidate.Range)];
        occupied.AddRange(denseRanges);

        var vector = CoalesceVectorFields(FindVectorFields(sheet, occupied)).ToList();

        var candidates = new List<FieldCandidate>(dense.Count + matrices.Count + vector.Count);
        candidates.AddRange(dense);
        candidates.AddRange(matrices);
        candidates.AddRange(vector);
        List<ExcelRange> fieldRanges = [.. candidates.Select(static candidate => candidate.Range)];
        var axisBuilder = new AxisBuilder(sheet, fieldRanges);
        return BuildLayout(sheet, axisBuilder, candidates);
    }

    private static SheetLayoutInfo BuildLayout(SheetFacts sheet, AxisBuilder axisBuilder, List<FieldCandidate> candidates)
    {
        var detectedFields = new List<DetectedField>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var detected = new DetectedField(candidate);
            detected.Axes.AddRange(axisBuilder.GetAxes(candidate));
            detectedFields.Add(detected);
        }

        InheritHorizontalContext(detectedFields, axisBuilder);
        axisBuilder.AttachSections(detectedFields);

        var fields = new List<MeasureFieldInfo>(candidates.Count);
        for (int i = 0; i < detectedFields.Count; i++)
        {
            DetectedField detected = detectedFields[i];
            detected.Field = CreateField(i + 1, detected.Candidate.Range, detected.Axes, sheet);
            fields.Add(detected.Field);
        }

        return new SheetLayoutInfo
        {
            Axes = axisBuilder.SnapshotAxes(),
            MeasureFields = fields,
            Groups = BuildGroups(detectedFields, sheet),
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

        // A genuine sensitivity matrix has numeric coordinates instead of a text header; if a
        // text header band already spans this run one row up, it's an ordinary table's data row.
        if (headerRow - 1 >= 1 && sheet.IsHeaderRowForRun(headerRow - 1, startCol, endCol))
        {
            return null;
        }

        // A standalone matrix always sits under a title or blank separator row; a uniform-stepped
        // forecast row embedded in a larger table (e.g. CAGR-driven model output that happens to
        // step near-uniformly) sits flush under that table's own data instead. If the row above
        // already carries real measure cells across the run's full span — including the
        // corner/coordinate column — this isn't a fresh matrix start, so reject it.
        // ponytail: flush-stacked matrices (no title/blank row between two tables) are a known
        // ceiling this heuristic can't resolve; it only catches the separator-row signal's absence.
        if (headerRow - 1 >= 1 && sheet.ScanRowContent(headerRow - 1, coordinateCol, endCol).HasMeasure)
        {
            return null;
        }

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
        if (!sheet.TryGetCell(lastRow, col, out var current))
        {
            return lastRow;
        }

        while (sheet.TryGetCell(lastRow + 1, col, out var next) && SheetFacts.IsMeasureCell(next))
        {
            double delta = next.NumericValue - current.NumericValue;
            if (delta == 0 || (firstDelta is { } reference && !IsUniformStep(delta, reference)))
            {
                break;
            }

            firstDelta ??= delta;
            current = next;
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
            // The leftmost sibling owns the table length; right-side blocks are aligned to it.
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
            // A row that is itself a header band for this run (e.g. a year row reprinted under
            // a band header) belongs to the nearer header's own table, not this one as data.
            if (sheet.IsFullHeaderBandForRun(row, startCol, endCol))
            {
                return 0;
            }

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
                // After a blank gap, a thin numeric straggler is treated as a footnote, not body.
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
    private static void InheritHorizontalContext(List<DetectedField> fields, AxisBuilder axisBuilder)
    {
        IReadOnlyList<LayoutAxis> axes = axisBuilder.SnapshotAxes();
        foreach (var field in fields)
        {
            if (field.Axes.Any(static axis => axis.Orientation == LayoutAxisOrientation.Horizontal))
            {
                continue;
            }

            ExcelRange range = field.Candidate.Range;
            LayoutAxis? inherited = axes
                .Where(axis => axis.Orientation == LayoutAxisOrientation.Horizontal &&
                    axis.Role == LayoutAxisRole.Primary &&
                    axis.Range.TopLeft.Row < range.TopLeft.Row &&
                    axis.Range.TopLeft.Column <= range.TopLeft.Column &&
                    axis.Range.BottomRight.Column >= range.BottomRight.Column &&
                    range.Width * 2 >= axis.Range.Width)
                .MaxBy(static axis => axis.Range.TopLeft.Row);
            if (inherited is not null)
            {
                field.Axes.Add(axisBuilder.CreateContextCopy(inherited));
            }
        }

        for (int i = 0; i < fields.Count; i++)
        {
            if (fields[i].Axes.Any(static axis => axis.Orientation == LayoutAxisOrientation.Horizontal))
            {
                continue;
            }

            InheritFromHost(fields, axisBuilder, i);
        }
    }

    private static void InheritFromHost(List<DetectedField> fields, AxisBuilder axisBuilder, int fragmentIndex)
    {
        ExcelRange fragment = fields[fragmentIndex].Candidate.Range;
        for (int host = 0; host < fields.Count; host++)
        {
            ExcelRange hostRange = fields[host].Candidate.Range;
            if (host == fragmentIndex ||
                fragment.BottomRight.Column != hostRange.TopLeft.Column - 1 ||
                fragment.TopLeft.Row < hostRange.TopLeft.Row ||
                fragment.BottomRight.Row > hostRange.BottomRight.Row)
            {
                continue;
            }

            foreach (var axis in fields[host].Axes)
            {
                if (axis.Orientation == LayoutAxisOrientation.Horizontal &&
                    axis.Range.TopLeft.Column <= fragment.TopLeft.Column &&
                    axis.Range.BottomRight.Column >= fragment.BottomRight.Column)
                {
                    fields[fragmentIndex].Axes.Add(axisBuilder.CreateContextCopy(axis));
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
                    GapIsBridgeable(sheet, current.Range, other.Range) &&
                    !HasDifferentSpanFieldBetween(ordered, current.Range, other.Range))
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

    private static bool HasDifferentSpanFieldBetween(List<FieldCandidate> fields, ExcelRange left, ExcelRange right)
    {
        int gapStartCol = left.BottomRight.Column + 1;
        int gapEndCol = right.TopLeft.Column - 1;
        if (gapStartCol > gapEndCol)
        {
            return false;
        }

        foreach (var field in fields)
        {
            ExcelRange range = field.Range;
            if (range == left || range == right)
            {
                continue;
            }

            bool columnsInGap = range.TopLeft.Column <= gapEndCol && range.BottomRight.Column >= gapStartCol;
            bool rowsOverlap = range.TopLeft.Row <= left.BottomRight.Row && range.BottomRight.Row >= left.TopLeft.Row;
            bool sameRows = range.TopLeft.Row == left.TopLeft.Row && range.BottomRight.Row == left.BottomRight.Row;
            if (columnsInGap && rowsOverlap && !sameRows)
            {
                return true;
            }
        }

        return false;
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
    // vector while at most one non-measure row separates them. `occupied` is fixed for the
    // whole pass, so each row's covering column intervals are computed once as the row starts
    // and then swept with a cursor, since columns strictly ascend within a row.
    private static IEnumerable<FieldCandidate> FindVectorFields(SheetFacts sheet, List<ExcelRange> occupied)
    {
        var candidates = new List<FieldCandidate>();
        int width = sheet.MaxCol - sheet.MinCol + 1;
        int[] startRows = new int[width];
        int[] lastRows = new int[width];
        var rowIntervals = new List<(int Start, int End)>();
        int currentRow = int.MinValue;
        int intervalCursor = 0;

        for (int i = 0; i < sheet.CellCount; i++)
        {
            LayoutCellFact cell = sheet.CellAt(i);
            if (cell.Row != currentRow)
            {
                currentRow = cell.Row;
                BuildRowIntervals(occupied, currentRow, rowIntervals);
                intervalCursor = 0;
            }

            while (intervalCursor < rowIntervals.Count && rowIntervals[intervalCursor].End < cell.Column)
            {
                intervalCursor++;
            }

            bool inOccupiedRange = intervalCursor < rowIntervals.Count && rowIntervals[intervalCursor].Start <= cell.Column;
            if (!SheetFacts.IsMeasureCell(cell) || inOccupiedRange)
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

    // Column intervals from ranges in `occupied` that cover `row`, merged and sorted ascending
    // so the caller's cursor never needs to revisit an interval once it has moved past it.
    private static void BuildRowIntervals(List<ExcelRange> occupied, int row, List<(int Start, int End)> buffer)
    {
        buffer.Clear();
        foreach (var range in occupied)
        {
            if (row >= range.TopLeft.Row && row <= range.BottomRight.Row)
            {
                buffer.Add((range.TopLeft.Column, range.BottomRight.Column));
            }
        }

        if (buffer.Count <= 1)
        {
            return;
        }

        buffer.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        int write = 0;
        for (int read = 1; read < buffer.Count; read++)
        {
            if (buffer[read].Start <= buffer[write].End + 1)
            {
                if (buffer[read].End > buffer[write].End)
                {
                    buffer[write] = (buffer[write].Start, buffer[read].End);
                }
            }
            else
            {
                write++;
                buffer[write] = buffer[read];
            }
        }

        buffer.RemoveRange(write + 1, buffer.Count - write - 1);
    }

    private static void AddVectorCandidate(
        SheetFacts sheet,
        List<ExcelRange> occupied,
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

    private static List<LayoutGroupInfo> BuildGroups(List<DetectedField> fields, SheetFacts sheet)
    {
        List<List<int>> clusters = ClusterFieldsBySharedAxis(fields);

        var groups = new List<LayoutGroupInfo>(clusters.Count);
        for (int g = 0; g < clusters.Count; g++)
        {
            List<int> cluster = clusters[g];
            bool hasAxis = false;
            foreach (int index in cluster)
            {
                if (fields[index].Axes.Count > 0)
                {
                    hasAxis = true;
                    break;
                }
            }

            if (hasAxis)
            {
                groups.Add(CreateGroup(groups.Count + 1, cluster, fields, sheet));
            }
        }

        return groups;
    }

    // A group's title is a lone text cell on an otherwise data-free row directly above it (up
    // to three rows up, so one blank spacer row is fine). Rows with several text cells are
    // header bands, not titles, and a row carrying measure data means another table sits
    // directly above — stop looking.
    private static string? FindGroupTitle(SheetFacts sheet, ExcelRange range)
    {
        int startCol = Math.Max(1, range.TopLeft.Column - 1);
        for (int row = range.TopLeft.Row - 1; row >= Math.Max(1, range.TopLeft.Row - 3); row--)
        {
            (int textCount, string? firstText, bool hasMeasure) = sheet.ScanRowContent(row, startCol, range.BottomRight.Column);
            if (hasMeasure)
            {
                return null;
            }

            if (textCount == 1 && firstText is not null)
            {
                return firstText;
            }
        }

        return null;
    }

    // Fields that share at least one axis id belong to one group; union-find over field indices
    // finds those clusters transitively (field A sharing with B, and B with C, joins A and C too).
    private static List<List<int>> ClusterFieldsBySharedAxis(List<DetectedField> fields)
    {
        int fieldCount = fields.Count;
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
            foreach (var axis in fields[i].Axes)
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

    private static LayoutGroupInfo CreateGroup(int id, List<int> members, List<DetectedField> fields, SheetFacts sheet)
    {
        ExcelRange range = fields[members[0]].Field.Range;
        var axisIds = new List<string>();
        var seenAxisIds = new HashSet<string>(StringComparer.Ordinal);
        var measureFieldIds = new List<string>(members.Count);
        foreach (int index in members)
        {
            DetectedField field = fields[index];
            range = Union(range, field.Field.Range);
            foreach (var axis in field.Axes)
            {
                range = Union(range, axis.Range);
                if (seenAxisIds.Add(axis.Id))
                {
                    axisIds.Add(axis.Id);
                }
            }

            measureFieldIds.Add(field.Field.Id);
        }

        return new LayoutGroupInfo
        {
            Id = $"group{id}",
            Title = FindGroupTitle(sheet, range),
            Range = range,
            AxisIds = axisIds,
            MeasureFieldIds = measureFieldIds,
        };
    }

    private static bool OverlapsAny(ExcelRange range, List<ExcelRange> ranges)
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

    private static ExcelRange Union(ExcelRange left, ExcelRange right) =>
        new(
            new ExcelAddress(Math.Min(left.TopLeft.Column, right.TopLeft.Column), Math.Min(left.TopLeft.Row, right.TopLeft.Row)),
            new ExcelAddress(Math.Max(left.BottomRight.Column, right.BottomRight.Column), Math.Max(left.BottomRight.Row, right.BottomRight.Row)));

    // HeaderRow == 0 marks header-less vector fields; HeaderStartCol/HeaderEndCol are the
    // horizontal header span for dense fields, matrix coordinate span for matrices, or own column for vectors.
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct FieldCandidate(ExcelRange Range, int HeaderRow, int HeaderStartCol, int HeaderEndCol);

    private sealed class DetectedField(FieldCandidate candidate)
    {
        public FieldCandidate Candidate { get; } = candidate;
        public List<LayoutAxis> Axes { get; } = [];
        public MeasureFieldInfo Field { get; set; } = null!;
    }

    private sealed class AxisBuilder(SheetFacts sheet, List<ExcelRange> fieldRanges)
    {
        private readonly Dictionary<AxisKey, LayoutAxis> _axesByKey = [];
        private int _nextAxisId;

        // A method, not a property: axes are still being added/mutated between call sites, so
        // each call allocates and sorts a fresh snapshot rather than caching a stale one.
        public IReadOnlyList<LayoutAxis> SnapshotAxes() =>
            [.. _axesByKey.Values.OrderBy(static axis => axis.Range.TopLeft.Row).ThenBy(static axis => axis.Range.TopLeft.Column)];

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

                // The scan moves right-to-left; ties deliberately keep walking to the leftmost label column.
                if (score >= bestScore)
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

        // A section header is an axis-column text row with no measure data across the attached
        // fields' columns — exactly the rows the field detection skipped over. The scan starts
        // two rows above the axis so a header directly above the first labeled row still counts.
        public void AttachSections(List<DetectedField> fields)
        {
            var spans = new Dictionary<string, (int MinCol, int MaxCol)>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                foreach (var axis in field.Axes)
                {
                    if (axis.Orientation != LayoutAxisOrientation.Vertical || axis.Role != LayoutAxisRole.Primary)
                    {
                        continue;
                    }

                    ExcelRange range = field.Candidate.Range;
                    spans[axis.Id] = spans.TryGetValue(axis.Id, out (int MinCol, int MaxCol) span)
                        ? (Math.Min(span.MinCol, range.TopLeft.Column), Math.Max(span.MaxCol, range.BottomRight.Column))
                        : (range.TopLeft.Column, range.BottomRight.Column);
                }
            }

            foreach (var key in _axesByKey.Keys.ToList())
            {
                LayoutAxis axis = _axesByKey[key];
                if (spans.TryGetValue(axis.Id, out (int MinCol, int MaxCol) span))
                {
                    List<LayoutAxisSection> sections = BuildSections(axis, span.MinCol, span.MaxCol);
                    if (sections.Count > 0)
                    {
                        _axesByKey[key] = CloneWithSections(axis, sections);
                    }
                }
            }
        }

        private List<LayoutAxisSection> BuildSections(LayoutAxis axis, int fieldStartCol, int fieldEndCol)
        {
            int col = axis.Range.TopLeft.Column;
            int bottom = axis.Range.BottomRight.Row;
            var headers = new List<(int Row, string Title)>();
            for (int row = Math.Max(1, axis.Range.TopLeft.Row - 2); row <= bottom; row++)
            {
                if (!sheet.ScanRowContent(row, fieldStartCol, fieldEndCol).HasMeasure &&
                    sheet.TryGetCell(row, col, out var cell) && sheet.GetText(cell) is { } title)
                {
                    headers.Add((row, title));
                }
            }

            var sections = new List<LayoutAxisSection>(headers.Count);
            for (int i = 0; i < headers.Count; i++)
            {
                int end = i + 1 < headers.Count ? headers[i + 1].Row - 1 : bottom;
                sections.Add(new LayoutAxisSection
                {
                    Title = headers[i].Title,
                    Range = new ExcelRange(new ExcelAddress(col, headers[i].Row), new ExcelAddress(col, end)),
                });
            }

            return sections;
        }

        private static LayoutAxis CloneWithSections(LayoutAxis axis, List<LayoutAxisSection> sections) =>
            new()
            {
                Id = axis.Id,
                Title = axis.Title,
                Orientation = axis.Orientation,
                ValueKind = axis.ValueKind,
                Role = axis.Role,
                Range = axis.Range,
                Coverage = axis.Coverage,
                Samples = axis.Samples,
                Sections = sections,
            };

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
            LayoutAxisValueKind valueKind = sheet.GetAxisKind(range);
            axis = new LayoutAxis
            {
                Id = $"axis{++_nextAxisId}",
                // A horizontal axis is a block caption's natural home (e.g. "CAGR (%)" merged
                // above a side-by-side header row) regardless of the header cells' own kind, so
                // it always probes for a title. A vertical axis only probes when it carries no
                // self-describing labels of its own (Numeric/Date) — a text label column already
                // identifies itself through its Samples, and probing above it would instead grab
                // an unrelated section header as noise.
                Title = orientation == LayoutAxisOrientation.Horizontal ||
                    valueKind is LayoutAxisValueKind.Numeric or LayoutAxisValueKind.Date
                        ? FindAxisTitle(range)
                        : null,
                Orientation = orientation,
                Role = role,
                ValueKind = valueKind,
                Range = range,
                Coverage = orientation == LayoutAxisOrientation.Vertical ? range.Height : range.Width,
                Samples = sheet.GetSamples(range),
            };
            _axesByKey.Add(key, axis);
            return axis;
        }

        // Finds the nearest text cell just before the axis's start: up to two cells above, then
        // up to two cells left (covers "WACC" over a coordinate column, "Inflation Rate" beside a
        // coordinate row, and a merged block caption like "CAGR (%)" sitting above a horizontal
        // header row that is itself text or mixed-kind).
        private string? FindAxisTitle(ExcelRange range)
        {
            int startCol = range.TopLeft.Column;
            int startRow = range.TopLeft.Row;
            Span<(int Col, int Row)> probes = [(startCol, startRow - 1), (startCol, startRow - 2), (startCol - 1, startRow), (startCol - 2, startRow)];
            foreach ((int col, int row) in probes)
            {
                if (col >= 1 && row >= 1 && sheet.TryGetCell(row, col, out var cell) && sheet.GetText(cell) is { } title)
                {
                    return title;
                }
            }

            return null;
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
        private readonly ISharedStringSource _sharedStrings;

        private SheetFacts(LayoutCellStore store, ISharedStringSource sharedStrings)
        {
            _store = store;
            _rowNumbers = store.RowNumbers;
            _sharedStrings = sharedStrings;
        }

        public List<int> Rows => _rowNumbers;
        public int MaxRow => _rowNumbers[^1];
        public int MinCol => _store.MinCol;
        public int MaxCol => _store.MaxCol;
        public int CellCount => _store.Count;

        public LayoutCellFact CellAt(int index) => _store[index];

        public static SheetFacts From(LayoutCellStore cells, ISharedStringSource sharedStrings) =>
            new(cells.IsSorted ? cells : cells.Rebuilt(), sharedStrings);

        /// <summary>Cell-index bounds [Lo, Hi) of the row at <paramref name="rowIndex"/> into <see cref="Rows"/>.</summary>
        public (int Lo, int Hi) RowSegment(int rowIndex) =>
            (_store.RowStartAt(rowIndex), _store.RowStartAt(rowIndex + 1));

        /// <summary>Text-cell count, first text sample, and measure-cell presence within one row's column span.</summary>
        public (int TextCount, string? FirstText, bool HasMeasure) ScanRowContent(int row, int startCol, int endCol)
        {
            int rowIndex = _rowNumbers.BinarySearch(row);
            if (rowIndex < 0)
            {
                return (0, null, false);
            }

            int textCount = 0;
            string? firstText = null;
            bool hasMeasure = false;
            int rowEnd = _store.RowStartAt(rowIndex + 1);
            for (int i = ColumnLowerBound(_store.RowStartAt(rowIndex), rowEnd, startCol); i < rowEnd && _store[i].Column <= endCol; i++)
            {
                LayoutCellFact cell = _store[i];
                if ((cell.KindMask & LayoutKindMask.Text) != LayoutKindMask.None)
                {
                    textCount++;
                    firstText ??= GetText(cell);
                }

                hasMeasure |= IsMeasureCell(cell);
            }

            return (textCount, firstText, hasMeasure);
        }

        public string? GetText(LayoutCellFact cell)
        {
            if ((cell.KindMask & LayoutKindMask.Text) == LayoutKindMask.None)
            {
                return null;
            }

            if (cell.SharedStringIndex < 0)
            {
                return cell.InlineText ?? string.Empty;
            }

            string text = _sharedStrings.GetString(cell.SharedStringIndex).Trim();
            if (text.Length <= 64)
            {
                return text;
            }

            int cutoff = char.IsHighSurrogate(text[63]) ? 63 : 64;
            return text[..cutoff];
        }

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
            double? previous = null;
            double runStart = 0;
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
                    double value = _store[i].NumericValue;

                    // Real year/date key columns run non-decreasing top-to-bottom, restarting
                    // at or below the first value of the previous run for each new group (e.g.
                    // per-name year ranges repeating down the sheet). Data unrelated to a key
                    // never sorts this way: it neither keeps climbing nor drops back to restart.
                    if (previous is { } last)
                    {
                        if (value < last && value > runStart)
                        {
                            return false;
                        }

                        if (value < last)
                        {
                            runStart = value;
                        }
                    }
                    else
                    {
                        runStart = value;
                    }

                    previous = value;
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

        // Stricter than IsHeaderRowForRun's half-width threshold: a reprinted header band (e.g.
        // a year row repeated under a band header) has no break, so header-like cells must cover
        // the run's entire width. A genuine first data row (leading label/year columns next to
        // real measure columns) only ever covers part of the width and must not match here.
        public bool IsFullHeaderBandForRun(int row, int startCol, int endCol)
        {
            int width = endCol - startCol + 1;
            int covered = 0;
            foreach ((int start, int end) in GetHeaderRuns(row))
            {
                covered += Math.Max(0, Math.Min(end, endCol) - Math.Max(start, startCol) + 1);
            }

            return covered == width;
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

            int total = text + number + date;
            if (date > total / 2)
            {
                return LayoutAxisValueKind.Date;
            }

            if (number > total / 2)
            {
                return LayoutAxisValueKind.Numeric;
            }

            if (text > total / 2 || total == 0)
            {
                return LayoutAxisValueKind.Text;
            }

            return LayoutAxisValueKind.Mixed;
        }

        public List<string> GetSamples(ExcelRange range)
        {
            var samples = new List<string>(3);
            foreach ((int lo, int hi) in GetSegments(range))
            {
                for (int i = lo; i < hi; i++)
                {
                    LayoutCellFact cell = _store[i];
                    if (GetText(cell) is { } text)
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
