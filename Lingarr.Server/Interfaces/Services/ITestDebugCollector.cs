namespace Lingarr.Server.Interfaces.Services;

public interface ITestDebugCollector
{
    void RecordApiCall(TestApiCall call);
    void RecordLineResult(TestLineResult result);
    void RecordTiming(string step, double milliseconds);
    TestDebugData GetCollectedData();
    void Reset();
}

public class TestApiCall
{
    public int CallIndex { get; set; }
    public string? Endpoint { get; set; }
    public string? RequestMethod { get; set; }
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public int? StatusCode { get; set; }
    public double DurationMs { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public List<int> LinePositions { get; set; } = [];
    public string? Error { get; set; }
}

public class TestLineResult
{
    public int Position { get; set; }
    public required string Original { get; set; }
    public string? Translated { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public double DurationMs { get; set; }
    public int StartTimeMs { get; set; }
    public int EndTimeMs { get; set; }
    public string? ApiRequest { get; set; }
    public string? ApiResponse { get; set; }
}

public class TestDebugData
{
    public List<TestApiCall> ApiCalls { get; set; } = [];
    public List<TestLineResult> LineResults { get; set; } = [];
    public Dictionary<string, double> Timings { get; set; } = new();
}

public class TestDebugCollector : ITestDebugCollector
{
    private readonly List<TestApiCall> _apiCalls = [];
    private readonly List<TestLineResult> _lineResults = [];
    private readonly Dictionary<string, double> _timings = new();
    private int _callIndex = 0;

    public void RecordApiCall(TestApiCall call)
    {
        call.CallIndex = _callIndex++;
        _apiCalls.Add(call);
    }

    public void RecordLineResult(TestLineResult result)
    {
        _lineResults.Add(result);
    }

    public void RecordTiming(string step, double milliseconds)
    {
        _timings[step] = milliseconds;
    }

    public TestDebugData GetCollectedData()
    {
        return new TestDebugData
        {
            ApiCalls = _apiCalls.ToList(),
            LineResults = _lineResults.ToList(),
            Timings = _timings.ToDictionary(x => x.Key, x => x.Value)
        };
    }

    public void Reset()
    {
        _apiCalls.Clear();
        _lineResults.Clear();
        _timings.Clear();
        _callIndex = 0;
    }
}
