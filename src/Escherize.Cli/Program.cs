using System.Globalization;

namespace Escherize.Cli;

/// <summary>Console entry point.</summary>
internal static class Program
{
    /// <summary>Exit code for a successful run (SPEC §11).</summary>
    internal const int ExitSuccess = 0;

    /// <summary>Exit code for invalid input (SPEC §11).</summary>
    internal const int ExitInvalidInput = 1;

    /// <summary>Exit code for an internal error (SPEC §11).</summary>
    internal const int ExitInternalError = 2;

    /// <summary>
    /// Parses the command line and dispatches to a command.
    /// </summary>
    /// <param name="args">The raw command line arguments.</param>
    /// <returns>0 on success, 1 on invalid input, 2 on an internal error.</returns>
    private static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        return Run(args, Console.Out, Console.Error);
    }

    /// <summary>Runs a command against the given streams, so that tests can drive it.</summary>
    /// <param name="args">The raw command line arguments.</param>
    /// <param name="output">The standard output stream.</param>
    /// <param name="error">The standard error stream.</param>
    /// <returns>The exit code.</returns>
    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0)
        {
            WriteUsage(error);
            return ExitInvalidInput;
        }

        if (args[0] is "-h" or "--help" or "help")
        {
            WriteUsage(output);
            return ExitSuccess;
        }

        try
        {
            CommandLine command = CommandLine.Parse(args);
            switch (command.Command)
            {
                case "preprocess":
                    return PreprocessCommand.Run(command, output);

                case "run":
                    return RunCommand.Run(command, output);

                // The render command arrives with phase F5 (SPEC §10).
                case "render":
                    error.WriteLine($"escherize: the command '{command.Command}' is not implemented yet.");
                    return ExitInvalidInput;

                default:
                    error.WriteLine($"escherize: unknown command '{command.Command}'.");
                    WriteUsage(error);
                    return ExitInvalidInput;
            }
        }
        catch (Exception exception) when (exception is CommandLineException
            or InvalidDataException
            or ArgumentException
            or FileNotFoundException
            or DirectoryNotFoundException
            or System.Text.Json.JsonException)
        {
            error.WriteLine($"escherize: {exception.Message}");
            return ExitInvalidInput;
        }
        catch (IOException exception)
        {
            error.WriteLine($"escherize: {exception.Message}");
            return ExitInternalError;
        }
    }

    /// <summary>Writes the usage text (SPEC §11).</summary>
    /// <param name="writer">The stream to write to.</param>
    internal static void WriteUsage(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine("escherize - Escherization in the polygon representation (" + EscherizeInfo.Reference + ")");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  escherize preprocess --input <file> [--n 64] [--smooth 24|--dp 0.005] [--invert]");
        writer.WriteLine("                       [--threshold T] [--ring-index i] [--feature-name name] [--out dir]");
        writer.WriteLine("  escherize run        --input <file> | --config job.json");
        writer.WriteLine("                       [--n 64] [--types IH4,IH5,IH6|all] [--top 20] [--render-top 5]");
        writer.WriteLine("                       [--min-k 0] [--diversity 0.02] [--min-neck 0.0] [--threads N]");
        writer.WriteLine("                       [--landmarks lm.json] [--tile-size-mm 200] [--out dir]");
        writer.WriteLine("  escherize render     --result summary.json --rank 3 [--tiles 80] [--out dir]");
        writer.WriteLine();
        writer.WriteLine("Exit codes: 0 success, 1 invalid input, 2 internal error.");
    }
}
