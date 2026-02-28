/**
 * Statistical analysis utilities for anomaly detection, forecasting,
 * and Bayesian smoothing.
 */

const DEFAULT_GLOBAL_PRIOR = 0.5
const DEFAULT_SIGNIFICANCE_THRESHOLD = 20

/**
 * Bayesian-smoothed win rate (returns 0–100 percentage).
 * Formula: (wins + C * globalPrior) / (totalMatches + C) * 100
 */
export function smoothedWinRate(
  wins: number,
  totalMatches: number,
  globalPrior: number = DEFAULT_GLOBAL_PRIOR,
  c: number = DEFAULT_SIGNIFICANCE_THRESHOLD,
): number {
  if (totalMatches <= 0) return Math.round(globalPrior * 100 * 100) / 100
  const smoothed = (wins + c * globalPrior) / (totalMatches + c)
  return Math.round(smoothed * 100 * 100) / 100
}

/**
 * Returns a human-readable label for sample size confidence.
 */
export function sampleSizeLabel(totalMatches: number): string {
  if (totalMatches < 10) return 'Very small sample'
  if (totalMatches < 30) return 'Small sample'
  if (totalMatches < 100) return 'Moderate sample'
  return 'Large sample'
}

export interface AnomalyResult<T> {
  value: T
  isAnomaly: boolean
  deviation: number // Number of standard deviations from mean
  zScore: number
}

export interface ForecastResult {
  projectedValue: number
  confidence: 'low' | 'medium' | 'high'
  trend: 'increasing' | 'decreasing' | 'stable'
  daysToTarget?: number
  targetValue?: number
}

/**
 * Calculates the mean of an array of numbers
 */
export function calculateMean(values: number[]): number {
  if (values.length === 0) return 0
  const sum = values.reduce((acc, val) => acc + val, 0)
  return sum / values.length
}

/**
 * Calculates the standard deviation of an array of numbers
 */
export function calculateStandardDeviation(values: number[]): number {
  if (values.length === 0) return 0
  const mean = calculateMean(values)
  const squaredDiffs = values.map((val) => Math.pow(val - mean, 2))
  const avgSquaredDiff = calculateMean(squaredDiffs)
  return Math.sqrt(avgSquaredDiff)
}

/**
 * Calculates the z-score (standard score) for a value
 */
export function calculateZScore(value: number, mean: number, stdDev: number): number {
  if (stdDev === 0) return 0
  return (value - mean) / stdDev
}

/**
 * Detects anomalies in an array of values
 * An anomaly is defined as a value that is 2 or more standard deviations from the mean
 * @param values Array of numeric values to analyze
 * @param threshold Number of standard deviations to use as threshold (default: 2)
 * @returns Array of anomaly results with the original value and anomaly status
 */
export function detectAnomalies(
  values: number[],
  threshold: number = 2
): AnomalyResult<number>[] {
  if (values.length === 0) return []

  const mean = calculateMean(values)
  const stdDev = calculateStandardDeviation(values)

  return values.map((value) => {
    const zScore = calculateZScore(value, mean, stdDev)
    const deviation = Math.abs(zScore)
    const isAnomaly = deviation >= threshold

    return {
      value,
      isAnomaly,
      deviation,
      zScore,
    }
  })
}

/**
 * Detects anomalies in an array of objects with a numeric property
 * @param data Array of objects
 * @param valueExtractor Function to extract the numeric value from each object
 * @param threshold Number of standard deviations to use as threshold (default: 2)
 */
export function detectAnomaliesInData<T>(
  data: T[],
  valueExtractor: (item: T) => number,
  threshold: number = 2
): Array<AnomalyResult<T>> {
  if (data.length === 0) return []

  const values = data.map(valueExtractor)
  const mean = calculateMean(values)
  const stdDev = calculateStandardDeviation(values)

  return data.map((item) => {
    const value = valueExtractor(item)
    const zScore = calculateZScore(value, mean, stdDev)
    const deviation = Math.abs(zScore)
    const isAnomaly = deviation >= threshold

    return {
      value: item,
      isAnomaly,
      deviation,
      zScore,
    }
  })
}

/**
 * Simple linear regression to calculate trend
 * Returns slope and intercept for y = mx + b
 * Handles single-point data by returning flat line (slope = 0)
 */
function linearRegression(x: number[], y: number[]): { slope: number; intercept: number } {
  const n = x.length
  if (n === 0) return { slope: 0, intercept: 0 }
  
  // Guard against single-point data to avoid 0/0 division
  if (n === 1) {
    return { slope: 0, intercept: y[0] || 0 }
  }

  const sumX = x.reduce((acc, val) => acc + val, 0)
  const sumY = y.reduce((acc, val) => acc + val, 0)
  const sumXY = x.reduce((acc, val, idx) => acc + val * y[idx], 0)
  const sumXX = x.reduce((acc, val) => acc + val * val, 0)

  const denominator = n * sumXX - sumX * sumX
  // Guard against division by zero
  const slope = denominator !== 0 ? (n * sumXY - sumX * sumY) / denominator : 0
  const intercept = (sumY - slope * sumX) / n

  return { slope, intercept }
}

/**
 * Calculates the coefficient of determination (R²) for regression quality
 */
function calculateRSquared(x: number[], y: number[], slope: number, intercept: number): number {
  const yMean = calculateMean(y)
  const predictedY = x.map((xi) => slope * xi + intercept)

  const totalSumSquares = y.reduce((acc, yi) => acc + Math.pow(yi - yMean, 2), 0)
  const residualSumSquares = y.reduce(
    (acc, yi, idx) => acc + Math.pow(yi - predictedY[idx], 2),
    0
  )

  if (totalSumSquares === 0) return 0
  return 1 - residualSumSquares / totalSumSquares
}

/**
 * Generates a simple forecast based on historical data
 * Uses linear regression to project future values
 * @param historicalValues Array of historical numeric values (ordered chronologically)
 * @param daysAhead Number of days to project ahead (default: 7)
 * @param targetValue Optional target value to calculate days to reach
 * @returns Forecast result with projected value and trend
 */
export function generateForecast(
  historicalValues: number[],
  daysAhead: number = 7,
  targetValue?: number
): ForecastResult {
  if (historicalValues.length === 0) {
    return {
      projectedValue: 0,
      confidence: 'low',
      trend: 'stable',
    }
  }

  // Handle single-point historical data to avoid NaN outputs
  // With only one point, linear regression would compute 0/0 for slope denominator
  if (historicalValues.length === 1) {
    return {
      projectedValue: historicalValues[0],
      confidence: 'low', // Low confidence with insufficient data
      trend: 'stable', // Cannot determine trend from single point
    }
  }

  // Use indices as x values (time points)
  const x = historicalValues.map((_, idx) => idx)
  const y = historicalValues

  const { slope, intercept } = linearRegression(x, y)
  
  // Guard against NaN values from regression
  if (!Number.isFinite(slope) || !Number.isFinite(intercept)) {
    return {
      projectedValue: historicalValues.at(-1) || 0,
      confidence: 'low',
      trend: 'stable',
    }
  }
  
  const rSquared = calculateRSquared(x, y, slope, intercept)

  // Project forward
  const lastIndex = historicalValues.length - 1
  const projectedValue = slope * (lastIndex + daysAhead) + intercept

  // Determine trend
  let trend: 'increasing' | 'decreasing' | 'stable'
  if (Math.abs(slope) < 0.01) {
    trend = 'stable'
  } else if (slope > 0) {
    trend = 'increasing'
  } else {
    trend = 'decreasing'
  }

  // Determine confidence based on R² and data points
  let confidence: 'low' | 'medium' | 'high'
  if (rSquared > 0.7 && historicalValues.length >= 10) {
    confidence = 'high'
  } else if (rSquared > 0.4 && historicalValues.length >= 5) {
    confidence = 'medium'
  } else {
    confidence = 'low'
  }

  // Calculate days to target if provided
  let daysToTarget: number | undefined
  if (targetValue !== undefined && slope !== 0) {
    // Solve: targetValue = slope * (lastIndex + days) + intercept
    // days = (targetValue - intercept) / slope - lastIndex
    const calculatedDays = (targetValue - intercept) / slope - lastIndex
    daysToTarget = calculatedDays > 0 ? Math.ceil(calculatedDays) : undefined
  }

  return {
    projectedValue: Math.max(0, projectedValue), // Ensure non-negative for ratings
    confidence,
    trend,
    daysToTarget,
    targetValue,
  }
}

/**
 * Calculates moving average for smoothing data
 * @param values Array of numeric values
 * @param windowSize Size of the moving window (default: 5)
 */
export function calculateMovingAverage(values: number[], windowSize: number = 5): number[] {
  if (values.length === 0) return []
  if (windowSize >= values.length) return [calculateMean(values)]

  const result: number[] = []
  for (let i = 0; i < values.length; i++) {
    const start = Math.max(0, i - Math.floor(windowSize / 2))
    const end = Math.min(values.length, i + Math.ceil(windowSize / 2))
    const window = values.slice(start, end)
    result.push(calculateMean(window))
  }

  return result
}

/**
 * Detects if a win rate is anomalous
 * Specifically checks if win rate deviates significantly from expected (50%)
 */
export function detectWinRateAnomaly(
  winRate: number,
  totalMatches: number,
  threshold: number = 2
): AnomalyResult<number> {
  // Expected win rate is 50% (0.5)
  const expectedWinRate = 0.5
  // Standard error for proportion: sqrt(p * (1-p) / n)
  const standardError = Math.sqrt((expectedWinRate * (1 - expectedWinRate)) / totalMatches)
  const zScore = (winRate - expectedWinRate) / standardError
  const deviation = Math.abs(zScore)
  const isAnomaly = deviation >= threshold

  return {
    value: winRate,
    isAnomaly,
    deviation,
    zScore,
  }
}

/**
 * Calculates a classic trailing moving average.
 * Only uses previous data points, not future ones.
 */
export function calculateTrailingMovingAverage(
  data: number[],
  windowSize: number
): number[] {
  if (windowSize <= 0 || data.length === 0) {
    return []
  }

  const result: number[] = []

  for (let i = 0; i < data.length; i++) {
    // Classic trailing window: only look back
    const start = Math.max(0, i - windowSize + 1)
    const end = i + 1

    let sum = 0
    let count = 0

    for (let j = start; j < end; j++) {
      sum += data[j]
      count++
    }

    result.push(count > 0 ? sum / count : data[i])
  }

  return result
}
