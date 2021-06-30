using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Spectre.Console.Cli.Yaml
{
    /// <summary>
    /// A class that has an ability to parse program arguments from Yaml files.
    /// </summary>
    public class YamlArgumentSource : IArgumentSource
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="YamlArgumentSource"/> class.
        /// </summary>
        /// <param name="argumentKey">Argument key.</param>
        public YamlArgumentSource(string argumentKey = "yaml")
        {
            ArgumentKey = argumentKey;
        }

        /// <inheritdoc />
        public string ArgumentKey { get; }

        /// <inheritdoc />
        public string[] ProvideArguments(string path)
        {
            var input = new StreamReader(path);
            var stream = new YamlStream();

            stream.Load(input);

            if (stream.Documents.Count != 1)
            {
                throw new FormatException("There should be exactly one Yaml document in a file");
            }
            
            if (stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                throw new FormatException("Root node should be a mapping");
            }

            var result = new List<string>();
            
            AppendMapping(root, result);

            return result.ToArray();
        }

        private void AppendMapping(YamlMappingNode mapping, List<string> args)
        {
            foreach (var child in mapping.Children)
            {
                if (child.Key is not YamlScalarNode key)
                {
                    throw new FormatException("Only scalar keys are supported for mappings");
                }
                
                if (string.IsNullOrEmpty(key.Value))
                {
                    throw new FormatException("Mapping element key can't be empty");
                }

                if (child.Value is YamlMappingNode mappingValue)
                {
                    args.Add(key.Value);
                    AppendMapping(mappingValue, args);
                }
                else if (child.Value is YamlScalarNode scalarValue)
                {
                    args.Add($"--{key.Value}");
                    if (!string.IsNullOrEmpty(scalarValue.Value))
                    {
                        args.Add(scalarValue.Value);
                    }
                }
                else if (child.Value is YamlSequenceNode sequenceValue)
                {
                    if (sequenceValue.Children.Any(x => x.NodeType != YamlNodeType.Scalar))
                    {
                        args.Add(key.Value);
                    }
                    
                    AppendSequence(sequenceValue, key.Value, args);
                }
            }
        }
        
        private void AppendSequence(YamlSequenceNode sequence, string key, List<string> args)
        {
            foreach (var child in sequence.Children)
            {
                if (child is YamlMappingNode mapping)
                {
                    AppendMapping(mapping, args);
                }
                else if (child is YamlScalarNode scalar)
                {
                    if (string.IsNullOrEmpty(scalar.Value))
                    {
                        continue;
                    }

                    args.Add($"--{key}");
                    args.Add(scalar.Value);
                }
                else if (child is YamlSequenceNode seq)
                {
                    AppendSequence(seq, key, args);
                }
            }
        }
    }
}