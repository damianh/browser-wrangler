using BrowserWrangler.Core.Models;

namespace BrowserWrangler.Core.Pipeline;

/// <summary>
/// A step in the URL pipeline that can inspect and mutate a click payload before rule matching.
/// </summary>
public interface IUrlPipelineStep
{
    string Name { get; }

    void Process(ClickPayload payload);
}

/// <summary>
/// Runs a click payload through an ordered list of steps (unwrap, unshorten, substitute, ...).
/// </summary>
public sealed class UrlPipeline
{
    private readonly List<IUrlPipelineStep> _steps = [];

    public IReadOnlyList<IUrlPipelineStep> Steps => _steps;

    public UrlPipeline Add(IUrlPipelineStep step)
    {
        _steps.Add(step);
        return this;
    }

    public void Process(ClickPayload payload)
    {
        foreach (IUrlPipelineStep step in _steps)
        {
            step.Process(payload);
        }
    }
}
