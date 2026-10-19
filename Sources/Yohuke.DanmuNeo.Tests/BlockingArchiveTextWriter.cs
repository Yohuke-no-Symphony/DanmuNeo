namespace Yohuke.DanmuNeo.Tests;

internal sealed class BlockingArchiveTextWriter : StringWriter
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc/>
    public override async Task WriteLineAsync(string? value)
    {
        Entered.TrySetResult();
        await Release.Task;
        await base.WriteLineAsync(value);
    }
}
