using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace GlassPulse
{
    internal static class ZstdCodec
    {
        private const string Digest = "8f07e1112ed283e5cd2798833e9a3c32d8961381bc36da04af57a1b0ca9bd40b";
        private static readonly object Gate = new object();
        private static string directory;
        private static IntPtr library;
        [StructLayout(LayoutKind.Sequential)] private struct Buffer { public IntPtr Data; public UIntPtr Size, Position; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Create();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate UIntPtr ContextCall(IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate UIntPtr Decompress(IntPtr context, ref Buffer output, ref Buffer input);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint IsError(UIntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ErrorName(UIntPtr result);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string name, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
        private static Create create;
        private static ContextCall initialize, release;
        private static Decompress decompress;
        private static IsError isError;
        private static ErrorName errorName;

        internal static void Configure(string dataDirectory)
        {
            lock (Gate) { if (directory == null) directory = Path.Combine(Path.GetFullPath(dataDirectory), "native"); }
        }

        private static T Function<T>(string name) where T : class
        {
            IntPtr pointer = GetProcAddress(library, name);
            if (pointer == IntPtr.Zero) throw new InvalidDataException("压缩解码器接口不完整");
            return (T)(object)Marshal.GetDelegateForFunctionPointer(pointer, typeof(T));
        }

        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static void EnsureLoaded()
        {
            lock (Gate)
            {
                if (library != IntPtr.Zero) return;
                if (IntPtr.Size != 8) throw new InvalidDataException("压缩历史读取需要 64 位 Windows 进程");
                if (directory == null) Configure(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"));
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "libzstd-1.5.7-x64.dll");
                if (!File.Exists(path) || Hash(path) != Digest)
                {
                    string temporary = path + ".tmp";
                    using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("GlassPulse.libzstd.deflate"))
                    {
                        if (resource == null) throw new InvalidDataException("未找到内置压缩日志解码器");
                        using (var decoder = new DeflateStream(resource, CompressionMode.Decompress))
                        using (var output = File.Create(temporary)) decoder.CopyTo(output);
                    }
                    if (Hash(temporary) != Digest) throw new InvalidDataException("压缩解码器校验失败");
                    if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                }
                library = LoadLibraryEx(path, IntPtr.Zero, 0x900); // Depend only on this DLL directory and Windows system DLLs.
                if (library == IntPtr.Zero) throw new InvalidDataException("无法载入压缩日志解码器", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
                create = Function<Create>("ZSTD_createDStream");
                initialize = Function<ContextCall>("ZSTD_initDStream");
                release = Function<ContextCall>("ZSTD_freeDStream");
                decompress = Function<Decompress>("ZSTD_decompressStream");
                isError = Function<IsError>("ZSTD_isError");
                errorName = Function<ErrorName>("ZSTD_getErrorName");
            }
        }

        internal static Stream Open(Stream source) { EnsureLoaded(); return new DecodedStream(source); }

        private sealed class DecodedStream : Stream
        {
            private readonly Stream source;
            private readonly byte[] input = new byte[131072], output = new byte[131072];
            private GCHandle inputPin, outputPin;
            private IntPtr context;
            private int inputSize, inputPosition, outputSize, outputPosition;
            private bool eof, disposed;
            private ulong remaining = 1;
            internal DecodedStream(Stream source)
            {
                this.source = source;
                try
                {
                    context = create();
                    if (context == IntPtr.Zero) throw new InvalidDataException("无法分配压缩解码上下文");
                    Check(initialize(context));
                    inputPin = GCHandle.Alloc(input, GCHandleType.Pinned);
                    outputPin = GCHandle.Alloc(output, GCHandleType.Pinned);
                }
                catch { Dispose(); throw; }
            }
            private static void Check(UIntPtr result)
            {
                if (isError(result) != 0) throw new InvalidDataException("压缩日志损坏：" + Marshal.PtrToStringAnsi(errorName(result)));
            }
            private bool Fill()
            {
                while (true)
                {
                    if (inputPosition == inputSize && !eof)
                    {
                        inputSize = source.Read(input, 0, input.Length); inputPosition = 0; eof = inputSize == 0;
                    }
                    if (eof && inputPosition == inputSize)
                    {
                        if (remaining != 0) throw new InvalidDataException("压缩日志尚未写入完整帧");
                        return false;
                    }
                    var ib = new Buffer { Data = inputPin.AddrOfPinnedObject(), Size = (UIntPtr)(uint)inputSize, Position = (UIntPtr)(uint)inputPosition };
                    var ob = new Buffer { Data = outputPin.AddrOfPinnedObject(), Size = (UIntPtr)(uint)output.Length };
                    UIntPtr result = decompress(context, ref ob, ref ib); Check(result); remaining = result.ToUInt64();
                    int previousInput = inputPosition; inputPosition = checked((int)ib.Position.ToUInt64());
                    outputSize = checked((int)ob.Position.ToUInt64()); outputPosition = 0;
                    if (outputSize > 0) return true;
                    if (inputPosition == previousInput && inputPosition < inputSize) throw new InvalidDataException("压缩日志无法继续解码");
                }
            }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (disposed) throw new ObjectDisposedException("ZstdStream");
                if (buffer == null) throw new ArgumentNullException("buffer");
                if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException();
                if (count == 0) return 0;
                if (outputPosition == outputSize && !Fill()) return 0;
                int size = Math.Min(count, outputSize - outputPosition);
                System.Buffer.BlockCopy(output, outputPosition, buffer, offset, size); outputPosition += size; return size;
            }
            protected override void Dispose(bool disposing)
            {
                if (!disposed)
                {
                    disposed = true;
                    if (context != IntPtr.Zero) { release(context); context = IntPtr.Zero; }
                    if (inputPin.IsAllocated) inputPin.Free();
                    if (outputPin.IsAllocated) outputPin.Free();
                    if (disposing) source.Dispose();
                }
                base.Dispose(disposing);
            }
            public override bool CanRead { get { return !disposed; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
    }
}