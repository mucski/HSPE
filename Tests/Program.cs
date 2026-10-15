using System;
using System.Collections.Generic;
using System.Xml;
using HSPE.Kit.Face;
using HSPE.Kit.Transfer;

internal static class Program
{
    private static int _failed, _passed;

    private static void Check(bool condition, string name)
    {
        if (condition) { _passed++; return; }
        _failed++;
        Console.WriteLine("FAIL: " + name);
    }

    // A <characterInfo> as CharaPoseController.SaveXml writes it (all six modules).
    private const string Sample =
        "<characterInfo optimizeIK=\"False\" crotchCorrection=\"True\" leftAnkleCorrection=\"False\" rightAnkleCorrection=\"False\" uniqueId=\"123\">" +
        "<advancedObjects><object name=\"BodyTop/p_cf_anim/cf_J_Root/cf_J_Hips\" rotW=\"1\" rotX=\"0\" rotY=\"0\" rotZ=\"0\" /><object name=\"missing/bone\" posX=\"0\" posY=\"1\" posZ=\"0\" /></advancedObjects>" +
        "<colliders><collider name=\"a/col\" radius=\"0.1\"><ignoredDynamicBone poseControllerId=\"55\" root=\"x\" /><ignoredDynamicBone2 poseControllerId=\"55\" id=\"BreastL\" /></collider></colliders>" +
        "<dynamicBones><dynamicBone root=\"hair/root\" enabled=\"True\" /></dynamicBones>" +
        "<skinnedMeshes eyesPtn=\"2\"><skinnedMesh name=\"head/face\"><blendShape index=\"3\" weight=\"50\" /></skinnedMesh><skinnedMesh name=\"head/other\"><blendShape index=\"1\" weight=\"10\" /></skinnedMesh></skinnedMeshes>" +
        "<iks />" +
        "<boobs alternativeUpdateMode=\"True\"><left enabled=\"True\" /></boobs>" +
        "</characterInfo>";

    private static List<MeshInfo> SampleMeshes()
    {
        return new List<MeshInfo>
        {
            new MeshInfo { Path = "head/face", Count = 4, Names = "a|b|c|d" },
            new MeshInfo { Path = "head/other", Count = 2, Names = "x|y" },
        };
    }

    private static void TestCaptureKeepsOnlySelectedModules()
    {
        PresetDocument d = PresetDocument.FromCharacterXml(Sample, "character", true, ModuleMask.Bones | ModuleMask.Boobs, SampleMeshes());
        Check(d.Character["advancedObjects"] != null, "capture keeps Bones node");
        Check(d.Character["boobs"] != null, "capture keeps Boobs node");
        Check(d.Character["colliders"] == null, "capture drops Colliders node");
        Check(d.Character["skinnedMeshes"] == null, "capture drops BlendShapes node");
        Check(d.Modules == (ModuleMask.Bones | ModuleMask.Boobs), "Modules records the selection");
        Check(d.Character.GetAttribute("crotchCorrection") == "True", "switches kept with Bones");
        Check(d.Character.HasAttribute("uniqueId") == false, "uniqueId stripped");
        Check(d.Meshes.Count == 0, "no mesh info without BlendShapes");
    }

    private static void TestCaptureWithoutBonesDropsSwitches()
    {
        PresetDocument d = PresetDocument.FromCharacterXml(Sample, "character", true, ModuleMask.DynamicBones, SampleMeshes());
        Check(d.Character.HasAttribute("optimizeIK") == false, "no switches without Bones");
        Check(d.Character.HasAttribute("crotchCorrection") == false, "no crotch switch without Bones");
    }

    private static void TestColliderLinksStripped()
    {
        PresetDocument d = PresetDocument.FromCharacterXml(Sample, "character", true, ModuleMask.Colliders, SampleMeshes());
        XmlElement collider = (XmlElement)d.Character["colliders"].FirstChild;
        Check(collider.ChildNodes.Count == 0, "ignore links removed from colliders");
        Check(collider.GetAttribute("radius") == "0.1", "collider values kept");
    }

    private static void TestRoundTrip()
    {
        PresetDocument d = PresetDocument.FromCharacterXml(Sample, "character", true, ModuleMask.All, SampleMeshes());
        PresetDocument p = PresetDocument.Parse(d.ToXml());
        Check(p != null, "parse own output");
        Check(p != null && p.Kind == "character" && p.IsFemale, "kind/sex round trip");
        Check(p != null && p.Modules == ModuleMask.All, "modules round trip");
        Check(p != null && p.Meshes.Count == 2 && p.Meshes[0].Names == "a|b|c|d", "mesh info round trip");
        Check(p != null && p.Character["skinnedMeshes"] != null, "module nodes round trip");
        Check(PresetDocument.Parse("<something/>") == null, "foreign xml rejected");
        Check(PresetDocument.Parse("not xml at all") == null, "garbage rejected");
        Check(PresetDocument.Parse("<hspePreset version=\"1\"><broken") == null, "truncated preset rejected");
        Check(PresetDocument.Parse(null) == null, "null rejected");
    }

    private static void TestApplyNodeFiltersModulesAndMeshes()
    {
        PresetDocument d = PresetDocument.FromCharacterXml(Sample, "character", true, ModuleMask.All, SampleMeshes());
        List<string> skipped = new List<string>();
        // target: head/face identical, head/other has different shapes
        XmlElement node = d.BuildApplyNode(ModuleMask.BlendShapes | ModuleMask.Bones, path =>
            path == "head/face" ? new MeshInfo { Path = path, Count = 4, Names = "a|b|c|d" }
            : path == "head/other" ? new MeshInfo { Path = path, Count = 3, Names = "x|y|z" } : null, skipped);
        Check(node.Name == "characterInfo", "apply node is a characterInfo");
        Check(node["colliders"] == null && node["boobs"] == null, "unselected modules not in apply node");
        XmlElement meshes = node["skinnedMeshes"];
        Check(meshes != null && meshes.ChildNodes.Count == 1, "mismatching mesh dropped");
        Check(meshes != null && ((XmlElement)meshes.FirstChild).GetAttribute("name") == "head/face", "matching mesh kept");
        Check(meshes != null && meshes.GetAttribute("eyesPtn") == "2", "eye pattern kept");
        Check(skipped.Count == 1 && skipped[0].Contains("head/other"), "skip reported");
        Check(node.GetAttribute("crotchCorrection") == "True", "switches present when Bones applied");
        XmlElement noBones = d.BuildApplyNode(ModuleMask.Boobs, p => null, new List<string>());
        Check(noBones.HasAttribute("crotchCorrection") == false, "switches absent when Bones not applied");
        // the document itself is untouched by building apply nodes
        Check(d.Character["skinnedMeshes"].ChildNodes.Count == 2, "BuildApplyNode does not modify the preset");
    }

    private static void TestCountEntries()
    {
        PresetDocument d = PresetDocument.FromCharacterXml(Sample, "character", true, ModuleMask.All, SampleMeshes());
        XmlElement node = d.BuildApplyNode(ModuleMask.Bones | ModuleMask.DynamicBones, p => null, new List<string>());
        int total;
        int found = PresetDocument.CountEntries(node, ModuleMask.Bones | ModuleMask.DynamicBones,
            (module, key) => key != "missing/bone", out total);
        Check(total == 3, "total counts bones + dynamic bones entries (got " + total + ")");
        Check(found == 2, "found skips the missing bone (got " + found + ")");
    }

    private const string SampleWithPose =
        "<characterInfo crotchCorrection=\"True\"><advancedObjects><object name=\"a\" posX=\"0\" posY=\"0\" posZ=\"0\" /></advancedObjects>" +
        "<studioPose version=\"101\" data=\"AAEC\" /></characterInfo>";

    private static void TestPoseIsOptIn()
    {
        Check((ModuleMask.All & ModuleMask.Pose) == 0, "All (the default) does not include Pose");
        PresetDocument without = PresetDocument.FromCharacterXml(SampleWithPose, "character", true, ModuleMask.All, null);
        Check(without.Character["studioPose"] == null, "pose not captured unless ticked");
        PresetDocument with = PresetDocument.FromCharacterXml(SampleWithPose, "character", true, ModuleMask.All | ModuleMask.Pose, null);
        Check(with.Character["studioPose"] != null && with.Character["studioPose"].GetAttribute("data") == "AAEC", "pose captured when ticked");
        PresetDocument parsed = PresetDocument.Parse(with.ToXml());
        Check(parsed != null && parsed.Character["studioPose"] != null, "pose survives the round trip");
        XmlElement apply = with.BuildApplyNode(ModuleMask.Bones, p => null, new List<string>());
        Check(apply["studioPose"] == null, "pose not applied unless ticked");
        XmlElement applyPose = with.BuildApplyNode(ModuleMask.Pose, p => null, new List<string>());
        Check(applyPose["studioPose"] != null, "pose applied when ticked");
    }

    // ---------------------------------------------------------------- face core

    private const string Recipe =
        "# test\n" +
        "ignore e00_face01\n" +
        "[mood Joy 0]\n" +
        "eyes pattern e01_yorokobi_op 80\n" +
        "mouth pattern k07_egao_op 60\n" +
        "mouth bone *_J_Mouth_L pos 0 0.002 0\n" +
        "[mood Anger 180]\n" +
        "eyes pattern e03_ikari_op 100\n" +
        "brows shape brows bs02_mayuge.e03_ikari_op_mu 40\n" +
        "[mood Pout 90 male]\n" +
        "mouth pattern k00_kiss 100\n" +
        "[vowel A]\n" +
        "mouth pattern k00_def_op 100\n" +
        "[neutral female]\n" +
        "eyes pattern e00_def_op 100\n" +
        "[point BrowL]\n" +
        "bone *_J_Mayu_L\n" +
        "at 0.3 0.2\n" +
        "twin BrowR\n" +
        "x pos -0.003 0 0\n" +
        "y rot 0 0 10\n";

    private static void TestRecipeParse()
    {
        FaceRecipes r = FaceRecipes.Parse(Recipe);
        Check(r.Errors.Count == 0, "recipe parses without errors");
        Check(r.Ignores.Count == 1 && r.Ignores[0] == "e00_face01", "ignore line");
        Check(r.Moods.Count == 3, "three moods");
        Check(r.Moods[0].Name == "Joy" && r.Moods[0].Angle == 0f, "mood name + angle");
        Check(r.Moods[2].Sex == "male", "mood sex");
        ShapeLine s = r.Moods[0].Shapes[1];
        Check(s.Part == FacePart.Mouth && s.Mesh == null && s.Name == "k07_egao_op" && s.Weight == 60f, "pattern line");
        ShapeLine e = r.Moods[1].Shapes[1];
        Check(e.Part == FacePart.Brows && e.Mesh == "brows" && e.Name == "bs02_mayuge.e03_ikari_op_mu", "exact shape line");
        Check(r.Moods[0].Bones.Count == 1 && r.Moods[0].Bones[0].Rotation == false && r.Moods[0].Bones[0].Value.Y == 0.002f, "bone line");
        Check(r.Vowels.Count == 1 && r.Vowels[0].Name == "A", "vowel");
        FacePoint p = r.Points[0];
        Check(p.Name == "BrowL" && p.Bone == "*_J_Mayu_L" && p.Twin == "BrowR" && p.AtX == 0.3f && p.AtY == 0.2f, "point header lines");
        Check(p.X.Rotation == false && p.X.Value.X == -0.003f && p.Y.Rotation && p.Y.Value.Z == 10f, "point axes");
    }

    private static void TestRecipeErrors()
    {
        FaceRecipes r = FaceRecipes.Parse("[mood Joy 0]\neyes pattern e01\nnonsense line\n[mood Bad]\n");
        Check(r.Errors.Count == 3, "bad pattern line, unknown line, header without angle -> 3 errors");
        Check(r.Moods.Count == 1, "valid entries still load");
    }

    private static void TestBoneMatches()
    {
        Check(FaceRecipes.BoneMatches("*_J_Mayu_L", "cf_J_Mayu_L"), "wildcard female");
        Check(FaceRecipes.BoneMatches("*_J_Mayu_L", "cm_J_Mayu_L"), "wildcard male");
        Check(FaceRecipes.BoneMatches("*_J_Mayu_L", "cf_J_Mayu_R") == false, "wildcard wrong bone");
        Check(FaceRecipes.BoneMatches("mod_brow", "mod_brow") && FaceRecipes.BoneMatches("mod_brow", "mod_brow2") == false, "exact names");
    }

    private static void TestPatternKeys()
    {
        string[] brows = FaceRecipes.Keys(new[] { "bs02_mayuge.e00_def_cl_mu", "bs02_mayuge.e00_def_op_mu", "bs02_mayuge.e07_egao_mu" });
        Check(brows[0] == "e00_def_cl" && brows[2] == "e07_egao", "female brows: common _mu stripped");
        string[] teeth = FaceRecipes.Keys(new[] { "bs06_ha.k00_def_cl_ha", "bs06_ha.k01_yoyokobi_op_ha" });
        Check(teeth[1] == "k01_yoyokobi_op", "teeth: _ha stripped");
        string[] head = FaceRecipes.Keys(new[] { "bs00_eye.e00_def_cl", "bs00_eye.e00_def_op", "bs00_eye.e07_egao" });
        Check(head[0] == "e00_def_cl" && head[2] == "e07_egao", "head eyes: nothing stripped");
        string[] male = FaceRecipes.Keys(new[] { "bs02_kuchi.k00_defo", "bs02_kuchi.k00_defo_op", "bs02_kuchi.k00_smile1_op" });
        Check(male[1] == "k00_defo_op", "male mouth: nothing stripped");
        string[] one = FaceRecipes.Keys(new[] { "bs04_sita.k00_defo" });
        Check(one[0] == "k00_defo", "single-name group");
    }

    private static bool Near(float a, float b) { return Math.Abs(a - b) < 0.0001f; }

    private static void TestPadWeights()
    {
        FaceRecipes r = FaceRecipes.Parse(Recipe);
        List<FaceEntry> moods = new List<FaceEntry>();
        FaceMix.Moods(r, "female", moods);
        Check(moods.Count == 2, "male-only mood filtered out for females");
        int a, b; float wa, wb;
        FaceMix.PadWeights(moods, 0f, 1f, out a, out b, out wa, out wb);
        Check(moods[a].Name == "Joy" && Near(wa, 1f) && Near(wb, 0f), "on a mood = that mood");
        FaceMix.PadWeights(moods, 90f, 1f, out a, out b, out wa, out wb);
        Check(Near(wa, 0.5f) && Near(wb, 0.5f), "halfway = half each");
        FaceMix.PadWeights(moods, 270f, 0.5f, out a, out b, out wa, out wb);
        Check(Near(wa + wb, 0.5f), "radius scales the total (wrap-around 180 -> 360)");
        FaceMix.PadWeights(moods, 45f, 0f, out a, out b, out wa, out wb);
        Check(wa == 0f && wb == 0f, "centre = neutral");
    }

    private static void TestMix()
    {
        FaceRecipes r = FaceRecipes.Parse(Recipe);
        FaceResult res = new FaceResult();
        FaceInput input = new FaceInput { Angle = 0f, Radius = 1f };
        FaceMix.Mix(r, "female", input, res);
        Check(Near(res.Patterns["eyes|e01_yorokobi_op"], 80f) && Near(res.Patterns["mouth|k07_egao_op"], 60f), "mood patterns");
        Check(Near(res.BonePos["*_J_Mouth_L"].Y, 0.002f), "mood bone");
        Check(res.UsesEyes && res.UsesMouth && res.UsesBrows == false, "parts used");
        Check(res.Patterns.ContainsKey("eyes|e00_def_op") == false, "full mood: no neutral");
        input.Radius = 0.25f;
        FaceMix.Mix(r, "female", input, res);
        Check(Near(res.Patterns["eyes|e01_yorokobi_op"], 20f) && Near(res.Patterns["eyes|e00_def_op"], 75f), "quarter mood: neutral fills the rest");
        Check(FaceRecipes.Parse(Recipe).Neutrals.Count == 1, "neutral entry parsed");
        input.Radius = 1f;
        input.Angle = 180f;
        FaceMix.Mix(r, "female", input, res);
        Check(Near(res.Shapes["brows|bs02_mayuge.e03_ikari_op_mu"], 40f) && res.UsesBrows, "exact shape + brows part");
        input.Angle = 0f;
        input.Vowel = "A";
        FaceMix.Mix(r, "female", input, res);
        Check(res.Patterns.ContainsKey("mouth|k07_egao_op") == false && res.BonePos.ContainsKey("*_J_Mouth_L") == false, "vowel drops the mood's mouth lines");
        Check(Near(res.Patterns["eyes|e01_yorokobi_op"], 80f) && Near(res.Patterns["mouth|k00_def_op"], 100f), "vowel keeps eyes, adds its mouth");
        input.Vowel = null;
        input.Radius = 0f;
        input.Drags["BrowL"] = new[] { 0.5f, -1f };
        FaceMix.Mix(r, "female", input, res);
        Check(res.Patterns.Count == 0 && res.UsesEyes == false, "centre pad = no shapes");
        Check(Near(res.BonePos["*_J_Mayu_L"].X, -0.0015f) && Near(res.BoneRot["*_J_Mayu_L"].Z, -10f), "drag adds scaled axis offsets");
        float[] m = FaceMix.MirrorDrag(new[] { 0.5f, -1f });
        Check(m[0] == -0.5f && m[1] == -1f, "mirror flips x only");

        // extra (runtime) points, neutral check, clone / copy for undo
        input.Drags.Clear();
        Check(input.IsNeutral, "radius 0, no vowel, no drags = neutral");
        input.ExtraPoints.Add(new FacePoint { Name = "x:mod_bone", Bone = "mod_bone", X = new BoneLine { Value = new V3(0.004f, 0f, 0f) } });
        input.Drags["x:mod_bone"] = new[] { 0.5f, 0f };
        Check(input.IsNeutral == false, "a drag is not neutral");
        FaceMix.Mix(r, "female", input, res);
        Check(Near(res.BonePos["mod_bone"].X, 0.002f), "extra point drags its bone");
        FaceInput c = input.Clone();
        input.Drags["x:mod_bone"][0] = 1f;
        Check(c.Drags["x:mod_bone"][0] == 0.5f, "clone copies drag arrays");
        input.CopyFrom(c);
        Check(input.Drags["x:mod_bone"][0] == 0.5f && input.ExtraPoints.Count == 1, "CopyFrom restores values, keeps points");
    }

    private const string OpenCloseRecipe =
        "[neutral]\n" +
        "eyes pattern e00_def_op 100\n" +
        "mouth pattern k00_def_cl 100\n" +
        "mouth shape teeth t_defo 100\n" +
        "[closed]\n" +
        "eyes pattern e00_def_cl 100\n" +
        "[open]\n" +
        "mouth pattern k00_def_op 100\n" +
        "mouth shape teeth t_defo 30\n" +
        "mouth shape teeth t_open 70\n" +
        "[mood Joy 0]\n" +
        "eyes pattern e01_yorokobi_op 100\n" +
        "eyes pattern e07_egao 0\n" +
        "mouth pattern k41_kuchibue 100\n";

    private static void TestOpenClose()
    {
        FaceRecipes r = FaceRecipes.Parse(OpenCloseRecipe);
        Check(r.Errors.Count == 0 && r.Closed.Count == 1 && r.Opened.Count == 1, "closed / open entries parse");
        HashSet<string> known = new HashSet<string> { "eyes|e00_def_op", "eyes|e00_def_cl", "eyes|e01_yorokobi_op", "eyes|e01_yorokobi_cl", "mouth|k00_def_cl", "mouth|k00_def_op", "mouth|k41_kuchibue" };
        FaceResult res = new FaceResult();
        FaceInput input = new FaceInput { Angle = 0f, Radius = 1f, EyeOpen = 0.25f };
        FaceMix.Mix(r, "female", input, res, known);
        Check(Near(res.Patterns["eyes|e01_yorokobi_op"], 25f) && Near(res.Patterns["eyes|e01_yorokobi_cl"], 75f), "eye close crossfades to the _cl partner");
        input.EyeOpen = 1f;
        input.MouthOpen = 0.5f;
        FaceMix.Mix(r, "female", input, res, known);
        Check(Near(res.Patterns["mouth|k41_kuchibue"], 50f) && Near(res.Patterns["mouth|k00_def_op"], 50f), "unpaired mouth shape fades to the open entry");
        input.Radius = 0f;
        FaceMix.Mix(r, "female", input, res, known);
        Check(Near(res.Patterns["mouth|k00_def_cl"], 50f) && Near(res.Patterns["mouth|k00_def_op"], 50f), "no mood: the neutral mouth opens via its partner");
        Check(Near(res.Shapes["teeth|t_defo"], 65f) && Near(res.Shapes["teeth|t_open"], 35f), "exact teeth lines fade to the open entry (sum stays 100)");
        Check(res.UsesEyes == false, "mouth slider alone leaves the eyes to the game");
        Check(input.IsNeutral == false, "an open mouth is not neutral");

        // value equality (Timeline auto-key compares with Equals)
        FaceInput e1 = new FaceInput { Angle = 10f, Radius = 0.5f, Vowel = "A" };
        e1.Drags["BrowL"] = new[] { 0.2f, 0f };
        FaceInput e2 = e1.Clone();
        Check(e1.Equals(e2) && e2.Equals(e1), "a clone equals the original");
        e2.Drags["BrowL"][1] = 0.1f;
        Check(e1.Equals(e2) == false, "a moved point differs");
        e2 = e1.Clone();
        e2.Drags["LipUp"] = new[] { 0f, 0f };
        Check(e1.Equals(e2), "a zero drag equals no drag");
        FaceInput c1 = new FaceInput { Angle = 10f }, c2 = new FaceInput { Angle = 200f };
        Check(c1.Equals(c2), "at the centre the angle does not matter");
    }

    private static void TestPointShapeLines()
    {
        FaceRecipes r = FaceRecipes.Parse("[point Tongue]\nat 0.5 0.8\ny- pattern mouth k44_sitadasi_a 100\nx+ shape mouth tongue bs07_sita.k55_name_a_sita 80\n");
        Check(r.Errors.Count == 0, "point shape lines parse");
        FacePoint p = r.Points[0];
        Check(p.Bone == null && p.Shapes.Count == 2, "shape-only point");
        Check(p.Shapes[0].Axis == 1 && p.Shapes[0].Sign == -1 && p.Shapes[0].Line.Mesh == null && p.Shapes[0].Line.Name == "k44_sitadasi_a", "y- pattern line");
        Check(p.Shapes[1].Axis == 0 && p.Shapes[1].Sign == 1 && p.Shapes[1].Line.Mesh == "tongue" && p.Shapes[1].Line.Weight == 80f, "x+ shape line");

        FaceInput input = new FaceInput();
        input.Drags["Tongue"] = new[] { 0.5f, -1f };
        FaceResult res = new FaceResult();
        FaceMix.Mix(r, "female", input, res);
        float w;
        Check(res.Patterns.TryGetValue("mouth|k44_sitadasi_a", out w) && Math.Abs(w - 100f) < 0.01f, "drag down drives the pattern fully");
        Check(res.Shapes.TryGetValue("tongue|bs07_sita.k55_name_a_sita", out w) && Math.Abs(w - 40f) < 0.01f, "half drag right drives the exact shape at half weight");
        input.Drags["Tongue"] = new[] { -0.5f, 1f };
        res.Clear();
        FaceMix.Mix(r, "female", input, res);
        Check(res.Patterns.ContainsKey("mouth|k44_sitadasi_a") == false && res.Shapes.ContainsKey("tongue|bs07_sita.k55_name_a_sita") == false, "opposite directions drive nothing");
    }

    private static int Main()
    {
        TestPointShapeLines();
        TestPoseIsOptIn();
        TestCaptureKeepsOnlySelectedModules();
        TestCaptureWithoutBonesDropsSwitches();
        TestColliderLinksStripped();
        TestRoundTrip();
        TestApplyNodeFiltersModulesAndMeshes();
        TestCountEntries();
        TestRecipeParse();
        TestRecipeErrors();
        TestBoneMatches();
        TestPatternKeys();
        TestPadWeights();
        TestMix();
        TestOpenClose();
        Console.WriteLine(_passed + " passed, " + _failed + " failed");
        return _failed == 0 ? 0 : 1;
    }
}
