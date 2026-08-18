using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Models.ViewModels;

namespace ReFuel_Macedonia.Services.Ai;

public interface IPricePredictionService
{
    Task<ForecastVm> ForecastAsync(int fuelTypeId, int horizonWeeks = 4);
    Task<List<ForecastVm>> ForecastAllAsync(int horizonWeeks = 4);
    Task<int> RegenerateAsync(int horizonWeeks = 4);
}

public class PricePredictionService : IPricePredictionService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<PricePredictionService> _logger;

    public PricePredictionService(ApplicationDbContext db, ILogger<PricePredictionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private const int Lags = 4;

    public async Task<List<ForecastVm>> ForecastAllAsync(int horizonWeeks = 4)
    {
        var fuels = await _db.FuelTypes.OrderBy(f => f.SortOrder).ToListAsync();
        var list = new List<ForecastVm>();
        foreach (var fuel in fuels)
            list.Add(await ForecastAsync(fuel.Id, horizonWeeks));
        return list;
    }

    public async Task<ForecastVm> ForecastAsync(int fuelTypeId, int horizonWeeks = 4)
    {
        var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var fuel = await _db.FuelTypes.FirstOrDefaultAsync(f => f.Id == fuelTypeId);

        var history = await _db.NationalPrices
            .Where(p => p.FuelTypeId == fuelTypeId)
            .OrderBy(p => p.EffectiveFrom)
            .Select(p => new { p.EffectiveFrom, p.Price })
            .ToListAsync();

        var vm = new ForecastVm
        {
            FuelTypeId = fuelTypeId,
            Fuel = fuel?.LocalizedName(culture) ?? string.Empty,
            Color = fuel?.ColorHex ?? "#2563EB",
            Model = "Holt + Ridge AR(4)"
        };

        if (history.Count < Lags + 4)
        {
            vm.Confidence = 0;
            vm.AdviceKey = "ai.advice.nodata";
            return vm;
        }

        var series = history.Select(h => (double)h.Price).ToArray();
        var lastDate = history[^1].EffectiveFrom;
        vm.CurrentPrice = history[^1].Price;

        var holdout = Math.Max(6, series.Length / 5);
        if (holdout >= series.Length - Lags - 2) holdout = Math.Max(3, (series.Length - Lags - 2) / 2);

        var trainLength = series.Length - holdout;

        var holtErrors = new List<double>();
        var ridgeErrors = new List<double>();

        for (var i = 0; i < holdout; i++)
        {
            var window = series[..(trainLength + i)];
            var actual = series[trainLength + i];

            holtErrors.Add(Math.Abs(Holt(window, 1)[0] - actual));
            ridgeErrors.Add(Math.Abs(RidgeForecast(window, 1)[0] - actual));
        }

        var holtMae = holtErrors.Count > 0 ? holtErrors.Average() : 1;
        var ridgeMae = ridgeErrors.Count > 0 ? ridgeErrors.Average() : 1;

        var wHolt = 1.0 / Math.Max(holtMae, 1e-6);
        var wRidge = 1.0 / Math.Max(ridgeMae, 1e-6);
        var sum = wHolt + wRidge;
        wHolt /= sum;
        wRidge /= sum;

        var blendedMae = wHolt * holtMae + wRidge * ridgeMae;
        var lastPrice = series[^1];
        var mape = lastPrice > 0 ? blendedMae / lastPrice * 100 : 0;

        vm.BacktestMae = Math.Round(blendedMae, 3);
        vm.BacktestMape = Math.Round(mape, 2);

        vm.Confidence = Math.Round(Math.Clamp(1.0 - mape / 6.0, 0.05, 0.98), 3);

        var holt = Holt(series, horizonWeeks);
        var ridge = RidgeForecast(series, horizonWeeks);

        for (var h = 0; h < horizonWeeks; h++)
        {
            var value = wHolt * holt[h] + wRidge * ridge[h];

            var band = 1.96 * blendedMae * Math.Sqrt(h + 1);

            vm.Points.Add(new ForecastPointVm
            {
                Date = lastDate.AddDays(7 * (h + 1)),
                Price = Math.Round((decimal)value, 2),
                Lower = Math.Round((decimal)(value - band), 2),
                Upper = Math.Round((decimal)(value + band), 2)
            });
        }

        var first = vm.Points[0].Price;
        var diff = first - vm.CurrentPrice;
        vm.Trend = diff > 0.3m ? "up" : diff < -0.3m ? "down" : "flat";

        vm.AdviceKey = vm.Trend switch
        {
            "up" => "ai.advice.fillnow",
            "down" => "ai.advice.wait",
            _ => "ai.advice.hold"
        };

        return vm;
    }

    private static double[] Holt(double[] y, int horizon)
    {
        if (y.Length < 3) return Enumerable.Repeat(y[^1], horizon).ToArray();

        var bestAlpha = 0.5;
        var bestBeta = 0.1;
        var bestError = double.MaxValue;

        for (var a = 0.1; a <= 0.95; a += 0.05)
        {
            for (var b = 0.02; b <= 0.6; b += 0.04)
            {
                var err = HoltError(y, a, b);
                if (err < bestError)
                {
                    bestError = err;
                    bestAlpha = a;
                    bestBeta = b;
                }
            }
        }

        var (level, trend) = HoltFit(y, bestAlpha, bestBeta);

        const double damping = 0.82;
        var result = new double[horizon];
        var damped = 0.0;
        for (var h = 1; h <= horizon; h++)
        {
            damped += Math.Pow(damping, h);
            result[h - 1] = level + damped * trend;
        }
        return result;
    }

    private static (double Level, double Trend) HoltFit(double[] y, double alpha, double beta)
    {
        var level = y[0];
        var trend = y[1] - y[0];

        for (var t = 1; t < y.Length; t++)
        {
            var prevLevel = level;
            level = alpha * y[t] + (1 - alpha) * (level + trend);
            trend = beta * (level - prevLevel) + (1 - beta) * trend;
        }

        return (level, trend);
    }

    private static double HoltError(double[] y, double alpha, double beta)
    {
        var level = y[0];
        var trend = y[1] - y[0];
        var sum = 0.0;

        for (var t = 1; t < y.Length; t++)
        {
            var forecast = level + trend;
            sum += Math.Abs(y[t] - forecast);

            var prevLevel = level;
            level = alpha * y[t] + (1 - alpha) * (level + trend);
            trend = beta * (level - prevLevel) + (1 - beta) * trend;
        }

        return sum / Math.Max(1, y.Length - 1);
    }

    private static double[] RidgeForecast(double[] y, int horizon, double lambda = 0.35)
    {
        var n = y.Length;
        if (n <= Lags + 1) return Enumerable.Repeat(y[^1], horizon).ToArray();

        var rows = n - Lags;
        var cols = Lags + 2; // intercept + lags + time index

        var x = new double[rows][];
        var target = new double[rows];

        for (var i = 0; i < rows; i++)
        {
            var row = new double[cols];
            row[0] = 1.0;
            for (var l = 1; l <= Lags; l++) row[l] = y[Lags + i - l];
            row[cols - 1] = (Lags + i) / (double)n;   // normalised time index
            x[i] = row;
            target[i] = y[Lags + i];
        }

        var weights = SolveRidge(x, target, lambda, cols);
        if (weights is null) return Enumerable.Repeat(y[^1], horizon).ToArray();

        var window = y.ToList();
        var result = new double[horizon];

        for (var h = 0; h < horizon; h++)
        {
            var value = weights[0];
            for (var l = 1; l <= Lags; l++) value += weights[l] * window[^l];
            value += weights[cols - 1] * ((window.Count) / (double)n);

            result[h] = value;
            window.Add(value);
        }

        return result;
    }

    private static double[]? SolveRidge(double[][] x, double[] y, double lambda, int cols)
    {
        var a = new double[cols, cols + 1];

        for (var i = 0; i < cols; i++)
        {
            for (var j = 0; j < cols; j++)
            {
                var sum = 0.0;
                for (var r = 0; r < x.Length; r++) sum += x[r][i] * x[r][j];
                if (i == j && i > 0) sum += lambda;   // do not penalise the intercept
                a[i, j] = sum;
            }

            var rhs = 0.0;
            for (var r = 0; r < x.Length; r++) rhs += x[r][i] * y[r];
            a[i, cols] = rhs;
        }

        for (var col = 0; col < cols; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < cols; r++)
                if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col])) pivot = r;

            if (Math.Abs(a[pivot, col]) < 1e-12) return null;

            if (pivot != col)
            {
                for (var c = 0; c <= cols; c++)
                    (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
            }

            var div = a[col, col];
            for (var c = 0; c <= cols; c++) a[col, c] /= div;

            for (var r = 0; r < cols; r++)
            {
                if (r == col) continue;
                var factor = a[r, col];
                if (Math.Abs(factor) < 1e-15) continue;
                for (var c = 0; c <= cols; c++) a[r, c] -= factor * a[col, c];
            }
        }

        var w = new double[cols];
        for (var i = 0; i < cols; i++) w[i] = a[i, cols];
        return w;
    }

    public async Task<int> RegenerateAsync(int horizonWeeks = 4)
    {
        var fuels = await _db.FuelTypes.ToListAsync();
        var written = 0;

        foreach (var fuel in fuels)
        {
            var forecast = await ForecastAsync(fuel.Id, horizonWeeks);
            if (forecast.Points.Count == 0) continue;

            var stale = await _db.PricePredictions
                .Where(p => p.FuelTypeId == fuel.Id)
                .ToListAsync();
            _db.PricePredictions.RemoveRange(stale);

            foreach (var point in forecast.Points)
            {
                _db.PricePredictions.Add(new PricePrediction
                {
                    FuelTypeId = fuel.Id,
                    TargetDate = point.Date,
                    PredictedPrice = point.Price,
                    LowerBound = point.Lower,
                    UpperBound = point.Upper,
                    Model = forecast.Model,
                    Confidence = forecast.Confidence,
                    Trend = forecast.Trend,
                    GeneratedAtUtc = DateTime.UtcNow
                });
                written++;
            }
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Generated {Count} price predictions.", written);
        return written;
    }
}
