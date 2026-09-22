using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

internal static class PositionWorkbookExporter
{
    private const int FirstDataRow = 3;
    private const int LastColumn = 11;
    private const int FirstPredictionYear = 2026;
    private const string SeedCacheResourceName = "SsqAnalyzer.Resources.position_export_cache.seed.json";
    private static readonly JsonSerializerOptions CacheJsonOptions = new() { WriteIndented = false };

    public static IReadOnlyList<PositionPrediction> GeneratePredictions(
        IReadOnlyList<DrawRecord> records,
        IProgress<int>? progress = null,
        int? predictionYear = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (records.Count < 4)
            throw new ArgumentException("历史数据至少需要4期", nameof(records));

        var snapshot = records.OrderBy(record => record.Period).ToArray();
        var predictor = new PositionPredictor(new SnapshotDataService(snapshot));
        int targetYear = ResolvePredictionYear(snapshot, predictionYear);
        int[] issues = GetPredictionIssues(snapshot, predictor, targetYear);
        int nextIssue = predictor.GetIssueOptions().First();
        var actualByIssue = snapshot.ToDictionary(record => record.Period);
        var recordIndexByIssue = snapshot.Select((record, index) => (record.Period, index))
            .ToDictionary(item => item.Period, item => item.index);
        var cachedByIssue = LoadCompatibleCache(snapshot, predictor.RuleVersionId, targetYear)
            .ToDictionary(prediction => prediction.Issue);
        var predictions = new PositionPrediction[issues.Length];
        int completed = 0;
        for (int index = 0; index < issues.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int issue = issues[index];
            int expectedAsOfIssue = issue == nextIssue
                ? snapshot[^1].Period
                : snapshot[recordIndexByIssue[issue] - 1].Period;
            predictions[index] = cachedByIssue.TryGetValue(issue, out var cached)
                && cached.AsOfIssue == expectedAsOfIssue
                    ? cached.ToPrediction(actualByIssue.GetValueOrDefault(issue))
                    : predictor.Predict(issue, "backtest");
            int current = ++completed;
            if (current % 10 == 0 || current == issues.Length)
                progress?.Report(current);
        }
        cancellationToken.ThrowIfCancellationRequested();
        TrySaveCache(snapshot, predictor.RuleVersionId, predictions, targetYear);
        return predictions;
    }

    public static int GetMissingPredictionCount(
        IReadOnlyList<DrawRecord> records,
        int? predictionYear = null)
    {
        if (records.Count < 4) return 0;

        var snapshot = records.OrderBy(record => record.Period).ToArray();
        var predictor = new PositionPredictor(new SnapshotDataService(snapshot));
        int targetYear = ResolvePredictionYear(snapshot, predictionYear);
        int nextIssue = predictor.GetIssueOptions().First();
        int[] issues = GetPredictionIssues(snapshot, predictor, targetYear);
        var cachedByIssue = LoadCompatibleCache(snapshot, predictor.RuleVersionId, targetYear)
            .ToDictionary(prediction => prediction.Issue);
        var recordIndexByIssue = snapshot.Select((record, index) => (record.Period, index))
            .ToDictionary(item => item.Period, item => item.index);

        int missing = 0;
        foreach (int issue in issues)
        {
            int expectedAsOfIssue = issue == nextIssue
                ? snapshot[^1].Period
                : snapshot[recordIndexByIssue[issue] - 1].Period;
            if (!cachedByIssue.TryGetValue(issue, out var cached) || cached.AsOfIssue != expectedAsOfIssue)
                missing++;
        }
        return missing;
    }

    public static int GetDefaultPredictionYear(IReadOnlyList<DrawRecord> records)
    {
        if (records.Count == 0)
            throw new ArgumentException("没有可用的开奖数据", nameof(records));
        return ResolvePredictionYear(
            records.OrderBy(record => record.Period).ToArray(),
            predictionYear: null);
    }

    public static int GetPredictionIssueCount(
        IReadOnlyList<DrawRecord> records,
        int? predictionYear = null)
    {
        if (records.Count < 4) return 0;
        var snapshot = records.OrderBy(record => record.Period).ToArray();
        var predictor = new PositionPredictor(new SnapshotDataService(snapshot));
        int targetYear = ResolvePredictionYear(snapshot, predictionYear);
        return GetPredictionIssues(snapshot, predictor, targetYear).Length;
    }

    private static int ResolvePredictionYear(
        IReadOnlyList<DrawRecord> snapshot,
        int? predictionYear)
    {
        int targetYear = predictionYear ?? snapshot[^1].Period / 1000;
        if (targetYear < FirstPredictionYear)
            throw new ArgumentOutOfRangeException(
                nameof(predictionYear),
                $"点位年度预测从 {FirstPredictionYear} 年开始");
        int latestAvailableYear = snapshot[^1].Period / 1000;
        if (targetYear > latestAvailableYear)
            throw new ArgumentOutOfRangeException(
                nameof(predictionYear),
                $"{targetYear} 年尚无开奖数据");
        return targetYear;
    }

    private static int[] GetPredictionIssues(
        IReadOnlyList<DrawRecord> snapshot,
        IPositionPredictor predictor,
        int targetYear)
    {
        if (snapshot[0].Period / 1000 == targetYear)
            throw new InvalidOperationException($"期号 {snapshot[0].Period} 之前没有历史数据，请先补全历史数据再导出全年预测");
        int nextIssue = predictor.GetIssueOptions().First();
        var issues = snapshot
            .Where(record => record.Period / 1000 == targetYear)
            .Select(record => record.Period)
            .ToList();
        if (nextIssue / 1000 == targetYear) issues.Add(nextIssue);
        if (issues.Count == 0)
            throw new InvalidOperationException($"{targetYear} 年没有可生成的点位期次");
        return issues.ToArray();
    }

    private static string GetCacheFilePath(int predictionYear) =>
        Path.Combine(AppPaths.Root, $"position_export_cache.{predictionYear}.json");

    public static void Export(string filePath, IReadOnlyList<PositionPrediction> predictions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (predictions.Count == 0)
            throw new ArgumentException("没有可导出的点位预测", nameof(predictions));
        using var document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = CreateStylesheet();
        stylesPart.Stylesheet.Save();

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        sheetData.Append(CreateHeaderRow(1));
        sheetData.Append(CreateHeaderRow(2));
        for (int index = 0; index < predictions.Count; index++)
            sheetData.Append(CreateDataRow((uint)(FirstDataRow + index), predictions[index]));
        int accuracyRow = FirstDataRow + predictions.Count;
        sheetData.Append(CreateAccuracyRow((uint)accuracyRow, predictions));

        var worksheet = new Worksheet(
            CreateSheetViews(),
            CreateColumns(),
            sheetData,
            CreateMergeCells());
        worksheet.Append(
            new PageMargins { Left = 0.25, Right = 0.25, Top = 0.5, Bottom = 0.5, Header = 0.2, Footer = 0.2 },
            new PageSetup { Orientation = OrientationValues.Landscape, FitToWidth = 1, FitToHeight = 0 });
        worksheetPart.Worksheet = worksheet;
        worksheetPart.Worksheet.Save();

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "点位数据"
        });
        workbookPart.Workbook.Save();
    }

    private static Row CreateHeaderRow(uint rowIndex)
    {
        var row = new Row { RowIndex = rowIndex, Height = 26, CustomHeight = true };
        var values = rowIndex == 1
            ? new Dictionary<int, string> { [1] = "期号", [2] = "开奖号", [3] = "点位", [9] = "三码围蓝" }
            : new Dictionary<int, string> { [9] = "独蓝", [10] = "两码围蓝" };
        for (int column = 1; column <= LastColumn; column++)
            row.Append(CreateCell(rowIndex, column, values.GetValueOrDefault(column, ""), 1));
        return row;
    }

    private static Row CreateDataRow(uint rowIndex, PositionPrediction prediction)
    {
        var values = BuildPredictionValues(prediction);
        var row = new Row { RowIndex = rowIndex, Height = 24, CustomHeight = true };
        for (int column = 1; column <= values.Length; column++)
        {
            uint styleIndex = column is >= 3 and <= LastColumn
                && IsPredictionHit(prediction, column - 3) ? 5U : 2U;
            row.Append(CreateCell(rowIndex, column, values[column - 1], styleIndex));
        }
        return row;
    }

    private static bool IsPredictionHit(PositionPrediction prediction, int predictionIndex)
    {
        var actual = prediction.Actual;
        if (actual is null) return false;

        return predictionIndex switch
        {
            >= 0 and < 6 => actual.RedBalls.Any(ball =>
                PositionPointRange.Contains(prediction.RedPoints[predictionIndex], ball, 33)),
            >= 6 and < 9 => BuildNestedBlueColumns(prediction)[predictionIndex - 6] == actual.BlueBall,
            _ => false
        };
    }

    private static object[] BuildPredictionValues(PositionPrediction prediction)
    {
        var actual = prediction.Actual;
        string actualNumbers = actual is null ? ""
            : $"{string.Join(' ', actual.RedBalls.Select(ball => ball.ToString("D2")))} + {actual.BlueBall:D2}";
        object[] helperValues = actual is null
            ? Enumerable.Repeat<object>("", 7).ToArray()
            : actual.RedBalls.Cast<object>().Append(actual.BlueBall).ToArray();
        return new object[] { prediction.Issue, actualNumbers }
            .Concat(prediction.RedPoints.Cast<object>())
            .Concat(BuildNestedBlueColumns(prediction).Cast<object>())
            .Concat(helperValues)
            .ToArray();
    }

    private static Row CreateAccuracyRow(
        uint rowIndex,
        IReadOnlyList<PositionPrediction> predictions)
    {
        var hits = new int[LastColumn - 2];
        int evaluated = 0;
        foreach (var prediction in predictions)
        {
            var actual = prediction.Actual;
            if (actual is null) continue;
            evaluated++;

            for (int index = 0; index < prediction.RedPoints.Count; index++)
                if (actual.RedBalls.Any(ball => PositionPointRange.Contains(prediction.RedPoints[index], ball, 33)))
                    hits[index]++;
            var blues = BuildNestedBlueColumns(prediction);
            for (int index = 0; index < blues.Length; index++)
                if (blues[index] == actual.BlueBall) hits[6 + index]++;
        }

        var row = new Row { RowIndex = rowIndex, Height = 24, CustomHeight = true };
        row.Append(CreateCell(rowIndex, 1, "正确率", 3));
        row.Append(CreateCell(rowIndex, 2, $"已开奖 {evaluated} 期", 3));
        for (int index = 0; index < hits.Length; index++)
            row.Append(CreateCell(rowIndex, index + 3,
                evaluated == 0 ? 0 : hits[index] / (double)evaluated, 4));
        return row;
    }

    private static int[] BuildNestedBlueColumns(PositionPrediction prediction)
    {
        var result = new List<int>(3) { prediction.SingleBlue };
        foreach (int ball in prediction.DoubleBlue)
            if (!result.Contains(ball)) result.Add(ball);
        foreach (int ball in prediction.TripleBlue)
            if (!result.Contains(ball)) result.Add(ball);
        if (result.Count != 3)
            throw new InvalidOperationException($"{prediction.Issue}期蓝球结果不满足严格嵌套");
        return result.ToArray();
    }

    private static Cell CreateCell(uint rowIndex, int column, object value, uint styleIndex)
    {
        var cell = new Cell
        {
            CellReference = $"{GetColumnName(column)}{rowIndex}",
            StyleIndex = styleIndex
        };
        if (value is string text)
        {
            cell.DataType = CellValues.InlineString;
            cell.InlineString = new InlineString(new Text(text)
            {
                Space = SpaceProcessingModeValues.Preserve
            });
        }
        else
        {
            cell.DataType = CellValues.Number;
            cell.CellValue = new CellValue(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0");
        }
        return cell;
    }

    private static string GetColumnName(int column)
    {
        string result = "";
        while (column > 0)
        {
            column--;
            result = (char)('A' + column % 26) + result;
            column /= 26;
        }
        return result;
    }

    private static SheetViews CreateSheetViews()
    {
        var view = new SheetView { WorkbookViewId = 0, ShowGridLines = true };
        view.Append(new Pane
        {
            HorizontalSplit = 2,
            VerticalSplit = 2,
            TopLeftCell = "C3",
            ActivePane = PaneValues.BottomRight,
            State = PaneStateValues.Frozen
        });
        return new SheetViews(view);
    }

    private static Columns CreateColumns() => new(
        new Column { Min = 1, Max = 1, Width = 12, CustomWidth = true },
        new Column { Min = 2, Max = 2, Width = 25, CustomWidth = true },
        new Column { Min = 3, Max = 8, Width = 8, CustomWidth = true },
        new Column { Min = 9, Max = 9, Width = 9, CustomWidth = true },
        new Column { Min = 10, Max = 11, Width = 10, CustomWidth = true },
        new Column { Min = 12, Max = 18, Width = 2, CustomWidth = true, Hidden = true });

    private static MergeCells CreateMergeCells() => new(
        new MergeCell { Reference = "A1:A2" },
        new MergeCell { Reference = "B1:B2" },
        new MergeCell { Reference = "C1:H2" },
        new MergeCell { Reference = "I1:K1" },
        new MergeCell { Reference = "J2:K2" });

    private static Stylesheet CreateStylesheet()
    {
        var fonts = new Fonts(
            CreateFont(12, false, "FF000000"),
            CreateFont(13, true, "FF000000"),
            CreateFont(12, true, "FF000000"),
            CreateFont(12, true, "FFFFFFFF")) { Count = 4 };
        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            CreateSolidFill("FFF2F2F2"),
            CreateSolidFill("FFFFF2CC"),
            CreateSolidFill("FFFF0000")) { Count = 5 };
        var borders = new Borders(
            new Border(),
            CreateThinBorder("FF000000"),
            CreateAccuracyBorder(),
            CreateThinBorder("FFD9D9D9")) { Count = 4 };
        var cellStyleFormats = new CellStyleFormats(new CellFormat()) { Count = 1 };
        var cellFormats = new CellFormats(
            new CellFormat { FontId = 0, FillId = 0, BorderId = 0 },
            CenteredFormat(1, 2, 1),
            CenteredFormat(0, 0, 3),
            CenteredFormat(2, 3, 2),
            CenteredFormat(2, 3, 2, 10),
            CenteredFormat(3, 4, 3)) { Count = 6 };
        var cellStyles = new CellStyles(
            new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 };
        return new Stylesheet(
            fonts,
            fills,
            borders,
            cellStyleFormats,
            cellFormats,
            cellStyles,
            new TableStyles { Count = 0, DefaultTableStyle = "TableStyleMedium2", DefaultPivotStyle = "PivotStyleLight16" });
    }

    private static Font CreateFont(double size, bool bold, string color)
    {
        var font = new Font(new FontSize { Val = size }, new Color { Rgb = color }, new FontName { Val = "Microsoft YaHei" });
        if (bold) font.PrependChild(new Bold());
        return font;
    }

    private static Fill CreateSolidFill(string color) => new(
        new PatternFill(
            new ForegroundColor { Rgb = color },
            new BackgroundColor { Indexed = 64 })
        { PatternType = PatternValues.Solid });

    private static Border CreateThinBorder(string rgb)
    {
        var color = new Color { Rgb = rgb };
        return new Border(
            new LeftBorder((Color)color.CloneNode(true)) { Style = BorderStyleValues.Thin },
            new RightBorder((Color)color.CloneNode(true)) { Style = BorderStyleValues.Thin },
            new TopBorder((Color)color.CloneNode(true)) { Style = BorderStyleValues.Thin },
            new BottomBorder((Color)color.CloneNode(true)) { Style = BorderStyleValues.Thin });
    }

    private static Border CreateAccuracyBorder()
    {
        var light = new Color { Rgb = "FFD9D9D9" };
        return new Border(
            new LeftBorder((Color)light.CloneNode(true)) { Style = BorderStyleValues.Thin },
            new RightBorder((Color)light.CloneNode(true)) { Style = BorderStyleValues.Thin },
            new TopBorder(new Color { Rgb = "FF000000" }) { Style = BorderStyleValues.Medium },
            new BottomBorder((Color)light.CloneNode(true)) { Style = BorderStyleValues.Thin });
    }

    private static CellFormat CenteredFormat(uint fontId, uint fillId, uint borderId, uint? numberFormatId = null) => new()
    {
        FontId = fontId,
        FillId = fillId,
        BorderId = borderId,
        NumberFormatId = numberFormatId ?? 0,
        ApplyFont = true,
        ApplyFill = true,
        ApplyBorder = true,
        ApplyNumberFormat = numberFormatId.HasValue,
        ApplyAlignment = true,
        Alignment = new Alignment
        {
            Horizontal = HorizontalAlignmentValues.Center,
            Vertical = VerticalAlignmentValues.Center
        }
    };

    private sealed class SnapshotDataService : IDataService
    {
        private readonly List<DrawRecord> _records;

        public SnapshotDataService(IEnumerable<DrawRecord> records) =>
            _records = records.OrderBy(record => record.Period).ToList();

        public event Action? DataUpdated;
        public bool HasLocalFile => false;
        public string LocalDataFilePath => "";
        public string? LastErrorMessage => null;
        public List<DrawRecord> GetAllRecords() => _records;
        public int GetLastPeriod() => _records.Count == 0 ? -1 : _records[^1].Period;
        public int GetRecordCount() => _records.Count;
        public Task<int> TryUpdateAsync() => Task.FromResult(0);
        public void ClearAllData() { }
        public void ResetCache() { }
        public void NotifyDataUpdated() => DataUpdated?.Invoke();
    }

    private static IReadOnlyList<CachedPrediction> LoadCompatibleCache(
        IReadOnlyList<DrawRecord> currentRecords,
        string ruleVersionId,
        int predictionYear)
    {
        var runtimePredictions = GetCompatiblePredictions(
            TryReadRuntimeCache(predictionYear), currentRecords, ruleVersionId, predictionYear);
        if (runtimePredictions.Count > 0) return runtimePredictions;

        return GetCompatiblePredictions(
            TryReadSeedCache(), currentRecords, ruleVersionId, predictionYear);
    }

    private static PredictionCache? TryReadRuntimeCache(int predictionYear)
    {
        string cacheFilePath = GetCacheFilePath(predictionYear);
        if (!File.Exists(cacheFilePath)) return null;
        try
        {
            return JsonSerializer.Deserialize<PredictionCache>(
                File.ReadAllText(cacheFilePath), CacheJsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static PredictionCache? TryReadSeedCache()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(SeedCacheResourceName);
            return stream is null
                ? null
                : JsonSerializer.Deserialize<PredictionCache>(stream, CacheJsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<CachedPrediction> GetCompatiblePredictions(
        PredictionCache? cache,
        IReadOnlyList<DrawRecord> currentRecords,
        string ruleVersionId,
        int predictionYear)
    {
        if (cache is null || cache.RuleVersionId != ruleVersionId)
            return Array.Empty<CachedPrediction>();
        int dataEndIndex = currentRecords.ToList()
            .FindIndex(record => record.Period == cache.DataThroughIssue);
        if (dataEndIndex < 0) return Array.Empty<CachedPrediction>();
        string currentPrefixHash = CreateDataHash(currentRecords.Take(dataEndIndex + 1));
        return string.Equals(currentPrefixHash, cache.DataHash, StringComparison.Ordinal)
            ? cache.Predictions.Where(prediction => prediction.Issue / 1000 == predictionYear).ToArray()
            : Array.Empty<CachedPrediction>();
    }

    private static void TrySaveCache(
        IReadOnlyList<DrawRecord> records,
        string ruleVersionId,
        IReadOnlyList<PositionPrediction> predictions,
        int predictionYear)
    {
        string cacheFilePath = GetCacheFilePath(predictionYear);
        string temporaryPath = cacheFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            AppPaths.EnsureRoot();
            var cache = new PredictionCache
            {
                RuleVersionId = ruleVersionId,
                DataThroughIssue = records[^1].Period,
                DataHash = CreateDataHash(records),
                Predictions = predictions.Select(CachedPrediction.From).ToArray()
            };
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(cache, CacheJsonOptions));
            File.Move(temporaryPath, cacheFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 缓存失败不影响本次工作簿导出。
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static string CreateDataHash(IEnumerable<DrawRecord> records)
    {
        string canonical = string.Join('|', records.Select(record =>
            $"{record.Period}:{string.Join(',', record.RedBalls)}:{record.BlueBall}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private sealed class PredictionCache
    {
        public string RuleVersionId { get; init; } = "";
        public int DataThroughIssue { get; init; }
        public string DataHash { get; init; } = "";
        public IReadOnlyList<CachedPrediction> Predictions { get; init; } = Array.Empty<CachedPrediction>();
    }

    private sealed class CachedPrediction
    {
        public int Issue { get; init; }
        public int AsOfIssue { get; init; }
        public string SnapshotId { get; init; } = "";
        public string RuleVersionId { get; init; } = "";
        public string RunId { get; init; } = "";
        public IReadOnlyList<int> RedPoints { get; init; } = Array.Empty<int>();
        public int SingleBlue { get; init; }
        public IReadOnlyList<int> DoubleBlue { get; init; } = Array.Empty<int>();
        public IReadOnlyList<int> TripleBlue { get; init; } = Array.Empty<int>();

        public static CachedPrediction From(PositionPrediction prediction) => new()
        {
            Issue = prediction.Issue,
            AsOfIssue = prediction.AsOfIssue,
            SnapshotId = prediction.SnapshotId,
            RuleVersionId = prediction.RuleVersionId,
            RunId = prediction.RunId,
            RedPoints = prediction.RedPoints.ToArray(),
            SingleBlue = prediction.SingleBlue,
            DoubleBlue = prediction.DoubleBlue.ToArray(),
            TripleBlue = prediction.TripleBlue.ToArray()
        };

        public PositionPrediction ToPrediction(DrawRecord? actual) => new()
        {
            Issue = Issue,
            AsOfIssue = AsOfIssue,
            SnapshotId = SnapshotId,
            RuleVersionId = RuleVersionId,
            RunId = RunId,
            RunMode = "backtest",
            RedPoints = RedPoints,
            SingleBlue = SingleBlue,
            DoubleBlue = DoubleBlue,
            TripleBlue = TripleBlue,
            Actual = actual
        };
    }
}
