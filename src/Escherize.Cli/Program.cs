using System.Globalization;

namespace Escherize.Cli;

/// <summary>Console entry point.</summary>
internal static class Program
{
    /// <summary>Exit code for a successful run (SPEC §11).</summary>
    private const int ExitSuccess = 0;

    /// <summary>Exit code for invalid input (SPEC §11).</summary>
    private const int ExitInvalidInput = 1;

    /// <summary>
    /// Parses the command line and dispatches to a command.
    /// </summary>
    /// <param name="args">The raw command line arguments.</param>
    /// <returns>0 on success, 1 on invalid input, 2 on an internal error.</returns>
    private static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            WriteUsage(Console.Out);
            return args.Length == 0 ? ExitInvalidInput : ExitSuccess;
        }

        // Commands are implemented from phase F1 onwards (SPEC §10, §11).
        Console.Error.WriteLine($"escherize: unknown command '{args[0]}'.");
        WriteUsage(Console.Error);
        return ExitInvalidInput;
    }

    /// <summary>Writes the usage text (SPEC §11).</summary>
    /// <param name="writer">The stream to write to.</param>
    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("escherize - Escherization in the polygon representation (" + EscherizeInfo.Reference + ")");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  escherize preprocess --input <file> [--n 64] [--smooth 24|--dp 0.005] [--invert]");
        writer.WriteLine("                       [--threshold T] [--ring-index i] [--out dir]");
        writer.WriteLine("  escherize run        --input <file> | --config job.json");
        writer.WriteLine("                       [--n 64] [--types IH4,IH5,IH6|all] [--top 20] [--render-top 5]");
        writer.WriteLine("                       [--min-k 0] [--diversity 0.02] [--min-neck 0.0] [--threads N]");
        writer.WriteLine("                       [--landmarks lm.json] [--tile-size-mm 200] [--out dir]");
        writer.WriteLine("  escherize render     --result summary.json --rank 3 [--tiles 80] [--out dir]");
    }
}
