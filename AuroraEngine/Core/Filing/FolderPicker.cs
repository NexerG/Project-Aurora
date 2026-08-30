using ArctisAurora.Core.Diagnostics;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using System.Runtime.InteropServices;

namespace ArctisAurora.Core.Filing
{
    // The OS folder dialog, run on its own thread so the engine keeps ticking behind it. The answer
    // comes back through Engine.Post, because controls, settings and windows are the main tick's.
    public static class FolderPicker
    {
        private static readonly LogChannel Log = LogChannel.For("Filing");

        public static bool isOpen { get; private set; }

        // One dialog at a time — a second ask would strand the first one's callback.
        public static void Pick(RenderWindow owner, string startFolder, Action<string> onPicked)
        {
            if (isOpen || onPicked == null) return;
            isOpen = true;

            IntPtr hwnd = owner != null ? owner.os.Hwnd : IntPtr.Zero;

            Thread thread = new Thread(() =>
            {
                string picked = null;
                try { picked = Show(hwnd, startFolder); }
                catch (Exception e) { Log.Warn($"folder picker failed: {e.Message}"); }

                Engine.Post(() =>
                {
                    isOpen = false;
                    onPicked(picked);
                });
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private static string Show(IntPtr owner, string startFolder)
        {
            CoInitializeEx(IntPtr.Zero, apartmentThreaded);
            try
            {
                Guid clsid = fileOpenDialog;
                Guid iid = typeof(IFileDialog).GUID;
                if (CoCreateInstance(ref clsid, IntPtr.Zero, inprocServer, ref iid, out IFileDialog dialog) != 0)
                    return null;

                try
                {
                    if (dialog.GetOptions(out uint options) != 0) return null;
                    dialog.SetOptions(options | pickFolders | forceFileSystem | pathMustExist);
                    SetStartFolder(dialog, startFolder);

                    // Cancelling is an HRESULT, not a failure.
                    if (dialog.Show(owner) != 0) return null;
                    if (dialog.GetResult(out IShellItem item) != 0) return null;

                    try
                    {
                        if (item.GetDisplayName(fileSystemPath, out IntPtr name) != 0) return null;

                        string path = Marshal.PtrToStringUni(name);
                        Marshal.FreeCoTaskMem(name);
                        return path;
                    }
                    finally { Marshal.ReleaseComObject(item); }
                }
                finally { Marshal.ReleaseComObject(dialog); }
            }
            finally { CoUninitialize(); }
        }

        // A folder that is gone simply leaves the dialog wherever it opens itself.
        private static void SetStartFolder(IFileDialog dialog, string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

            Guid iid = typeof(IShellItem).GUID;
            if (SHCreateItemFromParsingName(folder, IntPtr.Zero, ref iid, out IShellItem item) != 0) return;

            dialog.SetFolder(item);
            Marshal.ReleaseComObject(item);
        }

        #region ---- win32 ----
        private static readonly Guid fileOpenDialog = new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");

        // COINIT_APARTMENTTHREADED, CLSCTX_INPROC_SERVER
        private const uint apartmentThreaded = 0x2;
        private const uint inprocServer = 0x1;

        // FOS_PICKFOLDERS, FOS_FORCEFILESYSTEM, FOS_PATHMUSTEXIST
        private const uint pickFolders = 0x20;
        private const uint forceFileSystem = 0x40;
        private const uint pathMustExist = 0x800;

        // SIGDN_FILESYSPATH
        private const uint fileSystemPath = 0x80058000;

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr reserved, uint model);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IFileDialog dialog);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        // The declaration order IS the vtable, so every slot is listed whether it is called or not.
        // Show is IModalWindow's and comes first; inheriting it as a base interface would not pin
        // the layout the way one flat list does.
        [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes();
            void SetFileTypeIndex();
            void GetFileTypeIndex();
            void Advise();
            void Unadvise();
            [PreserveSig] int SetOptions(uint options);
            [PreserveSig] int GetOptions(out uint options);
            void SetDefaultFolder();
            [PreserveSig] int SetFolder([MarshalAs(UnmanagedType.Interface)] IShellItem folder);
            void GetFolder();
            void GetCurrentSelection();
            void SetFileName();
            void GetFileName();
            void SetTitle();
            void SetOkButtonLabel();
            void SetFileNameLabel();
            [PreserveSig] int GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItem item);
            void AddPlace();
            void SetDefaultExtension();
            void Close();
            void SetClientGuid();
            void ClearClientData();
            void SetFilter();
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler();
            void GetParent();
            [PreserveSig] int GetDisplayName(uint form, out IntPtr name);
            void GetAttributes();
            void Compare();
        }
        #endregion
    }
}
