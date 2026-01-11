using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Infrastructure.Data;

namespace PulpMXFantasy.Infrastructure.MachineLearning;

/// <summary>
/// Trains LightGBM models for rider fantasy point prediction.
/// Implements IModelTrainer for use in command handlers.
/// </summary>
/// <remarks>
/// WHY THIS CLASS EXISTS:
/// ======================
/// Separates training logic from inference:
/// - Training is periodic (daily/weekly background job)
/// - Inference is real-time (every user request)
/// - Training requires database access and significant compute
/// - Inference needs to be fast and lightweight
///
/// TRAINING DATA REQUIREMENTS:
/// ===========================
/// Minimum 200 samples per bike class (expert recommendation):
/// - Less data = overfitting risk
/// - Typical season has ~17 events × 40 riders = 680 samples per class
/// - Early season: Use previous season's model until sufficient data
///
/// TRAINING STRATEGY:
/// ==================
/// 1. **Extract Features**: Query database for completed events
/// 2. **Data Validation**: Remove invalid/incomplete records
/// 3. **Train/Test Split**: 80% train, 20% validation
/// 4. **Configure LightGBM**: Hyperparameters optimized for fantasy scoring
/// 5. **Train Model**: Fit on training data
/// 6. **Evaluate**: Calculate R², MAE, Top-3 accuracy
/// 7. **Save Model**: Version with metadata (date, metrics)
///
/// LIGHTGBM HYPERPARAMETERS:
/// ==========================
/// Tuned for fantasy point prediction (based on ML expert guidance):
///
/// - **NumberOfLeaves**: 31 (default, good starting point)
///   Controls tree complexity. More leaves = more complex model.
///   Too many = overfitting, too few = underfitting.
///
/// - **MinimumExampleCountPerLeaf**: 20
///   Prevents overfitting by requiring minimum samples per leaf.
///   With 200-680 training samples, 20 is appropriate.
///
/// - **LearningRate**: 0.1
///   Controls training speed. Lower = slower but more accurate.
///   0.1 is standard, can reduce to 0.05 if overfitting.
///
/// - **NumberOfIterations**: 100
///   Number of boosting rounds. More = better fit but slower.
///   100 is good balance, monitor validation metrics.
///
/// - **LabelColumnName**: "Label" (FantasyPoints)
///   What we're predicting.
///
/// - **FeatureColumnName**: "Features"
///   Combined feature vector from all 16 features.
///
/// EVALUATION METRICS:
/// ===================
/// - **R² (R-Squared)**: How well model explains variance (0-1, higher = better)
///   Target: > 0.6 (explains 60% of variance)
///
/// - **MAE (Mean Absolute Error)**: Average prediction error in points
///   Target: < 5 points (given fantasy points range 0-50)
///
/// - **Top-3 Accuracy**: % of times actual top-3 finishers in predicted top-3
///   Target: > 30% (random = ~10%, perfect = 100%)
///   This is the most important metric for fantasy users.
///
/// MODEL VERSIONING:
/// ==================
/// Saved models include metadata:
/// - Version: Semantic versioning (v1.0.0)
/// - Date: Training timestamp
/// - Metrics: R², MAE, Top-3 accuracy
/// - Filename: v1.0.0_20250109_r2_0.65.zip (version_date_r2)
///
/// Keep previous model as backup in case new model performs worse.
/// </remarks>
public class ModelTrainer : IModelTrainer
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ModelTrainer> _logger;
    private readonly MLContext _mlContext;

    private const int MinimumTrainingSamples = 200;

    public ModelTrainer(
        ApplicationDbContext dbContext,
        ILogger<ModelTrainer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
        _mlContext = new MLContext(seed: 42); // Fixed seed for reproducibility
    }

    /// <summary>
    /// Trains a new LightGBM model for a specific bike class.
    /// </summary>
    /// <param name="bikeClass">250 or 450 class</param>
    /// <param name="outputDirectory">Directory to save trained model</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Model metadata including metrics and file path</returns>
    /// <remarks>
    /// Steps:
    /// 1. Extract training data from database (completed events)
    /// 2. Validate sufficient data (>= 200 samples)
    /// 3. Configure and train LightGBM model
    /// 4. Evaluate on validation set
    /// 5. Save model with version metadata
    ///
    /// Throws exception if insufficient training data or training fails.
    /// </remarks>
    public async Task<ModelMetadata> TrainModelAsync(
        Domain.Enums.BikeClass bikeClass,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting model training for {BikeClass} class", bikeClass);

        // Step 1: Extract training data
        var trainingData = await ExtractTrainingDataAsync(bikeClass, cancellationToken);

        if (trainingData.Count < MinimumTrainingSamples)
        {
            throw new InvalidOperationException(
                $"Insufficient training data for {bikeClass} class. " +
                $"Found {trainingData.Count} samples, need at least {MinimumTrainingSamples}.");
        }

        _logger.LogInformation(
            "Extracted {SampleCount} training samples for {BikeClass} class",
            trainingData.Count,
            bikeClass);

        // Step 2: Load data into ML.NET IDataView
        var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

        // Step 3: Split into train (80%) and validation (20%)
        var trainTestSplit = _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2, seed: 42);

        // Step 4: Build training pipeline
        // First, convert boolean columns to float (ML.NET Concatenate requires same types)
        var pipeline = _mlContext.Transforms.Conversion.ConvertType(
                new[]
                {
                    new InputOutputColumnPair("IsAllStarFloat", nameof(RiderModelData.IsAllStar)),
                    new InputOutputColumnPair("IsInjuredFloat", nameof(RiderModelData.IsInjured)),
                    new InputOutputColumnPair("TrackTypeIndoorFloat", nameof(RiderModelData.TrackTypeIndoor)),
                    new InputOutputColumnPair("TeamQualityFactoryFloat", nameof(RiderModelData.TeamQualityFactory))
                },
                DataKind.Single)
            .Append(_mlContext.Transforms.Concatenate(
                "Features",
                nameof(RiderModelData.Handicap),
                "IsAllStarFloat",
                "IsInjuredFloat",
                nameof(RiderModelData.PickTrend),
                nameof(RiderModelData.QualifyingPosition),
                nameof(RiderModelData.QualifyingLapTime),
                nameof(RiderModelData.QualyGapToLeader),
                nameof(RiderModelData.AvgFinishLast5),
                nameof(RiderModelData.AvgFantasyPointsLast5),
                nameof(RiderModelData.FinishRate),
                nameof(RiderModelData.SeasonPoints),
                nameof(RiderModelData.TrackHistory),
                "TrackTypeIndoorFloat",
                nameof(RiderModelData.DaysSinceInjury),
                "TeamQualityFactoryFloat",
                nameof(RiderModelData.RecentMomentum)))
            .Append(_mlContext.Regression.Trainers.LightGbm(
                labelColumnName: "Label",
                featureColumnName: "Features",
                numberOfLeaves: 31,
                minimumExampleCountPerLeaf: 20,
                learningRate: 0.1,
                numberOfIterations: 100));

        // Step 5: Train model
        _logger.LogInformation("Training LightGBM model for {BikeClass} class...", bikeClass);
        var startTime = DateTimeOffset.UtcNow;
        var model = pipeline.Fit(trainTestSplit.TrainSet);
        var trainingDuration = DateTimeOffset.UtcNow - startTime;
        _logger.LogInformation(
            "Model training completed in {Duration:F2} seconds",
            trainingDuration.TotalSeconds);

        // Step 6: Evaluate model on validation set
        var predictions = model.Transform(trainTestSplit.TestSet);
        var metrics = _mlContext.Regression.Evaluate(predictions, labelColumnName: "Label");

        _logger.LogInformation(
            "Model evaluation metrics - R²: {RSquared:F3}, MAE: {Mae:F2}, RMSE: {Rmse:F2}",
            metrics.RSquared,
            metrics.MeanAbsoluteError,
            metrics.RootMeanSquaredError);

        // Step 7: Save model with versioning
        Directory.CreateDirectory(outputDirectory);
        var version = "v1.0.0"; // TODO: Implement proper versioning
        var dateStamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var modelFileName = $"{version}_{bikeClass}_{dateStamp}_r2_{metrics.RSquared:F2}.zip";
        var modelPath = Path.Combine(outputDirectory, modelFileName);

        _mlContext.Model.Save(model, dataView.Schema, modelPath);
        _logger.LogInformation("Model saved to {ModelPath}", modelPath);

        return new ModelMetadata(
            Version: version,
            BikeClass: bikeClass,
            TrainedAt: DateTimeOffset.UtcNow,
            TrainingSamples: trainingData.Count,
            RSquared: metrics.RSquared,
            MeanAbsoluteError: metrics.MeanAbsoluteError,
            RootMeanSquaredError: metrics.RootMeanSquaredError,
            ModelPath: modelPath,
            ModelType: "FantasyPoints",
            ValidationAccuracy: 0.0);
    }

    /// <summary>
    /// Extracts training data from database for a specific bike class.
    /// </summary>
    /// <remarks>
    /// Queries all completed events and builds feature vectors.
    /// Only includes riders with:
    /// - Finish position recorded (race completed)
    /// - Valid handicap value
    /// - Fantasy points calculated
    ///
    /// Feature extraction mirrors PredictionService logic for consistency.
    /// </remarks>
    private async Task<List<RiderModelData>> ExtractTrainingDataAsync(
        Domain.Enums.BikeClass bikeClass,
        CancellationToken cancellationToken)
    {
        // Get all completed event riders for this class
        var eventRiders = await _dbContext.EventRiders
            .Include(er => er.Rider)
            .Include(er => er.Event)
            .Where(er =>
                er.BikeClass == bikeClass &&
                er.Event.IsCompleted &&
                er.FinishPosition.HasValue &&
                er.FantasyPoints.HasValue)
            .OrderBy(er => er.Event.EventDate)
            .ToListAsync(cancellationToken);

        var trainingData = new List<RiderModelData>();

        foreach (var eventRider in eventRiders)
        {
            try
            {
                // Extract features (same logic as PredictionService)
                var historicalRaces = await _dbContext.EventRiders
                    .Where(er =>
                        er.RiderId == eventRider.RiderId &&
                        er.BikeClass == eventRider.BikeClass &&
                        er.Event.IsCompleted &&
                        er.Event.EventDate < eventRider.Event.EventDate && // Only past races
                        er.FinishPosition.HasValue)
                    .OrderByDescending(er => er.Event.EventDate)
                    .Take(5)
                    .ToListAsync(cancellationToken);

                var avgFinishLast5 = historicalRaces.Any()
                    ? (float)historicalRaces.Average(er => er.FinishPosition!.Value)
                    : -1;

                var avgFantasyPointsLast5 = historicalRaces.Any()
                    ? (float)historicalRaces.Average(er => er.FantasyPoints ?? 0)
                    : -1;

                var finishRate = historicalRaces.Any()
                    ? (float)historicalRaces.Count(er => er.FinishPosition <= 22) / historicalRaces.Count * 100
                    : 100;

                var seasonPoints = await _dbContext.EventRiders
                    .Where(er =>
                        er.RiderId == eventRider.RiderId &&
                        er.Event.SeriesId == eventRider.Event.SeriesId &&
                        er.Event.IsCompleted &&
                        er.Event.EventDate < eventRider.Event.EventDate)
                    .SumAsync(er => er.FantasyPoints ?? 0, cancellationToken);

                var trackHistory = await _dbContext.EventRiders
                    .Where(er =>
                        er.RiderId == eventRider.RiderId &&
                        er.Event.Venue == eventRider.Event.Venue &&
                        er.Event.IsCompleted &&
                        er.Event.EventDate < eventRider.Event.EventDate &&
                        er.FinishPosition.HasValue)
                    .AverageAsync(er => (float?)er.FinishPosition, cancellationToken) ?? -1;

                var recentMomentum = CalculateRecentMomentum(historicalRaces);

                var modelData = new RiderModelData
                {
                    Handicap = eventRider.Handicap,
                    IsAllStar = eventRider.IsAllStar,
                    IsInjured = eventRider.IsInjured,
                    PickTrend = (float)(eventRider.PickTrend ?? 0),
                    QualifyingPosition = (float)(eventRider.CombinedQualyPosition ?? -1),
                    QualifyingLapTime = (float)(eventRider.BestQualyLapSeconds ?? -1),
                    QualyGapToLeader = (float)(eventRider.QualyGapToLeader ?? -1),
                    AvgFinishLast5 = avgFinishLast5,
                    AvgFantasyPointsLast5 = avgFantasyPointsLast5,
                    FinishRate = finishRate,
                    SeasonPoints = seasonPoints,
                    TrackHistory = trackHistory,
                    TrackTypeIndoor = eventRider.Event.SeriesType == Domain.Enums.SeriesType.Supercross,
                    DaysSinceInjury = 0, // TODO: Implement injury tracking
                    TeamQualityFactory = avgFantasyPointsLast5 >= 30, // Factory = high performers
                    RecentMomentum = recentMomentum,
                    FantasyPoints = eventRider.FantasyPoints!.Value // Target variable
                };

                // Only include samples with sufficient historical data for better model quality
                // Require at least 2 previous races (avgFantasyPointsLast5 > 0)
                if (avgFantasyPointsLast5 < 0)
                {
                    // Skip riders without historical data (first few events)
                    continue;
                }

                trainingData.Add(modelData);

                // Log first few training samples for debugging
                if (trainingData.Count <= 3)
                {
                    _logger.LogInformation(
                        "Sample {Index}: Handicap={H}, Qualy={Q}, AvgPts={Avg}, Label={Label}",
                        trainingData.Count,
                        modelData.Handicap,
                        modelData.QualifyingPosition,
                        modelData.AvgFantasyPointsLast5,
                        modelData.FantasyPoints);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to extract features for rider {RiderId} in event {EventId}, skipping",
                    eventRider.RiderId,
                    eventRider.EventId);
            }
        }

        // Log feature statistics
        var qualyCount = trainingData.Count(d => d.QualifyingPosition > 0);
        var avgPtsCount = trainingData.Count(d => d.AvgFantasyPointsLast5 > 0);
        var handicapRange = trainingData.Any() ?
            $"{trainingData.Min(d => d.Handicap)} to {trainingData.Max(d => d.Handicap)}" : "N/A";
        var labelRange = trainingData.Any() ?
            $"{trainingData.Min(d => d.FantasyPoints)} to {trainingData.Max(d => d.FantasyPoints)}" : "N/A";

        _logger.LogInformation(
            "Feature stats: {QualyPct:F1}% have qualy, {AvgPtsPct:F1}% have history. " +
            "Handicap range: {HRange}, Label range: {LRange}",
            qualyCount * 100f / Math.Max(1, trainingData.Count),
            avgPtsCount * 100f / Math.Max(1, trainingData.Count),
            handicapRange,
            labelRange);

        return trainingData;
    }

    /// <summary>
    /// Calculates recent momentum (improving/declining trend).
    /// </summary>
    private float CalculateRecentMomentum(List<Domain.Entities.EventRider> historicalRaces)
    {
        if (historicalRaces.Count < 3)
            return 0;

        var recentThree = historicalRaces.Take(3).Average(er => er.FantasyPoints ?? 0);

        // If rider only has 3 races total, no previous races to compare
        if (historicalRaces.Count <= 3)
            return 0;

        var previousRaces = historicalRaces.Skip(3).Average(er => er.FantasyPoints ?? 0);

        return (float)(recentThree - previousRaces);
    }

    /// <summary>
    /// Trains binary classification model to predict if rider makes main event (top 22).
    /// </summary>
    public async Task<TrainedModelResult> TrainQualificationModelAsync(
        Domain.Enums.BikeClass bikeClass,
        string modelDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting qualification model training for {BikeClass} class", bikeClass);

        // Step 1: Extract training data (includes ALL riders, even DNQs)
        var trainingData = await ExtractQualificationFeaturesAsync(bikeClass, cancellationToken);

        if (trainingData.Count < MinimumTrainingSamples)
        {
            throw new InvalidOperationException(
                $"Insufficient training data for {bikeClass} qualification model. " +
                $"Need {MinimumTrainingSamples}, got {trainingData.Count}. " +
                $"Import more historical events.");
        }

        _logger.LogInformation(
            "Extracted {TrainingCount} training samples for {BikeClass} qualification model",
            trainingData.Count,
            bikeClass);

        // Step 2: Convert to IDataView
        var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

        // Step 3: Train/test split (80/20)
        // SCHEMA DEBUGGING: Log the input schema
        _logger.LogInformation("=== INPUT DATAVIEW SCHEMA ===");
        foreach (var column in dataView.Schema)
        {
            _logger.LogInformation(
                "Column: {Name}, Type: {Type}, Index: {Index}",
                column.Name,
                column.Type,
                column.Index);
        }

        var trainTestSplit = _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2, seed: 42);

        // Step 4: Build binary classification pipeline using MLConstants for consistency
        // FastTree binary classification has built-in calibration for probability output
        var pipeline = _mlContext.Transforms.Concatenate(
                MLConstants.QualificationFeatures.Features,
                MLConstants.QualificationFeatures.FeatureOrder)
            .Append(_mlContext.BinaryClassification.Trainers.FastTree(
                labelColumnName: MLConstants.QualificationFeatures.Label,
                featureColumnName: MLConstants.QualificationFeatures.Features,
                numberOfLeaves: MLConstants.TrainingParams.NumberOfLeaves,
                numberOfTrees: MLConstants.TrainingParams.NumberOfTrees,
                minimumExampleCountPerLeaf: MLConstants.TrainingParams.MinimumExampleCountPerLeaf,
                learningRate: MLConstants.TrainingParams.LearningRate));

        // Step 5: Train model
        _logger.LogInformation("Training LightGBM qualification model for {BikeClass} class...", bikeClass);
        var startTime = DateTimeOffset.UtcNow;
        var model = pipeline.Fit(trainTestSplit.TrainSet);
        var trainingDuration = DateTimeOffset.UtcNow - startTime;
        _logger.LogInformation("Model training completed in {Duration:F2} seconds", trainingDuration.TotalSeconds);

        // Step 6: Evaluate model
        var predictions = model.Transform(trainTestSplit.TestSet);
        var metrics = _mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: "Label");

        _logger.LogInformation(
            "Qualification model metrics - Accuracy: {Accuracy:F3}, AUC: {Auc:F3}, F1: {F1:F3}",
            metrics.Accuracy,
            metrics.AreaUnderRocCurve,
            metrics.F1Score);

        // Diagnostic: Check some test set predictions
        var testPredictions = _mlContext.Data.CreateEnumerable<QualificationPrediction>(predictions, reuseRowObject: false).Take(5).ToList();
        var testData = _mlContext.Data.CreateEnumerable<QualificationModelData>(trainTestSplit.TestSet, reuseRowObject: false).Take(5).ToList();
        for (int i = 0; i < Math.Min(testPredictions.Count, testData.Count); i++)
        {
            _logger.LogInformation(
                "Test Sample {Num}: Hcp={Hcp}, AvgFinish={Avg}, FinishRate={Rate}, AllStar={AS} → Predicted={Pred}, Probability={Prob:F2}, Actual={Actual}",
                i + 1,
                testData[i].Handicap,
                testData[i].AvgFinishLast5,
                testData[i].FinishRate,
                testData[i].IsAllStar,
                testPredictions[i].PredictedMadeMain,
                testPredictions[i].Probability,
                testData[i].MadeMain);
        }

        // Step 7: Save model with consistent file name for watchForChanges to work
        // TEST: Verify model works BEFORE saving
        var predEngine = _mlContext.Model.CreatePredictionEngine<QualificationModelData, QualificationPrediction>(model);

        // Test with sample from test set
        var firstTestSample = _mlContext.Data.CreateEnumerable<QualificationModelData>(trainTestSplit.TestSet, reuseRowObject: false).First();
        var testSampleResult = predEngine.Predict(firstTestSample);
        _logger.LogInformation(
            "TEST SAMPLE: Hcp={Hcp}, Avg={Avg:F1}, Rate={Rate:F0}, Track={Track:F1}, AS={AS} → Score={Score:F2}, Prob={Prob:F2}",
            firstTestSample.Handicap, firstTestSample.AvgFinishLast5, firstTestSample.FinishRate,
            firstTestSample.TrackHistory, firstTestSample.IsAllStar,
            testSampleResult.Score, testSampleResult.Probability);

        // Test with elite rider (should have high qualification probability)
        var eliteRider = new QualificationModelData
        {
            Handicap = 1,
            AvgFinishLast5 = 5,
            FinishRate = 100,
            TrackHistory = -1,
            IsAllStar = 1.0f
        };
        var eliteResult = predEngine.Predict(eliteRider);
        _logger.LogInformation(
            "ELITE RIDER: Hcp={Hcp}, Avg={Avg:F1}, Rate={Rate:F0}, Track={Track:F1}, AS={AS} → Score={Score:F2}, Prob={Prob:F2}",
            eliteRider.Handicap, eliteRider.AvgFinishLast5, eliteRider.FinishRate,
            eliteRider.TrackHistory, eliteRider.IsAllStar,
            eliteResult.Score, eliteResult.Probability);

        // CRITICAL: Using consistent names (not date-stamped) so PredictionEnginePool can detect changes
        var modelFileName = MLConstants.GetModelFileName(bikeClass, "Qualification");
        var modelPath = Path.Combine(modelDirectory, modelFileName);

        // SCHEMA DEBUGGING: Log the schema being saved with the model
        _logger.LogInformation("=== MODEL SAVE SCHEMA (what inference expects) ===");
        foreach (var column in dataView.Schema)
        {
            _logger.LogInformation(
                "SaveSchema Column: {Name}, Type: {Type}, Index: {Index}",
                column.Name,
                column.Type,
                column.Index);
        }

        _mlContext.Model.Save(model, dataView.Schema, modelPath);
        _logger.LogInformation(
            "Qualification model saved to {ModelPath} (AUC={Auc:F3})",
            modelPath, metrics.AreaUnderRocCurve);

        return new TrainedModelResult(
            Version: "v1.0.0",
            BikeClass: bikeClass.ToString(),
            ModelType: "Qualification",
            TrainedAt: DateTimeOffset.UtcNow,
            TrainingSamples: trainingData.Count,
            RSquared: metrics.AreaUnderRocCurve, // Use AUC as quality metric for classification
            MeanAbsoluteError: 1.0 - metrics.Accuracy, // Use error rate
            RootMeanSquaredError: 0,
            ModelPath: modelPath,
            ValidationAccuracy: metrics.Accuracy);
    }

    /// <summary>
    /// Trains regression model to predict finish position (1-22) for riders who make main event.
    /// </summary>
    public async Task<TrainedModelResult> TrainFinishPositionModelAsync(
        Domain.Enums.BikeClass bikeClass,
        string modelDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting finish position model training for {BikeClass} class", bikeClass);

        // Step 1: Extract training data (only riders who made main event)
        var trainingData = await ExtractFinishPositionFeaturesAsync(bikeClass, cancellationToken);

        if (trainingData.Count < MinimumTrainingSamples)
        {
            throw new InvalidOperationException(
                $"Insufficient training data for {bikeClass} finish position model. " +
                $"Need {MinimumTrainingSamples}, got {trainingData.Count}. " +
                $"Import more historical events.");
        }

        _logger.LogInformation(
            "Extracted {TrainingCount} training samples for {BikeClass} finish position model",
            trainingData.Count,
            bikeClass);

        // Step 2: Convert to IDataView
        var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

        // Step 3: Train/test split (80/20)
        var trainTestSplit = _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2, seed: 42);

        // Step 4: Build regression pipeline using MLConstants for consistency
        var pipeline = _mlContext.Transforms.Conversion.ConvertType(
                new[]
                {
                    new InputOutputColumnPair(MLConstants.FinishPositionFeatures.IsAllStarFloat, MLConstants.FinishPositionFeatures.IsAllStar),
                    new InputOutputColumnPair(MLConstants.FinishPositionFeatures.TrackTypeIndoorFloat, MLConstants.FinishPositionFeatures.TrackTypeIndoor)
                },
                DataKind.Single)
            .Append(_mlContext.Transforms.Concatenate(
                MLConstants.FinishPositionFeatures.Features,
                MLConstants.FinishPositionFeatures.FeatureOrder))
            .Append(_mlContext.Regression.Trainers.FastTree(
                labelColumnName: MLConstants.FinishPositionFeatures.Label,
                featureColumnName: MLConstants.FinishPositionFeatures.Features,
                numberOfLeaves: MLConstants.TrainingParams.NumberOfLeaves,
                numberOfTrees: MLConstants.TrainingParams.NumberOfTrees,
                minimumExampleCountPerLeaf: MLConstants.TrainingParams.MinimumExampleCountPerLeaf,
                learningRate: MLConstants.TrainingParams.LearningRate));

        // Step 5: Train model
        _logger.LogInformation("Training FastTree finish position model for {BikeClass} class...", bikeClass);
        var startTime = DateTimeOffset.UtcNow;
        var model = pipeline.Fit(trainTestSplit.TrainSet);
        var trainingDuration = DateTimeOffset.UtcNow - startTime;
        _logger.LogInformation("Model training completed in {Duration:F2} seconds", trainingDuration.TotalSeconds);

        // Step 6: Evaluate model
        var predictions = model.Transform(trainTestSplit.TestSet);
        var metrics = _mlContext.Regression.Evaluate(predictions, labelColumnName: "Label");

        _logger.LogInformation(
            "Finish position model metrics - R²: {RSquared:F3}, MAE: {Mae:F2}, RMSE: {Rmse:F2}",
            metrics.RSquared,
            metrics.MeanAbsoluteError,
            metrics.RootMeanSquaredError);

        // Step 7: Save model with consistent file name for watchForChanges to work
        // CRITICAL: Using consistent names (not date-stamped) so PredictionEnginePool can detect changes
        var modelFileName = MLConstants.GetModelFileName(bikeClass, "FinishPosition");
        var modelPath = Path.Combine(modelDirectory, modelFileName);

        _mlContext.Model.Save(model, dataView.Schema, modelPath);
        _logger.LogInformation(
            "Finish position model saved to {ModelPath} (R²={RSquared:F3})",
            modelPath, metrics.RSquared);

        return new TrainedModelResult(
            Version: "v1.0.0",
            BikeClass: bikeClass.ToString(),
            ModelType: "FinishPosition",
            TrainedAt: DateTimeOffset.UtcNow,
            TrainingSamples: trainingData.Count,
            RSquared: metrics.RSquared,
            MeanAbsoluteError: metrics.MeanAbsoluteError,
            RootMeanSquaredError: metrics.RootMeanSquaredError,
            ModelPath: modelPath,
            ValidationAccuracy: 0.0); // Not applicable for regression
    }

    /// <summary>
    /// Extracts features for qualification prediction (includes ALL riders).
    /// </summary>
    /// <remarks>
    /// CRITICAL: Only uses historical data from the SAME series type:
    /// - Supercross predictions use ONLY Supercross history (last 2 years)
    /// - Motocross predictions use ONLY Motocross history (last 2 years)
    /// - SuperMotocross predictions use ONLY SuperMotocross history
    ///
    /// This is essential because rider performance differs significantly between disciplines.
    /// </remarks>
    private async Task<List<QualificationModelData>> ExtractQualificationFeaturesAsync(
        Domain.Enums.BikeClass bikeClass,
        CancellationToken cancellationToken)
    {
        // Get ALL completed event riders (including DNQs)
        var eventRiders = await _dbContext.EventRiders
            .Include(er => er.Rider)
            .Include(er => er.Event)
                .ThenInclude(e => e.Series)
            .Where(er =>
                er.BikeClass == bikeClass &&
                er.Event.IsCompleted)
            .OrderBy(er => er.Event.EventDate)
            .ToListAsync(cancellationToken);

        var allStarCount = eventRiders.Count(er => er.IsAllStar);
        _logger.LogInformation(
            "Initial query loaded {Total} event riders for {Class}, {AllStars} are All-Stars",
            eventRiders.Count,
            bikeClass,
            allStarCount);

        var trainingData = new List<QualificationModelData>();
        var twoYearsAgo = DateTimeOffset.UtcNow.AddYears(-2);
        var skippedNoHistory = 0;
        var skippedNoHistoryAllStars = 0;

        foreach (var eventRider in eventRiders)
        {
            try
            {
                var eventSeriesType = eventRider.Event.Series.SeriesType;

                // Get historical races from SAME series type only (last 2 years)
                // CRITICAL: Include ALL races (both made main AND DNQ) to avoid data leakage
                var historicalRaces = await _dbContext.EventRiders
                    .Include(er => er.Event)
                        .ThenInclude(e => e.Series)
                    .Where(er =>
                        er.RiderId == eventRider.RiderId &&
                        er.BikeClass == eventRider.BikeClass &&
                        er.Event.IsCompleted &&
                        er.Event.EventDate < eventRider.Event.EventDate &&
                        er.Event.EventDate >= twoYearsAgo && // Last 2 years only
                        er.Event.Series.SeriesType == eventSeriesType) // SAME series type
                    .OrderByDescending(er => er.Event.EventDate)
                    .Take(5)
                    .ToListAsync(cancellationToken);

                // For DNQ races (NULL finish position), treat as position 30 (worse than 22)
                var avgFinishLast5 = historicalRaces.Any()
                    ? (float)historicalRaces.Average(er => er.FinishPosition ?? 30)
                    : -1;

                // Finish rate: percentage that made main (finish ≤ 22)
                var finishRate = historicalRaces.Any()
                    ? (float)historicalRaces.Count(er => er.FinishPosition.HasValue && er.FinishPosition <= 22) / historicalRaces.Count * 100
                    : 50; // Default to 50% instead of 100% (more realistic)

                // Season points from CURRENT series only (not filtered by series type - same series ID)
                var seasonPoints = await _dbContext.EventRiders
                    .Where(er =>
                        er.RiderId == eventRider.RiderId &&
                        er.Event.SeriesId == eventRider.Event.SeriesId &&
                        er.Event.IsCompleted &&
                        er.Event.EventDate < eventRider.Event.EventDate)
                    .SumAsync(er => er.FantasyPoints ?? 0, cancellationToken);

                // Track history from SAME series type (last 2 years)
                // CRITICAL: Include ALL races (DNQ treated as position 30)
                var trackHistoryRaces = await _dbContext.EventRiders
                    .Include(er => er.Event)
                        .ThenInclude(e => e.Series)
                    .Where(er =>
                        er.RiderId == eventRider.RiderId &&
                        er.Event.Venue == eventRider.Event.Venue &&
                        er.Event.IsCompleted &&
                        er.Event.EventDate < eventRider.Event.EventDate &&
                        er.Event.EventDate >= twoYearsAgo &&
                        er.Event.Series.SeriesType == eventSeriesType)
                    .ToListAsync(cancellationToken);

                var trackHistory = trackHistoryRaces.Any()
                    ? (float)trackHistoryRaces.Average(er => er.FinishPosition ?? 30)
                    : -1;

                // Target: Did they make the main event?
                bool madeMain = eventRider.FinishPosition.HasValue && eventRider.FinishPosition <= 22;

                // CRITICAL: Skip riders without ANY historical data
                // Training on riders with no history just teaches the model to predict based on handicap alone
                // and pollutes the feature space with -1 defaults
                if (avgFinishLast5 <= 0)
                {
                    skippedNoHistory++;
                    if (eventRider.IsAllStar)
                    {
                        skippedNoHistoryAllStars++;
                    }
                    continue; // Skip this sample - no meaningful historical data
                }

                var sample = new QualificationModelData
                {
                    Handicap = eventRider.Handicap,
                    AvgFinishLast5 = avgFinishLast5,
                    FinishRate = finishRate,
                    TrackHistory = trackHistory,
                    // NOTE: PickTrend removed - not available at prediction time
                    IsAllStar = eventRider.IsAllStar ? 1.0f : 0.0f,
                    MadeMain = madeMain
                };

                // Log first 3 training samples
                if (trainingData.Count < 3)
                {
                    _logger.LogInformation(
                        "Training Sample {Num}: Hcp={Hcp}, AvgFinish={Avg}, FinishRate={Rate}, Track={Track}, AllStar={All}, MadeMain={Made}",
                        trainingData.Count + 1,
                        sample.Handicap,
                        sample.AvgFinishLast5,
                        sample.FinishRate,
                        sample.TrackHistory,
                        sample.IsAllStar,
                        sample.MadeMain);
                }

                trainingData.Add(sample);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to extract qualification features for rider {RiderId}, skipping",
                    eventRider.RiderId);
            }
        }

        // Diagnostic logging: Check training data quality
        var madeMainCount = trainingData.Count(d => d.MadeMain);
        var dnqCount = trainingData.Count - madeMainCount;
        var avgHandicap = trainingData.Average(d => d.Handicap);
        var samplesWithHistoricalData = trainingData.Count(d => d.AvgFinishLast5 > 0);
        var allStarsInTraining = trainingData.Count(d => d.IsAllStar >= 0.5f);

        _logger.LogInformation(
            "Skipped {SkippedTotal} riders with no history ({SkippedAllStars} All-Stars)",
            skippedNoHistory,
            skippedNoHistoryAllStars);

        _logger.LogInformation(
            "Qualification training data stats - Total: {Total}, MadeMain: {MadeMain} ({Pct:F1}%), DNQ: {DNQ}, AvgHandicap: {AvgHandicap:F2}, WithHistory: {WithHistory}, AllStars: {AllStars}",
            trainingData.Count,
            madeMainCount,
            100.0 * madeMainCount / trainingData.Count,
            dnqCount,
            avgHandicap,
            samplesWithHistoricalData,
            allStarsInTraining);

        // Sample first few records and first All-Star for inspection
        _logger.LogInformation("First 3 training samples:");
        foreach (var sample in trainingData.Take(3))
        {
            _logger.LogInformation(
                "Sample: Handicap={H}, AvgFinish={AF:F1}, FinishRate={FR:F1}, Track={Track}, AllStar={AS}, MadeMain={MM}",
                sample.Handicap,
                sample.AvgFinishLast5,
                sample.FinishRate,
                sample.TrackHistory,
                sample.IsAllStar,
                sample.MadeMain);
        }

        // Show first All-Star sample if exists
        var firstAllStar = trainingData.FirstOrDefault(d => d.IsAllStar >= 0.5f);
        if (firstAllStar != null)
        {
            _logger.LogInformation(
                "First All-Star sample: Handicap={H}, AvgFinish={AF:F1}, FinishRate={FR:F1}, AllStar={AS}, MadeMain={MM}",
                firstAllStar.Handicap,
                firstAllStar.AvgFinishLast5,
                firstAllStar.FinishRate,
                firstAllStar.IsAllStar,
                firstAllStar.MadeMain);
        }
        else
        {
            _logger.LogWarning("WARNING: No All-Star riders in training data!");
        }

        return trainingData;
    }

    /// <summary>
    /// Extracts features for finish position prediction (only riders who made main).
    /// </summary>
    /// <remarks>
    /// CRITICAL: Only uses historical data from the SAME series type:
    /// - Supercross predictions use ONLY Supercross history (last 2 years)
    /// - Motocross predictions use ONLY Motocross history (last 2 years)
    /// - SuperMotocross predictions use ONLY SuperMotocross history
    ///
    /// This is essential because rider performance differs significantly between disciplines.
    /// </remarks>
    private async Task<List<FinishPositionModelData>> ExtractFinishPositionFeaturesAsync(
        Domain.Enums.BikeClass bikeClass,
        CancellationToken cancellationToken)
    {
        // Get ONLY riders who made the main event (finish position ≤ 22)
        var eventRiders = await _dbContext.EventRiders
            .Include(er => er.Rider)
            .Include(er => er.Event)
                .ThenInclude(e => e.Series)
            .Where(er =>
                er.BikeClass == bikeClass &&
                er.Event.IsCompleted &&
                er.FinishPosition.HasValue &&
                er.FinishPosition <= 22) // Only main event riders
            .OrderBy(er => er.Event.EventDate)
            .ToListAsync(cancellationToken);

        var trainingData = new List<FinishPositionModelData>();
        var twoYearsAgo = DateTimeOffset.UtcNow.AddYears(-2);

        foreach (var eventRider in eventRiders)
        {
            try
            {
                var eventSeriesType = eventRider.Event.Series.SeriesType;

                // Get historical races from SAME series type only (last 2 years)
                // CRITICAL: Include ALL races (both made main AND DNQ) to avoid data leakage
                var historicalRaces = await _dbContext.EventRiders
                    .Include(er => er.Event)
                        .ThenInclude(e => e.Series)
                    .Where(er =>
                        er.RiderId == eventRider.RiderId &&
                        er.BikeClass == eventRider.BikeClass &&
                        er.Event.IsCompleted &&
                        er.Event.EventDate < eventRider.Event.EventDate &&
                        er.Event.EventDate >= twoYearsAgo && // Last 2 years only
                        er.Event.Series.SeriesType == eventSeriesType) // SAME series type
                    .OrderByDescending(er => er.Event.EventDate)
                    .Take(5)
                    .ToListAsync(cancellationToken);

                // For DNQ races (NULL finish position), treat as position 30 (worse than 22)
                var avgFinishLast5 = historicalRaces.Any()
                    ? (float)historicalRaces.Average(er => er.FinishPosition ?? 30)
                    : -1;

                var avgFantasyPointsLast5 = historicalRaces.Any()
                    ? (float)historicalRaces.Average(er => er.FantasyPoints ?? 0)
                    : -1;

                // Skip riders without historical data (they won't help train the model)
                if (avgFantasyPointsLast5 < 0)
                    continue;

                // Track history from SAME series type (last 2 years)
                // CRITICAL: Include ALL races (DNQ treated as position 30)
                var trackHistoryRaces = await _dbContext.EventRiders
                    .Include(er => er.Event)
                        .ThenInclude(e => e.Series)
                    .Where(er =>
                        er.RiderId == eventRider.RiderId &&
                        er.Event.Venue == eventRider.Event.Venue &&
                        er.Event.IsCompleted &&
                        er.Event.EventDate < eventRider.Event.EventDate &&
                        er.Event.EventDate >= twoYearsAgo &&
                        er.Event.Series.SeriesType == eventSeriesType)
                    .ToListAsync(cancellationToken);

                var trackHistory = trackHistoryRaces.Any()
                    ? (float)trackHistoryRaces.Average(er => er.FinishPosition ?? 30)
                    : -1;

                var recentMomentum = CalculateRecentMomentum(historicalRaces);

                // Season points from CURRENT series only (not filtered by series type - same series ID)
                var seasonPoints = await _dbContext.EventRiders
                    .Where(er =>
                        er.RiderId == eventRider.RiderId &&
                        er.Event.SeriesId == eventRider.Event.SeriesId &&
                        er.Event.IsCompleted &&
                        er.Event.EventDate < eventRider.Event.EventDate)
                    .SumAsync(er => er.FantasyPoints ?? 0, cancellationToken);

                trainingData.Add(new FinishPositionModelData
                {
                    Handicap = eventRider.Handicap,
                    AvgFinishLast5 = avgFinishLast5,
                    AvgFantasyPointsLast5 = avgFantasyPointsLast5,
                    TrackHistory = trackHistory,
                    RecentMomentum = recentMomentum,
                    SeasonPoints = seasonPoints,
                    IsAllStar = eventRider.IsAllStar,
                    TrackTypeIndoor = eventRider.Event.SeriesType == Domain.Enums.SeriesType.Supercross,
                    FinishPosition = eventRider.FinishPosition!.Value
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to extract finish position features for rider {RiderId}, skipping",
                    eventRider.RiderId);
            }
        }

        return trainingData;
    }
}

/// <summary>
/// Metadata about a trained model.
/// </summary>
/// <remarks>
/// Saved alongside model file for tracking:
/// - Version history
/// - Training performance metrics
/// - Model selection (choose best R²)
/// </remarks>
public record ModelMetadata(
    string Version,
    Domain.Enums.BikeClass BikeClass,
    DateTimeOffset TrainedAt,
    int TrainingSamples,
    double RSquared,
    double MeanAbsoluteError,
    double RootMeanSquaredError,
    string ModelPath,
    string ModelType = "FantasyPoints",
    double ValidationAccuracy = 0.0);
