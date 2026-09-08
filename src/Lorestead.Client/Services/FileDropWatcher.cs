using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Galdr.Native;
using Lorestead.Client.Services.Abstractions;

namespace Lorestead.Client.Services;

// A file dragged in from a file manager never reaches the webview's JavaScript on
// WebKitGTK: the DOM drop event fires, but every flavour of its DataTransfer reads
// empty. (The paste half of the same story is webkit.org/b/218519.) The URIs do
// reach GTK, because WebKit asks the source for its uri-list target itself while
// the drag is in flight - so this listens in on that answer and, when the drag is
// released over the window, takes the drop and publishes what was in it. The
// frontend knows *where* from its own dragover events; this supplies *what*.
//
// The shape is wry's (Tauri's webview layer, src/webkitgtk/drag_drop.rs), which is
// the one known to work: observe drag-data-received, remember the URIs, and take
// the drop in drag-drop. Nothing is asked of the drag and nothing is claimed - no
// drag status, no data request, no drop target of our own. Every earlier attempt
// here that did any of those fed WebKit answers it had not asked for, and its own
// drag handling came apart.
//
// Prototype. If it holds up it belongs in Galdr.Native beside the window hooks,
// which is why it reaches the webview only through handles Galdr already exposes.
public sealed class FileDropWatcher : IFileDropWatcher
{
    private const string GObjectLib = "libgobject-2.0.so.0";
    private const string GtkLib = "libgtk-3.so.0";
    private const string GLibLib = "libglib-2.0.so.0";

    // Named for what it carries rather than for the platform, so moving this into
    // Galdr.Native later keeps the same contract with the frontend.
    private const string EventName = "files:dropped";

    private readonly Galdr.Native.Galdr _galdr;
    private readonly IEventService _events;
    private readonly ILoggingService _logger;

    // Delegates are held for the life of the app: GTK keeps the raw pointers, and
    // a collected delegate is a crash the next time a file is dragged in.
    private DragDataReceivedDelegate _dataHandler;
    private GCHandle _dataHandlerHandle;
    private DragSignalDelegate _dropHandler;
    private GCHandle _dropHandlerHandle;
    private DragLeaveDelegate _leaveHandler;
    private GCHandle _leaveHandlerHandle;
    private bool _started;

    // What WebKit's own uri-list request came back with for the drag in flight.
    // GTK emits drag-leave just before drag-drop, so "leaving" is the tell that the
    // next drop belongs to this drag; a leave with no drop is the pointer going away.
    private List<string> _uris;
    private bool _leaving;

    public FileDropWatcher(Galdr.Native.Galdr galdr, IEventService events, ILoggingService logger)
    {
        _galdr = galdr;
        _events = events;
        _logger = logger;
    }

    public void Start()
    {
        if (OperatingSystem.IsLinux() && !_started)
        {
            _started = true;

            try
            {
                IntPtr handle = _galdr.GetNativeHandle(WebviewNativeHandleKind.UIWidget);
                // The handle is whatever this build of the webview library calls its
                // UI widget - the window on some, the web view on others - and the
                // drag signals only reach the web view itself.
                IntPtr webview = FindWebView(handle);

                if (webview == IntPtr.Zero)
                {
                    _logger.Warn("FileDrop", "No web view widget to watch for file drops");
                }
                else
                {
                    _dataHandler = OnDragDataReceived;
                    _dataHandlerHandle = GCHandle.Alloc(_dataHandler);
                    Connect(webview, "drag-data-received", Marshal.GetFunctionPointerForDelegate(_dataHandler));

                    _leaveHandler = OnDragLeave;
                    _leaveHandlerHandle = GCHandle.Alloc(_leaveHandler);
                    Connect(webview, "drag-leave", Marshal.GetFunctionPointerForDelegate(_leaveHandler));

                    _dropHandler = OnDragDrop;
                    _dropHandlerHandle = GCHandle.Alloc(_dropHandler);
                    Connect(webview, "drag-drop", Marshal.GetFunctionPointerForDelegate(_dropHandler));
                }
            }
            catch (Exception ex)
            {
                // A missing symbol, or a GTK4 build with no such signal. The
                // frontend keeps the behaviour it has without this, so the failure
                // costs nothing beyond the log line.
                _logger.Error("FileDrop", "Could not watch the webview for file drops", ex);
            }
        }
    }

    private static void Connect(IntPtr instance, string signal, IntPtr handler)
    {
        g_signal_connect_data(instance, signal, handler, IntPtr.Zero, IntPtr.Zero, 0);
    }

    // Every delivery WebKit requested for itself passes through here too. Only a
    // uri-list is of interest; the rest is whatever WebKit asked for.
    private void OnDragDataReceived(
        IntPtr widget, IntPtr context, int x, int y, IntPtr selectionData, uint info, uint time, IntPtr userData)
    {
        try
        {
            List<string> uris = ReadUris(selectionData);

            if (uris.Count > 0)
            {
                _uris = uris;
            }
        }
        catch (Exception ex)
        {
            // Thrown back into GTK this would take the process down.
            _logger.Error("FileDrop", "Reading the dragged files failed", ex);
        }
    }

    private void OnDragLeave(IntPtr widget, IntPtr context, uint time, IntPtr userData)
    {
        _leaving = true;
    }

    // The drop is taken here rather than left to WebKit, which would open the file
    // in the webview and take the app off screen with it. Returning 1 ends the
    // emission, so WebKit's handler never runs: no navigation, and no DOM drop event
    // - the page hears about this through files:dropped, and the zone the pointer
    // was last over is the one that gets it. A drag that never carried files is
    // passed straight through, and behaves as it always did.
    private int OnDragDrop(IntPtr widget, IntPtr context, int x, int y, uint time, IntPtr userData)
    {
        int handled = 0;

        try
        {
            List<string> uris = _leaving ? _uris : null;
            _leaving = false;
            _uris = null;

            if (uris != null && uris.Count > 0)
            {
                gtk_drag_finish(context, 1, 0, time);
                _events.PublishEvent(EventName, BuildJson(uris));
                handled = 1;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("FileDrop", "Taking the dropped files failed", ex);
        }

        return handled;
    }

    // Walks down from whatever handle the webview library hands back until it finds
    // the WebKitWebView. Returns the handle itself when it is already the web view,
    // and zero when there is no web view under it at all.
    private static IntPtr FindWebView(IntPtr widget)
    {
        IntPtr found = IntPtr.Zero;

        if (widget != IntPtr.Zero)
        {
            if (TypeNameOf(widget) == "WebKitWebView")
            {
                found = widget;
            }
            else if (g_type_check_instance_is_a(widget, gtk_container_get_type()) != 0)
            {
                IntPtr children = gtk_container_get_children(widget);
                IntPtr node = children;

                while (node != IntPtr.Zero && found == IntPtr.Zero)
                {
                    found = FindWebView(Marshal.ReadIntPtr(node));
                    node = Marshal.ReadIntPtr(node, IntPtr.Size);
                }

                g_list_free(children);
            }
        }

        return found;
    }

    private static string TypeNameOf(IntPtr instance)
    {
        string name = "none";

        if (instance != IntPtr.Zero)
        {
            // A GObject instance starts with a GTypeInstance, whose first field is
            // the class, whose first field is the type.
            IntPtr instanceClass = Marshal.ReadIntPtr(instance);

            if (instanceClass != IntPtr.Zero)
            {
                IntPtr namePtr = g_type_name(Marshal.ReadIntPtr(instanceClass));
                name = namePtr == IntPtr.Zero ? "unknown" : Marshal.PtrToStringAnsi(namePtr);
            }
        }

        return name;
    }

    private static List<string> ReadUris(IntPtr selectionData)
    {
        List<string> uris = new List<string>();

        if (selectionData != IntPtr.Zero)
        {
            // Null for a delivery that is not a uri-list at all.
            IntPtr array = gtk_selection_data_get_uris(selectionData);

            if (array != IntPtr.Zero)
            {
                int index = 0;
                IntPtr entry = Marshal.ReadIntPtr(array);

                while (entry != IntPtr.Zero)
                {
                    string uri = Marshal.PtrToStringUTF8(entry);

                    if (!string.IsNullOrEmpty(uri))
                    {
                        uris.Add(uri);
                    }

                    index++;
                    entry = Marshal.ReadIntPtr(array, index * IntPtr.Size);
                }

                g_strfreev(array);
            }
        }

        return uris;
    }

    // Hand-built rather than run through GaldrJson: one array of strings, against a
    // serializer that would want a registered type for it.
    private static string BuildJson(List<string> uris)
    {
        StringBuilder json = new StringBuilder("{\"uris\":[");

        for (int i = 0; i < uris.Count; i++)
        {
            if (i > 0)
            {
                json.Append(',');
            }

            json.Append('"');

            foreach (char character in uris[i])
            {
                if (character == '"' || character == '\\')
                {
                    json.Append('\\').Append(character);
                }
                else if (character < ' ')
                {
                    json.Append("\\u").Append(((int)character).ToString("x4"));
                }
                else
                {
                    json.Append(character);
                }
            }

            json.Append('"');
        }

        return json.Append("]}").ToString();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DragDataReceivedDelegate(
        IntPtr widget, IntPtr context, int x, int y, IntPtr selectionData, uint info, uint time, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DragLeaveDelegate(IntPtr widget, IntPtr context, uint time, IntPtr userData);

    // gboolean is a gint, so this returns int rather than bool: a one-byte bool
    // leaves the rest of the register undefined, and a stray "true" back to GTK
    // would stop the emission and take a drop away from WebKit.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DragSignalDelegate(IntPtr widget, IntPtr context, int x, int y, uint time, IntPtr userData);

    [DllImport(GObjectLib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "g_signal_connect_data")]
    private static extern ulong g_signal_connect_data(
        IntPtr instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string detailedSignal,
        IntPtr handler,
        IntPtr data,
        IntPtr destroyData,
        int connectFlags);

    [DllImport(GtkLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr gtk_selection_data_get_uris(IntPtr selectionData);

    [DllImport(GtkLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void gtk_drag_finish(IntPtr context, int success, int del, uint time);

    [DllImport(GtkLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr gtk_container_get_children(IntPtr container);

    [DllImport(GtkLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr gtk_container_get_type();

    [DllImport(GObjectLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr g_type_name(IntPtr type);

    [DllImport(GObjectLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int g_type_check_instance_is_a(IntPtr instance, IntPtr type);

    [DllImport(GLibLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_list_free(IntPtr list);

    [DllImport(GLibLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_strfreev(IntPtr array);
}
