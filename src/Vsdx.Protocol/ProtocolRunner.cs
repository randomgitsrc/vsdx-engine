using System.Diagnostics;
using System.Text.Json;
using Vsdx.Adapters;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;

namespace Vsdx.Protocol;

/// <summary>
/// End-to-end execution from the two protocol documents (IR + DesignSpec) to files and a result.
///
/// This is the stable surface an external agent should call. It deliberately wraps the existing
/// engine rather than reimplementing any stage, so behaviour cannot drift between the CLI, the
/// C# API and the protocol path.
/// </summary>
public sealed class ProtocolRunner
{
    private readonly VsdxEngine _engine;

    public ProtocolRunner(VsdxEngine? engine = null) => _engine = engine ?? new VsdxEngine();

    /// <summary>
    /// Executes IR (+ optional DesignSpec) into a VSDX, optionally rendering previews.
    ///
    /// Returns a result describing every stage that ran, every constraint that could not be met,
    /// and a quality block split into hard/soft. It never throws for a design-level problem; only
    /// for I/O failure.
    /// </summary>
    public ExecutionResult Execute(
        DiagramIr ir,
        DesignSpec? spec,
        string outputVsdxPath,
        bool render = false,
        string? renderFormat = "png",
        double dpi = 144)
    {
        var started = Stopwatch.StartNew();
        var result = new ExecutionResult
        {
            IrVersion = ir.IrVersion,
            DesignSpecVersion = spec?.SpecVersion
        };

        try
        {
            // ---- 1. IR validation ----------------------------------------
            var irErrors = IrValidator.Validate(ir);
            if (irErrors.Count > 0)
            {
                result.Errors.AddRange(irErrors.Select(e => new ExecutionError
                {
                    Code = e.Code, Message = e.Message, Target = e.Target
                }));
                result.Status = ExecutionStatus.Failed;
                result.Message = "Diagram IR 校验失败";
                result.DurationMs = started.ElapsedMilliseconds;
                return result;
            }
            result.StagesCompleted.Add("ir.validate");

            // ---- 2. IR → model -------------------------------------------
            var diagram = DiagramIrConverter.ToDiagram(ir);
            result.StagesCompleted.Add("ir.toModel");

            // ---- 3. Design spec ------------------------------------------
            if (spec is not null)
            {
                var application = DesignSpecApplier.Apply(diagram, spec);
                result.AppliedDesignNotes.AddRange(application.Applied);
                result.UnmetConstraints.AddRange(application.Unmet);

                // Fields the loader could not map. Reported rather than dropped: a caller who wrote
                // `canvas.profile` gets no effect, and without this line no explanation either.
                foreach (var field in spec.UnknownFields)
                {
                    result.UnmetConstraints.Add(new UnmetConstraint
                    {
                        Code = "spec.unknown-field",
                        Reason = $"DesignSpec 中不存在字段 '{field}'，已忽略",
                        IsHard = false
                    });
                }

                result.StagesCompleted.Add("design.apply");
            }
            else
            {
                // No spec: still place and size, using engine defaults.
                DiagramSizer.Apply(diagram);
                DiagramLayoutEngine.Apply(diagram, new LayoutOptions(),
                    DiagramLayoutEngine.SuggestStrategy(diagram));
                DiagramLayout.Apply(diagram);
                foreach (var page in diagram.Pages)
                {
                    DiagramSizer.Recenter(page);
                    DiagramSizer.FitPage(page, shrink: true);
                }
                result.StagesCompleted.Add("layout.default");
            }

            OrthogonalRouter.Route(diagram);
            result.StagesCompleted.Add("route");

            // ---- 4. Write VSDX -------------------------------------------
            var write = _engine.Create(diagram, outputVsdxPath, new WriteOptions
            {
                AutoLayout = false,      // geometry already final; do not re-run placement
                ValidateAfterSave = true
            });
            result.Warnings.AddRange(ExecutionResultBuilder.FromCompatibility(write.Compatibility));
            result.OutputFiles.Add(write.OutputPath);
            result.StagesCompleted.Add("vsdx.write");

            // ---- 5. Quality ----------------------------------------------
            var report = LayoutAnalyzer.Analyze(_engine.Read(outputVsdxPath).Diagram);
            result.Quality = ExecutionResultBuilder.FromReport(report);
            result.StagesCompleted.Add("quality.check");

            // ---- 6. Render -----------------------------------------------
            if (render)
            {
                var renderPath = Path.ChangeExtension(outputVsdxPath, "." + (renderFormat ?? "png"));
                var rendered = _engine.Render(new RenderRequest
                {
                    InputPath = outputVsdxPath,
                    OutputPath = renderPath,
                    Format = renderFormat ?? "png",
                    Dpi = dpi
                });

                foreach (var page in rendered.Pages)
                {
                    result.Renders.Add(new RenderOutput
                    {
                        Format = page.Format,
                        Path = page.OutputPath,
                        PageIndex = page.PageIndex,
                        PageName = page.PageName,
                        Bytes = page.Bytes,
                        Width = page.Width,
                        Height = page.Height
                    });
                    result.OutputFiles.Add(page.OutputPath);
                }

                result.Warnings.AddRange(ExecutionResultBuilder.FromCompatibility(rendered.Compatibility));
                result.StagesCompleted.Add("render");
            }

            // ---- 7. Object mapping ---------------------------------------
            result.ObjectMapping = ExecutionResultBuilder.BuildMapping(ir, diagram);

            // ---- 8. Status ------------------------------------------------
            bool anyDegradation = result.Warnings.Any(w =>
                w.Code.Contains("fallback", StringComparison.OrdinalIgnoreCase) ||
                w.Code.Contains("nested-group-applied", StringComparison.OrdinalIgnoreCase));

            bool anyUnmetHard = result.UnmetConstraints.Any(u => u.IsHard);

            result.Status = ExecutionResultBuilder.DeriveStatus(
                anyErrors: result.Errors.Count > 0,
                hardPassed: result.Quality.HardConstraintsPassed,
                anyUnmetHardConstraint: anyUnmetHard,
                anyDegradation: anyDegradation);

            result.Message = result.Status switch
            {
                ExecutionStatus.Succeeded => "执行完成，硬约束全部满足",
                ExecutionStatus.Partial => "执行完成，但存在未满足的硬约束或设计约束",
                ExecutionStatus.Degraded => "执行完成，部分能力以降级方式实现",
                _ => "执行失败"
            };
        }
        catch (Exception ex)
        {
            result.Errors.Add(new ExecutionError
            {
                Code = "execute.exception",
                Message = $"{ex.GetType().Name}: {ex.Message}"
            });
            result.Status = ExecutionStatus.Failed;
            result.Message = "执行过程中发生异常";
        }

        result.DurationMs = started.ElapsedMilliseconds;
        result.CompletedUtc = DateTimeOffset.UtcNow;
        return result;
    }
}

/// <summary>
/// Validates a Diagram IR before any geometry is attempted.
///
/// Catching these early matters: a dangling edge reference would otherwise surface as a writer
/// warning after layout has already run, which is both slower and harder to attribute.
/// </summary>
public static class IrValidator
{
    public static List<ExecutionError> Validate(DiagramIr ir)
    {
        var errors = new List<ExecutionError>();

        if (!ProtocolVersion.IsCompatible(ir.IrVersion, "1"))
        {
            errors.Add(new ExecutionError
            {
                Code = "ir.version",
                Message = $"不支持的 IR 版本 '{ir.IrVersion}'，当前支持 1.x"
            });
        }

        // Validate each page independently; the shorthand form is one implicit page.
        var pages = ir.Pages.Count > 0
            ? ir.Pages.Select(p => (p.Id, p.Nodes, p.Edges, p.Containers, p.TextBlocks, p.Paths)).ToList()
            : new List<(string, List<IrNode>, List<IrEdge>, List<IrContainer>, List<IrTextBlock>, List<IrPath>)>
            {
                (ir.Id, ir.Nodes, ir.Edges, ir.Containers, ir.TextBlocks, ir.Paths)
            };

        var pageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (pageId, _, _, _, _, _) in pages)
        {
            if (string.IsNullOrWhiteSpace(pageId)) continue;
            if (!pageIds.Add(pageId))
            {
                errors.Add(new ExecutionError
                {
                    Code = "ir.page-id-duplicate",
                    Message = $"页面 id 重复：'{pageId}'",
                    Target = pageId
                });
            }
        }

        if (pages.Sum(p => p.Item2.Count) == 0)
            errors.Add(new ExecutionError { Code = "ir.empty", Message = "IR 不含任何节点" });

        foreach (var (pageId, pageNodes, pageEdges, pageContainers, pageText, pagePaths) in pages)
            ValidatePage(pageId, pageNodes, pageEdges, pageContainers, pageText, pagePaths, errors);

        return errors;
    }

    /// <summary>Validates one page's references and hierarchy.</summary>
    private static void ValidatePage(
        string pageId, List<IrNode> irNodes, List<IrEdge> irEdges,
        List<IrContainer> irContainers, List<IrTextBlock> irText, List<IrPath> irPaths,
        List<ExecutionError> errors)
    {
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in irNodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                errors.Add(new ExecutionError { Code = "ir.node-id", Message = "存在缺少 id 的节点" });
                continue;
            }
            if (!nodeIds.Add(node.Id))
            {
                errors.Add(new ExecutionError
                {
                    Code = "ir.node-id-duplicate",
                    Message = $"节点 id 重复：'{node.Id}'",
                    Target = node.Id
                });
            }
        }

        var containerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var container in irContainers)
        {
            if (string.IsNullOrWhiteSpace(container.Id))
            {
                errors.Add(new ExecutionError { Code = "ir.container-id", Message = "存在缺少 id 的容器" });
                continue;
            }
            if (!containerIds.Add(container.Id))
            {
                errors.Add(new ExecutionError
                {
                    Code = "ir.container-id-duplicate",
                    Message = $"容器 id 重复：'{container.Id}'",
                    Target = container.Id
                });
            }
        }

        foreach (var node in irNodes.Where(n => n.ContainerId is not null))
        {
            if (!containerIds.Contains(node.ContainerId!))
            {
                errors.Add(new ExecutionError
                {
                    Code = "ir.container-missing",
                    Message = $"节点 '{node.Id}' 引用不存在的容器 '{node.ContainerId}'",
                    Target = node.Id
                });
            }
        }

        foreach (var container in irContainers.Where(c => c.ParentId is not null))
        {
            if (!containerIds.Contains(container.ParentId!))
            {
                errors.Add(new ExecutionError
                {
                    Code = "ir.parent-missing",
                    Message = $"容器 '{container.Id}' 引用不存在的父容器 '{container.ParentId}'",
                    Target = container.Id
                });
            }
            if (string.Equals(container.ParentId, container.Id, StringComparison.Ordinal))
            {
                errors.Add(new ExecutionError
                {
                    Code = "ir.parent-self",
                    Message = $"容器 '{container.Id}' 不能以自身为父",
                    Target = container.Id
                });
            }
        }

        foreach (var edge in irEdges)
        {
            if (string.IsNullOrWhiteSpace(edge.Id))
                errors.Add(new ExecutionError { Code = "ir.edge-id", Message = "存在缺少 id 的连接" });

            if (!nodeIds.Contains(edge.From))
            {
                errors.Add(new ExecutionError
                {
                    Code = "ir.edge-from-missing",
                    Message = $"连接 '{edge.Id}' 的起点 '{edge.From}' 不存在",
                    Target = edge.Id
                });
            }
            if (!nodeIds.Contains(edge.To))
            {
                errors.Add(new ExecutionError
                {
                    Code = "ir.edge-to-missing",
                    Message = $"连接 '{edge.Id}' 的终点 '{edge.To}' 不存在",
                    Target = edge.Id
                });
            }
        }

        // Text blocks and paths must have ids and usable geometry, otherwise they are dropped or
        // written at a nonsense position. Silently accepting them would be the same class of bug
        // as the dropped fields this validation exists to prevent.
        var extIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in irText)
        {
            if (string.IsNullOrWhiteSpace(text.Id))
            {
                errors.Add(new ExecutionError { Code = "ir.text-id", Message = "存在缺少 id 的文本块" });
                continue;
            }
            if (!extIds.Add(text.Id))
            {
                errors.Add(new ExecutionError
                {
                    Code = "ir.text-id-duplicate",
                    Message = $"文本块 id 重复：'{text.Id}'", Target = text.Id
                });
            }
            if (string.IsNullOrEmpty(text.Text))
            {
                errors.Add(new ExecutionError
                {
                    Code = "ir.text-empty",
                    Message = $"文本块 '{text.Id}' 没有内容", Target = text.Id
                });
            }
            if (text.Box is null)
            {
                errors.Add(new ExecutionError
                {
                    Code = "ir.text-box",
                    Message = $"文本块 '{text.Id}' 缺少位置（box）；文本不参与自动布局", Target = text.Id
                });
            }
        }

        foreach (var path in irPaths)
        {
            if (string.IsNullOrWhiteSpace(path.Id))
            {
                errors.Add(new ExecutionError { Code = "ir.path-id", Message = "存在缺少 id 的路径" });
                continue;
            }
            if (!extIds.Add(path.Id))
            {
                errors.Add(new ExecutionError
                {
                    Code = "ir.path-id-duplicate",
                    Message = $"路径 id 重复：'{path.Id}'", Target = path.Id
                });
            }
            if (path.Rows.Count == 0)
            {
                errors.Add(new ExecutionError
                {
                    Code = "ir.path-no-rows",
                    Message = $"路径 '{path.Id}' 没有任何几何行", Target = path.Id
                });
            }
            if (path.Rows.Count > 0 && !string.Equals(path.Rows[0].Type, "MoveTo", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ExecutionError
                {
                    Code = "ir.path-start",
                    Message = $"路径 '{path.Id}' 的第一行必须是 MoveTo", Target = path.Id
                });
            }
        }

        // Ids are shared: the same id in a node and a text block would collide in z-order.
        foreach (var id in extIds)
        {
            if (nodeIds.Contains(id) || containerIds.Contains(id))
            {
                errors.Add(new ExecutionError
                {
                    Code = "ir.id-collision",
                    Message = $"id '{id}' 同时用于节点/容器与文本块/路径，z-order 无法区分",
                    Target = id
                });
            }
        }

        // Cycles in the container hierarchy would make nesting unresolvable.
        foreach (var container in irContainers)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { container.Id };
            var cursor = container.ParentId;
            while (cursor is not null)
            {
                if (!seen.Add(cursor))
                {
                    errors.Add(new ExecutionError
                    {
                        Code = "ir.parent-cycle",
                        Message = $"容器层级存在环：'{container.Id}'",
                        Target = container.Id
                    });
                    break;
                }
                cursor = irContainers.FirstOrDefault(c => c.Id == cursor)?.ParentId;
            }
        }
    }
}
