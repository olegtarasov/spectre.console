using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.XPath;

namespace Spectre.Console.Cli
{
    internal sealed class CommandExecutor
    {
        private readonly ITypeRegistrar _registrar;

        public CommandExecutor(ITypeRegistrar registrar)
        {
            _registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
            _registrar.Register(typeof(DefaultPairDeconstructor), typeof(DefaultPairDeconstructor));
        }

        public async Task<int> Execute(IConfiguration configuration, IEnumerable<string> args)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            _registrar.RegisterInstance(typeof(IConfiguration), configuration);
            _registrar.RegisterLazy(typeof(IAnsiConsole), () => configuration.Settings.Console.GetConsole());

            // Create the command model.
            var model = CommandModelBuilder.Build(configuration);
            _registrar.RegisterInstance(typeof(CommandModel), model);
            _registrar.RegisterDependencies(model);

            // No default command?
            if (model.DefaultCommand == null)
            {
                // Got at least one argument?
                var firstArgument = args.FirstOrDefault();
                if (firstArgument != null)
                {
                    // Asking for version? Kind of a hack, but it's alright.
                    // We should probably make this a bit better in the future.
                    if (firstArgument.Equals("--version", StringComparison.OrdinalIgnoreCase) ||
                        firstArgument.Equals("-v", StringComparison.OrdinalIgnoreCase))
                    {
                        var console = configuration.Settings.Console.GetConsole();
                        console.WriteLine(ResolveApplicationVersion(configuration));
                        return 0;
                    }
                }
            }

            // Parse and map the model against the arguments.
            var parser = new CommandTreeParser(model, configuration.Settings);
            var parsedResult = parser.Parse(args);

            // Get additional arguments from custom sources.
            var (additionalArgs, processedKeys) = GetAdditionalArgs(configuration.Settings.ArgumentSources, parsedResult.Remaining.Parsed);
            if (processedKeys.Length > 0)
            {
                var remaining = parsedResult.Remaining.Parsed
                    .Where(x => !processedKeys.Contains(x.Key))
                    .SelectMany(group => group, (group, value) => new { group.Key, value })
                    .ToLookup(pair => pair.Key, pair => pair.value);
                parsedResult = new CommandTreeParserResult(parsedResult.Tree, new RemainingArguments(remaining, parsedResult.Remaining.Raw));
            }

            if (additionalArgs.Length > 0)
            {
                var additionalParsed = additionalArgs.Select(x => parser.Parse(x)).ToArray();
                var finalResult = additionalParsed[0].Tree;

                for (int i = 1; i < additionalParsed.Length; i++)
                {
                    finalResult = MergeTrees(finalResult, additionalParsed[i].Tree, null);
                }

                parsedResult = new CommandTreeParserResult(MergeTrees(finalResult, parsedResult.Tree, null), parsedResult.Remaining);
            }

            _registrar.RegisterInstance(typeof(CommandTreeParserResult), parsedResult);

            // Currently the root?
            if (parsedResult.Tree == null)
            {
                // Display help.
                configuration.Settings.Console.SafeRender(HelpWriter.Write(model));
                return 0;
            }

            // Get the command to execute.
            var leaf = parsedResult.Tree.GetLeafCommand();
            if (leaf.Command.IsBranch || leaf.ShowHelp)
            {
                // Branches can't be executed. Show help.
                configuration.Settings.Console.SafeRender(HelpWriter.WriteCommand(model, leaf.Command));
                return leaf.ShowHelp ? 0 : 1;
            }

            // Register the arguments with the container.
            _registrar.RegisterInstance(typeof(IRemainingArguments), parsedResult.Remaining);

            // Create the resolver and the context.
            using (var resolver = new TypeResolverAdapter(_registrar.Build()))
            {
                var context = new CommandContext(parsedResult.Remaining, leaf.Command.Name, leaf.Command.Data);

                // Execute the command tree.
                return await Execute(leaf, parsedResult.Tree, context, resolver, configuration);
            }
        }

        private CommandTree? MergeTrees(CommandTree? left, CommandTree? right, CommandTree? parent)
        {
            if (left == null)
            {
                return right;
            }

            if (right == null)
            {
                return null;
            }

            if (left.Command.Name != right.Command.Name)
            {
                return right;
            }

            var result = new CommandTree(parent, left.Command);
            result.Mapped.AddRange(left.Mapped);
            result.Unmapped.AddRange(left.Unmapped);
            foreach (var group in right.Mapped.GroupBy(x => x.Parameter.Id))
            {
                result.Mapped.RemoveAll(x => x.Parameter.Id == group.Key);
                result.Mapped.AddRange(group);

                result.Unmapped.RemoveAll(x => x.Id == group.Key);
            }

            if (right.Next != null)
            {
                result.Next = MergeTrees(left.Next, right.Next, result);
            }

            return result;
        }
        
        private (string[][] Args, string[] ProcessedKeys) GetAdditionalArgs(List<IArgumentSource> sources, ILookup<string, string?> remainingArgs)
        {
            var result = new List<string[]>();
            var processedKeys = new List<string>();
            foreach (var grouping in remainingArgs)
            {
                var filteredSources = sources.Where(x => string.Equals(grouping.Key, x.ArgumentKey, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (filteredSources.Length > 0)
                {
                    processedKeys.Add(grouping.Key);
                }

                foreach (var source in filteredSources)
                {
                    foreach (var path in grouping)
                    {
                        if (string.IsNullOrEmpty(path))
                        {
                            continue;
                        }

                        result.Add(source.ProvideArguments(path));
                    }
                }
            }

            return (result.ToArray(), processedKeys.ToArray());
        }

        private static string ResolveApplicationVersion(IConfiguration configuration)
        {
            return
                configuration.Settings.ApplicationVersion ?? // potential override
                VersionHelper.GetVersion(Assembly.GetEntryAssembly());
        }

        private static Task<int> Execute(
            CommandTree leaf,
            CommandTree tree,
            CommandContext context,
            ITypeResolver resolver,
            IConfiguration configuration)
        {
            // Bind the command tree against the settings.
            var settings = CommandBinder.Bind(tree, leaf.Command.SettingsType, resolver);
            configuration.Settings.Interceptor?.Intercept(context, settings);

            // Create and validate the command.
            var command = leaf.CreateCommand(resolver);
            var validationResult = command.Validate(context, settings);
            if (!validationResult.Successful)
            {
                throw CommandRuntimeException.ValidationFailed(validationResult);
            }

            // Execute the command.
            return command.Execute(context, settings);
        }
    }
}