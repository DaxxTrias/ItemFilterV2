using ExileCore;
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Dynamic.Core.Exceptions;
using System.Linq.Expressions;

namespace ItemFilterLibrary;

public class ItemQuery
{
    private static readonly ParsingConfig ParsingConfig = new ParsingConfig()
    {
        AllowNewToEvaluateAnyType = true,
        ResolveTypesBySimpleName = true,
        CustomTypeProvider = new CustomDynamicLinqCustomTypeProvider(),
    };

    public string Query { get; set; }
    public string RawQuery { get; set; }
    public Func<ItemData, bool> CompiledQuery { get; set; }
    public int InitialLine { get; set; }
    public bool FailedToCompile { get; set; } = false;

    public bool Matches(ItemData item)
    {
        return Matches(item, false);
    }

    public bool Matches(ItemData item, bool enableDebug)
    {
        if (FailedToCompile)
        {
            return false;
        }

        try
        {
            if (CompiledQuery(item))
            {
                if (enableDebug)
                    DebugWindow.LogMsg($"[ItemQueryProcessor] {RawQuery} matched item {item.BaseName}", 10);
                return true;
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ItemQueryProcessor] Evaluation error for query {RawQuery}. Item {item.BaseName}\n{ex}");
            return false;
        }

        return false;
    }


    public static ItemQuery Load(string query)
    {
        return Load(query, query, 0);
    }

    public static ItemQuery Load(string query, string rawQuery, int line)
    {
        try
        {
            var lambda = ParseItemDataLambda(query);
            var compiledLambda = lambda.Compile();

            return new ItemQuery
            {
                Query = query,
                RawQuery = rawQuery,
                CompiledQuery = compiledLambda,
                InitialLine = line
            };
        }
        catch (Exception ex)
        {
            var exMessage = ex is ParseException parseEx
                ? $"{parseEx.Message} (at index {parseEx.Position})"
                : ex.ToString();

            DebugWindow.LogError($"[ItemQueryProcessor] Error processing query ({rawQuery}) on Line # {line}: {exMessage}", 15);

            return new ItemQuery
            {
                Query = query,
                RawQuery = rawQuery,
                CompiledQuery = null,
                InitialLine = line,
                FailedToCompile = true // to use with stashie to output the same number of inputs and match up the syntax style correctly
            };
        }
    }

    private static Expression<Func<ItemData, bool>> ParseItemDataLambda(string expression)
    {
        return DynamicExpressionParser.ParseLambda<ItemData, bool>(ParsingConfig, false, expression);
    }
}

public class ItemFilter
{
    private readonly List<ItemQuery> _queries;

    public ItemFilter(List<ItemQuery> queries)
    {
        _queries = queries;
    }

    public static ItemFilter LoadFromPath(string filterFilePath)
    {
        return new ItemFilter(GetQueries(filterFilePath, File.ReadAllLines(filterFilePath)));
    }

    public static ItemFilter LoadFromList(string filterName, IEnumerable<string> list)
    {
        var compiledQueries = list.Select((query, i) => ItemQuery.Load(query, query, i + 1)).ToList();

        DebugWindow.LogMsg($@"[ItemQueryProcessor] Processed {filterName.Split("\\").LastOrDefault()} with {compiledQueries.Count} queries", 2);
        return new ItemFilter(compiledQueries);
    }

    public static ItemFilter LoadFromString(string @string)
    {
        return new ItemFilter(GetQueries("memory", @string.ReplaceLineEndings("\n").Split("\n")));
    }

    public bool Matches(ItemData item)
    {
        return Matches(item, false);
    }

    public bool Matches(ItemData item, bool enableDebug)
    {
        foreach (var cachedQuery in _queries)
        {
            try
            {
                if (!cachedQuery.FailedToCompile && cachedQuery.CompiledQuery(item))
                {
                    if (enableDebug)
                        DebugWindow.LogMsg($"[ItemQueryProcessor] Matches an Item\nLine # {cachedQuery.InitialLine}\nItem({item.BaseName})\n{cachedQuery.RawQuery}", 10);

                    return true;
                }
            }
            catch (Exception ex)
            {
                // huge issue when the amount of catching starts creeping up
                // 4500 lines that produce an error on one item take 50ms per Tick() vs handling the error taking 0.2ms
                DebugWindow.LogError($"Evaluation Error! Line # {cachedQuery.InitialLine} Entry: '{cachedQuery.RawQuery}' Item {item.BaseName}\n{ex}");
            }
        }

        return false;
    }

    private static List<ItemQuery> GetQueries(string filterFilePath, string[] rawLines)
    {
        var compiledQueries = new List<ItemQuery>();
        var lines = SplitQueries(rawLines);

        foreach (var (query, rawQuery, initialLine) in lines)
        {
            compiledQueries.Add(ItemQuery.Load(query, rawQuery, initialLine));
        }

        DebugWindow.LogMsg($@"[ItemQueryProcessor] Processed {filterFilePath.Split("\\").LastOrDefault()} with {compiledQueries.Count} queries", 2);
        return compiledQueries;
    }

    private static List<(string section, string rawSection, int sectionStartLine)> SplitQueries(string[] rawLines)
    {
        string section = null;
        string rawSection = null;
        var sectionStartLine = 0;
        var lines = new List<(string section, string rawSection, int sectionStartLine)>();

        foreach (var (line, index) in rawLines.Append("").Select((value, i) => (value, i)))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                var lineWithoutComment = line.IndexOf("//", StringComparison.Ordinal) is var commentIndex and not -1
                    ? line[..commentIndex]
                    : line;
                if (section == null)
                {
                    sectionStartLine = index + 1; // Set at the start of each section
                }

                section += $"{lineWithoutComment}\n";
                rawSection += $"{line}\n";
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(section))
                {
                    lines.Add((section, rawSection.TrimEnd('\n'), sectionStartLine));
                }

                section = null;
                rawSection = null;
            }
        }

        return lines;
    }
}