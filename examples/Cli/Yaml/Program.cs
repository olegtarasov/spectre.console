using System;
using System.Linq;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Yaml;

namespace Yaml
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var app = new CommandApp();
            app.Configure(config =>
            {
                config.AddBranch<AddSettings>("add", add =>
                {
                    add.AddCommand<AddPackageCommand>("package");
                    add.AddCommand<AddReferenceCommand>("reference");
                });
                config.AddArgumentSource(new YamlArgumentSource());
            });

            return app.Run(args);
        }
    }
}