#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace BeetleDesktop.Native;

/// <summary>
/// Native WPF presentation of the project's actual FBX surfaces. The camera looks
/// straight down onto the back. No game engine, browser, or external renderer is used.
/// </summary>
public sealed class BeetleVisual : Viewport3D
{
    sealed class MeshAsset
    {
        public string SourceSha256 { get; set; } = "";
        public List<MeshPart> Parts { get; set; } = new();
    }

    sealed class MeshPart
    {
        public int Id { get; set; }
        public double[] Positions { get; set; } = Array.Empty<double>();
        public double[] Normals { get; set; } = Array.Empty<double>();
        public double[] Uv { get; set; } = Array.Empty<double>();
        public int[] Triangles { get; set; } = Array.Empty<int>();
    }

    static readonly object CacheLock = new();
    static Dictionary<int, MeshGeometry3D>? geometryCache;
    static readonly Dictionary<int, Material> materialCache = new();
    static readonly Dictionary<int, BitmapSource> textureCache = new();
    readonly List<IridescentShellSurface> iridescentSurfaces = new();
    readonly Transform3DGroup bodyTransform = new();
    readonly Transform3DGroup headTransform = new();
    readonly ArticulatedLeg[] legs = new ArticulatedLeg[6];
    readonly GroundedGait gait;
    readonly AxisAngleRotation3D[] antennae = new AxisAngleRotation3D[2];
    readonly AxisAngleRotation3D heading = new(new Vector3D(0, 0, 1), 0);
    readonly AxisAngleRotation3D bodyPitch = new(new Vector3D(1, 0, 0), 0);
    readonly AxisAngleRotation3D bodyRoll = new(new Vector3D(0, 1, 0), 0);
    readonly AxisAngleRotation3D headPitch = new(new Vector3D(1, 0, 0), 0);
    readonly AxisAngleRotation3D headTurn = new(new Vector3D(0, 0, 1), 0);
    readonly ScaleTransform3D bodyScale = new(1, 1, 1);
    readonly TranslateTransform3D bodyShift = new();
    double clock, eat, groom, sleep, held, fight, startled, inspect, overturnedPose;
    bool stillPoseCached;
    Point? stillPosition;
    double stillHeading, stillLookYaw, stillPixelsPerUnit;

    public int PartCount { get; }
    public int TriangleCount { get; }
    public string CurrentAction { get; private set; } = "idle";
    public double HeadingDegrees { get; private set; }
    public bool IsIridescent { get; }
    public int IridescentSurfaceCount => iridescentSurfaces.Count;
    public BeetleFootDiagnostics FootDiagnostics => gait.Diagnostics;
    public double OverturnDegrees => overturnedPose * 180;

    /// <param name="iridescent">Use the menu's rare rainbow shell; false preserves the source RGB textures.</param>
    public BeetleVisual(bool iridescent = false)
    {
        IsIridescent = iridescent;
        Width = Height = 200;
        IsHitTestVisible = false;
        ClipToBounds = false;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
        Camera = new OrthographicCamera(new Point3D(0, 0, 3), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 1.32)
        {
            NearPlaneDistance = .1,
            FarPlaneDistance = 10
        };
        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(147, 151, 156)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(255, 244, 222), new Vector3D(.4, -.65, -1.4)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(142, 188, 218), new Vector3D(-.7, .25, -.7)));

        var root = new Model3DGroup { Transform = new RotateTransform3D(heading) };
        var body = new Model3DGroup();
        bodyTransform.Children.Add(bodyScale);
        bodyTransform.Children.Add(new RotateTransform3D(bodyPitch, Pivot(-.02, .43, 0)));
        bodyTransform.Children.Add(new RotateTransform3D(bodyRoll, Pivot(-.02, .43, 0)));
        bodyTransform.Children.Add(bodyShift);
        body.Transform = bodyTransform;
        root.Children.Add(body);
        var head = new Model3DGroup();
        headTransform.Children.Add(new RotateTransform3D(headPitch, Pivot(.025, .68, 0)));
        headTransform.Children.Add(new RotateTransform3D(headTurn, Pivot(.025, .68, 0)));
        head.Transform = headTransform;
        body.Children.Add(head);

        var geometry = LoadGeometry();
        PartCount = geometry.Count;
        TriangleCount = geometry.Values.Sum(mesh => mesh.TriangleIndices.Count / 3);
        foreach (int id in new[] { 0, 12, 3, 22 }) body.Children.Add(Surface(id, geometry));
        foreach (int id in new[] { 5, 13, 15, 20, 21 }) head.Children.Add(Surface(id, geometry));
        for (int i = 0; i < 2; i++)
        {
            int side = i == 0 ? -1 : 1;
            int id = i == 0 ? 2 : 14;
            antennae[i] = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
            var feeler = new Model3DGroup
            {
                Transform = new RotateTransform3D(antennae[i], new Point3D(side * .047, .258686, -.0744336))
            };
            feeler.Children.Add(Surface(id, geometry));
            head.Children.Add(feeler);
        }
        for (int sideIndex = 0; sideIndex < 2; sideIndex++)
        for (int pair = 0; pair < 3; pair++)
        {
            int side = sideIndex == 0 ? -1 : 1;
            var leg = new ArticulatedLeg(side, pair);
            var group = new Model3DGroup();
            int[] ids = side < 0 ? (pair == 0 ? new[] { 1 } : pair == 1 ? new[] { 16, 10 } : new[] { 6, 18 })
                : (pair == 0 ? new[] { 7, 9 } : pair == 1 ? new[] { 4, 19 } : new[] { 17, 8 });
            foreach (int id in ids) group.Children.Add(leg.Bind(geometry[id], MaterialFor(id)));
            // Skinning already solves the transformed hip to a screen-locked contact; body transforms must not move the foot twice.
            root.Children.Add(group);
            legs[sideIndex * 3 + pair] = leg;
        }
        gait = new GroundedGait(legs);
        scene.Children.Add(root);
        Children.Add(new ModelVisual3D { Content = scene });
        clock = iridescent ? 3.1 : .7;
        Present("idle", 0, 0, 0);
    }

    // Anatomical measurements were made in the source project's imported coordinate system.
    // Its X sign is reversed by that importer; the native FBX export keeps dorsal +Z.
    static Point3D Pivot(double x, double y, double z) => new(-z, y - .5, -x);

    static Dictionary<int, MeshGeometry3D> LoadGeometry()
    {
        lock (CacheLock)
        {
            if (geometryCache != null) return geometryCache;
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "beetle.mesh.json");
            var data = JsonSerializer.Deserialize<MeshAsset>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("The copied beetle mesh is empty.");
            if (data.Parts.Count != 22) throw new InvalidDataException("The beetle must contain its original 22 surfaces.");
            var result = new Dictionary<int, MeshGeometry3D>();
            foreach (MeshPart part in data.Parts)
            {
                var positions = new Point3DCollection(part.Positions.Length / 3);
                var normals = new Vector3DCollection(part.Normals.Length / 3);
                var uv = new PointCollection(part.Uv.Length / 2);
                for (int i = 0; i < part.Positions.Length; i += 3)
                    positions.Add(new Point3D(part.Positions[i], part.Positions[i + 1], part.Positions[i + 2]));
                for (int i = 0; i < part.Normals.Length; i += 3)
                    normals.Add(new Vector3D(part.Normals[i], part.Normals[i + 1], part.Normals[i + 2]));
                for (int i = 0; i < part.Uv.Length; i += 2) uv.Add(new Point(part.Uv[i], part.Uv[i + 1]));
                var mesh = new MeshGeometry3D
                {
                    Positions = positions, Normals = normals, TextureCoordinates = uv,
                    TriangleIndices = new Int32Collection(part.Triangles)
                };
                mesh.Freeze();
                result.Add(part.Id, mesh);
            }
            geometryCache = result;
            return result;
        }
    }

    Model3D Surface(int id, Dictionary<int, MeshGeometry3D> geometry)
    {
        Material original = MaterialFor(id);
        MeshGeometry3D source = geometry[id];
        if (!IsIridescent || (id != 0 && id != 12 && id != 3 && id != 5))
            return new GeometryModel3D(source, original) { BackMaterial = original };
        // Original Part3 and Part5 fuse the horn and shell. The source menu splits
        // them at these same measured boundaries; horns retain their original texture.
        var shellTriangles = new Int32Collection();
        var hornTriangles = new Int32Collection();
        for (int i = 0; i < source.TriangleIndices.Count; i += 3)
        {
            int a = source.TriangleIndices[i], b = source.TriangleIndices[i + 1], c = source.TriangleIndices[i + 2];
            Point3D pa = source.Positions[a], pb = source.Positions[b], pc = source.Positions[c];
            double y = (pa.Y + pb.Y + pc.Y) / 3, z = (pa.Z + pb.Z + pc.Z) / 3;
            bool horn = id == 5 ? y >= .27 : id == 3 && y >= .21 && z >= .09;
            var target = horn ? hornTriangles : shellTriangles;
            target.Add(a); target.Add(b); target.Add(c);
        }
        var iridescence = new IridescentShellSurface(source, shellTriangles, OriginalTexture(id), id == 5);
        iridescentSurfaces.Add(iridescence);
        if (hornTriangles.Count == 0) return iridescence.Model;
        var hornMesh = new MeshGeometry3D
        {
            Positions = source.Positions, Normals = source.Normals,
            TextureCoordinates = source.TextureCoordinates, TriangleIndices = hornTriangles
        };
        hornMesh.Freeze();
        var group = new Model3DGroup();
        group.Children.Add(iridescence.Model);
        group.Children.Add(new GeometryModel3D(hornMesh, original) { BackMaterial = original });
        return group;
    }

    static Material MaterialFor(int id)
    {
        lock (CacheLock)
        {
            if (materialCache.TryGetValue(id, out Material? cached)) return cached;
            bool eye = id == 15 || id == 20;
            bool shell = id == 0 || id == 12 || id == 3 || id == 5;
            var brush = new ImageBrush(OriginalTexture(id));
            brush.Freeze();
            var group = new MaterialGroup();
            // Preserve original RGB pixels. No luminance conversion, tint, color variant,
            // or brightness remapping is applied to normal beetles or non-shell anatomy.
            group.Children.Add(new DiffuseMaterial(brush));
            // Source Toon material uses SpecStrength=.23, SpecPower=64, SpecColor=(1,.8,.45).
            Color glint = eye ? Color.FromArgb(90, 255, 255, 255) : Color.FromArgb(59, 255, 204, 115);
            group.Children.Add(new SpecularMaterial(new SolidColorBrush(glint), eye ? 100 : shell ? 64 : 35));
            group.Freeze();
            materialCache.Add(id, group);
            return group;
        }
    }

    static BitmapSource OriginalTexture(int id)
    {
        lock (CacheLock)
        {
            if (textureCache.TryGetValue(id, out BitmapSource? cached)) return cached;
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Textures", "Part" + id + ".jpg");
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 512;
            bitmap.EndInit();
            bitmap.Freeze();
            textureCache.Add(id, bitmap);
            return bitmap;
        }
    }

    /// <summary>
    /// Animate visual state. Heading is degrees clockwise from screen-up; speed is
    /// desktop pixels per second. Position and dragging remain owned by the host.
    /// </summary>
    public void Present(string action, double speed, double dt, double heading,
        double turnRate = 0, double lookYaw = 0, double travelDistance = double.NaN, Point? desktopPosition = null, bool overturned = false)
    {
        dt = Math.Clamp(dt, 0, .1);
        string mode = (action ?? "idle").ToLowerInvariant();
        double pixelsPerUnit = (ActualWidth > 0 ? ActualWidth : Width) / 1.32;
        bool canCacheStill = mode == "still" && !overturned && Math.Abs(speed) < 1e-7 && Math.Abs(turnRate) < 1e-7;
        // Keep the already rendered geometry. Behavior timers and name settings
        // are updated by the host independently, so the pet can wake normally.
        if (canCacheStill && stillPoseCached && desktopPosition == stillPosition
            && heading == stillHeading && lookYaw == stillLookYaw && pixelsPerUnit == stillPixelsPerUnit)
            return;
        stillPoseCached = false;
        CurrentAction = mode;
        HeadingDegrees = heading;
        this.heading.Angle = -heading;
        double blend = dt <= 0 ? 1 : 1 - Math.Exp(-dt * 8);
        eat = Mix(eat, mode is "eat" or "eating" or "feeding" ? 1 : 0, blend);
        groom = Mix(groom, mode is "groom" or "grooming" ? 1 : 0, blend);
        sleep = Mix(sleep, mode is "sleep" or "sleeping" ? 1 : 0, blend);
        held = Mix(held, mode is "held" or "grabbed" ? 1 : 0, blend);
        fight = Mix(fight, mode is "fight" or "fighting" ? 1 : 0, blend);
        startled = Mix(startled, mode is "startled" ? 1 : 0, blend);
        inspect = Mix(inspect, mode is "inspect" ? 1 : 0, blend);
        overturnedPose = Mix(overturnedPose, overturned || mode == "overturned" ? 1 : 0, blend * .7);
        bool stillTransitionsFinished = canCacheStill && Math.Max(Math.Max(Math.Max(eat, groom), Math.Max(sleep, held)),
            Math.Max(Math.Max(fight, startled), Math.Max(inspect, overturnedPose))) < .0001;
        if (stillTransitionsFinished)
            eat = groom = sleep = held = fight = startled = inspect = overturnedPose = 0;
        clock += mode == "still" ? 0 : dt;
        bodyPitch.Angle = held * 12 - fight * (4 + Math.Sin(clock * 9) * 3) + eat * 3;
        bodyRoll.Angle = overturnedPose * 180 + held * Math.Sin(clock * 3) * 9;
        bodyScale.ScaleX = bodyScale.ScaleY = 1;
        bodyShift.OffsetX = held * Math.Sin(clock * 3.5) * .008;
        bodyShift.OffsetY = 0;
        bodyShift.OffsetZ = 0;
        headPitch.Angle = eat * (5 + Math.Sin(clock * 14) * 5) + fight * (12 + Math.Sin(clock * 10) * 10) + startled * 12;
        headTurn.Angle = Math.Sin(clock * 1.8) * inspect * 5
            + groom * Math.Sin(clock * 6) * 5 + fight * Math.Sin(clock * 9) * 5 - Math.Clamp(lookYaw, -25, 25) * .45;
        for (int i = 0; i < antennae.Length; i++)
        {
            int side = i == 0 ? -1 : 1;
            antennae[i].Angle = (Math.Sin(clock * 3.5 + i * 1.7) * (7 + inspect * 7) + Math.Sin(clock * 8 + i) * 2) * (1 - sleep * .94)
                + side * (sleep * 12 - fight * 14 - startled * 17 + eat * Math.Sin(clock * 17) * 5);
        }
        gait.Update(speed, dt, heading, turnRate, travelDistance, desktopPosition, pixelsPerUnit, bodyTransform.Value,
            clock, held, sleep, groom, fight, eat, startled, mode is "held" or "grabbed" || overturned || mode == "overturned", overturnedPose,
            resting: mode is "still" or "idle" or "inspect");
        foreach (var surface in iridescentSurfaces) surface.Update(bodyTransform.Value, headTransform.Value);
        if (stillTransitionsFinished && gait.IsRestSettled)
        {
            stillPoseCached = true;
            stillPosition = desktopPosition; stillHeading = heading;
            stillLookYaw = lookYaw; stillPixelsPerUnit = pixelsPerUnit;
        }
    }

    static double Mix(double a, double b, double t) => a + (b - a) * t;
}
