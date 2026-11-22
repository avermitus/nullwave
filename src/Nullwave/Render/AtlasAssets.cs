using System.Numerics;
using Silk.NET.Assimp;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace Nullwave.Render;

/// <summary>Shared atlas for OBJ models. PNG row zero and imported V zero both address the top row.</summary>
internal sealed class AtlasAssets : IDisposable
{
    static readonly string[] Maps = { "Albedo", "Normal", "Roughness", "Emissive" };
    static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Assets");
    readonly GL _gl;
    readonly uint[] _textures = new uint[Maps.Length];
    uint _modelSheet, _modelNormals;

    /// <summary>The fal enemy model sheet (Models.png) is in place.</summary>
    public static bool HasModelSheet => System.IO.File.Exists(Path.Combine(Root, "Textures", "Models.png"));

    public AtlasAssets(GL gl)
    {
        _gl = gl;
        try
        {
            for (int i = 0; i < Maps.Length; i++)
            {
                using var file = System.IO.File.OpenRead(Path.Combine(Root, "Textures", Maps[i] + ".png"));
                var image = ImageResult.FromStream(file, ColorComponents.RedGreenBlueAlpha);
                if (image.Width != 1024 || image.Height != 1024)
                    throw new InvalidDataException($"Expected 1024 square {Maps[i]} atlas.");
                _textures[i] = gl.GenTexture();
                gl.BindTexture(TextureTarget.Texture2D, _textures[i]);
                unsafe
                {
                    var format = Maps[i] is "Albedo" or "Emissive" ? InternalFormat.Srgb8Alpha8 : InternalFormat.Rgba8;
                    fixed (byte* pixels = image.Data)
                        gl.TexImage2D(TextureTarget.Texture2D, 0, format, (uint)image.Width,
                            (uint)image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                }
                gl.GenerateMipmap(TextureTarget.Texture2D);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            }
        }
        catch { Dispose(); throw; }
        if (HasModelSheet)
        {
            _modelSheet = LoadSheet(gl, "Models.png", InternalFormat.Srgb8Alpha8);
            if (System.IO.File.Exists(Path.Combine(Root, "Textures", "ModelsNormal.png")))
                _modelNormals = LoadSheet(gl, "ModelsNormal.png", InternalFormat.Rgba8);
        }
    }

    static unsafe uint LoadSheet(GL gl, string file, InternalFormat format)
    {
        using var stream = System.IO.File.OpenRead(Path.Combine(Root, "Textures", file));
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        fixed (byte* pixels = image.Data)
            gl.TexImage2D(TextureTarget.Texture2D, 0, format, (uint)image.Width, (uint)image.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        gl.GenerateMipmap(TextureTarget.Texture2D);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        return tex;
    }

    /// <summary>A fal enemy model (Assets/Models/enemy_{name}.obj), or null if it isn't there.</summary>
    public static Mesh? TryLoadEnemy(string name)
    {
        if (!HasModelSheet || !System.IO.File.Exists(Path.Combine(Root, "Models", "enemy_" + name + ".obj"))) return null;
        try
        {
            var m = LoadMesh("enemy_" + name);
            // drawn 15 % larger than the unit radius so the detail reads at top-down scale (hitboxes are unchanged)
            var scaled = new System.Numerics.Vector3[m.Positions.Length];
            for (int i = 0; i < scaled.Length; i++) scaled[i] = m.Positions[i] * 1.15f;
            return new Mesh(scaled, m.Normals, m.Materials, m.UV) { GpuStatic = true };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MODEL | enemy_{name} failed to load, using the built-in shape: {ex.Message}");
            return null;
        }
    }

    public void Bind(Shader shader, float intensity)
    {
        for (int i = 0; i < Maps.Length; i++)
        {
            _gl.ActiveTexture(TextureUnit.Texture0 + i);
            _gl.BindTexture(TextureTarget.Texture2D, _textures[i]);
            shader.SetInt("u" + Maps[i] + "Map", i);
        }
        if (_modelSheet != 0)
        {
            _gl.ActiveTexture(TextureUnit.Texture0 + Maps.Length);
            _gl.BindTexture(TextureTarget.Texture2D, _modelSheet);
            shader.SetInt("uModelMap", Maps.Length);
            _gl.ActiveTexture(TextureUnit.Texture0 + Maps.Length + 1);
            _gl.BindTexture(TextureTarget.Texture2D, _modelNormals != 0 ? _modelNormals : _textures[1]);
            shader.SetInt("uModelNormalMap", Maps.Length + 1);
        }
        _gl.ActiveTexture(TextureUnit.Texture0);
        shader.SetFloat("uEmissiveIntensity", intensity);
    }

    public static unsafe Mesh LoadMesh(string name)
    {
        using var api = Assimp.GetApi();
        var flags = PostProcessSteps.Triangulate | PostProcessSteps.GenerateNormals | PostProcessSteps.CalculateTangentSpace
            | PostProcessSteps.FlipUVs | PostProcessSteps.ValidateDataStructure;
        var scene = api.ImportFile(Path.Combine(Root, "Models", name + ".obj"), (uint)flags);
        if (scene == null) throw new InvalidDataException($"Assimp failed to load {name}: {api.GetErrorStringS()}");
        try
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            for (int m = 0; m < scene->MNumMeshes; m++)
            {
                var mesh = scene->MMeshes[m];
                if (mesh->MTextureCoords[0] == null || mesh->MNormals == null || mesh->MTangents == null)
                    throw new InvalidDataException($"Missing UV, normal or tangent data on {name}.");
                for (int f = 0; f < mesh->MNumFaces; f++)
                {
                    var face = mesh->MFaces[f];
                    // a stray line or point left by decimation is skipped rather than rejecting the model
                    if (face.MNumIndices != 3) continue;
                    for (int j = 0; j < 3; j++)
                    {
                        var index = face.MIndices[j];
                        positions.Add(mesh->MVertices[index]);
                        normals.Add(mesh->MNormals[index]);
                        var coord = mesh->MTextureCoords[0][index];
                        uv.Add(new Vector2(coord.X, coord.Y));
                    }
                }
            }
            if (positions.Count == 0) throw new InvalidDataException($"Empty mesh: {name}.");
            return new Mesh(positions.ToArray(), normals.ToArray(), new Mat[positions.Count], uv.ToArray());
        }
        finally { api.ReleaseImport(scene); }
    }

    public void Dispose()
    {
        foreach (uint texture in _textures)
            if (texture != 0) _gl.DeleteTexture(texture);
        if (_modelSheet != 0) _gl.DeleteTexture(_modelSheet);
        if (_modelNormals != 0) _gl.DeleteTexture(_modelNormals);
    }
}
