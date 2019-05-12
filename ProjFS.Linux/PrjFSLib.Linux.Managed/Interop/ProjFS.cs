using System;
using System.Runtime.InteropServices;

namespace PrjFSLib.Linux.Interop
{
    internal partial class ProjFS
    {
        private const string PrjFSLibPath = "libprojfs.so";

        private const string ProviderIdAttrName = "vfsforgit.providerid";
        private const string ContentIdAttrName = "vfsforgit.contentid";

        private readonly IntPtr providerIdAttrNamePtr = Marshal.StringToHGlobalAnsi(ProviderIdAttrName);
        private readonly IntPtr contentIdAttrNamePtr = Marshal.StringToHGlobalAnsi(ContentIdAttrName);

        private readonly IntPtr handle;

        private ProjFS(IntPtr handle)
        {
            this.handle = handle;
        }

        public delegate int EventHandler(ref Event ev);

        public static ProjFS New(
            string lowerdir,
            string mountdir,
            Handlers handlers)
        {
            IntPtr handle = _New(
                lowerdir,
                mountdir,
                ref handlers,
                (uint)Marshal.SizeOf<Handlers>(),
                IntPtr.Zero);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            return new ProjFS(handle);
        }

        public int Start()
        {
            return _Start(this.handle);
        }

        public void Stop()
        {
            _Stop(this.handle);
        }

        public Result CreateProjDir(
            string relativePath,
            uint fileMode)
        {
            return _CreateProjDir(
                this.handle,
                relativePath,
                fileMode,
                new Attr[0],
                0).ToResult();
        }

        public Result CreateProjFile(
            string relativePath,
            ulong fileSize,
            uint fileMode,
            byte[] providerId,
            byte[] contentId)
        {
            unsafe
            {
                fixed (byte* providerIdPtr = providerId, contentIdPtr = contentId)
                {
                    Attr[] attrs = new[]
                    {
                        new Attr
                        {
                            Name = (byte*)this.providerIdAttrNamePtr,
                            Value = providerIdPtr,
                            Size = providerId.Length
                        },
                        new Attr
                        {
                            Name = (byte*)this.contentIdAttrNamePtr,
                            Value = contentIdPtr,
                            Size = contentId.Length
                        }
                    };

                    return _CreateProjFile(
                        this.handle,
                        relativePath,
                        fileSize,
                        fileMode,
                        attrs,
                        (uint)attrs.Length).ToResult();
                }
            }
        }

        public Result CreateProjSymlink(
            string relativePath,
            string symlinkTarget)
        {
            return _CreateProjSymlink(
                this.handle,
                relativePath,
                symlinkTarget).ToResult();
        }

        public Result GetProjAttrs(
            string relativePath,
            byte[] providerId,
            byte[] contentId)
        {
            unsafe
            {
                fixed (byte* providerIdPtr = providerId, contentIdPtr = contentId)
                {
                    Attr[] attrs = new[]
                    {
                        new Attr
                        {
                            Name = (byte*)this.providerIdAttrNamePtr,
                            Value = providerIdPtr,
                            Size = providerId.Length
                        },
                        new Attr
                        {
                            Name = (byte*)this.contentIdAttrNamePtr,
                            Value = contentIdPtr,
                            Size = contentId.Length
                        }
                    };

                    return _GetProjAttrs(
                        this.handle,
                        relativePath,
                        attrs,
                        (uint)attrs.Length).ToResult();
                }
            }
        }

        [DllImport(PrjFSLibPath, EntryPoint = "projfs_new")]
        private static extern IntPtr _New(
            string lowerdir,
            string mountdir,
            ref Handlers handlers,
            uint handlers_size,
            IntPtr user_data);

        [DllImport(PrjFSLibPath, EntryPoint = "projfs_start")]
        private static extern int _Start(
            IntPtr fs);

        [DllImport(PrjFSLibPath, EntryPoint = "projfs_stop")]
        private static extern IntPtr _Stop(
            IntPtr fs);

        [DllImport(PrjFSLibPath, EntryPoint = "projfs_create_proj_dir")]
        private static extern int _CreateProjDir(
            IntPtr fs,
            string relativePath,
            uint fileMode,
            Attr[] attrs,
            uint nattrs);

        [DllImport(PrjFSLibPath, EntryPoint = "projfs_create_proj_file")]
        private static extern int _CreateProjFile(
            IntPtr fs,
            string relativePath,
            ulong fileSize,
            uint fileMode,
            Attr[] attrs,
            uint nattrs);

        [DllImport(PrjFSLibPath, EntryPoint = "projfs_create_proj_symlink")]
        private static extern int _CreateProjSymlink(
            IntPtr fs,
            string relativePath,
            string symlinkTarget);

        [DllImport(PrjFSLibPath, EntryPoint = "projfs_get_attrs")]
        private static extern int _GetProjAttrs(
            IntPtr fs,
            string relativePath,
            [In, Out] Attr[] attrs,
            uint nattrs);

        [StructLayout(LayoutKind.Sequential)]
        public struct Event
        {
            public IntPtr Fs;
            public ulong Mask;
            public int Pid;
            public IntPtr Path;
            public IntPtr TargetPath;
            public int Fd;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Handlers
        {
            public EventHandler HandleProjEvent;
            public EventHandler HandleNotifyEvent;
            public EventHandler HandlePermEvent;
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct Attr
        {
            public byte* Name;
            public byte* Value;
            public long Size;
        }
    }
}
