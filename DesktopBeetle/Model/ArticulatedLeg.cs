#nullable enable
using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;

namespace BeetleDesktop.Native;

/// <summary>Three rigid exoskeleton segments, with a narrow flexible skin at the two actual joints.</summary>
internal sealed class ArticulatedLeg
{
    sealed class Skin
    {
        public readonly MeshGeometry3D Mesh;
        public readonly Point3D[] Rest, Posed;
        public readonly Vector3D[] RestNormals, PosedNormals;
        public readonly int[] First, Second;
        public readonly double[] Blend;
        public readonly double[] AttachmentMotion;

        public Skin(MeshGeometry3D source, Point3D[] joints)
        {
            int count = source.Positions.Count;
            Rest = new Point3D[count]; Posed = new Point3D[count];
            RestNormals = new Vector3D[count]; PosedNormals = new Vector3D[count];
            First = new int[count]; Second = new int[count]; Blend = new double[count];
            AttachmentMotion = new double[count];
            source.Positions.CopyTo(Rest, 0); source.Normals.CopyTo(RestNormals, 0);
            Mesh = new MeshGeometry3D
            {
                Positions = source.Positions, Normals = source.Normals,
                TextureCoordinates = source.TextureCoordinates, TriangleIndices = source.TriangleIndices
            };
            for (int i = 0; i < count; i++)
            {
                double best = double.MaxValue;
                int nearest = 0;
                for (int bone = 0; bone < 3; bone++)
                {
                    Vector3D line = joints[bone + 1] - joints[bone];
                    double t = Math.Clamp(Vector3D.DotProduct(Rest[i] - joints[bone], line) / line.LengthSquared, 0, 1);
                    double distance = (Rest[i] - (joints[bone] + line * t)).LengthSquared;
                    if (distance < best) { best = distance; nearest = bone; }
                }
                First[i] = Second[i] = nearest;
                // The coxa/socket belongs to the trunk. Blend articulation in
                // along the upper leg, rather than rotating its entire root cap.
                double upperAlong = Vector3D.DotProduct(Rest[i] - joints[0], LegMath.Unit(joints[1] - joints[0]));
                AttachmentMotion[i] = nearest == 0 ? LegMath.Smooth(Math.Clamp((upperAlong - .018) / .030, 0, 1)) : 1;
                // The source rig's joint radius .16 / model scale 5 = .032 in native mesh units.
                for (int joint = 1; joint <= 2; joint++)
                {
                    if ((Rest[i] - joints[joint]).Length > .032 || (nearest != joint - 1 && nearest != joint)) continue;
                    Vector3D bisector = LegMath.Unit(joints[joint] - joints[joint - 1]) + LegMath.Unit(joints[joint + 1] - joints[joint]);
                    double along = Vector3D.DotProduct(Rest[i] - joints[joint], LegMath.Unit(bisector));
                    First[i] = joint - 1; Second[i] = joint;
                    Blend[i] = LegMath.Smooth(Math.Clamp((along + .015) / .030, 0, 1));
                }
            }
        }

        public void Pose(Matrix3D[] bones, Matrix3D body)
        {
            for (int i = 0; i < Rest.Length; i++)
            {
                Point3D a = bones[First[i]].Transform(Rest[i]);
                Vector3D n = bones[First[i]].Transform(RestNormals[i]);
                if (First[i] != Second[i])
                {
                    a += (bones[Second[i]].Transform(Rest[i]) - a) * Blend[i];
                    n += (bones[Second[i]].Transform(RestNormals[i]) - n) * Blend[i];
                }
                if (AttachmentMotion[i] < 1)
                {
                    Point3D attached = body.Transform(Rest[i]);
                    Vector3D attachedNormal = body.Transform(RestNormals[i]);
                    a = attached + (a - attached) * AttachmentMotion[i];
                    n = attachedNormal + (n - attachedNormal) * AttachmentMotion[i];
                }
                Posed[i] = a;
                PosedNormals[i] = LegMath.Unit(n);
            }
            // Publish each collection once. Changing individual WPF collection entries sends thousands of change notifications.
            var positions = new Point3DCollection(Posed); positions.Freeze();
            var normals = new Vector3DCollection(PosedNormals); normals.Freeze();
            Mesh.Positions = positions; Mesh.Normals = normals;
        }
    }

    readonly List<Skin> skins = new();
    readonly Point3D[] joints;
    readonly Matrix3D[] bones = new Matrix3D[3];
    readonly Vector3D bend;
    readonly double upperLength, lowerLength;
    public readonly int Side, Pair, Tripod;
    public readonly Point3D NeutralTip;
    public Point3D Hip => joints[0];
    public Vector3D FootOffset => joints[3] - joints[2];
    public Point3D ActualTip { get; private set; }
    public double TargetError { get; private set; }
    public double LengthError { get; private set; }
    public int VertexCount { get; private set; }

    public ArticulatedLeg(int side, int pair)
    {
        Side = side; Pair = pair; Tripod = (pair + (side < 0 ? 0 : 1)) % 2;
        // Measured coxa, knee, tibia/tarsus, claw: source BeetleImportedModelBuilder.cs.
        // Imported measurement X has the opposite sign to the raw FBX's dorsal axis.
        Point3D P(double x, double y, double z) => new(z * side, y - .5, -x);
        joints = pair == 0 ? new[] { P(.025, .625, .090), P(.070, .625, .180), P(.115, .756, .220), P(.148, .895, .229) }
            : pair == 1 ? new[] { P(.012, .510, .098), P(.045, .477, .179), P(.106, .405, .222), P(.155, .248, .282) }
            : new[] { P(.004, .355, .105), P(.020, .285, .179), P(.062, .180, .180), P(.062, .019, .182) };
        upperLength = (joints[1] - joints[0]).Length; lowerLength = (joints[2] - joints[1]).Length;
        Vector3D direction = LegMath.Unit(joints[2] - joints[0]);
        bend = LegMath.Unit(joints[1] - joints[0] - direction * Vector3D.DotProduct(joints[1] - joints[0], direction));
        // Retract the nearly straight source middle/hind legs so each has room for a planted power stroke.
        NeutralTip = joints[3] + new Vector3D(0, pair == 0 ? -.024 : pair == 1 ? .041 : .066, 0);
        ActualTip = NeutralTip;
    }

    public GeometryModel3D Bind(MeshGeometry3D source, Material material)
    {
        var skin = new Skin(source, joints); skins.Add(skin); VertexCount += source.Positions.Count;
        return new GeometryModel3D(skin.Mesh, material) { BackMaterial = material };
    }

    public double Reach(Point3D target, Vector3D footOffset, Matrix3D body)
        => (target - footOffset - body.Transform(Hip)).Length / (upperLength + lowerLength);

    public Point3D PlanReach(Point3D target, Vector3D footOffset, Matrix3D body)
    {
        Point3D hip = body.Transform(Hip);
        Vector3D delta = target - footOffset - hip;
        double maximum = (upperLength + lowerLength) * .91;
        // Preserve the ground/tarsus height when fitting a new contact into the leg's reachable workspace.
        double radius = Math.Sqrt(Math.Max(.000001, maximum * maximum - delta.Z * delta.Z));
        double horizontal = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        if (horizontal > radius)
            target = new Point3D(hip.X + footOffset.X + delta.X * radius / horizontal,
                hip.Y + footOffset.Y + delta.Y * radius / horizontal, target.Z);
        return target;
    }

    public void Solve(Point3D target, Vector3D footOffset, Matrix3D body, double airborne = 0, double kickPhase = 0)
    {
        Point3D hip = body.Transform(Hip);
        Vector3D offset = target - footOffset - hip;
        double length = Math.Clamp(offset.Length, Math.Abs(upperLength - lowerLength) + .0001,
            (upperLength + lowerLength) * .998);
        Vector3D direction = LegMath.Unit(offset);
        Point3D ankle = hip + direction * length;
        Vector3D hint = body.Transform(bend);
        Vector3D pole = hint - direction * Vector3D.DotProduct(hint, direction);
        if (pole.LengthSquared < 1e-8) pole = new Vector3D(Side, 0, 0) - direction * direction.X * Side;
        pole = LegMath.Unit(pole);
        double along = (upperLength * upperLength - lowerLength * lowerLength + length * length) / (2 * length);
        Point3D knee = hip + direction * along + pole * Math.Sqrt(Math.Max(0, upperLength * upperLength - along * along));
        if (airborne > 0)
        {
            // Airborne motion is joint-driven: the upper segment stays fixed to
            // the shell, with the tibia and tarsus flexing around their own joints.
            double weight = Math.Clamp(airborne, 0, 1);
            Vector3D restLower = joints[2] - joints[1];
            Vector3D kneeAxis = LegMath.Unit(Vector3D.CrossProduct(restLower, joints[0] - joints[1]));
            Vector3D ankleAxis = LegMath.Unit(Vector3D.CrossProduct(FootOffset, -restLower));
            var kneeRotation = Matrix3D.Identity;
            kneeRotation.Rotate(new Quaternion(kneeAxis, 27 + Math.Sin(kickPhase) * 17));
            var ankleRotation = Matrix3D.Identity;
            ankleRotation.Rotate(new Quaternion(ankleAxis, 20 + Math.Sin(kickPhase + .9) * 14));
            Vector3D airUpper = body.Transform(joints[1] - joints[0]);
            Vector3D airLower = body.Transform(kneeRotation.Transform(restLower));
            Vector3D airFoot = body.Transform(kneeRotation.Transform(ankleRotation.Transform(FootOffset)));
            Vector3D upper = LegMath.Unit((knee - hip) * (1 - weight) + airUpper * weight) * upperLength;
            Vector3D lower = LegMath.Unit((ankle - knee) * (1 - weight) + airLower * weight) * lowerLength;
            footOffset = LegMath.Unit(footOffset * (1 - weight) + airFoot * weight) * FootOffset.Length;
            knee = hip + upper;
            ankle = knee + lower;
        }
        ActualTip = ankle + footOffset;
        TargetError = airborne >= .999 ? 0 : (ActualTip - target).Length;
        LengthError = Math.Max(Math.Abs((knee - hip).Length - upperLength), Math.Abs((ankle - knee).Length - lowerLength));
        // Solve rotations in trunk-local coordinates, then carry the whole bone
        // with the trunk. A shortest-arc world rotation loses the shell's roll
        // when upside down and twists the root mesh away from its attachment.
        var inverseBody = body; inverseBody.Invert();
        bones[0] = LegMath.Segment(joints[0], joints[1], inverseBody.Transform(hip), inverseBody.Transform(knee));
        bones[1] = LegMath.Segment(joints[1], joints[2], inverseBody.Transform(knee), inverseBody.Transform(ankle));
        bones[2] = LegMath.Segment(joints[2], joints[3], inverseBody.Transform(ankle), inverseBody.Transform(ActualTip));
        for (int i = 0; i < bones.Length; i++) bones[i].Append(body);
        foreach (Skin skin in skins) skin.Pose(bones, body);
    }
}

internal static class LegMath
{
    public static double Smooth(double t) => t * t * (3 - 2 * t);
    public static Vector3D Unit(Vector3D v) => v.LengthSquared < 1e-16 ? new Vector3D(0, 1, 0) : v / v.Length;
    public static Vector3D Rotate(Vector3D v, double degrees)
    {
        double angle = degrees * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
        return new Vector3D(v.X * c - v.Y * s, v.X * s + v.Y * c, v.Z);
    }
    public static Point3D Rotate(Point3D p, double degrees) => (Point3D)Rotate((Vector3D)p, degrees);
    public static Matrix3D Segment(Point3D restA, Point3D restB, Point3D posedA, Point3D posedB)
    {
        Vector3D from = Unit(restB - restA), to = Unit(posedB - posedA);
        Vector3D axis = Vector3D.CrossProduct(from, to);
        double dot = Math.Clamp(Vector3D.DotProduct(from, to), -1, 1);
        var matrix = Matrix3D.Identity;
        if (axis.LengthSquared > 1e-14) matrix.Rotate(new Quaternion(Unit(axis), Math.Acos(dot) * 180 / Math.PI));
        else if (dot < 0) matrix.Rotate(new Quaternion(Unit(Vector3D.CrossProduct(from, new Vector3D(0, 0, 1))), 180));
        matrix.Translate(posedA - matrix.Transform(restA));
        return matrix;
    }
}
