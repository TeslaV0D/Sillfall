namespace Sillfall.Render;

/// <summary>Loads the shader bytecode that the build embedded into this assembly (see Shaders/Shaders.targets).</summary>
internal static class ShaderLibrary
{
    /// <summary>Returns the bytecode of a shader, for example <c>TestPattern.ps</c>.</summary>
    public static byte[] Load(string name)
    {
        string resourceName = $"Shaders/{name}.cso";
        using Stream stream = typeof(ShaderLibrary).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Shader '{name}' is not embedded in {typeof(ShaderLibrary).Assembly.GetName().Name}. " +
                "Shaders are compiled with d3dcompiler_47.dll while building on Windows. A build made on another system cannot render.");

        byte[] bytecode = new byte[stream.Length];
        stream.ReadExactly(bytecode);
        return bytecode;
    }
}
