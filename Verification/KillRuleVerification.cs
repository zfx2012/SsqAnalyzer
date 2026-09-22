using System.IO;
using System.Text.Json;
using SsqAnalyzer;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;
using SsqAnalyzer.Services.Kill;

internal static partial class VerificationSuite
{
    private static void VerifyBuiltinKillRuleCuration()
    {
        double redBaseline = 27.0 / 33.0;
        double blueBaseline = 15.0 / 16.0;
        Assert(Math.Abs(BuiltinRules.RandomKillAccuracy(BallType.Red) - redBaseline) < 1e-12,
            "red kill random baseline");
        Assert(Math.Abs(BuiltinRules.RandomKillAccuracy(BallType.Blue) - blueBaseline) < 1e-12,
            "blue kill random baseline");
    
        var selected = new BacktestWindowStat(50, 100, 84, 0.84, SampleInsufficient: false);
        var belowBaseline = new BacktestWindowStat(50, 100, 81, 0.81, SampleInsufficient: false);
        var insufficient = new BacktestWindowStat(20, 40, 36, 0.90, SampleInsufficient: true);
        Assert(BuiltinRules.IsCuratedCandidate(BallType.Red, selected, 0.80),
            "positive lift builtin selected");
        Assert(!BuiltinRules.IsCuratedCandidate(BallType.Red, belowBaseline, 0.80),
            "below random baseline rejected");
        Assert(!BuiltinRules.IsCuratedCandidate(BallType.Red, insufficient, 0.80),
            "insufficient builtin rejected");
        Assert(!BuiltinRules.IsCuratedCandidate(BallType.Red, selected, 0.85),
            "user threshold remains effective");
    
        string[] expectedIds = { "B-G-B-001", "B-G-B-002", "B-G-R-001", "B-G-R-002", "B-G-R-003", "B-G-R-004", "B-G-R-005", "B-G-R-006", "B-G-R-007", "B-G-R-008", "B-G-R-009", "B-G-R-010", "B-G-R-011", "B-G-R-012" };
        var loadedBuiltins = BuiltinRules.LoadAll();
        Assert(expectedIds.Distinct(StringComparer.Ordinal).Count() == expectedIds.Length
            && loadedBuiltins.Where(rule => rule.RuleId.Count(c => c == '-') == 3)
                .Select(rule => rule.RuleId)
                .Distinct(StringComparer.Ordinal)
                .Count() == expectedIds.Length,
            "builtin base IDs are unique and contiguous");
        Assert(expectedIds.All(id => loadedBuiltins.Any(rule => rule.RuleId == id))
            && loadedBuiltins.Count == expectedIds.Length + 12 * 3,
            "expected builtin rules and scoped variants remain");
        Assert(loadedBuiltins.Any(rule => rule.RuleId == "B-G-R-003-HS")
            && loadedBuiltins.Any(rule => rule.RuleId == "B-G-R-003-OE")
            && loadedBuiltins.Any(rule => rule.RuleId == "B-G-R-003-CY"),
            "basic red rules expand to same-period parity and cycle variants");
    
        var coldRedRule = BuiltinRules.LoadAll().Single(rule => rule.RuleId == "B-G-R-001");
        var records = Enumerable.Range(1, 12).Select(index => new DrawRecord
        {
            Period = 2026000 + index,
            DrawDate = new DateTime(2026, 1, 1).AddDays(index),
            RedBalls = new[] { 2, 3, 4, 5, 6, 7 },
            BlueBall = 1
        }).ToArray();
        var result = new JintRuleExecutor().Execute(coldRedRule, new RuleContextBuilder().Build(records, records.Length));
        Assert(result.KilledBalls.SequenceEqual(new[] { 1 }),
            "cold red rule kills one maximum-omission ball with stable tie break");
    
        var tripleRepeatRule = BuiltinRules.LoadAll().Single(rule => rule.RuleId == "B-G-R-002");
        var repeatRecords = new[]
        {
            new DrawRecord { Period = 2026001, DrawDate = new DateTime(2026, 1, 1), RedBalls = new[] { 1, 5, 9, 13, 17, 21 }, BlueBall = 1 },
            new DrawRecord { Period = 2026002, DrawDate = new DateTime(2026, 1, 4), RedBalls = new[] { 2, 5, 10, 13, 18, 22 }, BlueBall = 2 }
        };
        var repeatResult = new JintRuleExecutor().Execute(tripleRepeatRule,
            new RuleContextBuilder().Build(repeatRecords, repeatRecords.Length));
        Assert(!tripleRepeatRule.ForceEnabled
            && repeatResult.KilledBalls.SequenceEqual(new[] { 5, 13 }),
            "triple repeat rule kills every red shared by the previous two draws");
    
        var lShapeRule = BuiltinRules.LoadAll().Single(rule => rule.RuleId == "B-G-R-003");
        var lShapeRecords = new[]
        {
            new DrawRecord { Period = 2026087, DrawDate = new DateTime(2026, 8, 1), RedBalls = new[] { 4, 6, 10, 18, 23, 31 }, BlueBall = 1 },
            new DrawRecord { Period = 2026088, DrawDate = new DateTime(2026, 8, 3), RedBalls = new[] { 6, 7, 11, 18, 22, 33 }, BlueBall = 2 }
        };
        var lShapeResult = new JintRuleExecutor().Execute(lShapeRule,
            new RuleContextBuilder().Build(lShapeRecords, lShapeRecords.Length));
        Assert(!lShapeRule.ForceEnabled && lShapeResult.KilledBalls.SequenceEqual(new[] { 6, 7 }),
            "L-shape rule kills the repeated red and its latest adjacent red");
    
        var samePeriodLRule = loadedBuiltins.Single(rule => rule.RuleId == "B-G-R-003-HS");
        var samePeriodLRecords = new[]
        {
            new DrawRecord { Period = 2024002, DrawDate = new DateTime(2024, 1, 4), RedBalls = new[] { 6, 10, 15, 20, 25, 30 }, BlueBall = 1 },
            new DrawRecord { Period = 2025002, DrawDate = new DateTime(2025, 1, 4), RedBalls = new[] { 6, 7, 11, 18, 22, 33 }, BlueBall = 2 },
            new DrawRecord { Period = 2026002, DrawDate = new DateTime(2026, 1, 4), RedBalls = new[] { 1, 2, 3, 4, 5, 8 }, BlueBall = 3 }
        };
        var samePeriodLResult = new JintRuleExecutor().Execute(samePeriodLRule,
            new RuleContextBuilder().Build(samePeriodLRecords, samePeriodLRecords.Length - 1));
        Assert(samePeriodLResult.KilledBalls.SequenceEqual(new[] { 6, 7 }),
            "same-period variant uses the predicted next issue suffix");
    
        var inverseLShapeRule = BuiltinRules.LoadAll().Single(rule => rule.RuleId == "B-G-R-004");
        var inverseLShapeRecords = new[]
        {
            new DrawRecord { Period = 2026001, DrawDate = new DateTime(2026, 1, 1), RedBalls = new[] { 3, 8, 13, 14, 22, 30 }, BlueBall = 1 },
            new DrawRecord { Period = 2026002, DrawDate = new DateTime(2026, 1, 4), RedBalls = new[] { 2, 7, 13, 19, 25, 32 }, BlueBall = 2 }
        };
        var inverseLShapeResult = new JintRuleExecutor().Execute(inverseLShapeRule,
            new RuleContextBuilder().Build(inverseLShapeRecords, inverseLShapeRecords.Length));
        Assert(!inverseLShapeRule.ForceEnabled
            && inverseLShapeResult.KilledBalls.SequenceEqual(new[] { 13, 14 }),
            "inverse L-shape rule kills the repeated red and its earlier adjacent red");
    
        var alternatingRule = BuiltinRules.LoadAll().Single(rule => rule.RuleId == "B-G-R-005");
        var alternatingRecords = new[]
        {
            new DrawRecord { Period = 2026001, DrawDate = new DateTime(2026, 1, 1), RedBalls = new[] { 3, 8, 13, 21, 28, 32 }, BlueBall = 1 },
            new DrawRecord { Period = 2026002, DrawDate = new DateTime(2026, 1, 4), RedBalls = new[] { 1, 5, 9, 14, 22, 30 }, BlueBall = 2 },
            new DrawRecord { Period = 2026003, DrawDate = new DateTime(2026, 1, 6), RedBalls = new[] { 2, 7, 12, 18, 24, 28 }, BlueBall = 3 },
            new DrawRecord { Period = 2026004, DrawDate = new DateTime(2026, 1, 8), RedBalls = new[] { 4, 6, 10, 17, 25, 33 }, BlueBall = 4 }
        };
        var alternatingResult = new JintRuleExecutor().Execute(alternatingRule,
            new RuleContextBuilder().Build(alternatingRecords, alternatingRecords.Length));
        Assert(!alternatingRule.ForceEnabled && alternatingResult.KilledBalls.SequenceEqual(new[] { 28 }),
            "alternating-period rule kills a red following open-miss-open-miss history");
    
        var repeatSymmetryRule = BuiltinRules.LoadAll().Single(rule => rule.RuleId == "B-G-R-006");
        var repeatSymmetryRecords = new[]
        {
            new DrawRecord { Period = 2026084, DrawDate = new DateTime(2026, 7, 23), RedBalls = new[] { 1, 5, 6, 10, 12, 16 }, BlueBall = 5 },
            new DrawRecord { Period = 2026085, DrawDate = new DateTime(2026, 7, 26), RedBalls = new[] { 6, 9, 13, 17, 24, 28 }, BlueBall = 15 },
            new DrawRecord { Period = 2026086, DrawDate = new DateTime(2026, 7, 28), RedBalls = new[] { 2, 5, 14, 25, 30, 32 }, BlueBall = 5 },
            new DrawRecord { Period = 2026087, DrawDate = new DateTime(2026, 7, 30), RedBalls = new[] { 4, 6, 10, 18, 23, 31 }, BlueBall = 1 }
        };
        var repeatSymmetryResult = new JintRuleExecutor().Execute(repeatSymmetryRule,
            new RuleContextBuilder().Build(repeatSymmetryRecords, repeatSymmetryRecords.Length));
        Assert(!repeatSymmetryRule.ForceEnabled && repeatSymmetryResult.KilledBalls.SequenceEqual(new[] { 6 }),
            "repeat-symmetry rule kills a red following open-open-miss-open history");
    
        var triangleCenterRule = BuiltinRules.LoadAll().Single(rule => rule.RuleId == "B-G-R-007");
        var triangleCenterRecords = new[]
        {
            new DrawRecord { Period = 2026001, DrawDate = new DateTime(2026, 1, 1), RedBalls = new[] { 5, 10, 15, 20, 25, 30 }, BlueBall = 1 },
            new DrawRecord { Period = 2026002, DrawDate = new DateTime(2026, 1, 4), RedBalls = new[] { 4, 6, 11, 18, 24, 32 }, BlueBall = 2 }
        };
        var triangleCenterResult = new JintRuleExecutor().Execute(triangleCenterRule,
            new RuleContextBuilder().Build(triangleCenterRecords, triangleCenterRecords.Length));
        Assert(!triangleCenterRule.ForceEnabled && triangleCenterResult.KilledBalls.SequenceEqual(new[] { 5 }),
            "triangle-center rule kills the absent center between two latest neighbors");
    
        var missingCornerRule = BuiltinRules.LoadAll().Single(rule => rule.RuleId == "B-G-R-008");
        var missingCornerRecords = new[]
        {
            new DrawRecord { Period = 2026001, DrawDate = new DateTime(2026, 1, 1), RedBalls = new[] { 3, 10, 15, 20, 25, 30 }, BlueBall = 1 },
            new DrawRecord { Period = 2026002, DrawDate = new DateTime(2026, 1, 4), RedBalls = new[] { 1, 9, 14, 19, 24, 29 }, BlueBall = 2 },
            new DrawRecord { Period = 2026003, DrawDate = new DateTime(2026, 1, 6), RedBalls = new[] { 2, 7, 13, 18, 23, 28 }, BlueBall = 3 },
            new DrawRecord { Period = 2026004, DrawDate = new DateTime(2026, 1, 8), RedBalls = new[] { 4, 8, 12, 17, 22, 27 }, BlueBall = 4 },
            new DrawRecord { Period = 2026005, DrawDate = new DateTime(2026, 1, 11), RedBalls = new[] { 5, 10, 16, 21, 26, 31 }, BlueBall = 5 }
        };
        var missingCornerResult = new JintRuleExecutor().Execute(missingCornerRule,
            new RuleContextBuilder().Build(missingCornerRecords, missingCornerRecords.Length));
        Assert(!missingCornerRule.ForceEnabled && missingCornerResult.KilledBalls.SequenceEqual(new[] { 9 }),
            "missing-corner rule scans an unrestricted empty gap and kills adjacent B");
    
        var samePeriodBlueRule = BuiltinRules.LoadAll().Single(rule => rule.RuleId == "B-G-B-001");
        var samePeriodRecords = Enumerable.Range(0, 18).Select(index => new DrawRecord
        {
            Period = (2008 + index) * 1000 + 1,
            DrawDate = new DateTime(2008 + index, 1, 1),
            RedBalls = new[] { 1, 2, 3, 4, 5, 6 },
            BlueBall = index switch { 0 => 1, 1 => 2, _ => 3 }
        }).ToArray();
        var blueResult = new JintRuleExecutor().Execute(samePeriodBlueRule,
            new RuleContextBuilder().Build(samePeriodRecords, samePeriodRecords.Length - 1));
        Assert(samePeriodBlueRule.BallType == BallType.Blue
            && blueResult.KilledBalls.Count > 1
            && blueResult.KilledBalls.Contains(1)
            && !blueResult.KilledBalls.Contains(2)
            && blueResult.KilledBalls.All(ball => ball is >= 1 and <= 16),
            "same-period blue rule kills all blue balls with omission at least 16");
    
        var repeatedBlueRule = BuiltinRules.LoadAll().Single(rule => rule.RuleId == "B-G-B-002");
        var repeatedBlueRecords = new[]
        {
            new DrawRecord { Period = 2026088, DrawDate = new DateTime(2026, 8, 2), RedBalls = new[] { 6, 7, 11, 18, 22, 33 }, BlueBall = 5 }
        };
        var repeatedBlueResult = new JintRuleExecutor().Execute(repeatedBlueRule,
            new RuleContextBuilder().Build(repeatedBlueRecords, repeatedBlueRecords.Length));
        Assert(repeatedBlueRule.BallType == BallType.Blue
            && !repeatedBlueRule.ForceEnabled
            && repeatedBlueResult.KilledBalls.SequenceEqual(new[] { 5 }),
            "repeated-blue rule kills the previous issue blue ball");
    
        var previousBlueKillsRedRule = BuiltinRules.LoadAll().Single(rule => rule.RuleId == "B-G-R-009");
        var previousBlueKillsRedResult = new JintRuleExecutor().Execute(previousBlueKillsRedRule,
            new RuleContextBuilder().Build(repeatedBlueRecords, repeatedBlueRecords.Length));
        Assert(previousBlueKillsRedRule.BallType == BallType.Red
            && !previousBlueKillsRedRule.ForceEnabled
            && previousBlueKillsRedResult.KilledBalls.SequenceEqual(new[] { 5 }),
            "previous-blue-kills-red rule maps the previous blue number to a red kill");
    
        var consecutiveTripleRule = BuiltinRules.LoadAll().Single(rule => rule.RuleId == "B-G-R-010");
        var consecutiveTripleRecords = new[]
        {
            new DrawRecord { Period = 2026088, DrawDate = new DateTime(2026, 8, 2), RedBalls = new[] { 4, 16, 17, 18, 19, 31 }, BlueBall = 5 }
        };
        var consecutiveTripleResult = new JintRuleExecutor().Execute(consecutiveTripleRule,
            new RuleContextBuilder().Build(consecutiveTripleRecords, consecutiveTripleRecords.Length));
        Assert(!consecutiveTripleRule.ForceEnabled
            && consecutiveTripleResult.KilledBalls.SequenceEqual(new[] { 16, 17, 18, 19 }),
            "consecutive-triple rule merges overlapping triples in a longer run");
    
        var fourDiagonalRule = BuiltinRules.LoadAll().Single(rule => rule.RuleId == "B-G-R-011");
        var fourDiagonalRecords = new[]
        {
            new DrawRecord { Period = 2026001, DrawDate = new DateTime(2026, 1, 1), RedBalls = new[] { 2, 7, 12, 17, 25, 31 }, BlueBall = 1 },
            new DrawRecord { Period = 2026002, DrawDate = new DateTime(2026, 1, 4), RedBalls = new[] { 5, 13, 19, 24, 28, 33 }, BlueBall = 2 },
            new DrawRecord { Period = 2026003, DrawDate = new DateTime(2026, 1, 6), RedBalls = new[] { 6, 14, 20, 23, 27, 32 }, BlueBall = 3 }
        };
        var fourDiagonalResult = new JintRuleExecutor().Execute(fourDiagonalRule,
            new RuleContextBuilder().Build(fourDiagonalRecords, fourDiagonalRecords.Length));
        Assert(!fourDiagonalRule.ForceEnabled
            && fourDiagonalResult.KilledBalls.SequenceEqual(new[] { 15, 22 }),
            "four-diagonal rule handles increasing and decreasing chains");
    
        var hollowRectangleRule = BuiltinRules.LoadAll().Single(rule => rule.RuleId == "B-G-R-012");
        var hollowRectangleRecords = new[]
        {
            new DrawRecord { Period = 2026001, DrawDate = new DateTime(2026, 1, 1), RedBalls = new[] { 3, 10, 18, 24, 29, 30 }, BlueBall = 1 },
            new DrawRecord { Period = 2026002, DrawDate = new DateTime(2026, 1, 4), RedBalls = new[] { 1, 5, 11, 19, 25, 32 }, BlueBall = 2 },
            new DrawRecord { Period = 2026003, DrawDate = new DateTime(2026, 1, 6), RedBalls = new[] { 2, 6, 12, 20, 26, 33 }, BlueBall = 3 },
            new DrawRecord { Period = 2026004, DrawDate = new DateTime(2026, 1, 8), RedBalls = new[] { 4, 9, 17, 23, 29, 30 }, BlueBall = 4 }
        };
        var hollowRectangleResult = new JintRuleExecutor().Execute(hollowRectangleRule,
            new RuleContextBuilder().Build(hollowRectangleRecords, hollowRectangleRecords.Length));
        Assert(!hollowRectangleRule.ForceEnabled
            && hollowRectangleResult.KilledBalls.SequenceEqual(new[] { 29, 30 }),
            "hollow-rectangle rule uses previous occurrence distances without full-history JS scans");
    }
    private static void VerifyBuiltinIdMigration()
    {
        string dataDirectory = AppPaths.DataDirectory;
        string path = Path.Combine(dataDirectory, "kill_rules_user.json");
        byte[]? original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        Directory.CreateDirectory(dataDirectory);
        try
        {
            File.WriteAllText(path, """
            {
              "version": 1,
              "userRules": [],
              "stats": {},
              "builtinOverrides": {
                "B-G-R-015": {
                  "isEnabled": false,
                  "forceEnabled": false,
                  "backtestStats": {
                    "window30": { "triggeredCount": 30, "killBallCount": 30, "correctBallCount": 27, "accuracy": 0.9, "sampleInsufficient": false },
                    "window50": { "triggeredCount": 50, "killBallCount": 50, "correctBallCount": 45, "accuracy": 0.9, "sampleInsufficient": false },
                    "window100": { "triggeredCount": 100, "killBallCount": 100, "correctBallCount": 88, "accuracy": 0.88, "sampleInsufficient": false },
                    "lastRunAt": "2026-08-04T00:00:00Z"
                  }
                },
                "B-G-R-015-HS": { "isEnabled": false, "forceEnabled": false }
              }
            }
            """);
    
            var repository = new RuleRepository();
            var migrated = repository.Find("B-G-R-003") as KillRule;
            var migratedVariant = repository.Find("B-G-R-003-HS") as KillRule;
            Assert(migrated is not null && !migrated.IsEnabled
                && migrated.BacktestStats?.Window50.Accuracy == 0.9,
                "base builtin ID migration preserves state and stats");
            Assert(migratedVariant is not null && !migratedVariant.IsEnabled,
                "scoped builtin ID migration preserves state");
    
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var overrides = document.RootElement.GetProperty("builtinOverrides");
            Assert(overrides.TryGetProperty("B-G-R-003", out _)
                && overrides.TryGetProperty("B-G-R-003-HS", out _)
                && !overrides.TryGetProperty("B-G-R-015", out _)
                && !overrides.TryGetProperty("B-G-R-015-HS", out _),
                "persisted builtin IDs are migrated");
        }
        finally
        {
            if (original is null)
            {
                if (File.Exists(path)) File.Delete(path);
            }
            else
            {
                File.WriteAllBytes(path, original);
            }
        }
    }
}
