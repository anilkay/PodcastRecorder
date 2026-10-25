using Avalonia.Threading;

namespace PodcastRecorder.App.Services;

/// <summary>
/// Waits for an Avalonia dialog on the UI thread, as WPF's ShowDialog does, so the shared view
/// models can keep their synchronous <see cref="IFileDialogs"/> and <see cref="ILiveSettingsDialog"/>.
/// The dispatcher keeps running a nested loop meanwhile, so the window stays responsive.
/// </summary>
internal static class Modal
{
    public static T Wait<T>(Task<T> task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }

        return task.GetAwaiter().GetResult();
    }
}
