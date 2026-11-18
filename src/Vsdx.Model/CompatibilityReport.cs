namespace Vsdx.Model;

/// <summary>Result of an operation that may report compatibility loss.</summary>
public sealed class CompatibilityReport
{
    /// <summary>
    /// True when nothing was lost — that is, no errors and no warnings.
    ///
    /// This used to be `Issues.Count == 0`, which counted informational notes. A clean round trip
    /// therefore reported `lossless: false` while carrying only observations ("wrote 2 text blocks",
    /// "restored shape order"): the flag said the file lost fidelity when it had lost nothing, and the
    /// useful notes read as evidence of damage.
    /// </summary>
    public bool Lossless => Issues.All(i => i.Severity == "info");
    public List<CompatibilityIssue> Issues { get; } = new();

    public void Add(string severity, string code, string message, string? target = null) =>
        Issues.Add(new CompatibilityIssue { Severity = severity, Code = code, Message = message, Target = target });

    public void Info(string code, string message, string? target = null) => Add("info", code, message, target);
    public void Warn(string code, string message, string? target = null) => Add("warning", code, message, target);
    public void Error(string code, string message, string? target = null) => Add("error", code, message, target);

    public IEnumerable<CompatibilityIssue> Warnings => Issues.Where(i => i.Severity == "warning");
    public IEnumerable<CompatibilityIssue> Errors => Issues.Where(i => i.Severity == "error");

    public void Merge(CompatibilityReport? other)
    {
        if (other is null) return;
        foreach (var issue in other.Issues) Issues.Add(issue);
    }

    public string Summary()
    {
        if (Issues.Count == 0) return "lossless";
        int e = Errors.Count(), w = Warnings.Count(), i = Issues.Count - e - w;
        return $"{e} error(s), {w} warning(s), {i} info";
    }
}

public sealed class CompatibilityIssue
{
    public string Severity { get; set; } = "info";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Target { get; set; }
}
