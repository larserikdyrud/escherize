using System.Diagnostics;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Escherize.Parametrization;
using Escherize.Search;
using Escherize.Templates;

namespace Escherize.Bench;

/// <summary>Performance measurement for the targets of SPEC §12.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        if (args.Length > 0 && args[0] == "--shape")
        {
            foreach (string name in TemplateLibrary.Names)
            {
                TemplateSpec t = TemplateLibrary.ByName(name);
                TemplatePlan plan = TemplatePlan.For(t);
                double smallest = double.MaxValue;
                double largest = 0;
                int denormals = 0;
                foreach (PlanRun run in plan.Runs)
                {
                    foreach (double value in run.Block)
                    {
                        double magnitude = Math.Abs(value);
                        if (magnitude == 0)
                        {
                            continue;
                        }

                        smallest = Math.Min(smallest, magnitude);
                        largest = Math.Max(largest, magnitude);
                        if (magnitude < 2.2250738585072014e-308)
                        {
                            denormals++;
                        }
                    }
                }

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{name,-5} md={plan.Md} runs={plan.Runs.Length} smallest={smallest:e2} largest={largest:e2} denormals={denormals}"));
            }

            return 0;
        }

        if (args.Length > 0 && args[0] == "--quick")
        {
            int pointCount = args.Length > 1
                ? int.Parse(args[1], CultureInfo.InvariantCulture)
                : 120;
            Quick(pointCount);
            return 0;
        }

        BenchmarkRunner.Run<EvaluationBenchmark>();
        return 0;
    }

    /// <summary>
    /// A plain timing loop, for when a full BenchmarkDotNet run would take longer than the
    /// measurement is worth.
    /// </summary>
    /// <param name="pointCount">The number of goal points.</param>
    private static void Quick(int pointCount)
    {
        foreach (string name in TemplateLibrary.Names)
        {
            (FastEvaluator evaluator, int n) = Build(name, pointCount);

            // Warm up properly: the first few thousand calls run the tier zero code, which
            // is several times slower than what the search actually executes.
            double sink = 0;
            for (int warmup = 0; warmup < 200; warmup++)
            {
                for (int j = 0; j < n; j++)
                {
                    sink += evaluator.Evaluate(j);
                }
            }

            const int Repeats = 300;
            var stopwatch = Stopwatch.StartNew();
            for (int repeat = 0; repeat < Repeats; repeat++)
            {
                for (int j = 0; j < n; j++)
                {
                    sink += evaluator.Evaluate(j);
                }
            }

            stopwatch.Stop();
            double nanoseconds = stopwatch.Elapsed.TotalNanoseconds / (Repeats * (double)n);
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name,-5} n={n}  {nanoseconds,8:0.0} ns per evaluation   (sink {sink:0.###})"));
        }
    }

    /// <summary>Builds an evaluator for one template at a given n, with a mid sized k vector.</summary>
    /// <param name="templateName">The template.</param>
    /// <param name="pointCount">The number of goal points.</param>
    /// <returns>The evaluator and the point count.</returns>
    internal static (FastEvaluator Evaluator, int PointCount) Build(string templateName, int pointCount)
    {
        TemplateSpec template = TemplateLibrary.ByName(templateName);

        // A balanced k vector, built directly. Enumerating them all would leave millions
        // of arrays alive and let the collector run in the middle of the measurement.
        int[] multiplicities = template.KMultiplicities();
        var k = new int[multiplicities.Length];
        int remaining = pointCount - template.VertexCount;
        while (remaining > 0)
        {
            bool progressed = false;
            for (int v = 0; v < k.Length && remaining > 0; v++)
            {
                if (multiplicities[v] <= remaining)
                {
                    k[v]++;
                    remaining -= multiplicities[v];
                    progressed = true;
                }
            }

            if (!progressed)
            {
                break;
            }
        }

        double[] w = TemplateValidator.NoisyCircle(pointCount, 1);
        var tables = new GoalTables(w);
        BasisPlan plan = BasisPlan.Create(TileLayout.Create(template, k));
        return (new FastEvaluator(plan, tables), pointCount);
    }
}

/// <summary>The microbenchmark of SPEC §12: one evaluation of IH4 at n = 120.</summary>
[MemoryDiagnoser]
public class EvaluationBenchmark
{
    private FastEvaluator _evaluator = null!;
    private int _offset;

    /// <summary>The template being measured.</summary>
    [Params("IH4", "IH1", "IH6")]
    public string Template { get; set; } = "IH4";

    /// <summary>The number of goal points.</summary>
    [Params(120)]
    public int PointCount { get; set; } = 120;

    /// <summary>Builds the evaluator once.</summary>
    [GlobalSetup]
    public void Setup() => (_evaluator, _) = Program.Build(Template, PointCount);

    /// <summary>One evaluation (SPEC §6.3).</summary>
    /// <returns>The distance, so the call is not optimised away.</returns>
    [Benchmark]
    public double Evaluate()
    {
        _offset = (_offset + 1) % PointCount;
        return _evaluator.Evaluate(_offset);
    }
}
