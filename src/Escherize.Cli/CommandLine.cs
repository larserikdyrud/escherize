using System.Globalization;

namespace Escherize.Cli;

/// <summary>Thrown when the command line cannot be interpreted; maps to exit code 1.</summary>
/// <param name="message">The message shown to the user.</param>
internal sealed class CommandLineException(string message) : Exception(message);

/// <summary>
/// A hand written parser for <c>--flag value</c> arguments (SPEC §2: no CLI package).
/// </summary>
internal sealed class CommandLine
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    private CommandLine(string command)
    {
        Command = command;
    }

    /// <summary>The command name, the first argument.</summary>
    public string Command { get; }

    /// <summary>Parses the arguments following the command name.</summary>
    /// <param name="args">The full argument list, including the command name.</param>
    /// <returns>The parsed command line.</returns>
    /// <exception cref="CommandLineException">An argument is malformed.</exception>
    public static CommandLine Parse(string[] args)
    {
        var result = new CommandLine(args[0]);

        for (int i = 1; i < args.Length; i++)
        {
            string argument = args[i];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                throw new CommandLineException($"Unexpected argument '{argument}'; expected a --flag.");
            }

            string name = argument[2..];
            string? value = null;

            int equals = name.IndexOf('=', StringComparison.Ordinal);
            if (equals >= 0)
            {
                value = name[(equals + 1)..];
                name = name[..equals];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            if (name.Length == 0)
            {
                throw new CommandLineException("An empty flag name is not valid.");
            }

            result._values[name] = value;
        }

        return result;
    }

    /// <summary>Returns a string flag value.</summary>
    /// <param name="name">The flag name without the leading dashes.</param>
    /// <returns>The value, or null when the flag is absent.</returns>
    /// <exception cref="CommandLineException">The flag is present without a value.</exception>
    public string? GetString(string name)
    {
        _used.Add(name);
        if (!_values.TryGetValue(name, out string? value))
        {
            return null;
        }

        return value ?? throw new CommandLineException($"The flag --{name} needs a value.");
    }

    /// <summary>Returns a required string flag value.</summary>
    /// <param name="name">The flag name.</param>
    /// <returns>The value.</returns>
    /// <exception cref="CommandLineException">The flag is absent or has no value.</exception>
    public string GetRequiredString(string name) =>
        GetString(name) ?? throw new CommandLineException($"The flag --{name} is required.");

    /// <summary>Returns an integer flag value.</summary>
    /// <param name="name">The flag name.</param>
    /// <param name="fallback">The value used when the flag is absent.</param>
    /// <returns>The value.</returns>
    /// <exception cref="CommandLineException">The value is not an integer.</exception>
    public int GetInt32(string name, int fallback)
    {
        string? text = GetString(name);
        if (text is null)
        {
            return fallback;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new CommandLineException($"The flag --{name} needs a whole number, not '{text}'.");
    }

    /// <summary>Returns a floating point flag value.</summary>
    /// <param name="name">The flag name.</param>
    /// <returns>The value, or null when the flag is absent.</returns>
    /// <exception cref="CommandLineException">The value is not a number.</exception>
    public double? GetDouble(string name)
    {
        string? text = GetString(name);
        if (text is null)
        {
            return null;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new CommandLineException($"The flag --{name} needs a number, not '{text}'.");
    }

    /// <summary>Returns whether a switch is present.</summary>
    /// <param name="name">The flag name.</param>
    /// <returns>True when the switch is set.</returns>
    public bool GetSwitch(string name)
    {
        _used.Add(name);
        if (!_values.TryGetValue(name, out string? value))
        {
            return false;
        }

        return value is null
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value == "1";
    }

    /// <summary>
    /// Fails when a flag was given that the command never read, so that a typo does not
    /// pass silently.
    /// </summary>
    /// <exception cref="CommandLineException">An unknown flag was given.</exception>
    public void EnsureNoUnknownFlags()
    {
        foreach (string name in _values.Keys.Order(StringComparer.Ordinal))
        {
            if (!_used.Contains(name))
            {
                throw new CommandLineException($"Unknown flag --{name} for command '{Command}'.");
            }
        }
    }
}
