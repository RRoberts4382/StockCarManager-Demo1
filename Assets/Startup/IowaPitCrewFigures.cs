using UnityEngine;
using Part = IowaPitWorkMesh.Part;

public sealed partial class TrackPreview
{
    private readonly struct IowaCrewPose
    {
        internal readonly Vector3 Pelvis;
        internal readonly Vector3 Chest;
        internal readonly Vector3 Head;
        internal readonly Vector3 LeftElbow;
        internal readonly Vector3 LeftHand;
        internal readonly Vector3 RightElbow;
        internal readonly Vector3 RightHand;
        internal readonly Vector3 LeftKnee;
        internal readonly Vector3 LeftFoot;
        internal readonly Vector3 RightKnee;
        internal readonly Vector3 RightFoot;

        internal IowaCrewPose(Vector3 pelvis, Vector3 chest, Vector3 head,
            Vector3 leftElbow, Vector3 leftHand, Vector3 rightElbow, Vector3 rightHand,
            Vector3 leftKnee, Vector3 leftFoot, Vector3 rightKnee, Vector3 rightFoot)
        {
            Pelvis = pelvis;
            Chest = chest;
            Head = head;
            LeftElbow = leftElbow;
            LeftHand = leftHand;
            RightElbow = rightElbow;
            RightHand = rightHand;
            LeftKnee = leftKnee;
            LeftFoot = leftFoot;
            RightKnee = rightKnee;
            RightFoot = rightFoot;
        }
    }

    private static readonly IowaCrewPose[] IowaCrewPoses =
    {
        // Headset lead checking the radio with a clipboard in the other hand.
        new IowaCrewPose(
            new Vector3(0f, .88f, 0f), new Vector3(0f, 1.34f, .01f), new Vector3(0f, 1.65f, .03f),
            new Vector3(-.30f, 1.12f, .09f), new Vector3(-.23f, 1.14f, .34f),
            new Vector3(.32f, 1.31f, .04f), new Vector3(.13f, 1.60f, .09f),
            new Vector3(-.12f, .46f, .03f), new Vector3(-.15f, .074f, .04f),
            new Vector3(.13f, .44f, -.07f), new Vector3(.17f, .074f, -.12f)),

        // Tire helper braced on both feet with gloves ready in front.
        new IowaCrewPose(
            new Vector3(0f, .83f, -.04f), new Vector3(0f, 1.28f, .09f), new Vector3(0f, 1.58f, .13f),
            new Vector3(-.31f, 1.11f, .18f), new Vector3(-.26f, .94f, .43f),
            new Vector3(.31f, 1.09f, .19f), new Vector3(.25f, .95f, .44f),
            new Vector3(-.16f, .47f, .13f), new Vector3(-.18f, .074f, .13f),
            new Vector3(.16f, .44f, .01f), new Vector3(.21f, .074f, -.15f)),

        // Tire changer down on one knee with the impact wrench in reach.
        new IowaCrewPose(
            new Vector3(0f, .57f, -.06f), new Vector3(0f, 1.00f, .13f), new Vector3(0f, 1.29f, .20f),
            new Vector3(-.30f, .86f, .28f), new Vector3(-.20f, .73f, .48f),
            new Vector3(.27f, .85f, .26f), new Vector3(.18f, .70f, .38f),
            new Vector3(-.18f, .48f, .24f), new Vector3(-.18f, .074f, .26f),
            new Vector3(.14f, .10f, .015f), new Vector3(.15f, .066f, -.34f)),

        // Jack attendant stooped forward with one foot set back.
        new IowaCrewPose(
            new Vector3(0f, .73f, -.09f), new Vector3(0f, 1.14f, .16f), new Vector3(0f, 1.43f, .22f),
            new Vector3(-.24f, .94f, .24f), new Vector3(-.15f, .69f, .40f),
            new Vector3(.28f, .92f, .22f), new Vector3(.17f, .69f, .42f),
            new Vector3(-.18f, .42f, .15f), new Vector3(-.20f, .074f, .13f),
            new Vector3(.18f, .40f, -.12f), new Vector3(.22f, .074f, -.24f)),

        // Utility crew member checking a glove at chest height.
        new IowaCrewPose(
            new Vector3(.015f, .89f, 0f), new Vector3(0f, 1.36f, .015f), new Vector3(-.015f, 1.67f, .025f),
            new Vector3(-.32f, 1.10f, .035f), new Vector3(-.14f, 1.15f, .30f),
            new Vector3(.30f, 1.09f, .09f), new Vector3(.10f, 1.15f, .30f),
            new Vector3(-.15f, .46f, .05f), new Vector3(-.20f, .074f, .10f),
            new Vector3(.13f, .45f, -.035f), new Vector3(.16f, .074f, -.09f)),
    };

    private static readonly Part[] IowaCrewSkinTones =
    {
        Part.SkinLight, Part.SkinMedium, Part.SkinDark,
    };

    private static void AddIowaPitCrewFigure(IowaPitWorkMesh mesh, Vector3 position, float yaw,
        int pose, float heightScale, int skinTone)
    {
        IowaCrewPose figure = IowaCrewPoses[pose];
        Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
        Vector3 torsoAxis = (figure.Chest - figure.Pelvis).normalized;
        Quaternion lean = Quaternion.FromToRotation(Vector3.up, torsoAxis);
        Vector3 torsoCenter = Vector3.Lerp(figure.Pelvis, figure.Chest, .56f);
        Vector3 highChest = figure.Chest + lean * new Vector3(0f, .035f, 0f);

        Shell(figure.Pelvis, new Vector3(.172f, .125f, .112f), lean, Part.Secondary);
        Shell(torsoCenter, new Vector3(.192f, .265f, .112f), lean, Part.Primary, 8, 4);
        Shell(highChest, new Vector3(.218f, .105f, .108f), lean, Part.Accent, 8, 4);
        Box(torsoCenter + lean * new Vector3(0f, -.105f, .109f),
            new Vector3(.242f, .049f, .027f), lean, Part.Secondary);
        Box(highChest + lean * new Vector3(-.086f, -.018f, .102f),
            new Vector3(.072f, .029f, .014f), lean, Part.Light);
        Limb(highChest + torsoAxis * .053f, figure.Head - Vector3.up * .046f,
            .072f, .068f, Part.Secondary, 6);

        Arm(-1f, figure.LeftElbow, figure.LeftHand);
        Arm(1f, figure.RightElbow, figure.RightHand);
        Leg(-1f, figure.LeftKnee, figure.LeftFoot, false);
        Leg(1f, figure.RightKnee, figure.RightFoot, pose == 2);

        Quaternion headAim = Quaternion.Euler(pose == 2 || pose == 3 ? 13f : 0f,
            pose == 4 ? -14f : 0f, 0f);
        if (pose == 0)
        {
            Part skin = IowaCrewSkinTones[(int)((uint)skinTone % IowaCrewSkinTones.Length)];
            Shell(figure.Head, new Vector3(.092f, .119f, .092f), headAim, skin, 8, 4);
            Shell(figure.Head + Vector3.up * .068f, new Vector3(.098f, .068f, .096f),
                headAim, Part.Primary, 8, 4);
            Box(figure.Head + new Vector3(0f, .051f, .10f),
                new Vector3(.174f, .025f, .13f), headAim, Part.Accent);
            Shell(figure.Head + new Vector3(-.097f, .011f, 0f),
                new Vector3(.026f, .066f, .052f), headAim, Part.DarkMetal);
            Shell(figure.Head + new Vector3(.097f, .011f, 0f),
                new Vector3(.026f, .066f, .052f), headAim, Part.DarkMetal);
            Limb(figure.Head + new Vector3(.11f, -.014f, .02f),
                figure.Head + new Vector3(.067f, -.051f, .119f), .013f, .011f, Part.DarkMetal, 5);
            Shell(figure.Head + new Vector3(.064f, -.051f, .12f),
                new Vector3(.018f, .013f, .018f), headAim, Part.Rubber, 6, 3);

            Quaternion clipboard = Quaternion.Euler(12f, 14f, -13f);
            Vector3 board = figure.LeftHand + new Vector3(0f, .074f, .022f);
            Box(board, new Vector3(.126f, .174f, .016f), clipboard, Part.DarkMetal);
            Box(board + clipboard * new Vector3(0f, -.01f, .01f),
                new Vector3(.095f, .13f, .004f), clipboard, Part.Light);
        }
        else
        {
            Shell(figure.Head, new Vector3(.114f, .133f, .115f), headAim, Part.Primary, 9, 5);
            Shell(figure.Head + headAim * new Vector3(0f, -.077f, .034f),
                new Vector3(.105f, .055f, .091f), headAim, Part.Secondary);
            Shell(figure.Head + headAim * new Vector3(0f, .014f, .092f),
                new Vector3(.102f, .050f, .046f), headAim, Part.Visor, 8, 4);
        }
        if (pose == 2)
        {
            Quaternion wrenchAim = facing * Quaternion.Euler(43f, -7f, 2f);
            mesh.ImpactWrench(World(figure.RightHand + new Vector3(-.006f, -.018f, .026f)), wrenchAim);
        }

        Vector3 World(Vector3 p) => position + facing * (p * heightScale);

        void Box(Vector3 center, Vector3 size, Quaternion rotation, Part part)
        {
            mesh.Box(World(center), size * heightScale, facing * rotation, part);
        }

        void Shell(Vector3 center, Vector3 radii, Quaternion rotation, Part part, int sides = 6, int rings = 3)
        {
            mesh.Ellipsoid(World(center), radii * heightScale, facing * rotation, part, sides, rings);
        }

        void Limb(Vector3 a, Vector3 b, float aRadius, float bRadius, Part part, int sides = 7)
        {
            mesh.Cylinder(World(a), World(b), aRadius * heightScale, bRadius * heightScale, part, sides);
        }

        void Arm(float side, Vector3 elbow, Vector3 hand)
        {
            Vector3 shoulder = highChest + lean * new Vector3(side * .18f, -.023f, -.008f);
            Vector3 forearm = hand - elbow;
            Vector3 wrist = hand - forearm.normalized * .040f;
            Vector3 cuff = hand - forearm.normalized * .066f;
            Quaternion handAlignment = Quaternion.FromToRotation(Vector3.up, forearm.normalized);
            Shell(shoulder, new Vector3(.077f, .078f, .078f), lean, Part.Accent);
            Limb(shoulder, elbow, .082f, .059f, Part.Primary);
            Shell(elbow, new Vector3(.061f, .065f, .061f), handAlignment, Part.Primary);
            Limb(elbow, cuff, .061f, .046f, Part.Secondary);
            Limb(cuff, wrist, .049f, .047f, Part.Accent, 6);
            Shell(hand, new Vector3(.049f, .066f, .047f), handAlignment, Part.DarkMetal);
        }

        void Leg(float side, Vector3 knee, Vector3 foot, bool tucked)
        {
            Vector3 hip = figure.Pelvis + Vector3.right * (side * .097f);
            Vector3 heel = foot + new Vector3(0f, .02f, -.045f);
            Limb(hip, knee, .099f, .070f, Part.Secondary, 8);
            Shell(knee, new Vector3(.073f, .073f, .073f), Quaternion.identity, Part.Secondary);
            Shell(knee + new Vector3(0f, .002f, .049f), new Vector3(.065f, .058f, .029f),
                Quaternion.identity, Part.DarkMetal);
            Limb(knee, heel, .066f, .048f, Part.Primary);
            Quaternion shoeAngle = Quaternion.Euler(tucked ? 15f : 0f, side * 9f, 0f);
            Box(foot + new Vector3(0f, tucked ? .002f : -.0395f, .043f),
                new Vector3(.140f, .069f, .235f), shoeAngle, Part.Rubber);
            Shell(foot + new Vector3(0f, .006f, .047f), new Vector3(.066f, .054f, .109f),
                shoeAngle, Part.DarkMetal);
        }
    }
}
