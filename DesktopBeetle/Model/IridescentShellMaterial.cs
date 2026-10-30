#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace BeetleDesktop.Native;

/// <summary>
/// Native approximation of the menu's BeetleIridescentInput.hlsl ShellReflectance.
/// WPF has no custom 3D fragment shader. A reflectance lookup texture and animated
/// per-vertex lookup coordinates retain the actual ramp, normal/view response,
/// object-space film pattern and original-texture luminance. WPF supplies the light.
/// </summary>
internal sealed class IridescentShellSurface
{
    // These values are the active Part0/Part12/Part3 MenuIridescence material preset,
    // not the older unused .72/-0.62 preset still present in the original asset folder.
    const double RampOffset = .32, AngleShift = .3, AnglePower = .7;
    const double FilmVariation = .045, FilmScale = 1.2, ReflectanceLift = .65;
    const double Saturation = 1.15, DetailStrength = .12;
    static Material? material;
    readonly MeshGeometry3D mesh;
    readonly Vector3D[] normals;
    readonly double[] film, detail;
    readonly bool followsHead;
    Matrix3D previousNormalTransform;
    bool hasTransform;

    public GeometryModel3D Model { get; }

    public IridescentShellSurface(MeshGeometry3D source, Int32Collection triangles, BitmapSource original, bool followsHead)
    {
        this.followsHead = followsHead;
        var samples = new TextureSamples(original);
        normals = new Vector3D[source.Normals.Count];
        film = new double[normals.Length];
        detail = new double[normals.Length];
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = source.Normals[i];
            // Native coordinates -> the model-space coordinates used by the Unity shader.
            Point3D p = source.Positions[i];
            double x = p.X * 5 * FilmScale;
            double y = (.17 + p.Z) * 5 * FilmScale;
            double z = (p.Y + .07) * 5 * FilmScale;
            film[i] = Math.Sin(x * 2.3 + z * 1.7) * Math.Sin(y * 2.1 + z * .8) * FilmVariation;
            var rgb = samples.Linear(source.TextureCoordinates[i].X, source.TextureCoordinates[i].Y);
            detail[i] = rgb.X * .2126 + rgb.Y * .7152 + rgb.Z * .0722;
        }
        mesh = new MeshGeometry3D
        {
            Positions = source.Positions, Normals = source.Normals,
            TriangleIndices = triangles, TextureCoordinates = new PointCollection(normals.Length)
        };
        Model = new GeometryModel3D(mesh, ReflectanceMaterial()) { BackMaterial = ReflectanceMaterial() };
        Update(Matrix3D.Identity, Matrix3D.Identity);
    }

    public void Update(Matrix3D body, Matrix3D head)
    {
        Matrix3D normalTransform = followsHead ? head * body : body;
        // A top-down heading rotation cannot change normal/view incidence. Recalculate
        // only when actual pitch/roll changes, never scroll the colors with elapsed time.
        if (hasTransform && MatrixDifference(normalTransform, previousNormalTransform) < .006) return;
        hasTransform = true;
        previousNormalTransform = normalTransform;
        var coordinates = new PointCollection(normals.Length);
        for (int i = 0; i < normals.Length; i++)
        {
            Vector3D n = normalTransform.Transform(normals[i]);
            double facing = n.Z / Math.Max(.00001, n.Length);
            double grazing = Math.Pow(Math.Clamp(1 - facing, 0, 1), AnglePower);
            coordinates.Add(new Point(Math.Clamp(RampOffset + grazing * AngleShift + film[i], 0, 1), detail[i]));
        }
        mesh.TextureCoordinates = coordinates;
    }

    static double MatrixDifference(Matrix3D a, Matrix3D b) =>
        Math.Abs(a.M11 - b.M11) + Math.Abs(a.M12 - b.M12) + Math.Abs(a.M13 - b.M13) +
        Math.Abs(a.M21 - b.M21) + Math.Abs(a.M22 - b.M22) + Math.Abs(a.M23 - b.M23) +
        Math.Abs(a.M31 - b.M31) + Math.Abs(a.M32 - b.M32) + Math.Abs(a.M33 - b.M33);

    static Material ReflectanceMaterial()
    {
        if (material != null) return material;
        var rampBitmap = new BitmapImage();
        rampBitmap.BeginInit();
        rampBitmap.CacheOption = BitmapCacheOption.OnLoad;
        rampBitmap.UriSource = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Textures", "ShellIridescenceRamp.jpg"));
        rampBitmap.EndInit();
        var ramp = new TextureSamples(rampBitmap);
        const int width = 1024, height = 64;
        var pixels = new byte[width * height * 4];
        for (int x = 0; x < width; x++)
        {
            Vector3D rgb = ramp.Linear((double)x / (width - 1), .5);
            double peak = Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z));
            double recovered = peak + (Math.Max(peak, .72) - peak) * ReflectanceLift;
            rgb *= recovered / Math.Max(peak, .001);
            double luminance = rgb.X * .2126 + rgb.Y * .7152 + rgb.Z * .0722;
            rgb = new Vector3D(
                Math.Clamp(luminance + (rgb.X - luminance) * Saturation, 0, 1),
                Math.Clamp(luminance + (rgb.Y - luminance) * Saturation, 0, 1),
                Math.Clamp(luminance + (rgb.Z - luminance) * Saturation, 0, 1));
            // The shipped menu has hueShift=0, brightness=1, iridescence=1 and white tint.
            for (int y = 0; y < height; y++)
            {
                double textureDetail = 1 + ((double)y / (height - 1) - 1) * DetailStrength;
                int offset = (y * width + x) * 4;
                pixels[offset] = ToSrgb(rgb.Z * textureDetail);
                pixels[offset + 1] = ToSrgb(rgb.Y * textureDetail);
                pixels[offset + 2] = ToSrgb(rgb.X * textureDetail);
                pixels[offset + 3] = 255;
            }
        }
        var lookup = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        lookup.Freeze();
        var brush = new ImageBrush(lookup);
        brush.Freeze();
        var group = new MaterialGroup();
        // WPF's diffuse light approximates the menu's studio probe; tinted specular and
        // a restrained white coat approximate its metallic=1/smoothness=.88/coat=.18 finish.
        group.Children.Add(new DiffuseMaterial(brush));
        group.Children.Add(new SpecularMaterial(brush, 88));
        group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)), 88));
        group.Freeze();
        material = group;
        return material;
    }

    static byte ToSrgb(double linear)
    {
        double value = linear <= .0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - .055;
        return (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
    }

    sealed class TextureSamples
    {
        readonly byte[] pixels;
        readonly int width, height, stride;
        public TextureSamples(BitmapSource source)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            width = converted.PixelWidth; height = converted.PixelHeight; stride = width * 4;
            pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);
        }

        public Vector3D Linear(double u, double v)
        {
            double x = Math.Clamp(u, 0, 1) * (width - 1), y = Math.Clamp(v, 0, 1) * (height - 1);
            int x0 = (int)x, y0 = (int)y;
            int x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
            return (Pixel(x0, y0) * (1 - (x - x0)) + Pixel(x1, y0) * (x - x0)) * (1 - (y - y0)) +
                (Pixel(x0, y1) * (1 - (x - x0)) + Pixel(x1, y1) * (x - x0)) * (y - y0);
        }

        Vector3D Pixel(int x, int y)
        {
            int offset = y * stride + x * 4;
            return new Vector3D(LinearByte(pixels[offset + 2]), LinearByte(pixels[offset + 1]), LinearByte(pixels[offset]));
        }

        static double LinearByte(byte b)
        {
            double value = b / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
    }
}
