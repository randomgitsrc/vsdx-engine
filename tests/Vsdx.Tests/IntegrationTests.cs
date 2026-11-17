using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// IntegrationTests: the complete round-1/round-2 acceptance scenarios end to end,
/// plus layout quality, since "the file was produced" is not acceptance.
/// </summary>
public sealed class IntegrationTests
{
    [Fact]
    public void Scenario_Round1_FifteenNodeArchitecture()
    {
        var dir = TestData.NewTempDir("int-r1");
        var vsdx = Path.Combine(dir, "architecture.vsdx");
        var png = Path.Combine(dir, "architecture.png");
        var svg = Path.Combine(dir, "architecture.svg");
        var json = Path.Combine(dir, "architecture.json");

        var engine = new VsdxEngine();
        var created = engine.Create(TestData.Architecture15(), vsdx, new WriteOptions { Title = "15-node architecture" });

        Assert.Equal(15, created.ShapesWritten);
        Assert.Empty(created.Compatibility.Errors);

        var read = engine.Read(vsdx);
        var page = read.Diagram.Pages[0];
        Assert.Equal(15, page.Nodes.Count);
        Assert.Equal(3, page.Groups.Count);
        Assert.True(page.Edges.Count >= 15, "multiple intra- and cross-band connectors required");

        // At least two distinct shape families.
        Assert.True(page.Nodes.Select(n => n.Shape).Distinct().Count() >= 2);

        var pngResult = engine.Render(new RenderRequest { InputPath = vsdx, OutputPath = png, Format = "png", Dpi = 120 });
        Assert.Single(pngResult.Pages);
        Assert.True(File.Exists(png));

        var svgResult = engine.Render(new RenderRequest { InputPath = vsdx, OutputPath = svg, Format = "svg" });
        Assert.Single(svgResult.Pages);
        Assert.True(File.Exists(svg));

        engine.ExportModel(vsdx, json);
        Assert.True(File.Exists(json));

        var validation = engine.Validate(vsdx);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public void Scenario_Round1_LayoutHasNoErrors()
    {
        var dir = TestData.NewTempDir("int-r1-layout");
        var vsdx = Path.Combine(dir, "architecture.vsdx");
        var engine = new VsdxEngine();
        engine.Create(TestData.Architecture15(), vsdx);

        var report = LayoutAnalyzer.Analyze(engine.Read(vsdx).Diagram);
        Assert.Empty(report.Errors);
        Assert.True(report.Score >= 80, $"layout score {report.Score}: {string.Join(", ", report.Issues.Select(i => i.Code))}");
    }

    [Fact]
    public void Scenario_Round2_FullIncrementalScenario()
    {
        var dir = TestData.NewTempDir("int-r2");
        var v1 = Path.Combine(dir, "architecture.vsdx");
        var engine = new VsdxEngine();
        engine.Create(TestData.Architecture15(), v1);

        var model = engine.Read(v1).Diagram;
        var page = model.Pages[0];

        // 1) rename Query Service -> Analytics Service
        var query = page.Nodes.Single(n => n.Id == "b2n2");
        query.Id = "analytics-service";
        query.Text = "分析服务\nAnalytics Service";
        query.VisioShapeId = "b2n2";
        foreach (var edge in page.Edges)
        {
            if (edge.From == "b2n2") edge.From = "analytics-service";
            if (edge.To == "b2n2") edge.To = "analytics-service";
        }

        // 2) add Audit Service
        page.Nodes.Add(new Node
        {
            Id = "audit-service",
            Text = "审计服务\nAudit Service",
            Shape = "Hexagon",
            Bounds = new Bounds(5.5, 3.2, 2.0, 0.9),
            GroupId = "grp_2",
            Style = new ShapeStyle { Fill = "#8E6BB5", TextColor = "#FFFFFF" }
        });

        // 3) connect Audit Service to the database node
        page.Edges.Add(new Edge
        {
            Id = "e-audit-db", From = "audit-service", To = "b3n4",
            Kind = "straight", Label = "audit rows", EndArrow = "triangle"
        });

        // 4) move Report Engine
        page.Nodes.Single(n => n.Id == "b2n4").Bounds = new Bounds(9.0, 6.9, 2.2, 0.95);

        // 5) rename a group
        page.Groups.Single(g => g.Id == "grp_2").Name = "Data Platform";

        var v2 = Path.Combine(dir, "architecture_v2.vsdx");
        var result = engine.Update(v1, model, v2);

        Assert.Equal(1, result.NodesAdded);
        // Exactly the renamed node and the moved node changed; nothing else is rewritten.
        Assert.Equal(2, result.NodesModified);
        Assert.Contains(result.Changes, c => c.Kind == "node.modified" && c.Target == "analytics-service");
        Assert.Contains(result.Changes, c => c.Kind == "node.modified" && c.Target == "b2n4");
        Assert.Equal(0, result.NodesDeleted);
        Assert.Equal(1, result.EdgesAdded);
        Assert.Contains(result.Changes, c => c.Kind == "group.renamed" && c.Target == "grp_2");

        var reread = engine.Read(v2).Diagram.Pages[0];
        Assert.Contains(reread.Nodes, n => n.Id == "analytics-service");
        Assert.DoesNotContain(reread.Nodes, n => n.Id == "b2n2");
        Assert.Contains(reread.Nodes, n => n.Id == "audit-service");
        Assert.Contains(reread.Edges, e => e.Id == "e-audit-db" && e.To == "b3n4");
        Assert.Equal("Data Platform", reread.Groups.Single(g => g.Id == "grp_2").Name);
        // grp_2 (data band) gains the new audit-service; the renamed node stays in grp_1.
        Assert.Equal(6, reread.Nodes.Count(n => n.GroupId == "grp_2"));
        Assert.Contains("audit-service", reread.Groups.Single(g => g.Id == "grp_2").Members);
        Assert.Contains("analytics-service", reread.Groups.Single(g => g.Id == "grp_1").Members);
        Assert.DoesNotContain("b2n2", reread.Groups.Single(g => g.Id == "grp_1").Members);
    }

    [Fact]
    public void Scenario_Round2_PreservesUntouchedObjects()
    {
        var dir = TestData.NewTempDir("int-r2-preserve");
        var v1 = Path.Combine(dir, "v1.vsdx");
        var engine = new VsdxEngine();
        engine.Create(TestData.SmallModel(), v1);

        var before = engine.Read(v1).Diagram.Pages[0].Nodes
            .Where(n => n.Id != "n_a")
            .ToDictionary(n => n.Id, n => (n.Text, n.Bounds.X, n.Bounds.Y, n.Style?.Fill));

        var model = engine.Read(v1).Diagram;
        model.Pages[0].Nodes.Single(n => n.Id == "n_a").Text = "changed";
        var v2 = Path.Combine(dir, "v2.vsdx");
        engine.Update(v1, model, v2);

        var after = engine.Read(v2).Diagram.Pages[0].Nodes.ToDictionary(n => n.Id, n => n);
        foreach (var (id, expected) in before)
        {
            Assert.Equal(expected.Text, after[id].Text);
            Assert.Equal(expected.X, after[id].Bounds.X, 2);
            Assert.Equal(expected.Y, after[id].Bounds.Y, 2);
            Assert.Equal(expected.Fill, after[id].Style?.Fill);
        }
    }

    [Fact]
    public void Scenario_Round3_RoundTripFidelity()
    {
        var dir = TestData.NewTempDir("int-r3");
        var v1 = Path.Combine(dir, "original.vsdx");
        var engine = new VsdxEngine();
        engine.Create(TestData.Architecture15(), v1);

        var original = engine.Read(v1).Diagram;
        var before = Fingerprint(original);

        // modify exactly one node
        var edited = engine.Read(v1).Diagram;
        edited.Pages[0].Nodes.Single(n => n.Id == "b1n3").Text = "changed node";
        var v2 = Path.Combine(dir, "edited.vsdx");
        engine.Update(v1, edited, v2);

        var reread = engine.Read(v2).Diagram;
        var after = Fingerprint(reread);

        Assert.Equal(before.Count, after.Count);
        int differing = before.Count(kv => !after.TryGetValue(kv.Key, out var v) || v != kv.Value);
        Assert.Equal(1, differing);

        // Structural counts must be identical.
        Assert.Equal(original.Pages[0].Nodes.Count, reread.Pages[0].Nodes.Count);
        Assert.Equal(original.Pages[0].Edges.Count, reread.Pages[0].Edges.Count);
        Assert.Equal(original.Pages[0].Groups.Count, reread.Pages[0].Groups.Count);
        Assert.Equal(original.Pages[0].Layers.Count, reread.Pages[0].Layers.Count);
    }

    [Fact]
    public void Scenario_Round3_DiffReportIsAccurate()
    {
        var dir = TestData.NewTempDir("int-r3-diff");
        var v1 = Path.Combine(dir, "a.vsdx");
        var engine = new VsdxEngine();
        engine.Create(TestData.SmallModel(), v1);

        var model = engine.Read(v1).Diagram;
        model.Pages[0].Nodes.Single(n => n.Id == "n_a").Text = "Alpha v2";
        model.Pages[0].Nodes.Add(new Node { Id = "n_z", Text = "Zeta", Bounds = new Bounds(9, 7, 1, 0.8) });
        var v2 = Path.Combine(dir, "b.vsdx");
        engine.Update(v1, model, v2);

        var diff = new VsdxEngine().Diff(v1, v2);
        Assert.False(diff.Identical);
        Assert.Contains(diff.Added, d => d.Target == "n_z");
        Assert.Contains(diff.Modified, d => d.Target == "n_a" && d.Property == "text");
    }

    [Fact]
    public void Scenario_Round4_ExternalPackageLifecycle()
    {
        var dir = TestData.NewTempDir("int-r4");
        var external = ExternalFixtures.WriteComplexPackage(dir, "external.vsdx");
        var engine = new VsdxEngine();

        // read -> export -> modify -> save -> reread -> render
        var read = engine.Read(external);
        var json = Path.Combine(dir, "external.json");
        engine.ExportModel(external, json);
        Assert.True(File.Exists(json));

        var model = engine.Read(external).Diagram;
        model.Pages[0].Nodes.First(n => n.Text == "Billing Worker").Text = "Billing Worker v2";

        var output = Path.Combine(dir, "external-v2.vsdx");
        var result = engine.Update(external, model, output);
        Assert.Empty(result.Compatibility.Errors);

        var reread = engine.Read(output).Diagram.Pages[0];
        Assert.Contains(reread.Nodes, n => n.Text == "Billing Worker v2");
        Assert.Contains(reread.Nodes, n => n.Text == "Orders DB");
        Assert.Single(reread.Groups);

        var svg = Path.Combine(dir, "external.svg");
        var render = engine.Render(new RenderRequest { InputPath = output, OutputPath = svg, Format = "svg" });
        Assert.Single(render.Pages);
        Assert.True(new FileInfo(svg).Length > 200);

        Assert.True(engine.Validate(output).IsValid);
    }

    [Fact]
    public void Scenario_EndToEnd_JsonModelArtifactLifecycle()
    {
        var dir = TestData.NewTempDir("int-e2e");
        var engine = new VsdxEngine();

        var modelPath = Path.Combine(dir, "model.json");
        var model = TestData.SmallModel();
        DiagramJson.Save(model, modelPath);

        var vsdx = Path.Combine(dir, "out.vsdx");
        engine.Create(modelPath, vsdx);
        Assert.True(File.Exists(vsdx));

        var exported = Path.Combine(dir, "exported.json");
        engine.ExportModel(vsdx, exported);
        var reloaded = DiagramJson.Load(exported);
        Assert.Equal(3, reloaded.Pages[0].Nodes.Count);

        var updatedModel = DiagramJson.Load(exported);
        updatedModel.Pages[0].Nodes.Single(n => n.Id == "n_c").Text = "改过的中文";
        var updatedPath = Path.Combine(dir, "updated.json");
        DiagramJson.Save(updatedModel, updatedPath);

        var v2 = Path.Combine(dir, "out_v2.vsdx");
        engine.Update(vsdx, updatedPath, v2);

        Assert.Equal("改过的中文", engine.Read(v2).Diagram.Pages[0].Nodes.Single(n => n.Id == "n_c").Text);
    }

    [Fact]
    public void Scenario_LayoutAnalyzerCatchesOverlap()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 11, Height = 8.5 },
            Nodes =
            {
                new Node { Id = "a", Bounds = new Bounds(2, 5, 2, 1) },
                new Node { Id = "b", Bounds = new Bounds(2.5, 5, 2, 1) }
            }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });
        Assert.Contains(report.Issues, i => i.Code == "node.overlap");
        Assert.Contains(report.Errors, i => i.Code == "node.overlap");
    }

    [Fact]
    public void Scenario_LayoutAnalyzerCatchesEdgeThroughNode()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 8 },
            Nodes =
            {
                new Node { Id = "left", Bounds = new Bounds(2.0, 5, 1.5, 1) },
                new Node { Id = "middle", Bounds = new Bounds(5.5, 5, 1.5, 1) },
                new Node { Id = "right", Bounds = new Bounds(9.0, 5, 1.5, 1) }
            },
            Edges =
            {
                new Edge { Id = "e", From = "left", To = "right", Kind = "straight" }
            }
        };

        // Analyse() routes first, so an obstructed straight edge gets a detour and the
        // checker (correctly) reports no violation. Verify the detector itself against the
        // straight path the author actually asked for.
        var issue = LayoutAnalyzer.CheckEdgeThroughNode(
            page.Nodes.Single(n => n.Id == "left"),
            page.Nodes.Single(n => n.Id == "right"),
            page.Nodes.Where(n => n.Id is "left" or "right").ToList().Count == 2
                ? new[] { page.Nodes.Single(n => n.Id == "middle") }
                : Array.Empty<Node>(),
            new Edge { Id = "e", From = "left", To = "right", Kind = "straight" });

        Assert.Equal("middle", issue);

        // And the router must actually fix it when asked to route.
        var diagram = new Diagram { Pages = { page } };
        OrthogonalRouter.Route(diagram);
        var report = LayoutAnalyzer.Analyze(diagram);
        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.through-node");
    }

    [Fact]
    public void Scenario_LayoutAnalyzerCatchesTextOverflow()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 11, Height = 8.5 },
            Nodes =
            {
                new Node
                {
                    Id = "tiny",
                    Text = "A very long label that cannot possibly fit inside this small shape",
                    Bounds = new Bounds(3, 5, 0.6, 0.3),
                    Style = new ShapeStyle { FontSize = 14 }
                }
            }
        };

        var report = LayoutAnalyzer.Analyze(new Diagram { Pages = { page } });
        Assert.Contains(report.Issues, i => i.Code.StartsWith("text.overflow", StringComparison.Ordinal));
    }

    [Fact]
    public void Scenario_RouterAvoidsNodes()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 12, Height = 9 },
            Nodes =
            {
                new Node { Id = "a", Bounds = new Bounds(2, 7, 1.6, 0.8) },
                new Node { Id = "b", Bounds = new Bounds(2, 3, 1.6, 0.8) },
                new Node { Id = "blocker", Bounds = new Bounds(2, 5, 1.6, 0.9) }
            },
            Edges =
            {
                new Edge { Id = "e", From = "a", To = "b", Kind = "rightAngle" }
            }
        };

        var diagram = new Diagram { Pages = { page } };
        OrthogonalRouter.Route(diagram);

        var report = LayoutAnalyzer.Analyze(diagram);
        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.through-node");
    }

    [Fact]
    public void Scenario_BatchCreateRenderValidate()
    {
        var dir = TestData.NewTempDir("int-batch");
        var models = Path.Combine(dir, "models");
        var outDir = Path.Combine(dir, "out");
        Directory.CreateDirectory(models);

        DiagramJson.Save(TestData.SmallModel("Batch A"), Path.Combine(models, "a.json"));
        DiagramJson.Save(TestData.Architecture15(), Path.Combine(models, "b.json"));

        var runner = new BatchRunner(new VsdxEngine());

        var created = runner.CreateMany(Directory.GetFiles(models, "*.json")
            .Select(p => (p, Path.Combine(outDir, Path.GetFileNameWithoutExtension(p) + ".vsdx"))));
        Assert.Equal(2, created.Total);
        Assert.Equal(2, created.Succeeded);
        Assert.True(created.Ok);

        var rendered = runner.RenderMany(Directory.GetFiles(outDir, "*.vsdx")
            .Select(p => (p, Path.ChangeExtension(p, ".png"))),
            new RenderRequest { InputPath = "", OutputPath = "", Format = "png", Dpi = 96 });
        Assert.Equal(2, rendered.Succeeded);

        var validated = runner.ValidateMany(Directory.GetFiles(outDir, "*.vsdx"));
        Assert.Equal(2, validated.Succeeded);
        Assert.True(validated.Ok);
    }

    [Fact]
    public void Scenario_BatchContinuesAfterFailure()
    {
        var dir = TestData.NewTempDir("int-batch-fail");
        var good = Path.Combine(dir, "good.json");
        DiagramJson.Save(TestData.SmallModel(), good);

        var bad = Path.Combine(dir, "bad.json");
        File.WriteAllText(bad, "{ not valid json");

        var outDir = Path.Combine(dir, "out");
        var runner = new BatchRunner(new VsdxEngine());
        var result = runner.CreateMany(new[]
        {
            (good, Path.Combine(outDir, "good.vsdx")),
            (bad, Path.Combine(outDir, "bad.vsdx"))
        });

        // One bad item must not abort the batch.
        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.False(result.Ok);
        Assert.True(File.Exists(Path.Combine(outDir, "good.vsdx")));
        Assert.NotNull(result.Items.Single(i => !i.Ok).Error);
    }

    private static Dictionary<string, string> Fingerprint(Diagram diagram)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var page in diagram.Pages)
        {
            foreach (var n in page.Nodes)
                map[$"{page.Name}/node/{n.Id}"] = $"{n.Text}|{n.Shape}|{n.Bounds.X:F2}|{n.Bounds.Y:F2}|{n.Style?.Fill}|{n.GroupId}";
            foreach (var e in page.Edges)
                map[$"{page.Name}/edge/{e.Id}"] = $"{e.From}->{e.To}|{e.Label}";
            foreach (var g in page.Groups)
                map[$"{page.Name}/group/{g.Id}"] = $"{g.Name}|{string.Join(",", g.Members.OrderBy(x => x, StringComparer.Ordinal))}";
        }
        return map;
    }
}
