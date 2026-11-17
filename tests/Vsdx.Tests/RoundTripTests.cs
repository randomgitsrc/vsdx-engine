using System.IO.Compression;
using System.Security.Cryptography;
using Vsdx.Adapters;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// RoundTripTests: save -> load -> save must not silently lose structure, and untouched
/// objects must survive an unrelated edit byte-for-byte in their modelled properties.
/// </summary>
public sealed class RoundTripTests
{
    [Fact]
    public void RoundTrip_ShapeCountsAreStable()
    {
        var dir = TestData.NewTempDir("rt-counts");
        var engine = new VsdxEngine();

        var v1 = Path.Combine(dir, "v1.vsdx");
        engine.Create(TestData.SmallModel(), v1);

        var model = engine.Read(v1).Diagram;
        var v2 = Path.Combine(dir, "v2.vsdx");
        engine.Update(v1, model, v2);

        var a = engine.Read(v1).Diagram.Pages[0];
        var b = engine.Read(v2).Diagram.Pages[0];

        Assert.Equal(a.Nodes.Count, b.Nodes.Count);
        Assert.Equal(a.Edges.Count, b.Edges.Count);
        Assert.Equal(a.Groups.Count, b.Groups.Count);
    }

    [Fact]
    public void RoundTrip_IdsAreStable()
    {
        var dir = TestData.NewTempDir("rt-ids");
        var engine = new VsdxEngine();

        var v1 = Path.Combine(dir, "v1.vsdx");
        engine.Create(TestData.SmallModel(), v1);
        var before = engine.Read(v1).Diagram;

        var v2 = Path.Combine(dir, "v2.vsdx");
        engine.Update(v1, before, v2);
        var after = engine.Read(v2).Diagram;

        Assert.Equal(
            before.Pages[0].Nodes.Select(n => n.Id).OrderBy(x => x),
            after.Pages[0].Nodes.Select(n => n.Id).OrderBy(x => x));
        Assert.Equal(
            before.Pages[0].Edges.Select(e => e.Id).OrderBy(x => x),
            after.Pages[0].Edges.Select(e => e.Id).OrderBy(x => x));
    }

    [Fact]
    public void RoundTrip_VisioShapeIdsAreStable()
    {
        var dir = TestData.NewTempDir("rt-visio-ids");
        var engine = new VsdxEngine();

        var v1 = Path.Combine(dir, "v1.vsdx");
        engine.Create(TestData.SmallModel(), v1);
        var before = engine.Read(v1).Diagram.Pages[0].Nodes.ToDictionary(n => n.Id, n => n.VisioShapeId);

        var model = engine.Read(v1).Diagram;
        var v2 = Path.Combine(dir, "v2.vsdx");
        engine.Update(v1, model, v2);
        var after = engine.Read(v2).Diagram.Pages[0].Nodes.ToDictionary(n => n.Id, n => n.VisioShapeId);

        foreach (var (id, visioId) in before)
            Assert.Equal(visioId, after[id]);
    }

    [Fact]
    public void RoundTrip_ShapeDataSurvives()
    {
        var dir = TestData.NewTempDir("rt-data");
        var engine = new VsdxEngine();

        var v1 = Path.Combine(dir, "v1.vsdx");
        engine.Create(TestData.SmallModel(), v1);
        var model = engine.Read(v1).Diagram;

        var v2 = Path.Combine(dir, "v2.vsdx");
        engine.Update(v1, model, v2);

        var node = engine.Read(v2).Diagram.Pages[0].Nodes.Single(n => n.Id == "n_a");
        Assert.Equal("team-a", node.ShapeData!["owner"]);
        Assert.Equal("1", node.ShapeData["tier"]);
    }

    [Fact]
    public void RoundTrip_StylesSurvive()
    {
        var dir = TestData.NewTempDir("rt-style");
        var engine = new VsdxEngine();

        var v1 = Path.Combine(dir, "v1.vsdx");
        engine.Create(TestData.SmallModel(), v1);
        var model = engine.Read(v1).Diagram;

        var v2 = Path.Combine(dir, "v2.vsdx");
        engine.Update(v1, model, v2);

        var node = engine.Read(v2).Diagram.Pages[0].Nodes.Single(n => n.Id == "n_a");
        Assert.Equal("#2E75B6", node.Style!.Fill);
        Assert.Equal("#FFFFFF", node.Style.TextColor);
    }

    [Fact]
    public void RoundTrip_ConnectorEndpointsSurvive()
    {
        var dir = TestData.NewTempDir("rt-conn");
        var engine = new VsdxEngine();

        var v1 = Path.Combine(dir, "v1.vsdx");
        engine.Create(TestData.SmallModel(), v1);
        var model = engine.Read(v1).Diagram;

        var v2 = Path.Combine(dir, "v2.vsdx");
        engine.Update(v1, model, v2);

        var edges = engine.Read(v2).Diagram.Pages[0].Edges;
        Assert.Equal("n_a", edges.Single(e => e.Id == "e_ab").From);
        Assert.Equal("n_b", edges.Single(e => e.Id == "e_ab").To);
    }

    [Fact]
    public void RoundTrip_PageSizeSurvives()
    {
        var dir = TestData.NewTempDir("rt-page");
        var engine = new VsdxEngine();

        var v1 = Path.Combine(dir, "v1.vsdx");
        engine.Create(TestData.SmallModel(), v1);
        var model = engine.Read(v1).Diagram;

        var v2 = Path.Combine(dir, "v2.vsdx");
        engine.Update(v1, model, v2);

        var page = engine.Read(v2).Diagram.Pages[0];
        Assert.Equal(11.0, page.Size!.Width, 2);
        Assert.Equal(8.5, page.Size.Height, 2);
    }

    [Fact]
    public void RoundTrip_ThreeGenerationsRemainStable()
    {
        var dir = TestData.NewTempDir("rt-three");
        var engine = new VsdxEngine();

        var previous = Path.Combine(dir, "g1.vsdx");
        engine.Create(TestData.SmallModel(), previous);
        var baseline = Snapshot(engine.Read(previous).Diagram);

        for (int gen = 2; gen <= 4; gen++)
        {
            var model = engine.Read(previous).Diagram;
            var next = Path.Combine(dir, $"g{gen}.vsdx");
            engine.Update(previous, model, next);

            var snapshot = Snapshot(engine.Read(next).Diagram);
            Assert.Equal(baseline, snapshot);
            previous = next;
        }
    }

    [Fact]
    public void RoundTrip_NoOpUpdateIsLossless()
    {
        var dir = TestData.NewTempDir("rt-noop");
        var engine = new VsdxEngine();

        var v1 = Path.Combine(dir, "v1.vsdx");
        engine.Create(TestData.SmallModel(), v1);

        var model = engine.Read(v1).Diagram;
        var v2 = Path.Combine(dir, "v2.vsdx");
        var result = engine.Update(v1, model, v2, new UpdateOptions { RecordHistory = false });

        Assert.Equal(0, result.NodesAdded);
        Assert.Equal(0, result.NodesDeleted);
        Assert.Equal(0, result.EdgesAdded);
        Assert.Equal(0, result.EdgesDeleted);
        Assert.Equal(0, result.NodesModified);
        Assert.True(result.NodesPreserved >= 3, $"expected untouched nodes to be preserved, got {result.NodesPreserved}");
    }

    [Fact]
    public void RoundTrip_ContainerStructureSurvives()
    {
        var dir = TestData.NewTempDir("rt-group");
        var engine = new VsdxEngine();

        var v1 = Path.Combine(dir, "v1.vsdx");
        engine.Create(TestData.SmallModel(), v1);
        var model = engine.Read(v1).Diagram;

        var v2 = Path.Combine(dir, "v2.vsdx");
        engine.Update(v1, model, v2);

        var group = engine.Read(v2).Diagram.Pages[0].Groups.Single();
        Assert.Equal("Layer One", group.Name);
        Assert.Equal(2, group.Members.Count);
    }

    [Fact]
    public void RoundTrip_BinaryIsReproducibleForSameModel()
    {
        var dir = TestData.NewTempDir("rt-deterministic");
        var engine = new VsdxEngine();
        var model = TestData.SmallModel();

        var a = Path.Combine(dir, "a.vsdx");
        var b = Path.Combine(dir, "b.vsdx");
        engine.Create(model, a);
        engine.Create(model, b);

        Assert.Equal(HashOfPackageParts(a), HashOfPackageParts(b));
    }

    /// <summary>Structural fingerprint of a diagram used to compare generations.</summary>
    private static string Snapshot(Diagram diagram)
    {
        var parts = new List<string>();
        foreach (var page in diagram.Pages)
        {
            parts.Add($"page:{page.Name}:{page.Size?.Width}x{page.Size?.Height}");
            foreach (var n in page.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
                parts.Add($"n:{n.Id}:{n.Text}:{n.Bounds.X:F2},{n.Bounds.Y:F2},{n.Bounds.W:F2},{n.Bounds.H:F2}:{n.Style?.Fill}:{n.GroupId}");
            foreach (var e in page.Edges.OrderBy(e => e.Id, StringComparer.Ordinal))
                parts.Add($"e:{e.Id}:{e.From}->{e.To}:{e.Label}");
            foreach (var g in page.Groups.OrderBy(g => g.Id, StringComparer.Ordinal))
                parts.Add($"g:{g.Id}:{g.Name}:{string.Join(",", g.Members.OrderBy(x => x, StringComparer.Ordinal))}");
            foreach (var l in page.Layers.OrderBy(l => l.Name, StringComparer.Ordinal))
                parts.Add($"l:{l.Name}:{l.Visible}");
        }
        return string.Join("\n", parts);
    }

    /// <summary>Hashes the XML parts (ignoring timestamps in docProps) to compare packages.</summary>
    private static string HashOfPackageParts(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var sha = SHA256.Create();
        var names = zip.Entries
            .Where(e => e.FullName.EndsWith(".xml") && !e.FullName.StartsWith("docProps", StringComparison.Ordinal))
            .OrderBy(e => e.FullName, StringComparer.Ordinal)
            .ToList();

        foreach (var entry in names)
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var nameBytes = System.Text.Encoding.UTF8.GetBytes(entry.FullName);
            sha.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0);
            var bytes = buffer.ToArray();
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }
}
