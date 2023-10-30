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

public class ItemFilterData
{
    public string Query { get; set; }
    public string RawQuery { get; set; }
    public Func<ItemData, bool> CompiledQuery { get; set; }
    public int InitialLine { get; set; }
    public bool FailedToCompile { get; set; } = false;
}

public class ItemFilter
{
    private readonly List<ItemFilterData> _queries;

    private static readonly ParsingConfig ParsingConfig = new ParsingConfig()
    {
        AllowNewToEvaluateAnyType = true,
        ResolveTypesBySimpleName = true,
        CustomTypeProvider = new CustomDynamicLinqCustomTypeProvider(),
    };

    private ItemFilter(List<ItemFilterData> queries)
    {
        _queries = queries;
    }

    public static ItemFilter LoadFromPath(string filterFilePath)
    {
        return new ItemFilter(GetQueries(filterFilePath, File.ReadAllLines(filterFilePath)));
    }

    public static ItemFilter LoadFromList(string filterName, List<string> list)
    {
        return new ItemFilter(GetQueries(filterName, list));
    }

    public static ItemFilter LoadFromString(string @string)
    {
        return new ItemFilter(GetQueries("memory", @string.ReplaceLineEndings("\n").Split("\n")));
    }

    public static ItemFilterData LoadFromStringWithLine(string @string, int line)
    {
        return GetQueries(@string.ReplaceLineEndings("\n"), line);
    }

    public bool Matches(ItemData item)
    {
        foreach (var cachedQuery in _queries)
        {
            try
            {
                if (!cachedQuery.FailedToCompile && cachedQuery.CompiledQuery(item))
                {
                    DebugWindow.LogMsg($"[ItemQueryProcessor] Matches an Item\nLine # {cachedQuery.InitialLine}\nItem({item.BaseName})\n{cachedQuery.Query.Replace("\n", "")}", 10, Color.LawnGreen);
                    return true; // Stop further checks once a match is found
                }
            }
            catch (Exception ex)
            {
                // huge issue when the amount of catching starts creeping up
                // 4500 lines that procude an error on one item take 50ms per Tick() vs handling the error taking 0.2ms
                DebugWindow.LogError($"Evaluation Error! Line # {cachedQuery.InitialLine} Entry: '{cachedQuery.Query}' Item {item.BaseName}\n{ex}");
                continue;
            }
        }

        return false;
    }

    public static bool Matches(ItemData item, Func<ItemData, bool> query)
    {
        try
        {
            if (query(item))
            {
                DebugWindow.LogMsg($"[ItemQueryProcessor] Matches custom query", 10, Color.LawnGreen);
                return true;
            }
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[ItemQueryProcessor] Evaluation Error on custom Query. Item {item.BaseName}\n{ex}");
            return false;
        }

        return false;
    }

    private static List<ItemFilterData> GetQueries(string filterFilePath, string[] rawLines)
    {
        var compiledQueries = new List<ItemFilterData>();
        var lines = SplitQueries(rawLines);

        foreach (var (query, rawQuery, initialLine) in lines)
        {
            try
            {
                var lambda = ParseItemDataLambda(query);
                var compiledLambda = lambda.Compile();
                compiledQueries.Add(new ItemFilterData
                {
                    Query = query,
                    RawQuery = rawQuery,
                    CompiledQuery = compiledLambda,
                    InitialLine = initialLine
                });
            }
            catch (Exception ex)
            {
                var exMessage = ex is ParseException parseEx
                    ? $"{parseEx.Message} (at index {parseEx.Position})"
                    : ex.ToString();
                DebugWindow.LogError($"[ItemQueryProcessor] Error processing query ({query}) from List Item # {initialLine}: {exMessage}", 15);

                compiledQueries.Add(new ItemFilterData
                {
                    Query = query,
                    RawQuery = rawQuery,
                    CompiledQuery = null,
                    InitialLine = initialLine,
                    FailedToCompile = true // to use with stashie to output the same number of inputs and match up the syntax style correctly
                });
            }
        }

        DebugWindow.LogMsg($@"[ItemQueryProcessor] Processed {filterFilePath.Split("\\").LastOrDefault()} with {compiledQueries.Count} queries", 2, Color.Orange);
        return compiledQueries;
    }

    private static List<ItemFilterData> GetQueries(string filterFilePath, List<string> queryList)
    {
        var compiledQueries = new List<ItemFilterData>();

        for (int i = 0; i < queryList.Count; i++)
        {
            string query = queryList[i];
            try
            {
                var lambda = ParseItemDataLambda(query);
                var compiledLambda = lambda.Compile();
                compiledQueries.Add(new ItemFilterData
                {
                    Query = query,
                    RawQuery = query,
                    CompiledQuery = compiledLambda,
                    InitialLine = i + 1
                });
            }
            catch (Exception ex)
            {
                var exMessage = ex is ParseException parseEx
                    ? $"{parseEx.Message} (at index {parseEx.Position})"
                    : ex.ToString();

                compiledQueries.Add(new ItemFilterData
                {
                    Query = query,
                    RawQuery = query,
                    CompiledQuery = null,
                    InitialLine = i + 1,
                    FailedToCompile = true // to use with stashie to output the same number of inputs and match up the syntax style correctly
                });
                DebugWindow.LogError($"[ItemQueryProcessor] Error processing query ({query.Replace("\n", "")}) on Line # {i + 1}: {exMessage}", 15);
            }
        }

        DebugWindow.LogMsg($@"[ItemQueryProcessor] Processed {filterFilePath.Split("\\").LastOrDefault()} with {compiledQueries.Count} queries", 2, Color.Orange);
        return compiledQueries;
    }

    private static ItemFilterData GetQueries(string query, int line)
    {
        try
        {
            var lambda = ParseItemDataLambda(query);
            var compiledLambda = lambda.Compile();

            DebugWindow.LogMsg($@"[ItemQueryProcessor] Processed {query} from Line # {line}.", 2, Color.Orange);

            return new ItemFilterData
            {
                Query = query,
                RawQuery = query,
                CompiledQuery = compiledLambda,
                InitialLine = line
            };
        }
        catch (Exception ex)
        {
            var exMessage = ex is ParseException parseEx
                ? $"{parseEx.Message} (at index {parseEx.Position})"
                : ex.ToString();

            DebugWindow.LogError($"[ItemQueryProcessor] Error processing query ({query.Replace("\n", "")}) on Line # {line}: {exMessage}", 15);

            return new ItemFilterData
            {
                Query = query,
                RawQuery = query,
                CompiledQuery = null,
                InitialLine = line,
                FailedToCompile = true // to use with stashie to output the same number of inputs and match up the syntax style correctly
            };
        }
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

    private static Expression<Func<ItemData, bool>> ParseItemDataLambda(string expression)
    {
        return DynamicExpressionParser.ParseLambda<ItemData, bool>(ParsingConfig, false, expression);
    }
}