using System;

namespace Spectre.Console.Cli
{
    /// <summary>
    /// Represents an argument source which provides program arguments
    /// as an array of strings.
    /// </summary>
    public interface IArgumentSource
    {
        /// <summary>
        /// Gets a key that specifies that this source should be chosen for parsing.
        /// </summary>
        string ArgumentKey { get; }
        
        /// <summary>
        /// Provides program arguments.
        /// </summary>
        /// <param name="path">Path from which to load arguments.</param>
        /// <returns>An array of string arguments.</returns>
        /// <exception cref="FormatException">Thrown when configuration format is invalid.</exception>
        string[] ProvideArguments(string path);
    }
}