namespace SirWordALot;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--version") || args.Contains("-v")) { Console.WriteLine("Sir Word-a-lot 0.1.0"); return; }
        if (args.Contains("--help") || args.Contains("-h")) { Console.WriteLine(Help); return; }
    }

    private const string Help = """
        Sir Word-a-lot - a vocabulary and thesaurus trainer

          -v, --version   Print version and exit
          -h, --help      Show this help and exit
        """;
}
