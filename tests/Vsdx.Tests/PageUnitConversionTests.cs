using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The page size must mean what its unit says.
///
/// The writer passed the model's width and height straight through and declared inches, so a page
/// authored as 10x8 cm was written as 10x8 inches — a 2.5x error in both directions, and a label
/// (`unit: "cm"`) that disagreed with the value beside it. `MasterCatalog.UnitFactor` already knew
/// the conversion; nothing called it on this path.
///
/// The incremental editor did convert, so create and update disagreed about the same model — worse
/// than a uniformly wrong conversion, because either path looked correct on its own.
/// </summary>
public sealed class PageUnitConversionTests
{
    private static Diagram WithPage(string unit, double width = 10, double height = 8) => new()
    {
        Id = "t",
        PageSize = new PageSize { Width = width, Height = height, Unit = unit },
        Pages =
        {
            new Page
            {
                Name = "p",
                Size = new PageSize { Width = width, Height = height, Unit = unit },
                Nodes = { new Node { Id = "n", Text = "N", Bounds = new Bounds(width / 2, height / 2, 1, 0.6) } }
            }
        }
    };

    private static (Diagram Written, string Path) Create(Diagram diagram)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-unit-{Guid.NewGuid():N}.vsdx");
        new VsdxEngine().Create(diagram, path, new WriteOptions { AutoLayout = false, ValidateAfterSave = false });
        return (new VsdxEngine().Read(path).Diagram, path);
    }

    [Fact]
    public void CentimetresAreConvertedToInches()
    {
        var (written, path) = Create(WithPage("cm"));
        try
        {
            var size = written.Pages[0].Size!;

            // 10cm = 3.937", 8cm = 3.150"
            Assert.Equal(3.937, size.Width, 2);
            Assert.Equal(3.150, size.Height, 2);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MillimetresAreConverted()
    {
        var (written, path) = Create(WithPage("mm", 254, 203));
        try
        {
            var size = written.Pages[0].Size!;

            Assert.Equal(10.0, size.Width, 1);   // 254mm = 10"
            Assert.Equal(7.99, size.Height, 1);  // 203mm = 7.99"
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PointsAreConverted()
    {
        var (written, path) = Create(WithPage("pt", 720, 540));
        try
        {
            var size = written.Pages[0].Size!;

            Assert.Equal(10.0, size.Width, 1);   // 720pt = 10"
            Assert.Equal(7.5, size.Height, 1);   // 540pt = 7.5"
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InchesAreLeftAlone()
    {
        var (written, path) = Create(WithPage("inches", 11, 8.5));
        try
        {
            var size = written.Pages[0].Size!;

            Assert.Equal(11, size.Width, 2);
            Assert.Equal(8.5, size.Height, 2);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheWrittenLabelMatchesTheWrittenValue()
    {
        // The original bug's signature: a value in inches with a unit of centimetres beside it.
        var (written, path) = Create(WithPage("cm"));
        try
        {
            var size = written.Pages[0].Size!;

            bool labelSaysCm = string.Equals(size.Unit, "cm", StringComparison.OrdinalIgnoreCase);
            bool valueIsCentimetreSized = Math.Abs(size.Width - 10) < 0.2;

            Assert.False(labelSaysCm && valueIsCentimetreSized,
                $"page reports {size.Width:F2} {size.Unit} — the label and value disagree");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AMissingUnitIsTreatedAsInches()
    {
        var diagram = WithPage("inches", 9, 6);
        diagram.PageSize!.Unit = null!;
        diagram.Pages[0].Size!.Unit = null!;

        var (written, path) = Create(diagram);
        try
        {
            Assert.Equal(9, written.Pages[0].Size!.Width, 2);
        }
        finally { File.Delete(path); }
    }
}
