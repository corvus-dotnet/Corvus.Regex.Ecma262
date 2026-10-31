// <copyright file="RegexEventSource.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics.Tracing;
using System.Threading;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// The library's events. A generated regular expression whose image the library does not read is built from its
/// pattern with the same answers, so nothing else shows it: the event names why, and the counter (on .NET) says how
/// many, so a consumer's tests or telemetry can see that its generated code no longer saves the work it was written to.
/// </summary>
[EventSource(Name = "Corvus.Regex.Ecma262")]
internal sealed class RegexEventSource : EventSource
{
    /// <summary>The library's event source.</summary>
    public static readonly RegexEventSource Log = new();

    private long imagesIgnored;

#if NET
    private PollingCounter? ignoredCounter;
#endif

    private RegexEventSource()
    {
    }

    /// <summary>Gets the number of images ignored in the life of the process.</summary>
    public long ImagesIgnored => Interlocked.Read(ref this.imagesIgnored);

    /// <summary>An image was not read, and the pattern was compiled instead.</summary>
    /// <param name="reason">Why (see <c>RegexImage.Ignored</c>): 1 layout, 2 revision, 3 settings, 4 unknown section, 5 missing section, 6 unknown instruction, 7 damaged.</param>
    /// <param name="reasonName">The name of the reason.</param>
    [Event(1, Level = EventLevel.Warning, Message = "A compiled image was ignored ({1}); the pattern was compiled instead.")]
    public void ImageIgnored(int reason, string reasonName)
    {
        Interlocked.Increment(ref this.imagesIgnored);
        if (this.IsEnabled())
        {
            this.WriteEvent(1, reason, reasonName);
        }
    }

#if NET
    /// <inheritdoc/>
    protected override void OnEventCommand(EventCommandEventArgs command)
    {
        if (command.Command == EventCommand.Enable)
        {
            this.ignoredCounter ??= new PollingCounter("images-ignored", this, () => this.ImagesIgnored)
            {
                DisplayName = "Compiled images ignored",
            };
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        this.ignoredCounter?.Dispose();
        this.ignoredCounter = null;
        base.Dispose(disposing);
    }
#endif
}
