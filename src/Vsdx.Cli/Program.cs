using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vsdx.Adapters;
using Vsdx.Engine;
using Vsdx.Model;

namespace Vsdx.Cli;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOut = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            return Run(args);
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine();
            Help.Print(Console.Error);
            return 2;
        }
        catch (FileNotFoundException ex)
        {
            return Fail("file.not-found", ex.Message);
        }
        catch (Exception ex)
        {
            return Fail("unhandled", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Help.Print(Console.Out);
            return 0;
        }

        var command = args[0];
        var options = Args.Parse(args.Skip(1).ToArray());
        var json = options.Has("json");

        return command switch
        {
            "create" => Create(options, json),
            "inspect" => Inspect(options, json),
            "export-model" => ExportModel(options, json),
            "update" => Update(options, json),
            "render" => Render(options, json),
            "validate" => Validate(options, json),
            "diff" => Diff(options, json),
            "check-layout" => CheckLayout(options, json),
            "batch" => Batch(options, json),
            "version" or "--version" => Version(json),
            _ => throw new UsageException($"unknown command '{command}'")
        };
    }

    private static int Create(Args options, bool json)
    {
        var input = options.Require("input");
        var output = options.Require("output");

        var engine = new VsdxEngine();
        var result = engine.Create(input, output, new WriteOptions
        {
            Title = options.Get("title"),
            Author = options.Get("author"),
            ValidateAfterSave = !options.Has("no-validate"),
            // Geometric derivation is opt-in so an author who already sized every shape keeps
            // byte-for-byte control over the output.
            FitToText = options.Has("fit-text"),
            FitPage = options.Has("fit-page"),
            Recenter = options.Has("recenter"),
            Layout = options.Get("layout")
        });

        if (json)
        {
            Emit(new
            {
                ok = result.Compatibility.Errors.Any() == false,
                command = "create",
                input = Path.GetFullPath(input),
                output = result.OutputPath,
                bytes = result.FileSize,
                pages = result.PagesWritten,
                shapes = result.ShapesWritten,
                connectors = result.ConnectorsWritten,
                backend = result.Backend,
                compatibility = Compatibility(result.Compatibility)
            });
        }
        else
        {
            Console.WriteLine($"created {result.OutputPath}");
            Console.WriteLine($"  pages={result.PagesWritten} shapes={result.ShapesWritten} connectors={result.ConnectorsWritten} bytes={result.FileSize}");
            PrintIssues(result.Compatibility);
        }

        return result.Compatibility.Errors.Any() ? 1 : 0;
    }

    private static int Inspect(Args options, bool json)
    {
        var input = options.Require("input");
        var engine = new VsdxEngine();
        var result = engine.Inspect(input);
        var diagram = result.Read.Diagram;

        if (json)
        {
            Emit(new
            {
                ok = result.Validation.IsValid,
                command = "inspect",
                file = Path.GetFullPath(input),
                bytes = result.Read.FileSize,
                backend = result.Read.Backend,
                backendVersion = result.Read.BackendVersion,
                pages = diagram.Pages.Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    visioId = p.VisioId,
                    size = new { width = p.Size?.Width, height = p.Size?.Height, unit = p.Size?.Unit },
                    nodes = p.Nodes.Count,
                    edges = p.Edges.Count,
                    groups = p.Groups.Count,
                    layers = p.Layers.Select(l => new { l.Name, l.Visible, l.Locked, members = l.Members.Count }),
                    nodesDetail = p.Nodes.Select(n => new
                    {
                        n.Id, n.Text, n.Type, n.Shape, n.GroupId,
                        bounds = new { n.Bounds.X, n.Bounds.Y, n.Bounds.W, n.Bounds.H },
                        shapeData = n.ShapeData,
                        layers = n.Layers
                    }),
                    edgesDetail = p.Edges.Select(e => new { e.Id, e.From, e.To, e.Kind, e.Label, e.EndArrow, e.Layers })
                }),
                masterCount = diagram.Pages.Sum(p => p.Nodes.Count),
                validation = new
                {
                    valid = result.Validation.IsValid,
                    errors = result.Validation.Errors.Select(e => new { e.Code, e.Message, e.Target }),
                    warnings = result.Validation.Warnings.Select(e => new { e.Code, e.Message, e.Target }),
                    checks = result.Validation.Checks.Select(c => new { c.Name, c.Passed, c.Detail })
                },
                compatibility = Compatibility(result.Read.Compatibility)
            });
            return result.Validation.IsValid ? 0 : 1;
        }

        Console.WriteLine($"file      {Path.GetFullPath(input)} ({result.Read.FileSize} bytes)");
        Console.WriteLine($"backend   {result.Read.Backend} {result.Read.BackendVersion}");
        Console.WriteLine($"pages     {diagram.Pages.Count}");
        foreach (var page in diagram.Pages)
        {
            Console.WriteLine($"  - {page.Name} (visio id {page.VisioId}) {page.Size?.Width}x{page.Size?.Height} {page.Size?.Unit}");
            Console.WriteLine($"    nodes={page.Nodes.Count} edges={page.Edges.Count} groups={page.Groups.Count} layers={page.Layers.Count}");
            foreach (var group in page.Groups)
                Console.WriteLine($"    group {group.Id} '{group.Name}' members=[{string.Join(", ", group.Members)}]");
            foreach (var layer in page.Layers)
                Console.WriteLine($"    layer {layer.Name} visible={layer.Visible} locked={layer.Locked} members={layer.Members.Count}");
        }
        Console.WriteLine($"validation {(result.Validation.IsValid ? "PASS" : "FAIL")} " +
                          $"({result.Validation.Checks.Count(c => c.Passed)}/{result.Validation.Checks.Count} checks, " +
                          $"{result.Validation.Errors.Count} errors, {result.Validation.Warnings.Count} warnings)");
        foreach (var error in result.Validation.Errors)
            Console.WriteLine($"  ERROR {error.Code}: {error.Message}");
        PrintIssues(result.Read.Compatibility);

        return result.Validation.IsValid ? 0 : 1;
    }

    private static int ExportModel(Args options, bool json)
    {
        var input = options.Require("input");
        var output = options.Require("output");

        var engine = new VsdxEngine();
        var result = engine.ExportModel(input, output);

        if (json)
        {
            Emit(new
            {
                ok = true,
                command = "export-model",
                input = Path.GetFullPath(input),
                output = Path.GetFullPath(output),
                pages = result.Diagram.Pages.Count,
                nodes = result.Diagram.Pages.Sum(p => p.Nodes.Count),
                edges = result.Diagram.Pages.Sum(p => p.Edges.Count),
                compatibility = Compatibility(result.Compatibility)
            });
        }
        else
        {
            Console.WriteLine($"exported {Path.GetFullPath(output)}");
            Console.WriteLine($"  pages={result.Diagram.Pages.Count} nodes={result.Diagram.Pages.Sum(p => p.Nodes.Count)} edges={result.Diagram.Pages.Sum(p => p.Edges.Count)}");
            PrintIssues(result.Compatibility);
        }
        return 0;
    }

    private static int Update(Args options, bool json)
    {
        var input = options.Require("input");
        var model = options.Require("model");
        var output = options.Require("output");

        var engine = new VsdxEngine();
        var result = engine.Update(input, model, output, new UpdateOptions
        {
            DeleteMissing = options.Has("delete-missing"),
            ValidateAfterSave = !options.Has("no-validate")
        });

        if (json)
        {
            Emit(new
            {
                ok = result.Compatibility.Errors.Any() == false,
                command = "update",
                input = Path.GetFullPath(input),
                model = Path.GetFullPath(model),
                output = result.OutputPath,
                bytes = result.FileSize,
                nodes = new { added = result.NodesAdded, modified = result.NodesModified, deleted = result.NodesDeleted, preserved = result.NodesPreserved },
                edges = new { added = result.EdgesAdded, modified = result.EdgesModified, deleted = result.EdgesDeleted },
                changes = result.Changes.Select(c => new { c.Kind, c.Target, c.Before, c.After, c.Detail }),
                backend = result.Backend,
                compatibility = Compatibility(result.Compatibility)
            });
        }
        else
        {
            Console.WriteLine($"updated {result.OutputPath}");
            Console.WriteLine($"  nodes: +{result.NodesAdded} ~{result.NodesModified} -{result.NodesDeleted} ={result.NodesPreserved} preserved");
            Console.WriteLine($"  edges: +{result.EdgesAdded} ~{result.EdgesModified} -{result.EdgesDeleted}");
            foreach (var change in result.Changes)
                Console.WriteLine($"  {change.Kind,-16} {change.Target}" + (change.Before is not null || change.After is not null
                    ? $"  {change.Before} -> {change.After}" : change.Detail is not null ? $"  {change.Detail}" : ""));
            PrintIssues(result.Compatibility);
        }
        return result.Compatibility.Errors.Any() ? 1 : 0;
    }

    private static int Render(Args options, bool json)
    {
        var input = options.Require("input");
        var format = (options.Get("format") ?? "png").ToLowerInvariant();
        if (format is not ("png" or "svg"))
            throw new UsageException($"unsupported --format '{format}' (expected png or svg)");

        var output = options.Get("output") ?? Path.ChangeExtension(input, "." + format);
        var dpi = options.GetDouble("dpi") ?? 144;

        int? pageIndex = null;
        var pageValue = options.Get("page");
        if (pageValue is not null)
        {
            if (!int.TryParse(pageValue, out var parsed))
                throw new UsageException($"--page must be a zero-based integer, got '{pageValue}'");
            pageIndex = parsed;
        }

        var engine = new VsdxEngine();
        var result = engine.Render(new RenderRequest
        {
            InputPath = input,
            OutputPath = output,
            Format = format,
            PageIndex = pageIndex,
            PageName = options.Get("page-name"),
            Dpi = dpi,
            Background = options.Get("background"),
            Transparent = options.Has("transparent"),
            FontFile = options.Get("font-file"),
            FontFace = options.Get("font-face"),
            RenderText = !options.Has("no-text")
        });

        if (json)
        {
            Emit(new
            {
                ok = result.Compatibility.Errors.Any() == false && result.Pages.Count > 0,
                command = "render",
                input = Path.GetFullPath(input),
                format,
                dpi,
                font = result.FontUsed,
                pages = result.Pages.Select(p => new { p.PageIndex, p.PageName, p.OutputPath, p.Format, p.Bytes, p.Width, p.Height }),
                compatibility = Compatibility(result.Compatibility)
            });
        }
        else
        {
            Console.WriteLine($"rendered {result.Pages.Count} page(s) as {format}");
            if (result.FontUsed is not null) Console.WriteLine($"  font: {result.FontUsed}");
            foreach (var page in result.Pages)
                Console.WriteLine($"  {page.OutputPath} ({page.Bytes} bytes{(page.Width > 0 ? $", {page.Width}x{page.Height}" : "")})");
            PrintIssues(result.Compatibility);
        }
        return result.Compatibility.Errors.Any() || result.Pages.Count == 0 ? 1 : 0;
    }

    private static int Validate(Args options, bool json)
    {
        var input = options.Require("input");
        var engine = new VsdxEngine();
        var result = engine.Validate(input, new ValidationOptions
        {
            Deep = !options.Has("shallow"),
            RenderSmoke = options.Has("render-smoke")
        });

        if (json)
        {
            Emit(new
            {
                ok = result.IsValid,
                command = "validate",
                input = Path.GetFullPath(input),
                valid = result.IsValid,
                errors = result.Errors.Select(e => new { e.Code, e.Message, e.Target }),
                warnings = result.Warnings.Select(e => new { e.Code, e.Message, e.Target }),
                checks = result.Checks.Select(c => new { c.Name, c.Passed, c.Detail })
            });
        }
        else
        {
            Console.WriteLine($"validating {Path.GetFullPath(input)}");
            foreach (var check in result.Checks)
                Console.WriteLine($"  [{(check.Passed ? "PASS" : "FAIL")}] {check.Name,-32} {check.Detail}");
            foreach (var error in result.Errors)
                Console.WriteLine($"  ERROR {error.Code}: {error.Message}");
            foreach (var warning in result.Warnings)
                Console.WriteLine($"  WARN  {warning.Code}: {warning.Message}");
            Console.WriteLine(result.IsValid ? "VALID" : "INVALID");
        }
        return result.IsValid ? 0 : 1;
    }

    private static int Diff(Args options, bool json)
    {
        var before = options.Require("before");
        var after = options.Require("after");

        var engine = new VsdxEngine();
        var diff = engine.Diff(before, after);

        if (json)
        {
            Emit(new
            {
                ok = true,
                command = "diff",
                before = Path.GetFullPath(before),
                after = Path.GetFullPath(after),
                identical = diff.Identical,
                summary = new
                {
                    total = diff.Changes.Count,
                    added = diff.Added.Count(),
                    removed = diff.Removed.Count(),
                    modified = diff.Modified.Count()
                },
                changes = diff.Changes.Select(c => new { c.Kind, c.Target, c.Property, c.Before, c.After, c.Detail })
            });
        }
        else
        {
            Console.WriteLine($"diff {Path.GetFileName(before)} -> {Path.GetFileName(after)}");
            if (diff.Identical) Console.WriteLine("  identical");
            foreach (var change in diff.Changes) Console.WriteLine("  " + change);
            Console.WriteLine($"  total={diff.Changes.Count} added={diff.Added.Count()} removed={diff.Removed.Count()} modified={diff.Modified.Count()}");
        }
        return 0;
    }

    private static int Batch(Args options, bool json)
    {
        var operation = options.Require("op").ToLowerInvariant();

        // validate writes nothing, so it must not demand an output directory.
        var needsOutDir = operation is "create" or "render" or "export-model";
        var outDir = options.Get("out-dir");
        if (needsOutDir)
        {
            if (string.IsNullOrWhiteSpace(outDir))
                throw new UsageException($"batch --op {operation} requires --out-dir");
            Directory.CreateDirectory(outDir);
        }
        outDir ??= ".";

        var inputs = ResolveBatchInputs(options);
        if (inputs.Count == 0) throw new UsageException("batch needs --input <file|dir|glob> matching at least one file");

        var runner = new BatchRunner(new VsdxEngine());
        BatchResult result;

        switch (operation)
        {
            case "create":
                result = runner.CreateMany(inputs.Select(p =>
                    (p, Path.Combine(outDir, Path.GetFileNameWithoutExtension(p) + ".vsdx"))));
                break;

            case "render":
                var format = (options.Get("format") ?? "png").ToLowerInvariant();
                if (format is not ("png" or "svg")) throw new UsageException($"unsupported --format '{format}'");
                var template = new RenderRequest
                {
                    InputPath = "",
                    OutputPath = "",
                    Format = format,
                    Dpi = options.GetDouble("dpi") ?? 144,
                    Background = options.Get("background"),
                    Transparent = options.Has("transparent"),
                    FontFile = options.Get("font-file")
                };
                result = runner.RenderMany(inputs.Select(p =>
                    (p, Path.Combine(outDir, Path.GetFileNameWithoutExtension(p) + "." + format))), template);
                break;

            case "validate":
                result = runner.ValidateMany(inputs);
                break;

            case "export-model":
                result = runner.ExportMany(inputs.Select(p =>
                    (p, Path.Combine(outDir, Path.GetFileNameWithoutExtension(p) + ".json"))));
                break;

            default:
                throw new UsageException($"batch --op must be create|render|validate|export-model, got '{operation}'");
        }

        if (json)
        {
            Emit(new
            {
                ok = result.Ok,
                command = "batch",
                operation = result.Operation,
                total = result.Total,
                succeeded = result.Succeeded,
                failed = result.Failed,
                items = result.Items.Select(i => new { i.Input, i.Output, i.Ok, i.Detail, i.Error, i.Issues })
            });
        }
        else
        {
            Console.WriteLine($"batch {result.Operation}: {result.Succeeded}/{result.Total} ok, {result.Failed} failed");
            foreach (var item in result.Items)
                Console.WriteLine($"  [{(item.Ok ? "OK  " : "FAIL")}] {Path.GetFileName(item.Input),-40} {item.Detail ?? item.Error}");
        }

        return result.Ok ? 0 : 1;
    }

    /// <summary>Expands --input (repeatable), --input-dir and --input-glob into a file list.</summary>
    private static List<string> ResolveBatchInputs(Args options)
    {
        var files = new List<string>();

        foreach (var value in options.GetAll("input"))
        {
            if (Directory.Exists(value))
                files.AddRange(Directory.EnumerateFiles(value, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(IsSupportedInput));
            else if (File.Exists(value))
                files.Add(value);
        }

        var dir = options.Get("input-dir");
        if (dir is not null)
        {
            if (!Directory.Exists(dir)) throw new UsageException($"--input-dir not found: {dir}");
            var pattern = options.Get("input-glob") ?? "*.*";
            files.AddRange(Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly)
                .Where(IsSupportedInput));
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsSupportedInput(string path) =>
        path.EndsWith(".vsdx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static int CheckLayout(Args options, bool json)
    {
        var input = options.Require("input");
        var engine = new VsdxEngine();
        var read = engine.Read(input);
        var report = LayoutAnalyzer.Analyze(read.Diagram);

        if (json)
        {
            Emit(new
            {
                ok = report.HardConstraintsPassed,
                command = "check-layout",
                input = Path.GetFullPath(input),
                // Deliverability depends only on hard constraints; the score is advisory.
                hardConstraintsPassed = report.HardConstraintsPassed,
                score = report.Score,
                clean = report.IsClean,
                counts = new
                {
                    errors = report.Errors.Count(),
                    warnings = report.Warnings.Count(),
                    infos = report.Infos.Count()
                },
                byCode = report.CountByCode(),
                utilisation = report.PageUtilisation,
                edgeDetourRatio = report.EdgeDetourRatio,
                issues = report.Issues.Select(i => new
                {
                    i.Code,
                    i.Severity,
                    i.Page,
                    i.Targets,
                    i.Message,
                    i.RootCause,
                    i.RecommendedFix,
                    level = i.SuggestedLevel.ToString()
                })
            });
        }
        else
        {
            Console.WriteLine($"layout check {Path.GetFullPath(input)}  score={report.Score}/100");
            foreach (var group in report.Issues.GroupBy(i => i.Code).OrderByDescending(g => g.Count()))
            {
                Console.WriteLine($"\n  {group.Key} ({group.Count()})");
                foreach (var issue in group.Take(12))
                    Console.WriteLine($"    [{issue.Severity}] {issue.Message}");
                if (group.Count() > 12) Console.WriteLine($"    ... {group.Count() - 12} more");
            }
            foreach (var (page, ratio) in report.PageUtilisation)
                Console.WriteLine($"\n  page utilisation {page}: {ratio:P0}");
            if (report.Issues.Count == 0) Console.WriteLine("  no layout issues");
        }

        return report.Errors.Any() ? 1 : 0;
    }

    private static int Version(bool json)
    {
        if (json)
        {
            Emit(new
            {
                ok = true,
                tool = "vsdx-tool",
                version = EngineInfo.Version,
                engine = EngineInfo.Name,
                backend = OfficeImoDocumentWriter.BackendName,
                backendVersion = OfficeImoDocumentReader.BackendVersion,
                modelVersion = ModelVersion.Current,
                runtime = Environment.Version.ToString()
            });
        }
        else
        {
            Console.WriteLine($"vsdx-tool {EngineInfo.Version}");
            Console.WriteLine($"  engine         {EngineInfo.Name}");
            Console.WriteLine($"  backend        {OfficeImoDocumentWriter.BackendName} {OfficeImoDocumentReader.BackendVersion}");
            Console.WriteLine($"  model version  {ModelVersion.Current}");
            Console.WriteLine($"  model runtime  .NET {Environment.Version}");
        }
        return 0;
    }

    private static object Compatibility(CompatibilityReport report) => new
    {
        lossless = report.Lossless,
        summary = report.Summary(),
        issues = report.Issues.Select(i => new { i.Severity, i.Code, i.Message, i.Target })
    };

    private static void PrintIssues(CompatibilityReport report)
    {
        foreach (var issue in report.Issues)
            Console.WriteLine($"  {issue.Severity.ToUpperInvariant()} {issue.Code}: {issue.Message}");
        if (report.Issues.Count == 0) Console.WriteLine("  compatibility: lossless");
    }

    private static void Emit(object payload) =>
        Console.WriteLine(JsonSerializer.Serialize(payload, JsonOut));

    private static int Fail(string code, string message)
    {
        if (Environment.GetCommandLineArgs().Contains("--json"))
        {
            Emit(new { ok = false, error = new { code, message } });
        }
        else
        {
            Console.Error.WriteLine($"error [{code}]: {message}");
        }
        return 1;
    }
}

public sealed class UsageException : Exception
{
    public UsageException(string message) : base(message) { }
}

/// <summary>Minimal, dependency-free <c>--flag value</c> / <c>--flag=value</c> parser.</summary>
public sealed class Args
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    public static Args Parse(string[] args)
    {
        var parsed = new Args();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                throw new UsageException($"unexpected argument '{arg}'");

            var body = arg.Substring(2);
            var eq = body.IndexOf('=');
            string name;
            string? value;

            if (eq > 0)
            {
                name = body.Substring(0, eq);
                value = body.Substring(eq + 1);
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                name = body;
                value = args[++i];
            }
            else
            {
                name = body;
                value = null;
            }

            parsed._values[name] = value;
            if (!parsed._all.TryGetValue(name, out var list))
                parsed._all[name] = list = new List<string?>();
            list.Add(value);
        }
        return parsed;
    }

    private readonly Dictionary<string, List<string?>> _all = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All values supplied for a repeatable option, in order.</summary>
    public IReadOnlyList<string> GetAll(string name) =>
        _all.TryGetValue(name, out var list)
            ? list.Where(v => v is not null).Select(v => v!).ToList()
            : Array.Empty<string>();

    public bool Has(string name) => _values.ContainsKey(name);
    public string? Get(string name) => _values.TryGetValue(name, out var v) ? v : null;

    public string Require(string name) =>
        Get(name) ?? throw new UsageException($"missing required option --{name}");

    public double? GetDouble(string name)
    {
        var raw = Get(name);
        if (raw is null) return null;
        return double.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new UsageException($"--{name} must be a number, got '{raw}'");
    }
}

public static class Help
{
    public static void Print(TextWriter writer)
    {
        writer.WriteLine("""
            vsdx-tool — Agent VSDX Engine

            USAGE
              vsdx-tool <command> [options]

            COMMANDS
              create        Create a VSDX from a JSON diagram model
                              --input <model.json> --output <file.vsdx>
                              [--title <text>] [--author <text>] [--no-validate] [--json]
                              [--fit-text] [--fit-page] [--recenter]
                              [--layout layered|grid|flow|tree|manual|auto]

              inspect       Read a VSDX and report its structure
                              --input <file.vsdx> [--json]

              export-model  Export a VSDX to the JSON diagram model
                              --input <file.vsdx> --output <model.json> [--json]

              update        Apply a JSON model onto an existing VSDX (incremental)
                              --input <source.vsdx> --model <model.json> --output <file.vsdx>
                              [--delete-missing] [--no-validate] [--json]

              render        Render a VSDX to PNG or SVG
                              --input <file.vsdx> --format <png|svg> [--output <path>]
                              [--page <n>] [--page-name <name>] [--dpi <n>]
                              [--background <#RRGGBB|transparent>] [--transparent]
                              [--font-file <path>] [--font-face <name>] [--no-text] [--json]

              validate      Validate package structure, OPC and Visio relationships
                              --input <file.vsdx> [--shallow] [--render-smoke] [--json]

              diff          Compare two VSDX packages
                              --before <a.vsdx> --after <b.vsdx> [--json]

              check-layout  Detect overlap, crossing, overflow and containment defects
                              --input <file.vsdx> [--json]

              batch         Run one operation across many files
                              --op <create|render|validate|export-model> --out-dir <dir>
                              --input <file|dir>   (repeatable) or --input-dir <dir> [--input-glob <pattern>]
                              [--format png|svg] [--dpi <n>] [--json]

              version       Print tool, backend and model versions [--json]

            EXIT CODES
              0  success
              1  operation completed with errors (see output)
              2  usage error
            """);
    }
}
