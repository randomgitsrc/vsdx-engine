using Vsdx.Model;

namespace Vsdx.Tests;

/// <summary>Shared fixtures: temp directories and a canonical in-memory model.</summary>
public static class TestData
{
    /// <summary>Creates a unique scratch directory that lives under the test output folder.</summary>
    public static string NewTempDir(string label)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "testout", $"{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Builds a small but structurally complete model (nodes, group, edge, shape data, layer).</summary>
    public static Diagram SmallModel(string name = "Small")
    {
        return new Diagram
        {
            Id = "test-diagram",
            Name = name,
            Type = "architecture",
            PageSize = new PageSize { Width = 11, Height = 8.5, Unit = "inches" },
            Pages =
            {
                new Page
                {
                    Id = "page-0",
                    Name = "Page-1",
                    Size = new PageSize { Width = 11, Height = 8.5, Unit = "inches" },
                    Layers =
                    {
                        new Layer { Id = "app", Name = "App" }
                    },
                    Groups =
                    {
                        new Group
                        {
                            Id = "grp_1",
                            Name = "Layer One",
                            Kind = "container",
                            AutoSize = true,
                            Style = new ShapeStyle { Fill = "#EAF3FB", Stroke = "#9DC3E6", Bold = true }
                        }
                    },
                    Nodes =
                    {
                        new Node
                        {
                            Id = "n_a", Text = "Alpha", Shape = "Rectangle",
                            Bounds = new Bounds(2.0, 6.0, 1.6, 0.8),
                            GroupId = "grp_1",
                            Layers = new List<string> { "app" },
                            Style = new ShapeStyle { Fill = "#2E75B6", TextColor = "#FFFFFF", Bold = true },
                            ShapeData = new Dictionary<string, string> { ["owner"] = "team-a", ["tier"] = "1" }
                        },
                        new Node
                        {
                            Id = "n_b", Text = "Beta", Shape = "Process",
                            Bounds = new Bounds(5.0, 6.0, 1.6, 0.8),
                            GroupId = "grp_1",
                            Style = new ShapeStyle { Fill = "#70AD47" }
                        },
                        new Node
                        {
                            Id = "n_c", Text = "中文节点", Shape = "Circle",
                            Bounds = new Bounds(8.0, 3.0, 1.0, 1.0),
                            Style = new ShapeStyle { Fill = "#C55A11" }
                        }
                    },
                    Edges =
                    {
                        new Edge
                        {
                            Id = "e_ab", From = "n_a", To = "n_b", Kind = "straight",
                            Label = "calls", EndArrow = "triangle", LineColor = "#2E75B6"
                        },
                        new Edge
                        {
                            Id = "e_bc", From = "n_b", To = "n_c", Kind = "straight",
                            Label = "reads", EndArrow = "triangle"
                        }
                    }
                }
            }
        };
    }

    /// <summary>A 15-node, 3-band model matching the round-1 acceptance task.</summary>
    public static Diagram Architecture15()
    {
        var model = new Diagram
        {
            Id = "arch-15",
            Name = "15-node architecture",
            Type = "architecture",
            PageSize = new PageSize { Width = 18, Height = 11, Unit = "inches" }
        };

        var page = new Page
        {
            Id = "page-0",
            Name = "Architecture",
            Size = model.PageSize
        };
        model.Pages.Add(page);

        string[][] bands =
        {
            new[] { "b1n1", "b1n2", "b1n3", "b1n4", "b1n5" },
            new[] { "b2n1", "b2n2", "b2n3", "b2n4", "b2n5" },
            new[] { "b3n1", "b3n2", "b3n3", "b3n4", "b3n5" }
        };
        double[] ys = { 9.4, 6.4, 3.2 };

        for (int band = 0; band < 3; band++)
        {
            var group = new Group
            {
                Id = $"grp_{band}",
                Name = $"Band {band + 1}",
                Kind = "container",
                AutoSize = true,
                Style = new ShapeStyle { Fill = "#F0F4F8", Stroke = "#B0C4DE", Bold = true }
            };
            page.Groups.Add(group);

            for (int i = 0; i < bands[band].Length; i++)
            {
                var id = bands[band][i];
                var shape = ((band + i) % 3) switch
                {
                    0 => "Rectangle",
                    1 => "Process",
                    _ => "Hexagon"
                };
                var node = new Node
                {
                    Id = id,
                    Text = $"{id}\n节点 {id}",
                    Shape = shape,
                    Bounds = new Bounds(2.6 + i * 2.9, ys[band], 2.2, 0.95),
                    GroupId = group.Id,
                    Style = new ShapeStyle
                    {
                        Fill = band switch { 0 => "#2E75B6", 1 => "#70AD47", _ => "#C55A11" },
                        TextColor = "#FFFFFF",
                        FontSize = 11
                    }
                };
                page.Nodes.Add(node);
                group.Members.Add(id);
            }
        }

        int edgeIndex = 0;
        for (int band = 0; band < 3; band++)
        for (int i = 0; i < 4; i++)
        {
            page.Edges.Add(new Edge
            {
                Id = $"e_in_{edgeIndex++}",
                From = bands[band][i],
                To = bands[band][i + 1],
                Kind = "straight",
                Label = $"flow{i}",
                EndArrow = "triangle"
            });
        }
        for (int i = 0; i < 5; i++)
        {
            page.Edges.Add(new Edge
            {
                Id = $"e_cross_{i}",
                From = bands[0][i],
                To = bands[1][i],
                Kind = "straight",
                EndArrow = "triangle"
            });
        }

        return model;
    }
}
