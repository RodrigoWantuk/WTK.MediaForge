using Silk.NET.Shaderc;
using System.Runtime.InteropServices;
using System.Text;

namespace WTK.MediaForge.Graphics.Vulkan;

internal static unsafe class GlslShaderCompiler
{
    private static readonly Shaderc Api = Shaderc.GetApi();

    public static byte[] Compile(string source, ShaderKind kind, string fileName)
    {
        Compiler* compiler = Api.CompilerInitialize();
        CompileOptions* options = Api.CompileOptionsInitialize();

        try
        {
            Api.CompileOptionsSetTargetEnv(options, TargetEnv.Vulkan, (uint)EnvVersion.Vulkan12);

            byte[] sourceBytes = Encoding.UTF8.GetBytes(source);

            fixed (byte* sourcePtr = sourceBytes)
            {
                CompilationResult* result = Api.CompileIntoSpv(
                    compiler,
                    sourcePtr,
                    (nuint)sourceBytes.Length,
                    kind,
                    fileName,
                    "main",
                    options);

                try
                {
                    if (Api.ResultGetCompilationStatus(result) != CompilationStatus.Success)
                    {
                        string error = Api.ResultGetErrorMessageS(result) ?? "Unknown shader compile error.";
                        throw new InvalidOperationException($"Shader compile failed ({fileName}): {error}");
                    }

                    nuint length = Api.ResultGetLength(result);
                    byte* bytes = Api.ResultGetBytes(result);

                    var spirv = new byte[length];
                    Marshal.Copy((nint)bytes, spirv, 0, (int)length);
                    return spirv;
                }
                finally
                {
                    Api.ResultRelease(result);
                }
            }
        }
        finally
        {
            Api.CompileOptionsRelease(options);
            Api.CompilerRelease(compiler);
        }
    }
}
