using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Does the engine reach Level 1 as `diagram-complexity-levels.md` defines it?
///
/// Level 1 is E1 + D2: flat containers, single-line labels, one relationship style — but real layout difficulty,
/// meaning multi-band alignment, cross-band routing that avoids obstacles, and many-to-many connectors that do
/// not turn into a knot.
///
/// The calibration document claimed the engine "has not reached any level, closest to Level 1, short in D". That
/// was asserted, never tested. This measures it with a four-band diagram — 13 nodes, 15 edges, four relationship
/// kinds — which is the shape Goal §4.3's Level 1 describes.
///
/// Result: 94/100, one `edge.crossing` code. D2's first two requirements hold; the third (crossing control) is
/// where it falls short, which matches the calibration's verdict for the right reason rather than by luck.
/// </summary>
public sealed class LevelOneCapabilityTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private const string FourBands = """
    {"irVersion":"1.0","id":"level1","kind":"architecture",
     "containers":[
      {"id":"user","label":"用户接入层","order":4},
      {"id":"gate","label":"网关与安全层","order":3},
      {"id":"svc","label":"业务服务层","order":2},
      {"id":"data","label":"数据存储层","order":1}],
     "nodes":[
      {"id":"web","label":"Web 门户","role":"client","containerId":"user"},
      {"id":"app","label":"移动 App","role":"client","containerId":"user"},
      {"id":"open","label":"开放 API","role":"client","containerId":"user"},
      {"id":"gw","label":"API 网关","role":"gateway","containerId":"gate"},
      {"id":"auth","label":"认证服务","role":"service","containerId":"gate"},
      {"id":"lim","label":"限流熔断","role":"service","containerId":"gate"},
      {"id":"order","label":"订单服务","role":"service","containerId":"svc"},
      {"id":"pay","label":"支付服务","role":"service","containerId":"svc"},
      {"id":"stock","label":"库存服务","role":"service","containerId":"svc"},
      {"id":"usr","label":"用户服务","role":"service","containerId":"svc"},
      {"id":"pg","label":"PostgreSQL","role":"database","containerId":"data"},
      {"id":"redis","label":"Redis","role":"database","containerId":"data"},
      {"id":"es","label":"Elasticsearch","role":"database","containerId":"data"}],
     "edges":[
      {"id":"e1","from":"web","to":"gw","label":"HTTPS"},
      {"id":"e2","from":"app","to":"gw","label":"HTTPS"},
      {"id":"e3","from":"open","to":"gw","label":"OAuth"},
      {"id":"e4","from":"gw","to":"auth","label":"校验"},
      {"id":"e5","from":"gw","to":"lim","label":"限流"},
      {"id":"e6","from":"lim","to":"order","label":"REST"},
      {"id":"e7","from":"lim","to":"pay","label":"REST"},
      {"id":"e8","from":"lim","to":"stock","label":"REST"},
      {"id":"e9","from":"order","to":"pay","label":"扣款"},
      {"id":"e10","from":"order","to":"stock","label":"锁定"},
      {"id":"e11","from":"order","to":"usr","label":"查询"},
      {"id":"e12","from":"order","to":"pg","label":"SQL"},
      {"id":"e13","from":"pay","to":"pg","label":"SQL"},
      {"id":"e14","from":"stock","to":"redis","label":"缓存"},
      {"id":"e15","from":"usr","to":"es","label":"检索"}]}
    """;

    private static ExecutionResult Run(out string file)
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(FourBands, Json)!;
        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(
            """{"specVersion":"1.0","layout":"layered","direction":"topToBottom","theme":{"palette":"enterprise"}}""",
            Json)!;

        file = Path.Combine(Path.GetTempPath(), $"vsdx-l1-{Guid.NewGuid():N}.vsdx");

        return new ProtocolRunner().Execute(ir, spec, file);
    }

    [Fact]
    public void AFourBandDiagramIsDeliverable()
    {
        var result = Run(out var file);

        try
        {
            Assert.True(result.Quality.HardConstraintsPassed,
                "hard failures: " + string.Join(", ", result.Quality.IssueCodes));

            Assert.True(result.Quality.Score >= 90, $"scored {result.Quality.Score}");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void BandsAlignAndAreOrdered()
    {
        // D2's first requirement. Four bands, each a full-width strip, top order on top.
        var result = Run(out var file);

        try
        {
            var page = new VsdxEngine().Read(file).Diagram.Pages[0];
            var bands = page.Groups.Where(g => g.Bounds is not null)
                .OrderByDescending(g => g.Bounds!.Y).ToList();

            Assert.Equal(4, bands.Count);

            for (int i = 0; i < bands.Count - 1; i++)
                Assert.True(bands[i].Bounds!.Y > bands[i + 1].Bounds!.Y,
                    $"{bands[i].Id} should sit above {bands[i + 1].Id}");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void NoConnectorCutsThroughAnUnrelatedNode()
    {
        // D2's second requirement, and the one that makes a diagram look broken rather than merely busy.
        var result = Run(out var file);

        try
        {
            Assert.DoesNotContain("edge.through-node", result.Quality.IssueCodes);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void CrossingsAreWhereItFallsShort()
    {
        // D2's third requirement. Recording the gap as a measurement rather than an opinion: this fixture does
        // produce crossings, and that is why the calibration puts the engine short of Level 1 in D rather than E.
        var result = Run(out var file);

        try
        {
            Assert.Contains("edge.crossing", result.Quality.IssueCodes);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }
}
