using SsqAnalyzer.Models;

namespace SsqAnalyzer.Services;

/// <summary>
/// Fast, point-only walk-forward research. It deliberately excludes linked red and blue
/// selections so point candidates can be evaluated without changing live predictions.
/// </summary>
internal static class PositionPointResearch
{
    private const int BallCount = 33;
    private const int PointCount = 6;
    private const int LogisticFeatureCount = 46;
    private const double ExpectedBallRate = 6.0 / BallCount;
    private const string BaselineModel = "stable-80-long-20-recent100";
    private const string V43Model = "stable-zone-route-transition05-prior120";
    private const string V45Model = "stable-season14-shrunk15-prior240-zone-route05-weight50";
    private const string CalendarV45Model = "stable-calendar-season14-shrunk15-prior240-zone-route05-weight50";

    private static readonly PointModel[] StaticModels =
    {
        new(BaselineModel, PointModelKind.Stable),
        new("stable-range-event-greedy", PointModelKind.RangeEventGreedy),
        new("stable-range-event-global", PointModelKind.RangeEventGlobal),
        new("stable-global-coverage", PointModelKind.StableGlobalCoverage),
        new("frequency-100-long", PointModelKind.CustomFrequency, 100, 0.00),
        new("frequency-95-long-05-recent100", PointModelKind.CustomFrequency, 100, 0.05),
        new("frequency-90-long-10-recent100", PointModelKind.CustomFrequency, 100, 0.10),
        new("frequency-85-long-15-recent100", PointModelKind.CustomFrequency, 100, 0.15),
        new("frequency-75-long-25-recent100", PointModelKind.CustomFrequency, 100, 0.25),
        new("frequency-70-long-30-recent100", PointModelKind.CustomFrequency, 100, 0.30),
        new("frequency-60-long-40-recent100", PointModelKind.CustomFrequency, 100, 0.40),
        new("frequency-95-long-05-recent50", PointModelKind.CustomFrequency, 50, 0.05),
        new("frequency-90-long-10-recent50", PointModelKind.CustomFrequency, 50, 0.10),
        new("frequency-80-long-20-recent50", PointModelKind.CustomFrequency, 50, 0.20),
        new("frequency-95-long-05-recent30", PointModelKind.CustomFrequency, 30, 0.05),
        new("frequency-90-long-10-recent30", PointModelKind.CustomFrequency, 30, 0.10),
        new("frequency-80-long-20-recent30", PointModelKind.CustomFrequency, 30, 0.20),
        new("stable-weekday-frequency05", PointModelKind.WeekdayFrequency05),
        new("stable-weekday-frequency10", PointModelKind.WeekdayFrequency10),
        new("stable-weekday-frequency20", PointModelKind.WeekdayFrequency20),
        new("stable-weekday-frequency30", PointModelKind.WeekdayFrequency30),
        new("stable-season07-frequency10", PointModelKind.CustomSeasonFrequency, 7, 0.10),
        new("stable-season10-frequency10", PointModelKind.CustomSeasonFrequency, 10, 0.10),
        new("stable-season14-frequency05", PointModelKind.CustomSeasonFrequency, 14, 0.05),
        new("stable-season14-frequency10", PointModelKind.CustomSeasonFrequency, 14, 0.10),
        new("stable-season14-frequency15", PointModelKind.CustomSeasonFrequency, 14, 0.15),
        new("stable-season14-frequency20", PointModelKind.CustomSeasonFrequency, 14, 0.20),
        new("stable-season18-frequency10", PointModelKind.CustomSeasonFrequency, 18, 0.10),
        new("stable-season21-frequency10", PointModelKind.CustomSeasonFrequency, 21, 0.10),
        new("stable-season30-frequency10", PointModelKind.CustomSeasonFrequency, 30, 0.10),
        new("stable-season30-frequency20", PointModelKind.CustomSeasonFrequency, 30, 0.20),
        new("stable-season60-frequency10", PointModelKind.CustomSeasonFrequency, 60, 0.10),
        new("stable-season14-shrunk10-prior120",
            PointModelKind.CustomShrunkSeasonFrequency, 14, 0.10,
            PriorStrength: 120),
        new("stable-season14-shrunk15-prior240",
            PointModelKind.CustomShrunkSeasonFrequency, 14, 0.15,
            PriorStrength: 240),
        new("stable-season14-shrunk20-prior480",
            PointModelKind.CustomShrunkSeasonFrequency, 14, 0.20,
            PriorStrength: 480),
        new("stable-season14-frequency025-zone-route05-prior120",
            PointModelKind.CustomSeasonTransition, 14, 0.025,
            PointModelKind.ZoneRouteTransition05Prior120, 0.50),
        new("stable-season14-frequency05-zone-route05-prior120",
            PointModelKind.CustomSeasonTransition, 14, 0.05,
            PointModelKind.ZoneRouteTransition05Prior120, 0.50),
        new("stable-season14-frequency025-zone-route10-prior120",
            PointModelKind.CustomSeasonTransition, 14, 0.025,
            PointModelKind.ZoneRouteTransitionPrior120, 0.50),
        new("stable-season14-frequency05-zone-route10-prior120",
            PointModelKind.CustomSeasonTransition, 14, 0.05,
            PointModelKind.ZoneRouteTransitionPrior120, 0.50),
        new("stable-season14-shrunk15-prior240-zone-route05-weight25",
            PointModelKind.CustomSeasonTransition,
            Window: 14,
            Weight: 0.15,
            SecondaryKind: PointModelKind.ZoneRouteTransition05Prior120,
            SecondaryWeight: 0.25,
            PriorStrength: 240),
        new(V45Model,
            PointModelKind.CustomSeasonTransition,
            Window: 14,
            Weight: 0.15,
            SecondaryKind: PointModelKind.ZoneRouteTransition05Prior120,
            SecondaryWeight: 0.50,
            PriorStrength: 240),
        new(CalendarV45Model,
            PointModelKind.CalendarSeasonTransition,
            Window: 14,
            Weight: 0.15,
            SecondaryKind: PointModelKind.ZoneRouteTransition05Prior120,
            SecondaryWeight: 0.50,
            PriorStrength: 240),
        new("stable-v45-blend25", PointModelKind.V45BaselineBlend, Weight: 0.25),
        new("stable-v45-blend50", PointModelKind.V45BaselineBlend, Weight: 0.50),
        new("stable-v45-blend75", PointModelKind.V45BaselineBlend, Weight: 0.75),
        new("stable-season14-shrunk15-prior240-zone-route05-weight75",
            PointModelKind.CustomSeasonTransition,
            Window: 14,
            Weight: 0.15,
            SecondaryKind: PointModelKind.ZoneRouteTransition05Prior120,
            SecondaryWeight: 0.75,
            PriorStrength: 240),
        new("confidence-ramp-recent800", PointModelKind.ConfidenceRamp800),
        new("confidence-ramp-recent1600", PointModelKind.ConfidenceRamp1600),
        new("confidence-ramp-recent2400", PointModelKind.ConfidenceRamp2400),
        new("stable-category-reversion15", PointModelKind.CategoryReversion15),
        new("stable-category-reversion30-light", PointModelKind.CategoryReversion30Light),
        new("stable-category-reversion30", PointModelKind.CategoryReversion30),
        new("stable-category-reversion30-strong", PointModelKind.CategoryReversion30Strong),
        new("stable-category-momentum30", PointModelKind.CategoryMomentum30),
        new("stable-category-transition05", PointModelKind.CategoryTransition05),
        new("stable-category-transition10", PointModelKind.CategoryTransition10),
        new("stable-category-transition15", PointModelKind.CategoryTransition15),
        new("stable-category-transition20", PointModelKind.CategoryTransition20),
        new("stable-category-transition25", PointModelKind.CategoryTransition25),
        new("stable-category-transition30", PointModelKind.CategoryTransition30),
        new("stable-zone-transition10", PointModelKind.ZoneTransition10),
        new("stable-parity-transition10", PointModelKind.ParityTransition10),
        new("stable-route-transition10", PointModelKind.RouteTransition10),
        new("stable-zone-parity-transition10", PointModelKind.ZoneParityTransition10),
        new("stable-zone-route-transition10", PointModelKind.ZoneRouteTransition10),
        new("stable-zone-route-transition05", PointModelKind.ZoneRouteTransition05),
        new("stable-zone-route-transition15", PointModelKind.ZoneRouteTransition15),
        new("stable-zone-route-transition20", PointModelKind.ZoneRouteTransition20),
        new("stable-zone-route-consensus-all", PointModelKind.ZoneRouteConsensusAll),
        new("stable-zone-route-consensus-low", PointModelKind.ZoneRouteConsensusLow),
        new("stable-zone-route-consensus-high", PointModelKind.ZoneRouteConsensusHigh),
        new("stable-zone-route-joint-transition05", PointModelKind.ZoneRouteJointTransition05),
        new("stable-zone-route-joint-transition10", PointModelKind.ZoneRouteJointTransition10),
        new("stable-zone-route-joint-transition15", PointModelKind.ZoneRouteJointTransition15),
        new("stable-local-range-transition05", PointModelKind.LocalRangeTransition05),
        new("stable-local-range-transition10", PointModelKind.LocalRangeTransition10),
        new("stable-local-range-transition15", PointModelKind.LocalRangeTransition15),
        new("stable-zone-route-transition10-prior60", PointModelKind.ZoneRouteTransitionPrior60),
        new("stable-zone-route-transition10-prior120", PointModelKind.ZoneRouteTransitionPrior120),
        new("stable-zone-route-transition10-prior240", PointModelKind.ZoneRouteTransitionPrior240),
        new("stable-zone-route-transition05-prior120", PointModelKind.ZoneRouteTransition05Prior120),
        new("stable-zone-route-transition15-prior120", PointModelKind.ZoneRouteTransition15Prior120),
        new("stable-zone-transition05-prior120", PointModelKind.ZoneTransition05Prior120),
        new("stable-route-transition05-prior120", PointModelKind.RouteTransition05Prior120),
        new("stable-zone2-route1-transition05-prior120", PointModelKind.Zone2Route1Transition05Prior120),
        new("stable-zone1-route2-transition05-prior120", PointModelKind.Zone1Route2Transition05Prior120),
        new("stable-parity-route-transition10", PointModelKind.ParityRouteTransition10),
        new("stable-structure-consensus", PointModelKind.StructureConsensus),
        new("stable-structure-robust-min", PointModelKind.StructureRobustMin),
        new("empirical-bayes-recent100", PointModelKind.EmpiricalBayesRecent100),
        new("empirical-bayes-rolling400", PointModelKind.EmpiricalBayesRolling400),
        new("drift-adaptive-shrunk-recent20", PointModelKind.DriftAdaptiveShrunkRecent20),
        new("forecast-first-multiscale-structure", PointModelKind.ForecastFirstMultiscaleStructure),
        new("confirmed-trend-swap", PointModelKind.ConfirmedTrendSwap),
        new("stable-80-long-20-ewma400", PointModelKind.Ewma400),
        new("stable-80-long-20-ewma800", PointModelKind.Ewma800),
        new("stable-80-long-20-ewma1600", PointModelKind.Ewma1600),
        new("stable-80-long-20-rolling400", PointModelKind.Rolling400),
        new("stable-80-long-20-rolling800", PointModelKind.Rolling800),
        new("stable-80-long-20-rolling1600", PointModelKind.Rolling1600),
        new("stable-repeat-10", PointModelKind.Repeat10),
        new("stable-neighbor-10", PointModelKind.Neighbor10),
        new("stable-state-gap-10", PointModelKind.StateGap10),
        new("stable-state-gap-20", PointModelKind.StateGap20),
        new("stable-ball-repeat-20", PointModelKind.BallRepeat20),
        new("stable-ball-neighbor-20", PointModelKind.BallNeighbor20),
        new("stable-ball-transition-20", PointModelKind.BallTransition20)
    };

    private static readonly string[] MetaModels =
    {
        "online-best-expert-400",
        "online-best-expert-800",
        "online-conservative-expert-400",
        "online-significant-expert-400",
        "online-significant-expert-800",
        "online-hedge-cumulative",
        "online-hedge-decay400",
        "online-logistic-multiscale",
        "online-logistic-anchor20",
        "online-v45-gated-400",
        "online-v45-gated-800",
        "online-v45-significant-400",
        "online-brier-hedge",
        "online-v45-dual-split200",
        "online-v45-dual-split400",
        "online-v43-gated-400",
        "online-v43-dual-split400",
        "online-v43-gated-200",
        "online-v43-gated-800",
        "online-v43-dual-rolling400"
    };

    private static readonly string[] ExpertPool =
    {
        BaselineModel,
        "stable-state-gap-20",
        "stable-ball-transition-20",
        "stable-category-reversion30",
        "stable-category-transition20"
    };

    public static PositionPointResearchReport Run(
        IReadOnlyList<DrawRecord> source,
        int windowSize = 400,
        int windowCount = 8)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (windowSize < 30) throw new ArgumentOutOfRangeException(nameof(windowSize));
        if (windowCount < 1) throw new ArgumentOutOfRangeException(nameof(windowCount));

        var ordered = source.OrderBy(record => record.Period).ToArray();
        if (ordered.Length == 0)
            throw new InvalidOperationException("At least 33 historical draws are required for point research.");
        int targetYear = ordered[^1].Period / 1000;
        var records = ordered.Where(record => record.Period / 1000 == targetYear).ToArray();
        int targetCount = Math.Min(windowSize * windowCount, records.Length - 3);
        if (targetCount < 30)
            throw new InvalidOperationException("At least 33 historical draws are required for point research.");

        int startIndex = records.Length - targetCount;
        var state = new RollingPointState();
        for (int index = 0; index < 3; index++) state.Add(records[index]);

        var allModelNames = StaticModels.Select(model => model.Name).Concat(MetaModels).ToArray();
        var observations = allModelNames.ToDictionary(
            model => model,
            _ => new List<PointObservation>(targetCount));
        var expertHits = ExpertPool.ToDictionary(model => model, _ => new List<int>(records.Length));
        var v45HitHistory = new List<int>(records.Length);
        var baselineRangeHitHistory = new List<int>(records.Length);
        var v45RangeHitHistory = new List<int>(records.Length);
        var v43HitHistory = new List<int>(records.Length);
        var v43RangeHitHistory = new List<int>(records.Length);
        var cumulativeHedge = new OnlineHedge(learningRate: 0.05, decay: 1.0);
        var decayedHedge = new OnlineHedge(
            learningRate: 0.10,
            decay: Math.Exp(Math.Log(0.5) / 400.0));
        var brierHedge = new OnlineBrierHedge(
            learningRate: 0.25,
            decay: Math.Exp(Math.Log(0.5) / 400.0));
        var onlineLogistic = new OnlineLogisticPointModel();

        for (int index = 3; index < records.Length; index++)
        {
            var target = records[index];
            var scoreSets = state.CreateScores(target.DrawDate.DayOfWeek);
            var logisticFeatures = state.CreateLogisticFeatures();
            var outcomes = new Dictionary<string, PointOutcome>(StaticModels.Length + MetaModels.Length);

            foreach (var model in StaticModels)
            {
                var points = model.Kind switch
                {
                    PointModelKind.StableGlobalCoverage =>
                        SelectPointsGlobally(scoreSets[PointModelKind.Stable]),
                    PointModelKind.RangeEventGreedy =>
                        SelectRangeEventPoints(state.CreateRangeEventScores()),
                    PointModelKind.RangeEventGlobal =>
                        SelectRangeEventPointsGlobally(state.CreateRangeEventScores()),
                    PointModelKind.CustomFrequency =>
                        SelectPoints(state.CreateFrequencyScores(
                            model.Window,
                            model.Weight)),
                    PointModelKind.CustomSeasonFrequency =>
                        SelectPoints(state.CreateSeasonFrequencyScores(
                            target.DrawDate.DayOfYear,
                            model.Window,
                            model.Weight)),
                    PointModelKind.CustomShrunkSeasonFrequency =>
                        SelectPoints(state.CreateShrunkSeasonFrequencyScores(
                            target.DrawDate.DayOfYear,
                            model.Window,
                            model.Weight,
                            model.PriorStrength)),
                    PointModelKind.CustomSeasonTransition =>
                        SelectPoints(BlendScores(
                            model.PriorStrength > 0
                                ? state.CreateShrunkSeasonFrequencyScores(
                                    target.DrawDate.DayOfYear,
                                    model.Window,
                                    model.Weight,
                                    model.PriorStrength)
                                : state.CreateSeasonFrequencyScores(
                                    target.DrawDate.DayOfYear,
                                    model.Window,
                                    model.Weight),
                            scoreSets[model.SecondaryKind!.Value],
                            model.SecondaryWeight)),
                    PointModelKind.CalendarSeasonTransition =>
                        SelectPoints(BlendScores(
                            state.CreateCalendarShrunkSeasonFrequencyScores(
                                target.DrawDate,
                                model.Window,
                                model.Weight,
                                model.PriorStrength),
                            scoreSets[model.SecondaryKind!.Value],
                            model.SecondaryWeight)),
                    PointModelKind.V45BaselineBlend =>
                        SelectPoints(BlendScores(
                            scoreSets[PointModelKind.Stable],
                            BlendScores(
                                state.CreateShrunkSeasonFrequencyScores(
                                    target.DrawDate.DayOfYear,
                                    14,
                                    0.15,
                                    240),
                                scoreSets[PointModelKind.ZoneRouteTransition05Prior120],
                                0.50),
                            model.Weight)),
                    PointModelKind.ZoneRouteConsensusAll =>
                        SelectConsensusPoints(scoreSets, includeLow: true, includeHigh: true),
                    PointModelKind.ZoneRouteConsensusLow =>
                        SelectConsensusPoints(scoreSets, includeLow: true, includeHigh: false),
                    PointModelKind.ZoneRouteConsensusHigh =>
                        SelectConsensusPoints(scoreSets, includeLow: false, includeHigh: true),
                    PointModelKind.ConfirmedTrendSwap =>
                        state.SelectConfirmedTrendSwapPoints(scoreSets[PointModelKind.Stable]),
                    _ => SelectPoints(scoreSets[model.Kind])
                };
                var covered = ExpandCoverage(points);
                int hits = target.RedBalls.Count(covered.Contains);
                outcomes[model.Name] = new PointOutcome(
                    hits,
                    CountRangeHits(points, target),
                    6.0 * covered.Count / BallCount,
                    points);
            }

            string best400 = SelectExpert(expertHits, 400, minimumHistory: 100);
            string best800 = SelectExpert(expertHits, 800, minimumHistory: 200);
            string conservative400 = SelectExpert(
                expertHits,
                400,
                minimumHistory: 100,
                minimumAdvantageToBaseline: 0.02);
            string significant400 = SelectSignificantExpert(expertHits, 400, minimumHistory: 200);
            string significant800 = SelectSignificantExpert(expertHits, 800, minimumHistory: 400);
            string v45Gated400 = SelectSpecificExpert(
                expertHits[BaselineModel], v45HitHistory, 400, minimumHistory: 100,
                candidateModel: V45Model);
            string v45Gated800 = SelectSpecificExpert(
                expertHits[BaselineModel], v45HitHistory, 800, minimumHistory: 200,
                candidateModel: V45Model);
            string v45Significant400 = SelectSignificantSpecificExpert(
                expertHits[BaselineModel], v45HitHistory, 400, minimumHistory: 200);
            string v45DualSplit200 = SelectDualMetricSplitExpert(
                expertHits[BaselineModel],
                v45HitHistory,
                baselineRangeHitHistory,
                v45RangeHitHistory,
                halfWindow: 100,
                candidateModel: V45Model);
            string v45DualSplit400 = SelectDualMetricSplitExpert(
                expertHits[BaselineModel],
                v45HitHistory,
                baselineRangeHitHistory,
                v45RangeHitHistory,
                halfWindow: 200,
                candidateModel: V45Model);
            string v43Gated400 = SelectSpecificExpert(
                expertHits[BaselineModel], v43HitHistory, 400, minimumHistory: 100,
                candidateModel: V43Model);
            string v43DualSplit400 = SelectDualMetricSplitExpert(
                expertHits[BaselineModel],
                v43HitHistory,
                baselineRangeHitHistory,
                v43RangeHitHistory,
                halfWindow: 200,
                candidateModel: V43Model);
            string v43Gated200 = SelectSpecificExpert(
                expertHits[BaselineModel], v43HitHistory, 200, minimumHistory: 50,
                candidateModel: V43Model);
            string v43Gated800 = SelectSpecificExpert(
                expertHits[BaselineModel], v43HitHistory, 800, minimumHistory: 200,
                candidateModel: V43Model);
            string v43DualRolling400 = SelectDualMetricRollingExpert(
                expertHits[BaselineModel],
                v43HitHistory,
                baselineRangeHitHistory,
                v43RangeHitHistory,
                window: 400,
                minimumHistory: 100,
                candidateModel: V43Model);
            outcomes[MetaModels[5]] = EvaluateScores(
                cumulativeHedge.CreateScores(scoreSets),
                target);
            outcomes[MetaModels[6]] = EvaluateScores(
                decayedHedge.CreateScores(scoreSets),
                target);
            var logisticScores = onlineLogistic.Predict(logisticFeatures);
            outcomes[MetaModels[7]] = EvaluateScores(logisticScores, target);
            outcomes[MetaModels[8]] = EvaluateScores(
                BlendScores(scoreSets[PointModelKind.Stable], logisticScores, 0.20),
                target);
            outcomes[MetaModels[9]] = outcomes[v45Gated400 == BaselineModel
                ? BaselineModel
                : V45Model];
            outcomes[MetaModels[10]] = outcomes[v45Gated800 == BaselineModel
                ? BaselineModel
                : V45Model];
            outcomes[MetaModels[11]] = outcomes[v45Significant400 == BaselineModel
                ? BaselineModel
                : V45Model];
            outcomes[MetaModels[12]] = EvaluateScores(
                brierHedge.CreateScores(scoreSets),
                target);
            outcomes[MetaModels[13]] = outcomes[v45DualSplit200 == BaselineModel
                ? BaselineModel
                : V45Model];
            outcomes[MetaModels[14]] = outcomes[v45DualSplit400 == BaselineModel
                ? BaselineModel
                : V45Model];
            outcomes[MetaModels[15]] = outcomes[v43Gated400 == BaselineModel
                ? BaselineModel
                : V43Model];
            outcomes[MetaModels[16]] = outcomes[v43DualSplit400 == BaselineModel
                ? BaselineModel
                : V43Model];
            outcomes[MetaModels[17]] = outcomes[v43Gated200 == BaselineModel
                ? BaselineModel
                : V43Model];
            outcomes[MetaModels[18]] = outcomes[v43Gated800 == BaselineModel
                ? BaselineModel
                : V43Model];
            outcomes[MetaModels[19]] = outcomes[v43DualRolling400 == BaselineModel
                ? BaselineModel
                : V43Model];
            outcomes[MetaModels[0]] = outcomes[best400];
            outcomes[MetaModels[1]] = outcomes[best800];
            outcomes[MetaModels[2]] = outcomes[conservative400];
            outcomes[MetaModels[3]] = outcomes[significant400];
            outcomes[MetaModels[4]] = outcomes[significant800];

            if (index >= startIndex)
            {
                int baselineHits = outcomes[BaselineModel].Hits;
                int baselineRangeHits = outcomes[BaselineModel].RangeHits;
                var baselinePoints = outcomes[BaselineModel].Points;
                foreach (string model in allModelNames)
                {
                    var outcome = outcomes[model];
                    observations[model].Add(new PointObservation(
                        target.Period,
                        outcome.Hits,
                        outcome.RangeHits,
                        outcome.RandomExpectedHits,
                        outcome.Hits - baselineHits,
                        outcome.RangeHits - baselineRangeHits,
                        outcome.Points,
                        !outcome.Points.SequenceEqual(baselinePoints)));
                }
            }

            foreach (string expert in ExpertPool) expertHits[expert].Add(outcomes[expert].Hits);
            v45HitHistory.Add(outcomes[V45Model].Hits);
            baselineRangeHitHistory.Add(outcomes[BaselineModel].RangeHits);
            v45RangeHitHistory.Add(outcomes[V45Model].RangeHits);
            v43HitHistory.Add(outcomes[V43Model].Hits);
            v43RangeHitHistory.Add(outcomes[V43Model].RangeHits);
            cumulativeHedge.Update(outcomes);
            decayedHedge.Update(outcomes);
            brierHedge.Update(scoreSets, target.RedBalls);
            onlineLogistic.Update(logisticFeatures, target.RedBalls);
            state.Add(target);
        }

        var reports = allModelNames.Select(model => BuildModelReport(
            model,
            observations[model],
            windowSize,
            windowCount)).ToArray();

        return new PositionPointResearchReport(
            records[^1].Period,
            targetCount,
            windowSize,
            BaselineModel,
            reports);
    }

    private static string SelectExpert(
        IReadOnlyDictionary<string, List<int>> hitHistory,
        int window,
        int minimumHistory,
        double minimumAdvantageToBaseline = 0)
    {
        int available = hitHistory[BaselineModel].Count;
        if (available < minimumHistory) return BaselineModel;
        int count = Math.Min(window, available);
        double baselineAverage = hitHistory[BaselineModel].TakeLast(count).Average();
        string best = BaselineModel;
        double bestAverage = baselineAverage;
        foreach (string expert in ExpertPool.Skip(1))
        {
            double average = hitHistory[expert].TakeLast(count).Average();
            if (average > bestAverage + 1e-12)
            {
                best = expert;
                bestAverage = average;
            }
        }
        return bestAverage >= baselineAverage + minimumAdvantageToBaseline ? best : BaselineModel;
    }

    private static string SelectSignificantExpert(
        IReadOnlyDictionary<string, List<int>> hitHistory,
        int window,
        int minimumHistory)
    {
        int available = hitHistory[BaselineModel].Count;
        if (available < minimumHistory) return BaselineModel;
        int count = Math.Min(window, available);
        var baseline = hitHistory[BaselineModel].TakeLast(count).ToArray();
        string best = BaselineModel;
        double bestLower95 = 0;
        foreach (string expert in ExpertPool.Skip(1))
        {
            var candidate = hitHistory[expert].TakeLast(count).ToArray();
            var differences = Enumerable.Range(0, count)
                .Select(index => (double)(candidate[index] - baseline[index]))
                .ToArray();
            double lower95 = MeanLower95(differences);
            if (lower95 > bestLower95 + 1e-12)
            {
                best = expert;
                bestLower95 = lower95;
            }
        }
        return best;
    }

    private static string SelectSpecificExpert(
        IReadOnlyList<int> baselineHits,
        IReadOnlyList<int> candidateHits,
        int window,
        int minimumHistory,
        string candidateModel,
        double minimumAdvantageToBaseline = 0)
    {
        if (baselineHits.Count < minimumHistory) return BaselineModel;
        int count = Math.Min(window, baselineHits.Count);
        double baselineAverage = baselineHits.TakeLast(count).Average();
        double candidateAverage = candidateHits.TakeLast(count).Average();
        return candidateAverage >= baselineAverage + minimumAdvantageToBaseline
            ? candidateModel
            : BaselineModel;
    }

    private static string SelectSignificantSpecificExpert(
        IReadOnlyList<int> baselineHits,
        IReadOnlyList<int> candidateHits,
        int window,
        int minimumHistory)
    {
        if (baselineHits.Count < minimumHistory) return BaselineModel;
        int count = Math.Min(window, baselineHits.Count);
        int baselineStart = baselineHits.Count - count;
        int candidateStart = candidateHits.Count - count;
        var differences = Enumerable.Range(0, count)
            .Select(index => (double)(candidateHits[candidateStart + index]
                - baselineHits[baselineStart + index]))
            .ToArray();
        return MeanLower95(differences) > 0 ? V45Model : BaselineModel;
    }

    private static string SelectDualMetricSplitExpert(
        IReadOnlyList<int> baselineHits,
        IReadOnlyList<int> candidateHits,
        IReadOnlyList<int> baselineRangeHits,
        IReadOnlyList<int> candidateRangeHits,
        int halfWindow,
        string candidateModel)
    {
        int required = 2 * halfWindow;
        if (baselineHits.Count < required) return BaselineModel;
        int start = baselineHits.Count - required;
        for (int segment = 0; segment < 2; segment++)
        {
            int segmentStart = start + segment * halfWindow;
            int hitAdvantage = 0;
            int rangeAdvantage = 0;
            for (int index = segmentStart; index < segmentStart + halfWindow; index++)
            {
                hitAdvantage += candidateHits[index] - baselineHits[index];
                rangeAdvantage += candidateRangeHits[index] - baselineRangeHits[index];
            }
            if (hitAdvantage < 0 || rangeAdvantage < 0) return BaselineModel;
        }
        return candidateModel;
    }

    private static string SelectDualMetricRollingExpert(
        IReadOnlyList<int> baselineHits,
        IReadOnlyList<int> candidateHits,
        IReadOnlyList<int> baselineRangeHits,
        IReadOnlyList<int> candidateRangeHits,
        int window,
        int minimumHistory,
        string candidateModel)
    {
        if (baselineHits.Count < minimumHistory) return BaselineModel;
        int count = Math.Min(window, baselineHits.Count);
        int start = baselineHits.Count - count;
        int hitAdvantage = 0;
        int rangeAdvantage = 0;
        for (int index = start; index < baselineHits.Count; index++)
        {
            hitAdvantage += candidateHits[index] - baselineHits[index];
            rangeAdvantage += candidateRangeHits[index] - baselineRangeHits[index];
        }
        return hitAdvantage >= 0 && rangeAdvantage >= 0
            ? candidateModel
            : BaselineModel;
    }

    private static PointOutcome EvaluateScores(
        IReadOnlyList<double> scores,
        DrawRecord target)
    {
        var points = SelectPoints(scores);
        var covered = ExpandCoverage(points);
        return new PointOutcome(
            target.RedBalls.Count(covered.Contains),
            CountRangeHits(points, target),
            6.0 * covered.Count / BallCount,
            points);
    }

    private static double[] BlendScores(
        IReadOnlyList<double> baseline,
        IReadOnlyList<double> candidate,
        double candidateWeight)
    {
        double baselineTotal = baseline.Skip(1).Sum();
        double candidateTotal = candidate.Skip(1).Sum();
        var result = new double[BallCount + 1];
        for (int ball = 1; ball <= BallCount; ball++)
        {
            result[ball] = (1 - candidateWeight) * baseline[ball] / baselineTotal
                + candidateWeight * candidate[ball] / candidateTotal;
        }
        return result;
    }

    private static PositionPointResearchModelReport BuildModelReport(
        string model,
        IReadOnlyList<PointObservation> observations,
        int windowSize,
        int requestedWindowCount)
    {
        int actualWindowCount = Math.Min(requestedWindowCount, observations.Count / windowSize);
        var windows = new List<PositionPointResearchWindow>(actualWindowCount);
        for (int offsetIndex = 0; offsetIndex < actualWindowCount; offsetIndex++)
        {
            int offset = offsetIndex * windowSize;
            var slice = observations
                .Skip(observations.Count - offset - windowSize)
                .Take(windowSize)
                .ToArray();
            windows.Add(BuildWindow(offset, slice));
        }

        var aggregate = BuildWindow(0, observations);
        int half = observations.Count / 2;
        double firstHalfLift = Average(observations.Take(half).Select(item => item.Lift));
        double secondHalfLift = Average(observations.Skip(half).Select(item => item.Lift));
        bool everyWindowAboveRandom = windows.Count == requestedWindowCount
            && windows.All(window => window.Lift > 0);
        bool everyWindowNoWorseThanStable = model == BaselineModel
            || windows.All(window => window.AdvantageToStable >= 0);
        bool passesGate = aggregate.LiftLower95 > 0
            && firstHalfLift > 0
            && secondHalfLift > 0
            && everyWindowAboveRandom
            && everyWindowNoWorseThanStable
            && (model == BaselineModel || aggregate.AdvantageToStableLower95 > 0);

        IReadOnlyList<PointObservation> validation;
        IReadOnlyList<PointObservation> development;
        if (observations.Count >= windowSize * 3)
        {
            validation = observations.Take(windowSize)
                .Concat(observations.TakeLast(windowSize))
                .ToArray();
            development = observations.Skip(windowSize)
                .Take(observations.Count - windowSize * 2)
                .ToArray();
        }
        else
        {
            validation = observations;
            development = observations;
        }

        var changed = observations.Where(item => item.DifferentFromStable).ToArray();
        var adjacent = observations.Zip(observations.Skip(1)).ToArray();
        return new PositionPointResearchModelReport(
            model,
            aggregate,
            firstHalfLift,
            secondHalfLift,
            everyWindowAboveRandom,
            everyWindowNoWorseThanStable,
            passesGate,
            windows)
        {
            DevelopmentAverageHits = development.Average(item => item.Hits),
            DevelopmentAdvantageToStable = development.Average(item => item.AdvantageToStable),
            DevelopmentAdvantageToStableLower95 = MeanLower95(
                development.Select(item => (double)item.AdvantageToStable).ToArray()),
            ValidationAverageHits = validation.Average(item => item.Hits),
            ValidationAdvantageToStable = validation.Average(item => item.AdvantageToStable),
            ValidationAdvantageToStableLower95 = MeanLower95(
                validation.Select(item => (double)item.AdvantageToStable).ToArray()),
            DevelopmentAverageRangeHits = development.Average(item => item.RangeHits),
            DevelopmentRangeAdvantageToStable = development.Average(
                item => item.RangeAdvantageToStable),
            ValidationAverageRangeHits = validation.Average(item => item.RangeHits),
            ValidationRangeAdvantageToStable = validation.Average(
                item => item.RangeAdvantageToStable),
            ExactRepeatRate = adjacent.Length == 0 ? 0 : adjacent.Count(pair =>
                pair.First.Points.SequenceEqual(pair.Second.Points)) / (double)adjacent.Length,
            AverageRetainedPoints = adjacent.Length == 0 ? PointCount : adjacent.Average(pair =>
                pair.First.Points.Intersect(pair.Second.Points).Count()),
            DifferentFromStableCount = changed.Length,
            ChangedAdvantageToStable = Average(changed.Select(item =>
                (double)item.AdvantageToStable)),
            ChangedAdvantageToStableLower95 = MeanLower95(changed.Select(item =>
                (double)item.AdvantageToStable).ToArray()),
            ChangedRangeAdvantageToStable = Average(changed.Select(item =>
                (double)item.RangeAdvantageToStable)),
            ChangedRangeAdvantageToStableLower95 = MeanLower95(changed.Select(item =>
                (double)item.RangeAdvantageToStable).ToArray())
        };
    }

    private static PositionPointResearchWindow BuildWindow(
        int offsetFromLatest,
        IReadOnlyList<PointObservation> observations)
    {
        var lifts = observations.Select(item => item.Lift).ToArray();
        var advantages = observations.Select(item => (double)item.AdvantageToStable).ToArray();
        return new PositionPointResearchWindow(
            offsetFromLatest,
            observations.Count,
            observations[0].Issue,
            observations[^1].Issue,
            observations.Average(item => item.Hits),
            observations.Average(item => item.RandomExpectedHits),
            Average(lifts),
            MeanLower95(lifts),
            Average(advantages),
            MeanLower95(advantages))
        {
            AverageRangeHits = observations.Average(item => item.RangeHits),
            RangeAdvantageToStable = observations.Average(item => item.RangeAdvantageToStable),
            RangeAdvantageToStableLower95 = MeanLower95(
                observations.Select(item => (double)item.RangeAdvantageToStable).ToArray())
        };
    }

    private static int[] SelectPoints(IReadOnlyList<double> scores)
    {
        var covered = new HashSet<int>();
        var selected = new List<int>(PointCount);
        while (selected.Count < PointCount)
        {
            int bestPoint = Enumerable.Range(1, BallCount)
                .Where(point => !selected.Contains(point))
                .OrderByDescending(point => Enumerable.Range(
                        Math.Max(1, point - PositionPointRange.Radius),
                        Math.Min(BallCount, point + PositionPointRange.Radius)
                            - Math.Max(1, point - PositionPointRange.Radius) + 1)
                    .Where(ball => !covered.Contains(ball))
                    .Sum(ball => scores[ball]))
                .ThenBy(point => point)
                .First();
            selected.Add(bestPoint);
            foreach (int ball in PointRange(bestPoint)) covered.Add(ball);
        }
        return selected.OrderBy(point => point).ToArray();
    }

    private static int[] SelectPointsGlobally(IReadOnlyList<double> scores)
    {
        // Full three-number ranges have centres 2..32. Dynamic programming finds
        // the best six non-overlapping ranges instead of committing greedily.
        var best = new PointSelection?[PointCount + 1, BallCount + 1];
        for (int centre = 0; centre <= BallCount; centre++)
            best[0, centre] = new PointSelection(0, Array.Empty<int>());

        for (int count = 1; count <= PointCount; count++)
        for (int centre = 2; centre < BallCount; centre++)
        {
            PointSelection? skipped = best[count, centre - 1];
            int previousCentre = centre - 3;
            PointSelection? previous = previousCentre >= 0
                ? best[count - 1, previousCentre]
                : count == 1 ? best[0, 0] : null;
            PointSelection? taken = previous is null
                ? null
                : new PointSelection(
                    previous.Score + PointRange(centre).Sum(ball => scores[ball]),
                    previous.Points.Append(centre).ToArray());
            best[count, centre] = BetterSelection(skipped, taken);
        }

        return best[PointCount, BallCount - 1]?.Points
            ?? throw new InvalidOperationException("Unable to select six point ranges.");
    }

    private static int[] SelectRangeEventPoints(IReadOnlyList<double> centreScores)
    {
        var selected = new List<int>(PointCount);
        foreach (int centre in Enumerable.Range(2, BallCount - 2)
                     .OrderByDescending(centre => centreScores[centre])
                     .ThenBy(centre => centre))
        {
            if (selected.Any(existing => Math.Abs(existing - centre) < 3)) continue;
            selected.Add(centre);
            if (selected.Count == PointCount) break;
        }
        if (selected.Count != PointCount)
            throw new InvalidOperationException("Unable to select six disjoint point ranges.");
        return selected.OrderBy(point => point).ToArray();
    }

    private static int[] SelectRangeEventPointsGlobally(IReadOnlyList<double> centreScores)
    {
        var best = new PointSelection?[PointCount + 1, BallCount + 1];
        for (int centre = 0; centre <= BallCount; centre++)
            best[0, centre] = new PointSelection(0, Array.Empty<int>());

        for (int count = 1; count <= PointCount; count++)
        for (int centre = 2; centre < BallCount; centre++)
        {
            PointSelection? skipped = best[count, centre - 1];
            int previousCentre = centre - 3;
            PointSelection? previous = previousCentre >= 0
                ? best[count - 1, previousCentre]
                : count == 1 ? best[0, 0] : null;
            PointSelection? taken = previous is null
                ? null
                : new PointSelection(
                    previous.Score + centreScores[centre],
                    previous.Points.Append(centre).ToArray());
            best[count, centre] = BetterSelection(skipped, taken);
        }

        return best[PointCount, BallCount - 1]?.Points
            ?? throw new InvalidOperationException("Unable to select six point ranges.");
    }

    private static int CountRangeHits(IEnumerable<int> points, DrawRecord target) =>
        points.Count(point => target.RedBalls.Any(ball =>
            PositionPointRange.Contains(point, ball, BallCount)));

    private static int[] SelectConsensusPoints(
        IReadOnlyDictionary<PointModelKind, double[]> scoreSets,
        bool includeLow,
        bool includeHigh)
    {
        var baseline = SelectPoints(scoreSets[PointModelKind.Stable]);
        var middle = SelectPoints(scoreSets[PointModelKind.ZoneRouteTransition10]);
        if (includeLow && !middle.SequenceEqual(
                SelectPoints(scoreSets[PointModelKind.ZoneRouteTransition05])))
            return baseline;
        if (includeHigh && !middle.SequenceEqual(
                SelectPoints(scoreSets[PointModelKind.ZoneRouteTransition15])))
            return baseline;
        return middle;
    }

    private static PointSelection? BetterSelection(PointSelection? first, PointSelection? second)
    {
        if (first is null) return second;
        if (second is null) return first;
        if (first.Score > second.Score + 1e-12) return first;
        if (second.Score > first.Score + 1e-12) return second;
        for (int index = 0; index < first.Points.Length; index++)
        {
            if (first.Points[index] < second.Points[index]) return first;
            if (second.Points[index] < first.Points[index]) return second;
        }
        return first;
    }

    private static HashSet<int> ExpandCoverage(IEnumerable<int> points) =>
        points.SelectMany(PointRange).ToHashSet();

    private static IEnumerable<int> PointRange(int point) => Enumerable.Range(
        Math.Max(1, point - PositionPointRange.Radius),
        Math.Min(BallCount, point + PositionPointRange.Radius)
            - Math.Max(1, point - PositionPointRange.Radius) + 1);

    private static double MeanLower95(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        double mean = values.Average();
        if (values.Count == 1) return mean;
        double variance = values.Sum(value => Math.Pow(value - mean, 2)) / (values.Count - 1);
        return mean - 1.959963984540054 * Math.Sqrt(variance / values.Count);
    }

    private static double Average(IEnumerable<double> values)
    {
        var materialized = values as double[] ?? values.ToArray();
        return materialized.Length == 0 ? 0 : materialized.Average();
    }

    private enum PointModelKind
    {
        Stable,
        RangeEventGreedy,
        RangeEventGlobal,
        StableGlobalCoverage,
        CustomFrequency,
        WeekdayFrequency05,
        WeekdayFrequency10,
        WeekdayFrequency20,
        WeekdayFrequency30,
        CustomSeasonFrequency,
        CustomShrunkSeasonFrequency,
        CustomSeasonTransition,
        CalendarSeasonTransition,
        V45BaselineBlend,
        ConfidenceRamp800,
        ConfidenceRamp1600,
        ConfidenceRamp2400,
        CategoryReversion15,
        CategoryReversion30Light,
        CategoryReversion30,
        CategoryReversion30Strong,
        CategoryMomentum30,
        CategoryTransition05,
        CategoryTransition10,
        CategoryTransition15,
        CategoryTransition20,
        CategoryTransition25,
        CategoryTransition30,
        ZoneTransition10,
        ParityTransition10,
        RouteTransition10,
        ZoneParityTransition10,
        ZoneRouteTransition10,
        ZoneRouteTransition05,
        ZoneRouteTransition15,
        ZoneRouteTransition20,
        ZoneRouteConsensusAll,
        ZoneRouteConsensusLow,
        ZoneRouteConsensusHigh,
        ZoneRouteJointTransition05,
        ZoneRouteJointTransition10,
        ZoneRouteJointTransition15,
        LocalRangeTransition05,
        LocalRangeTransition10,
        LocalRangeTransition15,
        ZoneRouteTransitionPrior60,
        ZoneRouteTransitionPrior120,
        ZoneRouteTransitionPrior240,
        ZoneRouteTransition05Prior120,
        ZoneRouteTransition15Prior120,
        ZoneTransition05Prior120,
        RouteTransition05Prior120,
        Zone2Route1Transition05Prior120,
        Zone1Route2Transition05Prior120,
        ParityRouteTransition10,
        StructureConsensus,
        StructureRobustMin,
        EmpiricalBayesRecent100,
        EmpiricalBayesRolling400,
        DriftAdaptiveShrunkRecent20,
        ForecastFirstMultiscaleStructure,
        ConfirmedTrendSwap,
        Ewma400,
        Ewma800,
        Ewma1600,
        Rolling400,
        Rolling800,
        Rolling1600,
        Repeat10,
        Neighbor10,
        StateGap10,
        StateGap20,
        BallRepeat20,
        BallNeighbor20,
        BallTransition20
    }

    private sealed record PointModel(
        string Name,
        PointModelKind Kind,
        int Window = 0,
        double Weight = 0,
        PointModelKind? SecondaryKind = null,
        double SecondaryWeight = 0,
        double PriorStrength = 0);
    private sealed record PointSelection(double Score, int[] Points);
    private sealed record TrendSwap(int PointIndex, int NewPoint, double Score);
    private sealed record TrendEvidence(bool Confirmed, double Score);
    private sealed record PointOutcome(
        int Hits,
        int RangeHits,
        double RandomExpectedHits,
        IReadOnlyList<int> Points);
    private sealed record PointObservation(
        int Issue,
        int Hits,
        int RangeHits,
        double RandomExpectedHits,
        int AdvantageToStable,
        int RangeAdvantageToStable,
        IReadOnlyList<int> Points,
        bool DifferentFromStable)
    {
        public double Lift => Hits - RandomExpectedHits;
    }

    private sealed class OnlineHedge
    {
        private static readonly IReadOnlyDictionary<string, PointModelKind> ExpertKinds =
            StaticModels.Where(model => ExpertPool.Contains(model.Name))
                .ToDictionary(model => model.Name, model => model.Kind);

        private readonly double _learningRate;
        private readonly double _decay;
        private readonly Dictionary<string, double> _logWeights = ExpertPool.ToDictionary(
            model => model,
            model => model == BaselineModel ? Math.Log(4.0) : 0.0);

        public OnlineHedge(double learningRate, double decay)
        {
            _learningRate = learningRate;
            _decay = decay;
        }

        public double[] CreateScores(IReadOnlyDictionary<PointModelKind, double[]> scoreSets)
        {
            double maxLogWeight = _logWeights.Values.Max();
            var weights = ExpertPool.ToDictionary(
                model => model,
                model => Math.Exp(_logWeights[model] - maxLogWeight));
            double weightTotal = weights.Values.Sum();
            var result = new double[BallCount + 1];
            foreach (string expert in ExpertPool)
            {
                var scores = scoreSets[ExpertKinds[expert]];
                double scoreTotal = scores.Skip(1).Sum();
                for (int ball = 1; ball <= BallCount; ball++)
                    result[ball] += weights[expert] / weightTotal * scores[ball] / scoreTotal;
            }
            return result;
        }

        public void Update(IReadOnlyDictionary<string, PointOutcome> outcomes)
        {
            int baselineHits = outcomes[BaselineModel].Hits;
            foreach (string expert in ExpertPool)
            {
                _logWeights[expert] = _decay * _logWeights[expert]
                    + _learningRate * (outcomes[expert].Hits - baselineHits);
            }
            double baselineLogWeight = _logWeights[BaselineModel];
            foreach (string expert in ExpertPool) _logWeights[expert] -= baselineLogWeight;
        }
    }

    private sealed class OnlineBrierHedge
    {
        private static readonly IReadOnlyDictionary<string, PointModelKind> ExpertKinds =
            ExpertPool.ToDictionary(
                model => model,
                model => StaticModels.Single(candidate => candidate.Name == model).Kind);

        private readonly double _learningRate;
        private readonly double _decay;
        private readonly Dictionary<string, double> _logWeights = ExpertPool.ToDictionary(
            model => model,
            model => model == BaselineModel ? Math.Log(4.0) : 0.0);

        public OnlineBrierHedge(double learningRate, double decay)
        {
            _learningRate = learningRate;
            _decay = decay;
        }

        public double[] CreateScores(IReadOnlyDictionary<PointModelKind, double[]> scoreSets)
        {
            double maxLogWeight = _logWeights.Values.Max();
            var weights = ExpertPool.ToDictionary(
                model => model,
                model => Math.Exp(_logWeights[model] - maxLogWeight));
            double weightTotal = weights.Values.Sum();
            var result = new double[BallCount + 1];
            foreach (string expert in ExpertPool)
            {
                var scores = scoreSets[ExpertKinds[expert]];
                double scoreTotal = scores.Skip(1).Sum();
                double weight = weights[expert] / weightTotal;
                for (int ball = 1; ball <= BallCount; ball++)
                    result[ball] += weight * 6.0 * scores[ball] / scoreTotal;
            }
            return result;
        }

        public void Update(
            IReadOnlyDictionary<PointModelKind, double[]> scoreSets,
            IReadOnlyList<int> actualBalls)
        {
            var actual = actualBalls.ToHashSet();
            foreach (string expert in ExpertPool)
            {
                var scores = scoreSets[ExpertKinds[expert]];
                double scoreTotal = scores.Skip(1).Sum();
                double loss = 0;
                for (int ball = 1; ball <= BallCount; ball++)
                {
                    double probability = 6.0 * scores[ball] / scoreTotal;
                    double observed = actual.Contains(ball) ? 1.0 : 0.0;
                    loss += Math.Pow(probability - observed, 2);
                }
                _logWeights[expert] = _decay * _logWeights[expert]
                    - _learningRate * loss;
            }

            double baselineLogWeight = _logWeights[BaselineModel];
            foreach (string expert in ExpertPool)
                _logWeights[expert] -= baselineLogWeight;
        }
    }

    private sealed class OnlineLogisticPointModel
    {
        private const double LearningRate = 0.05;
        private const double L2 = 0.0005;
        private readonly double[] _weights = new double[LogisticFeatureCount];
        private readonly double[] _squaredGradients = new double[LogisticFeatureCount];

        public OnlineLogisticPointModel()
        {
            _weights[0] = Math.Log(ExpectedBallRate / (1 - ExpectedBallRate));
        }

        public double[] Predict(IReadOnlyList<double[]> features)
        {
            var scores = new double[BallCount + 1];
            for (int ball = 1; ball <= BallCount; ball++)
                scores[ball] = Sigmoid(Dot(_weights, features[ball]));
            return scores;
        }

        public void Update(IReadOnlyList<double[]> features, IReadOnlyList<int> actualBalls)
        {
            var actual = actualBalls.ToHashSet();
            var predictions = Predict(features);
            var gradients = new double[LogisticFeatureCount];
            for (int ball = 1; ball <= BallCount; ball++)
            {
                double error = predictions[ball] - (actual.Contains(ball) ? 1.0 : 0.0);
                for (int feature = 0; feature < LogisticFeatureCount; feature++)
                    gradients[feature] += error * features[ball][feature] / BallCount;
            }

            for (int feature = 0; feature < LogisticFeatureCount; feature++)
            {
                if (feature != 0) gradients[feature] += L2 * _weights[feature];
                _squaredGradients[feature] += gradients[feature] * gradients[feature];
                _weights[feature] -= LearningRate * gradients[feature]
                    / (Math.Sqrt(_squaredGradients[feature]) + 1e-8);
            }
        }

        private static double Dot(IReadOnlyList<double> left, IReadOnlyList<double> right)
        {
            double total = 0;
            for (int index = 0; index < left.Count; index++) total += left[index] * right[index];
            return total;
        }

        private static double Sigmoid(double value)
        {
            if (value >= 0) return 1.0 / (1.0 + Math.Exp(-value));
            double exp = Math.Exp(value);
            return exp / (1.0 + exp);
        }
    }

    private sealed class RollingPointState
    {
        private readonly int[] _allCounts = new int[BallCount + 1];
        private readonly int[] _rangeEventCounts = new int[BallCount + 1];
        private readonly int[] _recentRangeEventCounts = new int[BallCount + 1];
        private readonly int[] _weekdayDrawCounts = new int[7];
        private readonly int[,] _weekdayBallCounts = new int[7, BallCount + 1];
        private readonly int[] _dayOfYearDrawCounts = new int[367];
        private readonly int[,] _dayOfYearBallCounts = new int[367, BallCount + 1];
        private readonly int[] _calendarDayDrawCounts = new int[367];
        private readonly int[,] _calendarDayBallCounts = new int[367, BallCount + 1];
        private readonly int[] _recentCounts = new int[BallCount + 1];
        private readonly Queue<HashSet<int>> _recent = new();
        private readonly Dictionary<int, double[]> _ewma = new()
        {
            [400] = Enumerable.Repeat(ExpectedBallRate, BallCount + 1).ToArray(),
            [800] = Enumerable.Repeat(ExpectedBallRate, BallCount + 1).ToArray(),
            [1600] = Enumerable.Repeat(ExpectedBallRate, BallCount + 1).ToArray()
        };
        private readonly Dictionary<int, Queue<HashSet<int>>> _rolling = new()
        {
            [400] = new(),
            [800] = new(),
            [1600] = new()
        };
        private readonly Dictionary<int, int[]> _rollingCounts = new()
        {
            [400] = new int[BallCount + 1],
            [800] = new int[BallCount + 1],
            [1600] = new int[BallCount + 1]
        };
        private readonly long[] _repeatExposures = new long[2];
        private readonly long[] _repeatHits = new long[2];
        private readonly long[] _neighborExposures = new long[2];
        private readonly long[] _neighborHits = new long[2];
        private readonly long[,] _ballRepeatExposures = new long[BallCount + 1, 2];
        private readonly long[,] _ballRepeatHits = new long[BallCount + 1, 2];
        private readonly long[,] _ballNeighborExposures = new long[BallCount + 1, 2];
        private readonly long[,] _ballNeighborHits = new long[BallCount + 1, 2];
        private readonly long[] _gapExposures = new long[13];
        private readonly long[] _gapHits = new long[13];
        private readonly long[,] _zoneStateExposures = new long[3, 7];
        private readonly long[,] _zoneStateNextCounts = new long[3, 7];
        private readonly long[,] _parityStateExposures = new long[2, 7];
        private readonly long[,] _parityStateNextCounts = new long[2, 7];
        private readonly long[,] _routeStateExposures = new long[3, 7];
        private readonly long[,] _routeStateNextCounts = new long[3, 7];
        private readonly long[,] _zoneRouteStateExposures = new long[9, 7];
        private readonly long[,] _zoneRouteStateNextCounts = new long[9, 7];
        private readonly long[,] _localRangeStateExposures = new long[BallCount + 1, 4];
        private readonly long[,] _localRangeStateNextCounts = new long[BallCount + 1, 4];
        private readonly int[] _gaps = new int[BallCount + 1];
        private HashSet<int>? _latest;
        private int _drawCount;

        public void Add(DrawRecord record)
        {
            var current = record.RedBalls.ToHashSet();
            if (_latest is not null)
            {
                UpdateCategoryTransitions(
                    _latest,
                    current,
                    _zoneStateExposures,
                    _zoneStateNextCounts,
                    3,
                    Zone);
                UpdateCategoryTransitions(
                    _latest,
                    current,
                    _parityStateExposures,
                    _parityStateNextCounts,
                    2,
                    ball => ball % 2);
                UpdateCategoryTransitions(
                    _latest,
                    current,
                    _routeStateExposures,
                    _routeStateNextCounts,
                    3,
                    ball => ball % 3);
                UpdateCategoryTransitions(
                    _latest,
                    current,
                    _zoneRouteStateExposures,
                    _zoneRouteStateNextCounts,
                    9,
                    ZoneRouteGroup);
                for (int centre = 2; centre < BallCount; centre++)
                {
                    int previousCount = PointRange(centre).Count(_latest.Contains);
                    int currentCount = PointRange(centre).Count(current.Contains);
                    _localRangeStateExposures[centre, previousCount]++;
                    _localRangeStateNextCounts[centre, previousCount] += currentCount;
                }
                for (int ball = 1; ball <= BallCount; ball++)
                {
                    int hit = current.Contains(ball) ? 1 : 0;
                    int repeated = _latest.Contains(ball) ? 1 : 0;
                    int neighbor = _latest.Contains(ball - 1) || _latest.Contains(ball + 1) ? 1 : 0;
                    int gapBucket = Math.Min(_gaps[ball], _gapExposures.Length - 1);
                    _repeatExposures[repeated]++;
                    _repeatHits[repeated] += hit;
                    _neighborExposures[neighbor]++;
                    _neighborHits[neighbor] += hit;
                    _ballRepeatExposures[ball, repeated]++;
                    _ballRepeatHits[ball, repeated] += hit;
                    _ballNeighborExposures[ball, neighbor]++;
                    _ballNeighborHits[ball, neighbor] += hit;
                    _gapExposures[gapBucket]++;
                    _gapHits[gapBucket] += hit;
                }
            }

            foreach (var pair in _ewma)
            {
                double alpha = 1 - Math.Exp(Math.Log(0.5) / pair.Key);
                for (int ball = 1; ball <= BallCount; ball++)
                    pair.Value[ball] += alpha * ((current.Contains(ball) ? 1.0 : 0.0) - pair.Value[ball]);
            }

            foreach (var pair in _rolling)
            {
                pair.Value.Enqueue(current);
                foreach (int ball in current) _rollingCounts[pair.Key][ball]++;
                if (pair.Value.Count <= pair.Key) continue;
                foreach (int ball in pair.Value.Dequeue()) _rollingCounts[pair.Key][ball]--;
            }

            int weekday = (int)record.DrawDate.DayOfWeek;
            _weekdayDrawCounts[weekday]++;
            foreach (int ball in current) _weekdayBallCounts[weekday, ball]++;
            int dayOfYear = record.DrawDate.DayOfYear;
            _dayOfYearDrawCounts[dayOfYear]++;
            foreach (int ball in current) _dayOfYearBallCounts[dayOfYear, ball]++;
            int calendarDay = CalendarDay(record.DrawDate);
            _calendarDayDrawCounts[calendarDay]++;
            foreach (int ball in current) _calendarDayBallCounts[calendarDay, ball]++;

            for (int ball = 1; ball <= BallCount; ball++)
            {
                if (current.Contains(ball))
                {
                    _allCounts[ball]++;
                    _recentCounts[ball]++;
                    _gaps[ball] = 0;
                }
                else
                {
                    _gaps[ball]++;
                }
            }

            for (int centre = 2; centre < BallCount; centre++)
            {
                if (!PointRange(centre).Any(current.Contains)) continue;
                _rangeEventCounts[centre]++;
                _recentRangeEventCounts[centre]++;
            }

            _recent.Enqueue(current);
            if (_recent.Count > 100)
            {
                var removed = _recent.Dequeue();
                foreach (int ball in removed) _recentCounts[ball]--;
                for (int centre = 2; centre < BallCount; centre++)
                    if (PointRange(centre).Any(removed.Contains))
                        _recentRangeEventCounts[centre]--;
            }
            _latest = current;
            _drawCount++;
        }

        public IReadOnlyDictionary<PointModelKind, double[]> CreateScores(
            DayOfWeek targetWeekday)
        {
            if (_latest is null || _drawCount == 0)
                throw new InvalidOperationException("Point state has no historical draw.");

            var longRates = NewScores();
            var recentRates = NewScores();
            var rolling400Rates = NewScores();
            for (int ball = 1; ball <= BallCount; ball++)
            {
                longRates[ball] = _allCounts[ball] / (double)_drawCount;
                recentRates[ball] = _recentCounts[ball] / (double)_recent.Count;
                rolling400Rates[ball] = RollingRate(400, ball);
            }
            double recentReliability = EmpiricalBayesReliability(
                longRates,
                recentRates,
                _drawCount,
                _recent.Count);
            double rolling400Reliability = EmpiricalBayesReliability(
                longRates,
                rolling400Rates,
                _drawCount,
                _rolling[400].Count);
            var categoryReversion15 = CreateCategoryReversionScores(longRates, recentRates, 15, 0.50);
            var categoryReversion30Light = CreateCategoryReversionScores(longRates, recentRates, 30, 0.25);
            var categoryReversion30 = CreateCategoryReversionScores(longRates, recentRates, 30, 0.50);
            var categoryReversion30Strong = CreateCategoryReversionScores(longRates, recentRates, 30, 0.75);
            var categoryMomentum30 = CreateCategoryReversionScores(longRates, recentRates, 30, -0.50);
            var categoryTransition05 = CreateCategoryTransitionScores(longRates, recentRates, 0.05);
            var categoryTransition10 = CreateCategoryTransitionScores(longRates, recentRates, 0.10);
            var categoryTransition15 = CreateCategoryTransitionScores(longRates, recentRates, 0.15);
            var categoryTransition20 = CreateCategoryTransitionScores(longRates, recentRates, 0.20);
            var categoryTransition25 = CreateCategoryTransitionScores(longRates, recentRates, 0.25);
            var categoryTransition30 = CreateCategoryTransitionScores(longRates, recentRates, 0.30);
            var zoneTransition10 = CreateCategoryTransitionScores(
                longRates, recentRates, 0.10, useZone: true, useParity: false, useRoute: false);
            var parityTransition10 = CreateCategoryTransitionScores(
                longRates, recentRates, 0.10, useZone: false, useParity: true, useRoute: false);
            var routeTransition10 = CreateCategoryTransitionScores(
                longRates, recentRates, 0.10, useZone: false, useParity: false, useRoute: true);
            var zoneParityTransition10 = CreateCategoryTransitionScores(
                longRates, recentRates, 0.10, useZone: true, useParity: true, useRoute: false);
            var zoneRouteTransition10 = CreateCategoryTransitionScores(
                longRates, recentRates, 0.10, useZone: true, useParity: false, useRoute: true);
            var zoneRouteTransition05 = CreateCategoryTransitionScores(
                longRates, recentRates, 0.05, useZone: true, useParity: false, useRoute: true);
            var zoneRouteTransition15 = CreateCategoryTransitionScores(
                longRates, recentRates, 0.15, useZone: true, useParity: false, useRoute: true);
            var zoneRouteTransition20 = CreateCategoryTransitionScores(
                longRates, recentRates, 0.20, useZone: true, useParity: false, useRoute: true);
            var parityRouteTransition10 = CreateCategoryTransitionScores(
                longRates, recentRates, 0.10, useZone: false, useParity: true, useRoute: true);
            var zoneRouteJointTransition05 = CreateJointZoneRouteTransitionScores(
                longRates, recentRates, 0.05);
            var zoneRouteJointTransition10 = CreateJointZoneRouteTransitionScores(
                longRates, recentRates, 0.10);
            var zoneRouteJointTransition15 = CreateJointZoneRouteTransitionScores(
                longRates, recentRates, 0.15);
            var localRangeTransition05 = CreateLocalRangeTransitionScores(
                longRates, recentRates, 0.05);
            var localRangeTransition10 = CreateLocalRangeTransitionScores(
                longRates, recentRates, 0.10);
            var localRangeTransition15 = CreateLocalRangeTransitionScores(
                longRates, recentRates, 0.15);
            var zoneRouteTransitionPrior60 = CreateCategoryTransitionScores(
                longRates, recentRates, 0.10,
                useZone: true, useParity: false, useRoute: true, priorStrength: 60);
            var zoneRouteTransitionPrior120 = CreateCategoryTransitionScores(
                longRates, recentRates, 0.10,
                useZone: true, useParity: false, useRoute: true, priorStrength: 120);
            var zoneRouteTransitionPrior240 = CreateCategoryTransitionScores(
                longRates, recentRates, 0.10,
                useZone: true, useParity: false, useRoute: true, priorStrength: 240);
            var zoneRouteTransition05Prior120 = CreateCategoryTransitionScores(
                longRates, recentRates, 0.05,
                useZone: true, useParity: false, useRoute: true, priorStrength: 120);
            var zoneRouteTransition15Prior120 = CreateCategoryTransitionScores(
                longRates, recentRates, 0.15,
                useZone: true, useParity: false, useRoute: true, priorStrength: 120);
            var zoneTransition05Prior120 = CreateCategoryTransitionScores(
                longRates, recentRates, 0.05,
                useZone: true, useParity: false, useRoute: false, priorStrength: 120);
            var routeTransition05Prior120 = CreateCategoryTransitionScores(
                longRates, recentRates, 0.05,
                useZone: false, useParity: false, useRoute: true, priorStrength: 120);
            var zone2Route1Transition05Prior120 = CreateWeightedZoneRouteTransitionScores(
                longRates, recentRates, 0.05, zoneWeight: 2.0 / 3.0, priorStrength: 120);
            var zone1Route2Transition05Prior120 = CreateWeightedZoneRouteTransitionScores(
                longRates, recentRates, 0.05, zoneWeight: 1.0 / 3.0, priorStrength: 120);
            var structureConsensus = NewScores();
            var structureRobustMin = NewScores();

            var stable = NewScores();
            var confidenceRamp800 = NewScores();
            var confidenceRamp1600 = NewScores();
            var confidenceRamp2400 = NewScores();
            var empiricalBayesRecent100 = NewScores();
            var empiricalBayesRolling400 = NewScores();
            var driftAdaptiveShrunkRecent20 = CreateDriftAdaptiveShrunkScores(longRates);
            var forecastFirstMultiscaleStructure = CreateForecastFirstMultiscaleScores(longRates);
            var ewma400 = NewScores();
            var ewma800 = NewScores();
            var ewma1600 = NewScores();
            var rolling400 = NewScores();
            var rolling800 = NewScores();
            var rolling1600 = NewScores();
            var repeat10 = NewScores();
            var neighbor10 = NewScores();
            var stateGap10 = NewScores();
            var stateGap20 = NewScores();
            var ballRepeat20 = NewScores();
            var ballNeighbor20 = NewScores();
            var ballTransition20 = NewScores();
            var weekdayFrequency05 = NewScores();
            var weekdayFrequency10 = NewScores();
            var weekdayFrequency20 = NewScores();
            var weekdayFrequency30 = NewScores();

            int weekday = (int)targetWeekday;
            int weekdayDrawCount = _weekdayDrawCounts[weekday];

            for (int ball = 1; ball <= BallCount; ball++)
            {
                double longRate = longRates[ball];
                double recentRate = recentRates[ball];
                double baseScore = 0.80 * longRate + 0.20 * recentRate;
                double weekdayRate = weekdayDrawCount == 0
                    ? longRate
                    : _weekdayBallCounts[weekday, ball] / (double)weekdayDrawCount;
                double repeatRatio = ConditionalRatio(
                    _latest.Contains(ball) ? 1 : 0,
                    _repeatHits,
                    _repeatExposures);
                double neighborRatio = ConditionalRatio(
                    _latest.Contains(ball - 1) || _latest.Contains(ball + 1) ? 1 : 0,
                    _neighborHits,
                    _neighborExposures);
                int gapBucket = Math.Min(_gaps[ball], _gapExposures.Length - 1);
                double gapRatio = SmoothedRatio(_gapHits[gapBucket], _gapExposures[gapBucket]);
                int repeated = _latest.Contains(ball) ? 1 : 0;
                int hasNeighbor = _latest.Contains(ball - 1) || _latest.Contains(ball + 1) ? 1 : 0;
                double ballRepeat = BallConditionalRate(
                    ball,
                    repeated,
                    _ballRepeatHits,
                    _ballRepeatExposures,
                    _repeatHits,
                    _repeatExposures);
                double ballNeighbor = BallConditionalRate(
                    ball,
                    hasNeighbor,
                    _ballNeighborHits,
                    _ballNeighborExposures,
                    _neighborHits,
                    _neighborExposures);

                stable[ball] = baseScore;
                confidenceRamp800[ball] = ConfidenceRampScore(longRate, recentRate, _drawCount, 800);
                confidenceRamp1600[ball] = ConfidenceRampScore(longRate, recentRate, _drawCount, 1600);
                confidenceRamp2400[ball] = ConfidenceRampScore(longRate, recentRate, _drawCount, 2400);
                empiricalBayesRecent100[ball] = longRate
                    + recentReliability * (recentRate - longRate);
                empiricalBayesRolling400[ball] = longRate
                    + rolling400Reliability * (rolling400Rates[ball] - longRate);
                ewma400[ball] = 0.80 * longRate + 0.20 * _ewma[400][ball];
                ewma800[ball] = 0.80 * longRate + 0.20 * _ewma[800][ball];
                ewma1600[ball] = 0.80 * longRate + 0.20 * _ewma[1600][ball];
                rolling400[ball] = 0.80 * longRate + 0.20 * RollingRate(400, ball);
                rolling800[ball] = 0.80 * longRate + 0.20 * RollingRate(800, ball);
                rolling1600[ball] = 0.80 * longRate + 0.20 * RollingRate(1600, ball);
                repeat10[ball] = baseScore * (0.90 + 0.10 * repeatRatio);
                neighbor10[ball] = baseScore * (0.90 + 0.10 * neighborRatio);
                double stateRatio = (repeatRatio + neighborRatio + gapRatio) / 3.0;
                stateGap10[ball] = baseScore * (0.90 + 0.10 * stateRatio);
                stateGap20[ball] = baseScore * (0.80 + 0.20 * stateRatio);
                ballRepeat20[ball] = 0.80 * baseScore + 0.20 * ballRepeat;
                ballNeighbor20[ball] = 0.80 * baseScore + 0.20 * ballNeighbor;
                ballTransition20[ball] = 0.80 * baseScore + 0.10 * ballRepeat + 0.10 * ballNeighbor;
                weekdayFrequency05[ball] = 0.95 * baseScore + 0.05 * weekdayRate;
                weekdayFrequency10[ball] = 0.90 * baseScore + 0.10 * weekdayRate;
                weekdayFrequency20[ball] = 0.80 * baseScore + 0.20 * weekdayRate;
                weekdayFrequency30[ball] = 0.70 * baseScore + 0.30 * weekdayRate;
            }

            for (int ball = 1; ball <= BallCount; ball++)
            {
                structureConsensus[ball] = (stable[ball]
                    + categoryReversion30[ball]
                    + categoryTransition20[ball]) / 3.0;
                structureRobustMin[ball] = Math.Min(
                    stable[ball],
                    Math.Min(categoryReversion30[ball], categoryTransition20[ball]));
            }

            return new Dictionary<PointModelKind, double[]>
            {
                [PointModelKind.Stable] = stable,
                [PointModelKind.ConfidenceRamp800] = confidenceRamp800,
                [PointModelKind.ConfidenceRamp1600] = confidenceRamp1600,
                [PointModelKind.ConfidenceRamp2400] = confidenceRamp2400,
                [PointModelKind.CategoryReversion15] = categoryReversion15,
                [PointModelKind.CategoryReversion30Light] = categoryReversion30Light,
                [PointModelKind.CategoryReversion30] = categoryReversion30,
                [PointModelKind.CategoryReversion30Strong] = categoryReversion30Strong,
                [PointModelKind.CategoryMomentum30] = categoryMomentum30,
                [PointModelKind.CategoryTransition05] = categoryTransition05,
                [PointModelKind.CategoryTransition10] = categoryTransition10,
                [PointModelKind.CategoryTransition15] = categoryTransition15,
                [PointModelKind.CategoryTransition20] = categoryTransition20,
                [PointModelKind.CategoryTransition25] = categoryTransition25,
                [PointModelKind.CategoryTransition30] = categoryTransition30,
                [PointModelKind.ZoneTransition10] = zoneTransition10,
                [PointModelKind.ParityTransition10] = parityTransition10,
                [PointModelKind.RouteTransition10] = routeTransition10,
                [PointModelKind.ZoneParityTransition10] = zoneParityTransition10,
                [PointModelKind.ZoneRouteTransition10] = zoneRouteTransition10,
                [PointModelKind.ZoneRouteTransition05] = zoneRouteTransition05,
                [PointModelKind.ZoneRouteTransition15] = zoneRouteTransition15,
                [PointModelKind.ZoneRouteTransition20] = zoneRouteTransition20,
                [PointModelKind.ParityRouteTransition10] = parityRouteTransition10,
                [PointModelKind.ZoneRouteJointTransition05] = zoneRouteJointTransition05,
                [PointModelKind.ZoneRouteJointTransition10] = zoneRouteJointTransition10,
                [PointModelKind.ZoneRouteJointTransition15] = zoneRouteJointTransition15,
                [PointModelKind.LocalRangeTransition05] = localRangeTransition05,
                [PointModelKind.LocalRangeTransition10] = localRangeTransition10,
                [PointModelKind.LocalRangeTransition15] = localRangeTransition15,
                [PointModelKind.ZoneRouteTransitionPrior60] = zoneRouteTransitionPrior60,
                [PointModelKind.ZoneRouteTransitionPrior120] = zoneRouteTransitionPrior120,
                [PointModelKind.ZoneRouteTransitionPrior240] = zoneRouteTransitionPrior240,
                [PointModelKind.ZoneRouteTransition05Prior120] = zoneRouteTransition05Prior120,
                [PointModelKind.ZoneRouteTransition15Prior120] = zoneRouteTransition15Prior120,
                [PointModelKind.ZoneTransition05Prior120] = zoneTransition05Prior120,
                [PointModelKind.RouteTransition05Prior120] = routeTransition05Prior120,
                [PointModelKind.Zone2Route1Transition05Prior120] = zone2Route1Transition05Prior120,
                [PointModelKind.Zone1Route2Transition05Prior120] = zone1Route2Transition05Prior120,
                [PointModelKind.StructureConsensus] = structureConsensus,
                [PointModelKind.StructureRobustMin] = structureRobustMin,
                [PointModelKind.EmpiricalBayesRecent100] = empiricalBayesRecent100,
                [PointModelKind.EmpiricalBayesRolling400] = empiricalBayesRolling400,
                [PointModelKind.DriftAdaptiveShrunkRecent20] = driftAdaptiveShrunkRecent20,
                [PointModelKind.ForecastFirstMultiscaleStructure] = forecastFirstMultiscaleStructure,
                [PointModelKind.Ewma400] = ewma400,
                [PointModelKind.Ewma800] = ewma800,
                [PointModelKind.Ewma1600] = ewma1600,
                [PointModelKind.Rolling400] = rolling400,
                [PointModelKind.Rolling800] = rolling800,
                [PointModelKind.Rolling1600] = rolling1600,
                [PointModelKind.Repeat10] = repeat10,
                [PointModelKind.Neighbor10] = neighbor10,
                [PointModelKind.StateGap10] = stateGap10,
                [PointModelKind.StateGap20] = stateGap20,
                [PointModelKind.BallRepeat20] = ballRepeat20,
                [PointModelKind.BallNeighbor20] = ballNeighbor20,
                [PointModelKind.BallTransition20] = ballTransition20,
                [PointModelKind.WeekdayFrequency05] = weekdayFrequency05,
                [PointModelKind.WeekdayFrequency10] = weekdayFrequency10,
                [PointModelKind.WeekdayFrequency20] = weekdayFrequency20,
                [PointModelKind.WeekdayFrequency30] = weekdayFrequency30
            };
        }

        public double[] CreateSeasonFrequencyScores(
            int targetDayOfYear,
            int radius,
            double seasonWeight)
        {
            if (_drawCount == 0)
                throw new InvalidOperationException("Point state has no historical draw.");
            if (radius is < 1 or > 182)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (seasonWeight is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(seasonWeight));

            var longRates = NewScores();
            var recentRates = NewScores();
            for (int ball = 1; ball <= BallCount; ball++)
            {
                longRates[ball] = _allCounts[ball] / (double)_drawCount;
                recentRates[ball] = _recentCounts[ball] / (double)_recent.Count;
            }
            var seasonRates = CreateSeasonRates(targetDayOfYear, radius, longRates);
            var scores = NewScores();
            for (int ball = 1; ball <= BallCount; ball++)
            {
                double baseScore = 0.80 * longRates[ball] + 0.20 * recentRates[ball];
                scores[ball] = (1 - seasonWeight) * baseScore
                    + seasonWeight * seasonRates[ball];
            }
            return scores;
        }

        public double[] CreateShrunkSeasonFrequencyScores(
            int targetDayOfYear,
            int radius,
            double seasonWeight,
            double priorStrength)
        {
            if (_drawCount == 0)
                throw new InvalidOperationException("Point state has no historical draw.");
            if (radius is < 1 or > 182)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (seasonWeight is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(seasonWeight));
            if (priorStrength < 0)
                throw new ArgumentOutOfRangeException(nameof(priorStrength));

            var longRates = NewScores();
            var recentRates = NewScores();
            for (int ball = 1; ball <= BallCount; ball++)
            {
                longRates[ball] = _allCounts[ball] / (double)_drawCount;
                recentRates[ball] = _recentCounts[ball] / (double)_recent.Count;
            }

            int seasonDrawCount = 0;
            var seasonCounts = new int[BallCount + 1];
            for (int day = 1; day <= 366; day++)
            {
                int distance = Math.Abs(day - targetDayOfYear);
                distance = Math.Min(distance, 366 - distance);
                if (distance > radius) continue;
                seasonDrawCount += _dayOfYearDrawCounts[day];
                for (int ball = 1; ball <= BallCount; ball++)
                    seasonCounts[ball] += _dayOfYearBallCounts[day, ball];
            }

            var scores = NewScores();
            for (int ball = 1; ball <= BallCount; ball++)
            {
                double baseScore = 0.80 * longRates[ball] + 0.20 * recentRates[ball];
                double seasonRate = (seasonCounts[ball] + priorStrength * longRates[ball])
                    / (seasonDrawCount + priorStrength);
                scores[ball] = (1 - seasonWeight) * baseScore + seasonWeight * seasonRate;
            }
            return scores;
        }

        public double[] CreateCalendarShrunkSeasonFrequencyScores(
            DateTime targetDate,
            int radius,
            double seasonWeight,
            double priorStrength)
        {
            if (_drawCount == 0)
                throw new InvalidOperationException("Point state has no historical draw.");
            if (radius is < 1 or > 182)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (seasonWeight is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(seasonWeight));
            if (priorStrength < 0)
                throw new ArgumentOutOfRangeException(nameof(priorStrength));

            var longRates = NewScores();
            var recentRates = NewScores();
            for (int ball = 1; ball <= BallCount; ball++)
            {
                longRates[ball] = _allCounts[ball] / (double)_drawCount;
                recentRates[ball] = _recentCounts[ball] / (double)_recent.Count;
            }

            int targetCalendarDay = CalendarDay(targetDate);
            int seasonDrawCount = 0;
            var seasonCounts = new int[BallCount + 1];
            for (int day = 1; day <= 366; day++)
            {
                int distance = Math.Abs(day - targetCalendarDay);
                distance = Math.Min(distance, 366 - distance);
                if (distance > radius) continue;
                seasonDrawCount += _calendarDayDrawCounts[day];
                for (int ball = 1; ball <= BallCount; ball++)
                    seasonCounts[ball] += _calendarDayBallCounts[day, ball];
            }

            var scores = NewScores();
            for (int ball = 1; ball <= BallCount; ball++)
            {
                double baseScore = 0.80 * longRates[ball] + 0.20 * recentRates[ball];
                double seasonRate = (seasonCounts[ball] + priorStrength * longRates[ball])
                    / (seasonDrawCount + priorStrength);
                scores[ball] = (1 - seasonWeight) * baseScore + seasonWeight * seasonRate;
            }
            return scores;
        }

        private double[] CreateSeasonRates(
            int targetDayOfYear,
            int radius,
            IReadOnlyList<double> fallbackRates)
        {
            int drawCount = 0;
            var counts = new int[BallCount + 1];
            for (int day = 1; day <= 366; day++)
            {
                int distance = Math.Abs(day - targetDayOfYear);
                distance = Math.Min(distance, 366 - distance);
                if (distance > radius) continue;
                drawCount += _dayOfYearDrawCounts[day];
                for (int ball = 1; ball <= BallCount; ball++)
                    counts[ball] += _dayOfYearBallCounts[day, ball];
            }

            var rates = NewScores();
            for (int ball = 1; ball <= BallCount; ball++)
                rates[ball] = drawCount == 0 ? fallbackRates[ball] : counts[ball] / (double)drawCount;
            return rates;
        }

        public double[] CreateFrequencyScores(int recentWindow, double recentWeight)
        {
            if (_drawCount == 0)
                throw new InvalidOperationException("Point state has no historical draw.");
            if (recentWindow is < 1 or > 100)
                throw new ArgumentOutOfRangeException(nameof(recentWindow));
            if (recentWeight is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(recentWeight));

            int actualWindow = Math.Min(recentWindow, _recent.Count);
            var recentCounts = WindowCounts(actualWindow);
            var scores = NewScores();
            for (int ball = 1; ball <= BallCount; ball++)
            {
                double longRate = _allCounts[ball] / (double)_drawCount;
                double recentRate = recentCounts[ball] / (double)actualWindow;
                scores[ball] = (1 - recentWeight) * longRate + recentWeight * recentRate;
            }
            return scores;
        }

        public double[] CreateRangeEventScores()
        {
            if (_drawCount == 0)
                throw new InvalidOperationException("Point state has no historical draw.");
            var scores = NewScores();
            for (int centre = 2; centre < BallCount; centre++)
            {
                double longRate = _rangeEventCounts[centre] / (double)_drawCount;
                double recentRate = _recentRangeEventCounts[centre] / (double)_recent.Count;
                scores[centre] = 0.80 * longRate + 0.20 * recentRate;
            }
            return scores;
        }

        public IReadOnlyList<double[]> CreateLogisticFeatures()
        {
            if (_latest is null || _drawCount == 0)
                throw new InvalidOperationException("Point state has no historical draw.");

            var counts30 = WindowCounts(30);
            var counts15 = WindowCounts(15);
            int window30Count = Math.Min(30, _recent.Count);
            int window15Count = Math.Min(15, _recent.Count);
            var window30 = _recent.TakeLast(window30Count).ToArray();
            var zoneCounts = new int[3];
            var parityCounts = new int[2];
            var routeCounts = new int[3];
            foreach (var draw in window30)
            foreach (int value in draw)
            {
                zoneCounts[Zone(value)]++;
                parityCounts[value % 2]++;
                routeCounts[value % 3]++;
            }

            var result = new double[BallCount + 1][];
            result[0] = new double[LogisticFeatureCount];
            for (int ball = 1; ball <= BallCount; ball++)
            {
                var features = new double[LogisticFeatureCount];
                double longRate = _allCounts[ball] / (double)_drawCount;
                double recent100Rate = _recentCounts[ball] / (double)_recent.Count;
                double recent30Rate = counts30[ball] / (double)window30Count;
                double recent15Rate = counts15[ball] / (double)window15Count;
                int repeated = _latest.Contains(ball) ? 1 : 0;
                int neighbor = _latest.Contains(ball - 1) || _latest.Contains(ball + 1) ? 1 : 0;
                double ballRepeat = BallConditionalRate(
                    ball,
                    repeated,
                    _ballRepeatHits,
                    _ballRepeatExposures,
                    _repeatHits,
                    _repeatExposures);
                double ballNeighbor = BallConditionalRate(
                    ball,
                    neighbor,
                    _ballNeighborHits,
                    _ballNeighborExposures,
                    _neighborHits,
                    _neighborExposures);

                features[0] = 1;
                features[ball] = 1;
                features[34] = (ball - 17) / 16.0;
                features[35] = 5 * (longRate - ExpectedBallRate);
                features[36] = 5 * (recent100Rate - ExpectedBallRate);
                features[37] = 5 * (recent30Rate - ExpectedBallRate);
                features[38] = 5 * (recent15Rate - ExpectedBallRate);
                features[39] = Math.Min(_gaps[ball], 20) / 20.0;
                features[40] = repeated;
                features[41] = neighbor;
                features[42] = CategoryDeficit(
                    zoneCounts[Zone(ball)], window30Count, ZonePopulation(Zone(ball)));
                features[43] = CategoryDeficit(
                    parityCounts[ball % 2], window30Count, ParityPopulation(ball % 2));
                features[44] = CategoryDeficit(
                    routeCounts[ball % 3], window30Count, RoutePopulation(ball % 3));
                features[45] = 5 * ((ballRepeat + ballNeighbor) / 2.0 - ExpectedBallRate);
                result[ball] = features;
            }
            return result;
        }

        private int[] WindowCounts(int windowSize)
        {
            var counts = new int[BallCount + 1];
            foreach (var draw in _recent.TakeLast(Math.Min(windowSize, _recent.Count)))
            foreach (int ball in draw)
                counts[ball]++;
            return counts;
        }

        public int[] SelectConfirmedTrendSwapPoints(IReadOnlyList<double> stableScores)
        {
            var points = SelectPoints(stableScores).ToList();
            while (true)
            {
                TrendSwap? best = null;
                for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
                {
                    int currentPoint = points[pointIndex];
                    var otherCoverage = points.Where((_, index) => index != pointIndex)
                        .SelectMany(PointRange)
                        .ToHashSet();
                    for (int candidatePoint = 1; candidatePoint <= BallCount; candidatePoint++)
                    {
                        if (points.Contains(candidatePoint)
                            || PointRange(candidatePoint).Any(otherCoverage.Contains)) continue;
                        var evidence = TrendEvidence(currentPoint, candidatePoint);
                        if (!evidence.Confirmed) continue;
                        var candidate = new TrendSwap(pointIndex, candidatePoint, evidence.Score);
                        if (best is null
                            || candidate.Score > best.Score + 1e-12
                            || Math.Abs(candidate.Score - best.Score) <= 1e-12
                                && candidate.NewPoint < best.NewPoint)
                            best = candidate;
                    }
                }

                if (best is null) break;
                int oldPoint = points[best.PointIndex];
                points[best.PointIndex] = best.NewPoint;
                points.Sort();
                if (points.Contains(oldPoint)) break;
            }
            return points.ToArray();
        }

        private TrendEvidence TrendEvidence(int currentPoint, int candidatePoint)
        {
            var differences30 = _recent.TakeLast(Math.Min(30, _recent.Count))
                .Select(draw => PointRange(candidatePoint).Count(draw.Contains)
                    - PointRange(currentPoint).Count(draw.Contains))
                .Select(value => (double)value)
                .ToArray();
            double mean30 = Average(differences30);
            double mean15 = Average(differences30.TakeLast(Math.Min(15, differences30.Length)));
            double mean5 = Average(differences30.TakeLast(Math.Min(5, differences30.Length)));
            if (mean30 <= 0 || mean15 <= 0 || mean5 <= 0)
                return new TrendEvidence(false, 0);

            double variance = differences30.Length <= 1 ? 0 : differences30.Sum(value =>
                Math.Pow(value - mean30, 2)) / (differences30.Length - 1);
            const double oneSided90Z = 1.2815515655446004;
            double lower90 = mean30 - oneSided90Z
                * Math.Sqrt(variance / differences30.Length);
            return new TrendEvidence(
                lower90 > 0,
                mean30 + 0.50 * mean15 + 0.25 * mean5);
        }

        private double[] CreateDriftAdaptiveShrunkScores(IReadOnlyList<double> longRates)
        {
            var history = _rolling[1600].ToArray();
            int windowSize = SelectLongestStationarySuffix(history);
            var localRates = NewScores();
            foreach (var draw in history.TakeLast(windowSize))
            foreach (int ball in draw)
                localRates[ball]++;
            for (int ball = 1; ball <= BallCount; ball++)
                localRates[ball] /= windowSize;

            double reliability = EmpiricalBayesReliability(
                longRates,
                localRates,
                _drawCount,
                windowSize);
            var scores = NewScores();
            for (int ball = 1; ball <= BallCount; ball++)
            {
                double shrunkLocal = longRates[ball]
                    + reliability * (localRates[ball] - longRates[ball]);
                scores[ball] = 0.80 * longRates[ball] + 0.20 * shrunkLocal;
            }
            return scores;
        }

        private double[] CreateForecastFirstMultiscaleScores(IReadOnlyList<double> longRates)
        {
            int window30 = Math.Min(30, _recent.Count);
            int window15 = Math.Min(15, _recent.Count);
            int window5 = Math.Min(5, _recent.Count);
            var latest = _latest ?? throw new InvalidOperationException("Point state has no historical draw.");
            var counts30 = WindowCounts(window30);
            var counts15 = WindowCounts(window15);
            var counts5 = WindowCounts(window5);
            var latestZoneCounts = CategoryCounts(latest, 3, Zone);
            var latestParityCounts = CategoryCounts(latest, 2, ball => ball % 2);
            var latestRouteCounts = CategoryCounts(latest, 3, ball => ball % 3);
            var scores = NewScores();

            for (int ball = 1; ball <= BallCount; ball++)
            {
                double recent30 = counts30[ball] / (double)window30;
                double recent15 = counts15[ball] / (double)window15;
                double recent5 = counts5[ball] / (double)window5;
                double multiscale = 0.20 * longRates[ball]
                    + 0.30 * recent30
                    + 0.30 * recent15
                    + 0.20 * recent5;

                double zoneRatio = CategoryTransitionRatio(
                    Zone(ball), latestZoneCounts[Zone(ball)], _zoneStateExposures,
                    _zoneStateNextCounts, ZonePopulation(Zone(ball)), longRates, Zone, 120);
                double parityRatio = CategoryTransitionRatio(
                    ball % 2, latestParityCounts[ball % 2], _parityStateExposures,
                    _parityStateNextCounts, ParityPopulation(ball % 2), longRates,
                    value => value % 2, 120);
                double routeRatio = CategoryTransitionRatio(
                    ball % 3, latestRouteCounts[ball % 3], _routeStateExposures,
                    _routeStateNextCounts, RoutePopulation(ball % 3), longRates,
                    value => value % 3, 120);
                double structureRatio = (zoneRatio + parityRatio + routeRatio) / 3.0;

                int repeated = latest.Contains(ball) ? 1 : 0;
                int hasNeighbor = latest.Contains(ball - 1) || latest.Contains(ball + 1) ? 1 : 0;
                int gapBucket = Math.Min(_gaps[ball], _gapExposures.Length - 1);
                double repeatRatio = ConditionalRatio(repeated, _repeatHits, _repeatExposures);
                double neighborRatio = ConditionalRatio(hasNeighbor, _neighborHits, _neighborExposures);
                double gapRatio = SmoothedRatio(_gapHits[gapBucket], _gapExposures[gapBucket]);
                double localRatio = (repeatRatio + neighborRatio + gapRatio) / 3.0;

                scores[ball] = multiscale
                    * (0.88 + 0.12 * structureRatio)
                    * (0.90 + 0.10 * localRatio);
            }
            return scores;
        }

        private static int SelectLongestStationarySuffix(IReadOnlyList<HashSet<int>> history)
        {
            int[] candidates = { 1600, 800, 400, 200, 100 };
            foreach (int windowSize in candidates)
            {
                if (history.Count < windowSize) continue;
                var window = history.Skip(history.Count - windowSize).ToArray();
                if (!HasDistributionShift(window)) return windowSize;
            }
            return Math.Min(100, history.Count);
        }

        private static bool HasDistributionShift(IReadOnlyList<HashSet<int>> window)
        {
            int half = window.Count / 2;
            if (half < 50) return false;
            var firstCounts = new int[BallCount + 1];
            var secondCounts = new int[BallCount + 1];
            for (int index = 0; index < half; index++)
            foreach (int ball in window[index])
                firstCounts[ball]++;
            for (int index = half; index < window.Count; index++)
            foreach (int ball in window[index])
                secondCounts[ball]++;

            double chiSquare = 0;
            for (int ball = 1; ball <= BallCount; ball++)
            {
                int total = firstCounts[ball] + secondCounts[ball];
                if (total == 0) continue;
                double difference = firstCounts[ball] - secondCounts[ball];
                chiSquare += difference * difference / total;
            }

            // Chi-square homogeneity test, 32 degrees of freedom, alpha = 0.01.
            return chiSquare > 53.486;
        }

        private double RollingRate(int window, int ball) =>
            _rollingCounts[window][ball] / (double)_rolling[window].Count;

        private static double ConfidenceRampScore(
            double longRate,
            double recentRate,
            int historyCount,
            int fullWeightAt)
        {
            double recentWeight = 0.20 * Math.Min(1.0, historyCount / (double)fullWeightAt);
            return (1 - recentWeight) * longRate + recentWeight * recentRate;
        }

        private double[] CreateCategoryReversionScores(
            IReadOnlyList<double> longRates,
            IReadOnlyList<double> recentRates,
            int windowSize,
            double strength)
        {
            var window = _recent.TakeLast(Math.Min(windowSize, _recent.Count)).ToArray();
            var zoneCounts = new int[3];
            var parityCounts = new int[2];
            var routeCounts = new int[3];
            foreach (var draw in window)
            foreach (int ball in draw)
            {
                zoneCounts[Zone(ball)]++;
                parityCounts[ball % 2]++;
                routeCounts[ball % 3]++;
            }

            var scores = NewScores();
            for (int ball = 1; ball <= BallCount; ball++)
            {
                int zone = Zone(ball);
                int parity = ball % 2;
                int route = ball % 3;
                double zoneDeficit = CategoryDeficit(zoneCounts[zone], window.Length, ZonePopulation(zone));
                double parityDeficit = CategoryDeficit(parityCounts[parity], window.Length, ParityPopulation(parity));
                double routeDeficit = CategoryDeficit(routeCounts[route], window.Length, RoutePopulation(route));
                double adjustment = (zoneDeficit + parityDeficit + routeDeficit) / 3.0;
                double baseScore = 0.80 * longRates[ball] + 0.20 * recentRates[ball];
                scores[ball] = baseScore * (1 + strength * adjustment);
            }
            return scores;
        }

        private static double CategoryDeficit(int observedCount, int drawCount, int population)
        {
            double expectedRate = ExpectedBallRate;
            double observedRate = observedCount / (double)(drawCount * population);
            return Math.Clamp((expectedRate - observedRate) / expectedRate, -0.50, 0.50);
        }

        private static int Zone(int ball) => ball <= 11 ? 0 : ball <= 22 ? 1 : 2;
        private static int CalendarDay(DateTime date) => new DateTime(2000, date.Month, date.Day).DayOfYear;
        private static int ZonePopulation(int _) => 11;
        private static int ParityPopulation(int parity) => parity == 0 ? 16 : 17;
        private static int RoutePopulation(int _) => 11;
        private static int ZoneRouteGroup(int ball) => Zone(ball) * 3 + ball % 3;
        private static int ZoneRoutePopulation(int group) => Enumerable.Range(1, BallCount)
            .Count(ball => ZoneRouteGroup(ball) == group);

        private double[] CreateCategoryTransitionScores(
            IReadOnlyList<double> longRates,
            IReadOnlyList<double> recentRates,
            double strength,
            bool useZone = true,
            bool useParity = true,
            bool useRoute = true,
            double priorStrength = 30)
        {
            var latestZoneCounts = CategoryCounts(_latest!, 3, Zone);
            var latestParityCounts = CategoryCounts(_latest!, 2, ball => ball % 2);
            var latestRouteCounts = CategoryCounts(_latest!, 3, ball => ball % 3);
            var scores = NewScores();
            for (int ball = 1; ball <= BallCount; ball++)
            {
                int zone = Zone(ball);
                int parity = ball % 2;
                int route = ball % 3;
                double zoneRatio = CategoryTransitionRatio(
                    zone,
                    latestZoneCounts[zone],
                    _zoneStateExposures,
                    _zoneStateNextCounts,
                    ZonePopulation(zone),
                    longRates,
                    Zone,
                    priorStrength);
                double parityRatio = CategoryTransitionRatio(
                    parity,
                    latestParityCounts[parity],
                    _parityStateExposures,
                    _parityStateNextCounts,
                    ParityPopulation(parity),
                    longRates,
                    value => value % 2,
                    priorStrength);
                double routeRatio = CategoryTransitionRatio(
                    route,
                    latestRouteCounts[route],
                    _routeStateExposures,
                    _routeStateNextCounts,
                    RoutePopulation(route),
                    longRates,
                    value => value % 3,
                    priorStrength);
                double transitionTotal = 0;
                int transitionCount = 0;
                if (useZone) { transitionTotal += zoneRatio; transitionCount++; }
                if (useParity) { transitionTotal += parityRatio; transitionCount++; }
                if (useRoute) { transitionTotal += routeRatio; transitionCount++; }
                double transitionRatio = transitionTotal / transitionCount;
                double baseScore = 0.80 * longRates[ball] + 0.20 * recentRates[ball];
                scores[ball] = baseScore * ((1 - strength) + strength * transitionRatio);
            }
            return scores;
        }

        private double[] CreateJointZoneRouteTransitionScores(
            IReadOnlyList<double> longRates,
            IReadOnlyList<double> recentRates,
            double strength)
        {
            var latestCounts = CategoryCounts(_latest!, 9, ZoneRouteGroup);
            var scores = NewScores();
            for (int ball = 1; ball <= BallCount; ball++)
            {
                int group = ZoneRouteGroup(ball);
                double ratio = CategoryTransitionRatio(
                    group,
                    latestCounts[group],
                    _zoneRouteStateExposures,
                    _zoneRouteStateNextCounts,
                    ZoneRoutePopulation(group),
                    longRates,
                    ZoneRouteGroup);
                double baseScore = 0.80 * longRates[ball] + 0.20 * recentRates[ball];
                scores[ball] = baseScore * ((1 - strength) + strength * ratio);
            }
            return scores;
        }

        private double[] CreateWeightedZoneRouteTransitionScores(
            IReadOnlyList<double> longRates,
            IReadOnlyList<double> recentRates,
            double strength,
            double zoneWeight,
            double priorStrength)
        {
            var latestZoneCounts = CategoryCounts(_latest!, 3, Zone);
            var latestRouteCounts = CategoryCounts(_latest!, 3, ball => ball % 3);
            var scores = NewScores();
            for (int ball = 1; ball <= BallCount; ball++)
            {
                int zone = Zone(ball);
                int route = ball % 3;
                double zoneRatio = CategoryTransitionRatio(
                    zone,
                    latestZoneCounts[zone],
                    _zoneStateExposures,
                    _zoneStateNextCounts,
                    ZonePopulation(zone),
                    longRates,
                    Zone,
                    priorStrength);
                double routeRatio = CategoryTransitionRatio(
                    route,
                    latestRouteCounts[route],
                    _routeStateExposures,
                    _routeStateNextCounts,
                    RoutePopulation(route),
                    longRates,
                    value => value % 3,
                    priorStrength);
                double ratio = zoneWeight * zoneRatio + (1 - zoneWeight) * routeRatio;
                double baseScore = 0.80 * longRates[ball] + 0.20 * recentRates[ball];
                scores[ball] = baseScore * ((1 - strength) + strength * ratio);
            }
            return scores;
        }

        private double[] CreateLocalRangeTransitionScores(
            IReadOnlyList<double> longRates,
            IReadOnlyList<double> recentRates,
            double strength)
        {
            var rangeRatios = NewScores();
            for (int centre = 2; centre < BallCount; centre++)
            {
                int state = PointRange(centre).Count(_latest!.Contains);
                double longExpected = PointRange(centre).Sum(ball => longRates[ball]);
                const double priorStrength = 50;
                double expectedNext = (_localRangeStateNextCounts[centre, state]
                        + priorStrength * longExpected)
                    / (_localRangeStateExposures[centre, state] + priorStrength);
                rangeRatios[centre] = longExpected <= 0
                    ? 1
                    : Math.Clamp(expectedNext / longExpected, 0.75, 1.25);
            }

            var scores = NewScores();
            for (int ball = 1; ball <= BallCount; ball++)
            {
                var containingRanges = Enumerable.Range(2, BallCount - 2)
                    .Where(centre => PositionPointRange.Contains(centre, ball, BallCount));
                double ratio = containingRanges.Average(centre => rangeRatios[centre]);
                double baseScore = 0.80 * longRates[ball] + 0.20 * recentRates[ball];
                scores[ball] = baseScore * ((1 - strength) + strength * ratio);
            }
            return scores;
        }

        private static double CategoryTransitionRatio(
            int group,
            int state,
            long[,] exposures,
            long[,] nextCounts,
            int population,
            IReadOnlyList<double> longRates,
            Func<int, int> groupSelector,
            double priorStrength = 30)
        {
            double longGroupRate = Enumerable.Range(1, BallCount)
                .Where(ball => groupSelector(ball) == group)
                .Sum(ball => longRates[ball]);
            double longExpectedCount = longGroupRate;
            double expectedNextCount = (nextCounts[group, state] + priorStrength * longExpectedCount)
                / (exposures[group, state] + priorStrength);
            double longPerBall = longExpectedCount / population;
            if (longPerBall <= 0) return 1;
            return Math.Clamp((expectedNextCount / population) / longPerBall, 0.75, 1.25);
        }

        private static void UpdateCategoryTransitions(
            IReadOnlySet<int> previous,
            IReadOnlySet<int> current,
            long[,] exposures,
            long[,] nextCounts,
            int groupCount,
            Func<int, int> groupSelector)
        {
            var previousCounts = CategoryCounts(previous, groupCount, groupSelector);
            var currentCounts = CategoryCounts(current, groupCount, groupSelector);
            for (int group = 0; group < groupCount; group++)
            {
                int state = previousCounts[group];
                exposures[group, state]++;
                nextCounts[group, state] += currentCounts[group];
            }
        }

        private static int[] CategoryCounts(
            IEnumerable<int> balls,
            int groupCount,
            Func<int, int> groupSelector)
        {
            var counts = new int[groupCount];
            foreach (int ball in balls) counts[groupSelector(ball)]++;
            return counts;
        }

        private static double EmpiricalBayesReliability(
            IReadOnlyList<double> baseline,
            IReadOnlyList<double> local,
            int baselineCount,
            int localCount)
        {
            if (baselineCount == 0 || localCount == 0) return 0;
            double observedVariance = Enumerable.Range(1, BallCount)
                .Average(ball => Math.Pow(local[ball] - baseline[ball], 2));
            if (observedVariance <= 0) return 0;
            double samplingVariance = Enumerable.Range(1, BallCount).Average(ball =>
                local[ball] * (1 - local[ball]) / localCount
                + baseline[ball] * (1 - baseline[ball]) / baselineCount);
            return Math.Clamp((observedVariance - samplingVariance) / observedVariance, 0, 1);
        }

        private static double[] NewScores() => new double[BallCount + 1];

        private static double ConditionalRatio(int state, long[] hits, long[] exposures) =>
            SmoothedRatio(hits[state], exposures[state]);

        private static double SmoothedRatio(long hits, long exposures)
        {
            const double priorStrength = 50;
            double rate = (hits + priorStrength * ExpectedBallRate) / (exposures + priorStrength);
            return Math.Clamp(rate / ExpectedBallRate, 0.75, 1.25);
        }

        private static double BallConditionalRate(
            int ball,
            int state,
            long[,] ballHits,
            long[,] ballExposures,
            long[] pooledHits,
            long[] pooledExposures)
        {
            const double pooledPriorStrength = 50;
            double pooledRate = (pooledHits[state] + pooledPriorStrength * ExpectedBallRate)
                / (pooledExposures[state] + pooledPriorStrength);
            const double ballPriorStrength = 200;
            return (ballHits[ball, state] + ballPriorStrength * pooledRate)
                / (ballExposures[ball, state] + ballPriorStrength);
        }
    }
}

internal sealed record PositionPointResearchReport(
    int DataAsOfIssue,
    int SampleSize,
    int WindowSize,
    string BaselineModel,
    IReadOnlyList<PositionPointResearchModelReport> Models);

internal sealed record PositionPointResearchModelReport(
    string Model,
    PositionPointResearchWindow Aggregate,
    double FirstHalfLift,
    double SecondHalfLift,
    bool EveryWindowAboveRandom,
    bool EveryWindowNoWorseThanStable,
    bool PassesPointPromotionGate,
    IReadOnlyList<PositionPointResearchWindow> Windows)
{
    public double DevelopmentAverageHits { get; init; }
    public double DevelopmentAdvantageToStable { get; init; }
    public double DevelopmentAdvantageToStableLower95 { get; init; }
    public double ValidationAverageHits { get; init; }
    public double ValidationAdvantageToStable { get; init; }
    public double ValidationAdvantageToStableLower95 { get; init; }
    public double DevelopmentAverageRangeHits { get; init; }
    public double DevelopmentRangeAdvantageToStable { get; init; }
    public double ValidationAverageRangeHits { get; init; }
    public double ValidationRangeAdvantageToStable { get; init; }
    public double ExactRepeatRate { get; init; }
    public double AverageRetainedPoints { get; init; }
    public int DifferentFromStableCount { get; init; }
    public double ChangedAdvantageToStable { get; init; }
    public double ChangedAdvantageToStableLower95 { get; init; }
    public double ChangedRangeAdvantageToStable { get; init; }
    public double ChangedRangeAdvantageToStableLower95 { get; init; }
}

internal sealed record PositionPointResearchWindow(
    int OffsetFromLatest,
    int SampleSize,
    int StartIssue,
    int EndIssue,
    double AverageHits,
    double RandomExpectedHits,
    double Lift,
    double LiftLower95,
    double AdvantageToStable,
    double AdvantageToStableLower95)
{
    public double AverageRangeHits { get; init; }
    public double RangeAdvantageToStable { get; init; }
    public double RangeAdvantageToStableLower95 { get; init; }
}
