using Vsdx.Adapters;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// ZOrderTests: z-order is document order in Visio. Phase 8 found `Page.ShapeOrder` was filled on
/// read but never consumed on write, so every save silently restacked the page.
/// </summary>
public sealed class ZOrderTests
{
    private static Diagram Stack()
    {
        // Five overlapping shapes with an unambiguous stacking: bottom -> top.
        return new Diagram
        {
            Id = "z", Name = "Z", Type = "architecture",
            PageSize = new PageSize { Width = 10, Height = 8 },
            Pages =
            {
                new Page
                {
                    Name = "P",
                    Size = new PageSize { Width = 10, Height = 8 },
                    Nodes =
                    {
                        Layer("bottom", 5.0, 4.0, 3.0, 2.0, "#2E75B6"),
                        Layer("second", 5.4, 4.2, 2.6, 1.8, "#70AD47"),
                        Layer("third", 5.8, 4.4, 2.2, 1.6, "#F2B705"),
                        Layer("fourth", 6.2, 4.6, 1.8, 1.4, "#C55A11"),
                        Layer("top", 6.6, 4.8, 1.4, 1.2, "#7030A0")
                    }
                }
            }
        };
    }

    private static Node Layer(string id, double x, double y, double w, double h, string fill) => new()
    {
        Id = id,
        Text = id,
        Bounds = new Bounds(x, y, w, h),
        Style = new ShapeStyle { Fill = fill }
    };

    [Fact]
    public void ReaderRecordsShapeOrder()
    {
        var dir = TestData.NewTempDir("z-read");
        var path = Path.Combine(dir, "z.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Stack(), path);

        var page = engine.Read(path).Diagram.Pages[0];

        Assert.NotNull(page.ShapeOrder);
        Assert.Equal(5, page.ShapeOrder!.Count);
        Assert.Contains("bottom", page.ShapeOrder);
        Assert.Contains("top", page.ShapeOrder);
    }

    [Fact]
    public void RoundTripPreservesShapeOrder()
    {
        var dir = TestData.NewTempDir("z-rt");
        var source = Path.Combine(dir, "a.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Stack(), source);
        var before = VisioZOrderBridge.ReadOrder(source);

        var model = engine.Read(source).Diagram;
        var output = Path.Combine(dir, "b.vsdx");
        engine.Update(source, model, output);

        Assert.Equal(before, VisioZOrderBridge.ReadOrder(output));
    }

    [Fact]
    public void WriterHonoursAnExplicitReorder()
    {
        // The decisive test: ask for the reverse stacking and verify it actually happens.
        var dir = TestData.NewTempDir("z-rev");
        var source = Path.Combine(dir, "a.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Stack(), source);

        var model = engine.Read(source).Diagram;
        model.Pages[0].ShapeOrder = new List<string> { "top", "fourth", "third", "second", "bottom" };

        var output = Path.Combine(dir, "c.vsdx");
        engine.Update(source, model, output);

        var names = VerbatimNames(output);
        Assert.Equal(new[] { "top", "fourth", "third", "second", "bottom" }, names);
    }

    [Fact]
    public void UnlistedShapesKeepRelativeOrderAndGoLast()
    {
        var dir = TestData.NewTempDir("z-partial");
        var source = Path.Combine(dir, "a.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Stack(), source);

        var model = engine.Read(source).Diagram;
        // Name only the top shape; the rest must stay in their original relative order, after it.
        model.Pages[0].ShapeOrder = new List<string> { "top" };

        var output = Path.Combine(dir, "d.vsdx");
        engine.Update(source, model, output);

        var names = VerbatimNames(output);
        Assert.Equal("top", names[0]);
        Assert.Equal(new[] { "bottom", "second", "third", "fourth" },
            names.Skip(1).ToArray());
    }

    [Fact]
    public void EmptyShapeOrderIsIgnored()
    {
        var dir = TestData.NewTempDir("z-empty");
        var source = Path.Combine(dir, "a.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Stack(), source);
        var before = VisioZOrderBridge.ReadOrder(source);

        var model = engine.Read(source).Diagram;
        model.Pages[0].ShapeOrder = new List<string>();

        var output = Path.Combine(dir, "e.vsdx");
        engine.Update(source, model, output);

        Assert.Equal(before, VisioZOrderBridge.ReadOrder(output));
    }

    [Fact]
    public void UnknownIdsAreSkippedWithoutFailure()
    {
        var dir = TestData.NewTempDir("z-unknown");
        var source = Path.Combine(dir, "a.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Stack(), source);

        var model = engine.Read(source).Diagram;
        model.Pages[0].ShapeOrder = new List<string> { "does-not-exist", "top" };

        var output = Path.Combine(dir, "f.vsdx");
        engine.Update(source, model, output);

        Assert.Equal("top", VerbatimNames(output)[0]);
    }

    [Fact]
    public void OrderIsStableAcrossThreeGenerations()
    {
        var dir = TestData.NewTempDir("z-gen");
        var current = Path.Combine(dir, "g0.vsdx");

        var engine = new VsdxEngine();
        engine.Create(Stack(), current);
        var original = VisioZOrderBridge.ReadOrder(current);

        for (int i = 1; i <= 3; i++)
        {
            var next = Path.Combine(dir, $"g{i}.vsdx");
            engine.Update(current, engine.Read(current).Diagram, next);
            current = next;
        }

        Assert.Equal(original, VisioZOrderBridge.ReadOrder(current));
    }

    /// <summary>Shape ids in document order, mapped back to the model names the writer stored.</summary>
    private static string[] VerbatimNames(string vsdxPath)
    {
        var doc = FidelityTests.ReadPage(vsdxPath);
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/office/visio/2012/main";
        return doc.Descendants(ns + "Shape")
            .Select(s => (string?)s.Attribute("Name"))
            .Where(n => n is not null)
            .Select(n => n!)
            .ToArray();
    }
}
