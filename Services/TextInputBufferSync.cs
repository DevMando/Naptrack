using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using RazorConsole.Core.Focus;

namespace Naptrack.Services;

/// <summary>
/// Keeps RazorConsole's keystroke buffer in step with a text box whose value the app changed.
///
/// RazorConsole does not type into a TextInput directly. Its KeyboardEventManager keeps a private
/// buffer for the focused element, appends each keystroke to it, and sends the whole buffer as the
/// new value. That buffer is only re-read from the element when focus moves, so when the app
/// clears the URL box -- on submit, on Esc -- or fills it from history, the box shows the new
/// value while the buffer still holds the old one, and the next paste lands on the end of the
/// previous URL.
///
/// The buffer is not exposed, so it is reached by reflection. Every step is guarded: on a
/// RazorConsole build where the internals have moved, this quietly does nothing and the app keeps
/// the old behaviour rather than failing.
/// </summary>
public class TextInputBufferSync
{
    private const string KeyboardManagerTypeName = "RazorConsole.Core.Input.KeyboardEventManager";

    private readonly IServiceProvider _services;
    private readonly FocusManager _focusManager;

    private ConcurrentDictionary<string, StringBuilder>? _buffers;
    private bool _resolved;

    public TextInputBufferSync(IServiceProvider services, FocusManager focusManager)
    {
        _services = services;
        _focusManager = focusManager;
    }

    /// <summary>
    /// Replaces the focused element's buffer with <paramref name="value"/>. Call it from the input
    /// event handler that changed the value, which runs on the same thread that appends keystrokes
    /// to the buffer, so the two never touch it at once.
    /// </summary>
    public void Reset(string value)
    {
        try
        {
            var key = _focusManager.CurrentFocusKey;
            if (key is null)
                return;

            // Only an existing buffer is rewritten. One that does not exist yet is seeded from the
            // element's current value the moment focus arrives, which is already correct.
            if (Buffers()?.TryGetValue(key, out var buffer) == true)
            {
                buffer.Clear();
                buffer.Append(value);
            }
        }
        catch
        {
            // Best effort: a stale buffer costs a paste that needs clearing by hand, not a crash.
        }
    }

    private ConcurrentDictionary<string, StringBuilder>? Buffers()
    {
        if (_resolved)
            return _buffers;

        _resolved = true;

        var type = typeof(FocusManager).Assembly.GetType(KeyboardManagerTypeName);
        var manager = type is null ? null : _services.GetService(type);
        var field = type?.GetField("_buffers", BindingFlags.Instance | BindingFlags.NonPublic);

        _buffers = manager is null ? null : field?.GetValue(manager) as ConcurrentDictionary<string, StringBuilder>;
        return _buffers;
    }
}
