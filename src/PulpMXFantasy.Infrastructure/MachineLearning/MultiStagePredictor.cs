using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ML;
using Microsoft.ML;
using PulpMXFantasy.Domain.Abstractions;
using PulpMXFantasy.Domain.Enums;

namespace PulpMXFantasy.Infrastructure.MachineLearning;

/// <summary>
/// Multi-stage ML predictor that separates qualification and finish position prediction.
/// </summary>
/// <remarks>
/// WHY MULTI-STAGE:
/// ================
/// Fantasy points result from a multi-step process:
/// 1. **Qualification**: Does rider make the main event? (22 of ~40 riders)
/// 2. **Finish Position**: Where do they finish if they qualify? (1-22)
/// 3. **Scoring Rules**: Apply handicap adjustments and All-star doubling
///
/// The old single-model approach tried to predict final fantasy points directly,
/// which conflated these three distinct problems. Result: R² ≈ 0 (model learns nothing).
///
/// ARCHITECTURE:
/// =============
/// Stage 1: Qualification Model (Binary Classification)
/// - Input: Handicap, AvgFinish, SeasonPoints, IsAllStar
/// - Output: P(makes main event)
/// - Expected: 80-85% accuracy
///
/// Stage 2: Finish Position Model (Regression)
/// - Input: Handicap, AvgFinish, TrackHistory, Momentum
/// - Output: Predicted finish (1-22)
/// - Expected: R² 0.30-0.50, MAE 3-5 positions
/// - Only runs if P(qual) > threshold
///
/// Stage 3: Deterministic Scoring (Business Logic)
/// - Apply handicap: adjusted = finish - handicap
/// - Apply All-star rules: single vs double points
/// - Calculate expected value: P(qual) * points
///
/// EXPECTED VALUE CALCULATION:
/// ===========================
/// Expected points = P(makes main) × Points(if qualified) + P(DNQ) × 0
///
/// Example:
/// - Rider has 85% chance to qualify
/// - If qualified, predicted 5th place
/// - Handicap +3 = adjusted 2nd = 22 base points
/// - Not All-Star, adjusted ≤ 10 = double = 44 points
/// - Expected value = 0.85 × 44 = 37.4 points
///
/// CONFIDENCE INTERVALS:
/// =====================
/// Confidence reduced by qualification uncertainty:
/// - If P(qual) = 0.95, confidence high
/// - If P(qual) = 0.60, confidence reduced
/// - If P(qual) < 0.30, return 0 expected points
/// </remarks>
public class MultiStagePredictor : IRiderPredictor
{
    private readonly PredictionEnginePool<QualificationModelData, QualificationPrediction> _qualificationPool;
    private readonly PredictionEnginePool<FinishPositionModelData, FinishPositionPrediction> _finishPool;
    private readonly ILogger<MultiStagePredictor> _logger;
    private readonly MLContext _mlContext;
    private readonly string _modelDirectory;

    // Direct-loaded models (bypass pool for reliability)
    private readonly object _modelLock = new();
    private Dictionary<string, PredictionEngine<QualificationModelData, QualificationPrediction>> _qualificationEngines = new();
    private Dictionary<string, PredictionEngine<FinishPositionModelData, FinishPositionPrediction>> _finishEngines = new();
    private bool _useDirectModels;

    // Fantasy points table (position → points)
    private static readonly int[] PointsTable =
        { 25, 22, 20, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 };

    public MultiStagePredictor(
        PredictionEnginePool<QualificationModelData, QualificationPrediction> qualificationPool,
        PredictionEnginePool<FinishPositionModelData, FinishPositionPrediction> finishPool,
        ILogger<MultiStagePredictor> logger,
        string modelDirectory = "./TrainedModels")
    {
        _qualificationPool = qualificationPool;
        _finishPool = finishPool;
        _logger = logger;
        _mlContext = new MLContext(seed: 42);
        _modelDirectory = modelDirectory;

        // Try to load models directly on startup
        ReloadModels();
    }

    /// <summary>
    /// Marks models as ready for prediction.
    /// </summary>
    public void LoadModel(string modelPath)
    {
        // Model loading is handled by PredictionEnginePool with watchForChanges
        // This method exists for interface compatibility
        _logger.LogInformation("Multi-stage predictor LoadModel called with path: {ModelPath}", modelPath);
    }

    /// <summary>
    /// Checks if models are loaded and ready by verifying model files exist on disk.
    /// </summary>
    /// <remarks>
    /// PredictionEnginePool automatically loads models when files appear/change.
    /// We check file existence as a proxy for model availability.
    /// </remarks>
    public bool IsModelReady()
    {
        // Check if at least one qualification and one finish position model exist
        var class250QualPath = Path.Combine(_modelDirectory, MLConstants.Class250QualificationFileName);
        var class450QualPath = Path.Combine(_modelDirectory, MLConstants.Class450QualificationFileName);
        var class250FinishPath = Path.Combine(_modelDirectory, MLConstants.Class250FinishPositionFileName);
        var class450FinishPath = Path.Combine(_modelDirectory, MLConstants.Class450FinishPositionFileName);

        // Need at least one model of each type to make predictions
        bool hasQualModel = File.Exists(class250QualPath) || File.Exists(class450QualPath);
        bool hasFinishModel = File.Exists(class250FinishPath) || File.Exists(class450FinishPath);

        if (!hasQualModel || !hasFinishModel)
        {
            _logger.LogDebug(
                "Models not ready - HasQual={HasQual}, HasFinish={HasFinish}, Directory={Dir}",
                hasQualModel, hasFinishModel, _modelDirectory);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Forces a reload of all ML models from disk.
    /// </summary>
    /// <remarks>
    /// This bypasses PredictionEnginePool's file watching which is unreliable in Docker.
    /// Call after training to ensure the predictor uses the latest models.
    /// </remarks>
    public void ReloadModels()
    {
        _logger.LogInformation("Reloading ML models from disk...");

        lock (_modelLock)
        {
            // Dispose old engines
            foreach (var engine in _qualificationEngines.Values)
            {
                (engine as IDisposable)?.Dispose();
            }
            foreach (var engine in _finishEngines.Values)
            {
                (engine as IDisposable)?.Dispose();
            }

            _qualificationEngines = new Dictionary<string, PredictionEngine<QualificationModelData, QualificationPrediction>>();
            _finishEngines = new Dictionary<string, PredictionEngine<FinishPositionModelData, FinishPositionPrediction>>();
            _useDirectModels = false;

            // Load qualification models
            var qualModelPaths = new Dictionary<string, string>
            {
                { MLConstants.Class250QualificationModel, Path.Combine(_modelDirectory, MLConstants.Class250QualificationFileName) },
                { MLConstants.Class450QualificationModel, Path.Combine(_modelDirectory, MLConstants.Class450QualificationFileName) }
            };

            foreach (var (modelName, modelPath) in qualModelPaths)
            {
                if (File.Exists(modelPath))
                {
                    try
                    {
                        var model = _mlContext.Model.Load(modelPath, out var inputSchema);

                        // Log the expected input schema from the loaded model
                        _logger.LogInformation("=== LOADED MODEL INPUT SCHEMA for {ModelName} ===", modelName);
                        foreach (var col in inputSchema)
                        {
                            _logger.LogInformation("  Column: {Name}, Type: {Type}", col.Name, col.Type);
                        }

                        var engine = _mlContext.Model.CreatePredictionEngine<QualificationModelData, QualificationPrediction>(model);

                        // Test prediction using BOTH methods to compare
                        var testInput = new QualificationModelData
                        {
                            Handicap = 1,
                            AvgFinishLast5 = 5,
                            FinishRate = 100,
                            TrackHistory = -1,
                            IsAllStar = 1.0f
                        };

                        // Method 1: PredictionEngine
                        var testResult = engine.Predict(testInput);
                        _logger.LogInformation(
                            "TEST via PredictionEngine for {ModelName}: Score={Score:F2}, Prob={Prob:F2}",
                            modelName, testResult.Score, testResult.Probability);

                        // Method 2: Transform on IDataView (like training evaluation)
                        var testDataView = _mlContext.Data.LoadFromEnumerable(new[] { testInput });
                        var transformedData = model.Transform(testDataView);
                        var transformResults = _mlContext.Data.CreateEnumerable<QualificationPrediction>(transformedData, reuseRowObject: false).ToList();
                        if (transformResults.Any())
                        {
                            var transformResult = transformResults[0];
                            _logger.LogInformation(
                                "TEST via Transform for {ModelName}: Score={Score:F2}, Prob={Prob:F2}",
                                modelName, transformResult.Score, transformResult.Probability);
                        }

                        _qualificationEngines[modelName] = engine;
                        _logger.LogInformation("Loaded qualification model: {ModelName} from {Path}", modelName, modelPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to load qualification model: {ModelName}", modelName);
                    }
                }
                else
                {
                    _logger.LogWarning("Qualification model file not found: {Path}", modelPath);
                }
            }

            // Load finish position models
            var finishModelPaths = new Dictionary<string, string>
            {
                { MLConstants.Class250FinishPositionModel, Path.Combine(_modelDirectory, MLConstants.Class250FinishPositionFileName) },
                { MLConstants.Class450FinishPositionModel, Path.Combine(_modelDirectory, MLConstants.Class450FinishPositionFileName) }
            };

            foreach (var (modelName, modelPath) in finishModelPaths)
            {
                if (File.Exists(modelPath))
                {
                    try
                    {
                        var model = _mlContext.Model.Load(modelPath, out _);
                        var engine = _mlContext.Model.CreatePredictionEngine<FinishPositionModelData, FinishPositionPrediction>(model);
                        _finishEngines[modelName] = engine;
                        _logger.LogInformation("Loaded finish position model: {ModelName} from {Path}", modelName, modelPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to load finish position model: {ModelName}", modelName);
                    }
                }
                else
                {
                    _logger.LogWarning("Finish position model file not found: {Path}", modelPath);
                }
            }

            // Use direct models if at least one of each type loaded
            _useDirectModels = _qualificationEngines.Count > 0 && _finishEngines.Count > 0;

            _logger.LogInformation(
                "Model reload complete. Direct models: {UseDirectModels}, Qual: {QualCount}, Finish: {FinishCount}",
                _useDirectModels, _qualificationEngines.Count, _finishEngines.Count);
        }
    }

    /// <summary>
    /// Gets model information (not applicable for multi-stage).
    /// </summary>
    public ModelInfo GetModelInfo()
    {
        return new ModelInfo(
            Version: "v2.0.0-multistage",
            TrainedDate: DateTimeOffset.UtcNow,
            TrainingSamples: 0,
            ValidationAccuracy: 0,
            MeanAbsoluteError: 0);
    }

    /// <summary>
    /// Predicts fantasy points using multi-stage pipeline.
    /// </summary>
    public RiderPrediction PredictFantasyPoints(RiderFeatures features)
    {
        if (!IsModelReady())
        {
            _logger.LogWarning("Models not ready, using fallback");
            return GenerateFallbackPrediction(features);
        }

        // CRITICAL: If rider has no historical data, cannot make meaningful prediction
        // Model was trained only on riders WITH history, so -1 values produce nonsense predictions
        if (features.AvgFinishLast5 == null || features.AvgFinishLast5 <= 0)
        {
            _logger.LogDebug(
                "Skipping rider {RiderId} - no historical data: AvgFinish={AvgFinish}",
                features.RiderId,
                features.AvgFinishLast5);
            return new RiderPrediction(
                RiderId: features.RiderId,
                BikeClass: features.BikeClass,
                IsAllStar: features.IsAllStar,
                ExpectedPoints: 0,
                PointsIfQualifies: 0,
                PredictedFinish: null,
                LowerBound: 0,
                UpperBound: 0,
                Confidence: 0);
        }

        _logger.LogDebug(
            "Predicting for rider {RiderId}: AvgFinish={AvgFinish}",
            features.RiderId,
            features.AvgFinishLast5);

        try
        {
            // Stage 1: Predict qualification probability
            var qualData = MapToQualificationData(features);
            var qualModelName = MLConstants.GetModelName(features.BikeClass, "Qualification");

            // SCHEMA DEBUGGING: Log exact values being passed to prediction engine
            _logger.LogDebug(
                "INFERENCE INPUT: Hcp={H}, Avg={A}, Rate={R}, Track={T}, AllStar={AS} (Type: {Type})",
                qualData.Handicap,
                qualData.AvgFinishLast5,
                qualData.FinishRate,
                qualData.TrackHistory,
                qualData.IsAllStar,
                qualData.IsAllStar.GetType().Name);

            // Use direct-loaded engines (thread-safe with lock) or fall back to pool
            QualificationPrediction qualPrediction;
            lock (_modelLock)
            {
                if (_useDirectModels && _qualificationEngines.TryGetValue(qualModelName, out var directQualEngine))
                {
                    qualPrediction = directQualEngine.Predict(qualData);
                    _logger.LogDebug("Using direct-loaded qualification engine for {Model}", qualModelName);
                }
                else
                {
                    qualPrediction = _qualificationPool.Predict(qualModelName, qualData);
                    _logger.LogDebug("Using pool qualification engine for {Model}", qualModelName);
                }
            }

            float qualificationProbability = qualPrediction.Probability;

            _logger.LogDebug(
                "Rider {RiderId} qual prediction - Probability: {Prob:F2}, Score: {Score:F2}, PredictedLabel: {Label}",
                features.RiderId,
                qualificationProbability,
                qualPrediction.Score,
                qualPrediction.PredictedMadeMain);

            // If very unlikely to qualify, return 0 expected points
            if (qualificationProbability < 0.20f)
            {
                return new RiderPrediction(
                    RiderId: features.RiderId,
                    BikeClass: features.BikeClass,
                    IsAllStar: features.IsAllStar,
                    ExpectedPoints: 0,
                    PointsIfQualifies: 0, // DNQ predicted
                    PredictedFinish: null, // DNQ predicted
                    LowerBound: 0,
                    UpperBound: 0,
                    Confidence: qualificationProbability);
            }

            // Stage 2: Predict finish position (for likely qualifiers)
            var finishData = MapToFinishPositionData(features);
            var finishModelName = MLConstants.GetModelName(features.BikeClass, "FinishPosition");

            // Use direct-loaded engines or fall back to pool
            FinishPositionPrediction finishPrediction;
            lock (_modelLock)
            {
                if (_useDirectModels && _finishEngines.TryGetValue(finishModelName, out var directFinishEngine))
                {
                    finishPrediction = directFinishEngine.Predict(finishData);
                    _logger.LogDebug("Using direct-loaded finish engine for {Model}", finishModelName);
                }
                else
                {
                    finishPrediction = _finishPool.Predict(finishModelName, finishData);
                    _logger.LogDebug("Using pool finish engine for {Model}", finishModelName);
                }
            }

            int predictedFinish = (int)Math.Round(finishPrediction.PredictedFinish);
            predictedFinish = Math.Clamp(predictedFinish, 1, 22); // Ensure valid range

            _logger.LogDebug(
                "Rider {RiderId} predicted finish: {Finish}",
                features.RiderId,
                predictedFinish);

            // Stage 3: Calculate fantasy points (deterministic)
            int adjustedPosition = Math.Max(1, predictedFinish - features.Handicap);
            int basePoints = GetBasePoints(adjustedPosition);

            // Apply All-star doubling rules
            int fantasyPoints = (!features.IsAllStar && adjustedPosition <= 10)
                ? basePoints * 2
                : basePoints;

            // Expected value = P(qualify) × points
            float expectedPoints = qualificationProbability * fantasyPoints;

            // Confidence reduced by qualification uncertainty
            float confidence = CalculateConfidence(features) * qualificationProbability;

            // Confidence interval based on both models
            float margin = expectedPoints * 0.25f; // 25% margin

            return new RiderPrediction(
                RiderId: features.RiderId,
                BikeClass: features.BikeClass,
                IsAllStar: features.IsAllStar,
                ExpectedPoints: expectedPoints,
                PointsIfQualifies: fantasyPoints,
                PredictedFinish: predictedFinish,
                LowerBound: Math.Max(0, expectedPoints - margin),
                UpperBound: expectedPoints + margin,
                Confidence: confidence);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Multi-stage prediction failed for rider {RiderId}, using fallback",
                features.RiderId);
            return GenerateFallbackPrediction(features);
        }
    }

    /// <summary>
    /// Batch prediction for multiple riders with force-ranked finish positions.
    /// </summary>
    /// <remarks>
    /// FORCE RANKING:
    /// ==============
    /// Supercross finishes must be unique positions 1-22 (no ties).
    /// After generating raw predictions, we:
    /// 1. Group riders by bike class (250/450)
    /// 2. Sort qualifying riders by expected points (descending)
    /// 3. Assign unique finish positions 1-22
    /// 4. Riders predicted as DNQ keep PredictedFinish = null
    /// </remarks>
    public IReadOnlyList<RiderPrediction> PredictBatch(IEnumerable<RiderFeatures> featuresList)
    {
        // Materialize features list for multiple lookups
        var allFeatures = featuresList.ToList();

        // Step 1: Generate raw predictions for all riders
        var rawPredictions = allFeatures.Select(PredictFantasyPoints).ToList();

        // Step 2: Force-rank finish positions within each bike class
        var rankedPredictions = new List<RiderPrediction>();

        foreach (var bikeClass in new[] { BikeClass.Class250, BikeClass.Class450 })
        {
            var classRiders = rawPredictions
                .Where(p => p.BikeClass == bikeClass)
                .ToList();

            // Separate qualifiers (have PredictedFinish) from DNQs
            var qualifiers = classRiders
                .Where(p => p.PredictedFinish.HasValue)
                .OrderBy(p => p.PredictedFinish) // Sort by ML-predicted finish (1st, 2nd, 3rd...)
                .ThenByDescending(p => p.ExpectedPoints) // Tie-breaker: higher expected points
                .ToList();

            var dnqs = classRiders
                .Where(p => !p.PredictedFinish.HasValue)
                .ToList();

            // Assign force-ranked positions 1-22 to qualifiers
            for (int i = 0; i < qualifiers.Count; i++)
            {
                var original = qualifiers[i];
                var forcedRank = Math.Min(i + 1, 22); // Cap at 22

                // Recalculate points based on force-ranked position
                // This ensures PointsIfQualifies matches the displayed PredictedFinish
                var riderFeatures = allFeatures.FirstOrDefault(f => f.RiderId == original.RiderId);
                int handicap = riderFeatures?.Handicap ?? 0;
                int adjustedPosition = Math.Max(1, forcedRank - handicap);
                int basePoints = GetBasePoints(adjustedPosition);
                int pointsIfQualifies = (!original.IsAllStar && adjustedPosition <= 10)
                    ? basePoints * 2
                    : basePoints;
                float expectedPoints = original.Confidence * pointsIfQualifies;
                float margin = expectedPoints * 0.25f;

                // Create new prediction with force-ranked position and recalculated points
                rankedPredictions.Add(original with
                {
                    PredictedFinish = forcedRank,
                    PointsIfQualifies = pointsIfQualifies,
                    ExpectedPoints = expectedPoints,
                    LowerBound = Math.Max(0, expectedPoints - margin),
                    UpperBound = expectedPoints + margin
                });
            }

            // DNQs keep null PredictedFinish
            rankedPredictions.AddRange(dnqs);

            _logger.LogInformation(
                "Force-ranked {BikeClass}: {QualifierCount} qualifiers (1-22), {DnqCount} DNQs",
                bikeClass, Math.Min(qualifiers.Count, 22), dnqs.Count);
        }

        return rankedPredictions;
    }

    /// <summary>
    /// Gets base points from fantasy points table.
    /// </summary>
    private int GetBasePoints(int adjustedPosition)
    {
        if (adjustedPosition < 1 || adjustedPosition > 22)
            return 0;

        return PointsTable[adjustedPosition - 1];
    }

    /// <summary>
    /// Maps rider features to qualification model input.
    /// </summary>
    private QualificationModelData MapToQualificationData(RiderFeatures features)
    {
        var qualData = new QualificationModelData
        {
            Handicap = features.Handicap,
            AvgFinishLast5 = (float)(features.AvgFinishLast5 ?? -1),
            FinishRate = (float)(features.FinishRate ?? 100),
            TrackHistory = (float)(features.TrackHistory ?? -1),
            // NOTE: PickTrend removed - not available at prediction time
            IsAllStar = features.IsAllStar ? 1.0f : 0.0f
        };

        _logger.LogDebug(
            "QualData for {RiderId}: Hcp={Hcp}, AvgFinish={AvgFinish}, FinishRate={Rate}, Track={Track}, AllStar={All}",
            features.RiderId,
            qualData.Handicap,
            qualData.AvgFinishLast5,
            qualData.FinishRate,
            qualData.TrackHistory,
            qualData.IsAllStar);

        return qualData;
    }

    /// <summary>
    /// Maps rider features to finish position model input.
    /// </summary>
    private FinishPositionModelData MapToFinishPositionData(RiderFeatures features)
    {
        return new FinishPositionModelData
        {
            Handicap = features.Handicap,
            AvgFinishLast5 = (float)(features.AvgFinishLast5 ?? -1),
            AvgFantasyPointsLast5 = (float)(features.AvgFantasyPointsLast5 ?? -1),
            TrackHistory = (float)(features.TrackHistory ?? -1),
            RecentMomentum = (float)(features.RecentMomentum ?? 0),
            SeasonPoints = features.SeasonPoints ?? 0,
            IsAllStar = features.IsAllStar,
            TrackTypeIndoor = features.TrackType == "Indoor"
        };
    }

    /// <summary>
    /// Calculates prediction confidence based on data quality.
    /// </summary>
    private float CalculateConfidence(RiderFeatures features)
    {
        float confidence = 0.3f; // Base confidence

        // Historical data available
        if (features.AvgFinishLast5.HasValue && features.AvgFinishLast5.Value > 0)
            confidence += 0.3f;

        // Track-specific history
        if (features.TrackHistory.HasValue && features.TrackHistory.Value > 0)
            confidence += 0.2f;

        // Not injured
        if (!features.IsInjured)
            confidence += 0.2f;

        return Math.Min(1.0f, confidence);
    }

    /// <summary>
    /// Generates fallback prediction when models unavailable.
    /// Uses handicap-based heuristic.
    /// </summary>
    private RiderPrediction GenerateFallbackPrediction(RiderFeatures features)
    {
        // Injured riders score 0
        if (features.IsInjured)
        {
            return new RiderPrediction(
                RiderId: features.RiderId,
                BikeClass: features.BikeClass,
                IsAllStar: features.IsAllStar,
                ExpectedPoints: 0,
                PointsIfQualifies: 0, // Injured - not racing
                PredictedFinish: null, // Injured - not racing
                LowerBound: 0,
                UpperBound: 0,
                Confidence: 1.0f);
        }

        // Estimate finish based on handicap
        // Handicap roughly correlates: +10 handicap ≈ 10-15th expected finish
        int estimatedFinish = Math.Clamp(12 - features.Handicap, 1, 22);
        int adjustedPosition = Math.Max(1, estimatedFinish - features.Handicap);
        float basePoints = GetBasePoints(adjustedPosition);

        // Apply doubling to get points if qualifies
        float pointsIfQualifies = (!features.IsAllStar && adjustedPosition <= 10)
            ? basePoints * 2
            : basePoints;

        // Reduce expected value by assumed 80% qualification rate
        float expectedPoints = pointsIfQualifies * 0.80f;

        // Wide confidence interval for fallback
        float margin = expectedPoints * 0.5f;

        return new RiderPrediction(
            RiderId: features.RiderId,
            BikeClass: features.BikeClass,
            IsAllStar: features.IsAllStar,
            ExpectedPoints: expectedPoints,
            PointsIfQualifies: pointsIfQualifies,
            PredictedFinish: estimatedFinish, // Handicap-based estimate
            LowerBound: Math.Max(0, expectedPoints - margin),
            UpperBound: expectedPoints + margin,
            Confidence: 0.3f); // Low confidence for fallback
    }
}
