using System.CommandLine;

var debugOption = new Option<string?>("--debug")
{
    Description = "Debug mode: enumerate | score | config"
};

var rootCommand = new RootCommand("focus — directional window focus navigator");
rootCommand.Options.Add(debugOption);

rootCommand.SetAction(parseResult =>
{
    var debugValue = parseResult.GetValue(debugOption);
    if (string.IsNullOrEmpty(debugValue))
    {
        Console.WriteLine("No command specified. Use --help for usage.");
        return 1;
    }
    if (debugValue == "enumerate")
    {
        Console.WriteLine("enumerate not yet implemented");
        return 0;
    }
    Console.Error.WriteLine($"Unknown --debug value: {debugValue}");
    return 2;
});

return rootCommand.Parse(args).Invoke();
