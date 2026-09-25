using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Core.Focus;

namespace Naptrack.Services;

/// <summary>
/// Hooks RazorConsole's keyboard loop, for two things it offers no API for: running work that must
/// take turns with input handling, and keyboard shortcuts that work whatever has focus.
///
/// Turn-taking: RazorConsole has no UI thread. Its dispatcher runs every delegate on whichever
/// thread calls it, so keystrokes and clicks render on the keyboard loop while anything repainting
/// from elsewhere -- the downloads list, updated from a timer -- enters Blazor's renderer at the
/// same moment. The renderer is not thread safe, and when the two overlap one side's changes are
/// dropped before they reach the screen: a pasted URL or a cancelled row that does not appear
/// until Tab forces the component to render again.
///
/// Shortcuts: RazorConsole delivers a key only to the focused element, and TextButton takes no
/// extra attributes, so there is nowhere to hang a handler that sees keys pressed on a button.
///
/// The keyboard loop asks its console input whether a key is waiting roughly every 50ms, then
/// reads it. That input is swapped for a proxy: each poll runs the registered work first, and each
/// key is offered to the shortcut handler before RazorConsole sees it. The input interface is
/// internal to RazorConsole, so the swap is made by reflection; if it cannot be made,
/// <see cref="HasHooked"/> stays false and callers fall back to their own timer and focusable
/// buttons.
/// </summary>
public class InputLoopScheduler
{
    private const string InputInterfaceName = "RazorConsole.Core.Input.IConsoleInput";
    private const string InputImplementationName = "RazorConsole.Core.Input.ConsoleInput";

    /// <summary>How recently the loop must have polled to count as alive.</summary>
    private const long AliveWindowMs = 1000;

    private Action? _work;
    private Func<ConsoleKeyInfo, bool>? _shortcuts;
    private long _lastPoll = long.MinValue / 2;

    /// <summary>
    /// True while the keyboard loop is polling through the proxy. Measured, not assumed: this
    /// only turns true once RazorConsole has actually picked the proxy up.
    /// </summary>
    public bool IsPolling => Environment.TickCount64 - Interlocked.Read(ref _lastPoll) < AliveWindowMs;

    /// <summary>
    /// True once the keyboard loop has polled through the proxy at all. Unlike
    /// <see cref="IsPolling"/> it never flips back, so a layout keyed on it cannot flicker when the
    /// loop is briefly busy.
    /// </summary>
    public bool HasHooked { get; private set; }

    /// <summary>
    /// True once the proxy has been registered. The keyboard loop has not necessarily picked it up
    /// yet -- that is <see cref="HasHooked"/> -- but without this it never will.
    /// </summary>
    public bool Installed { get; private set; }

    /// <summary>Sets the work to run on each poll, replacing any previous registration.</summary>
    public void Register(Action work) => _work = work;

    public void Unregister(Action work) => Interlocked.CompareExchange(ref _work, null, work);

    /// <summary>
    /// Sets the shortcut handler. It runs on the keyboard loop for every key before RazorConsole
    /// sees it, and returns true to consume the key.
    /// </summary>
    public void RegisterShortcuts(Func<ConsoleKeyInfo, bool> handler) => _shortcuts = handler;

    public void UnregisterShortcuts(Func<ConsoleKeyInfo, bool> handler) =>
        Interlocked.CompareExchange(ref _shortcuts, null, handler);

    internal void OnPoll()
    {
        Interlocked.Exchange(ref _lastPoll, Environment.TickCount64);
        HasHooked = true;

        try
        {
            _work?.Invoke();
        }
        catch
        {
            // Never into the keyboard loop: it treats some exceptions as fatal and stops reading
            // keys for the rest of the session.
        }
    }

    internal bool TryHandleShortcut(ConsoleKeyInfo key)
    {
        try
        {
            return _shortcuts?.Invoke(key) ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Registers the proxy as RazorConsole's console input. Safe whether this runs before or after
    /// RazorConsole's own registrations: RazorConsole adds its input with TryAdd, which skips when
    /// this is already there, and if it came first, the later registration wins when a single
    /// service is resolved.
    /// </summary>
    public void Install(IServiceCollection services)
    {
        try
        {
            var assembly = typeof(FocusManager).Assembly;
            var inputInterface = assembly.GetType(InputInterfaceName);
            var inputImplementation = assembly.GetType(InputImplementationName);

            if (inputInterface is null || inputImplementation is null)
                return;

            var inner = Activator.CreateInstance(inputImplementation, nonPublic: true);
            if (inner is null)
                return;

            services.AddSingleton(inputInterface, ConsoleInputProxy.Create(inputInterface, inner, this));
            Installed = true;
        }
        catch
        {
            // Internals moved in a newer RazorConsole. Callers keep their fallbacks.
        }
    }
}

/// <summary>
/// Stands in for RazorConsole's console input: polls the scheduler, and filters shortcuts out of
/// the key stream so RazorConsole never sees them.
/// </summary>
public class ConsoleInputProxy : DispatchProxy
{
    private object _inner = null!;
    private InputLoopScheduler _scheduler = null!;
    private MethodInfo _innerKeyAvailable = null!;
    private MethodInfo _innerReadKey = null!;

    /// <summary>
    /// A key already read from the console to check it against the shortcuts, and not one of
    /// them: reported as available, and handed over by the next ReadKey.
    /// </summary>
    private ConsoleKeyInfo? _pending;

    internal static object Create(Type inputInterface, object inner, InputLoopScheduler scheduler)
    {
        var proxy = DispatchProxy.Create(inputInterface, typeof(ConsoleInputProxy));
        var self = (ConsoleInputProxy)proxy;

        self._inner = inner;
        self._scheduler = scheduler;
        self._innerKeyAvailable = inputInterface.GetProperty("KeyAvailable")?.GetMethod
            ?? throw new MissingMemberException(inputInterface.Name, "KeyAvailable");
        self._innerReadKey = inputInterface.GetMethod("ReadKey")
            ?? throw new MissingMemberException(inputInterface.Name, "ReadKey");

        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        return targetMethod.Name switch
        {
            "get_KeyAvailable" => KeyAvailable(),
            "ReadKey" => ReadKey(args),
            _ => Forward(targetMethod, args),
        };
    }

    private bool KeyAvailable()
    {
        _scheduler.OnPoll();

        if (_pending is not null)
            return true;

        // Shortcuts are consumed here, before RazorConsole could deliver them to the focused
        // element. Everything else is held back for the ReadKey that follows.
        while ((bool)Forward(_innerKeyAvailable, null)!)
        {
            var key = (ConsoleKeyInfo)Forward(_innerReadKey, [true])!;

            if (_scheduler.TryHandleShortcut(key))
                continue;

            _pending = key;
            return true;
        }

        return false;
    }

    private object? ReadKey(object?[]? args)
    {
        if (_pending is { } key)
        {
            _pending = null;
            return key;
        }

        return Forward(_innerReadKey, args);
    }

    private object? Forward(MethodInfo method, object?[]? args)
    {
        try
        {
            return method.Invoke(_inner, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // Rethrown unwrapped: the keyboard loop tells a redirected console apart from a
            // transient failure by exception type, and a wrapper would hide that.
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
