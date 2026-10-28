using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HSPE.AMModules;
using HSPE.Kit.Undo;
using Studio;
using UnityEngine;

namespace HSPE.Kit
{
    /// <summary>
    /// Dev-only scene round trip (UserData/HSPE/selftest.txt = "scenesave"; tools/selftest.ps1 -Mode scenesave).
    /// Character A gets one edit in every HSPE module (blend shapes on head / teeth / tongue, bones, Dynamic Bones,
    /// colliders, boobs, IK), character B only the Face window's tongue point. What is DISPLAYED is measured, the scene
    /// is saved with Studio's own SaveScene and loaded with LoadScene, and the same values are measured again: every
    /// difference is something a scene does not keep. The scene file it wrote is deleted afterwards.
    /// </summary>
    internal static class SceneSaveSelfTest
    {
        private static void Check(bool ok, string what, string detail = null) { UndoSelfTest.Check(ok, "save " + what, detail); }
        private static void Info(string s) { UndoSelfTest.Info("save " + s); }

        private sealed class Probe
        {
            public string Name;
            public Func<OCIChar, string> Read; // the displayed value, as text (null = not found)
        }

        private static string F(float v) { return v.ToString("F3"); }
        private static string V(Vector3 v) { return v.ToString("F4"); }

        private static SkinnedMeshRenderer Renderer(OCIChar c, string name)
        {
            foreach (SkinnedMeshRenderer r in c.charInfo.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.name == name) return r;
            return null;
        }

        private static string Weights(SkinnedMeshRenderer r)
        {
            if (r == null || r.sharedMesh == null) return null;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < r.sharedMesh.blendShapeCount; i++)
            {
                float w = r.GetBlendShapeWeight(i);
                if (w > 0.5f) sb.Append(i).Append('=').Append(Mathf.Round(w)).Append(' ');
            }
            return sb.ToString();
        }

        private static PoseController Pc(OCIChar c) { return c.guideObject.transformTarget.GetComponent<PoseController>(); }

        private static IEnumerator Add(Action<OCICharFemale> got)
        {
            string[] cards = Directory.GetFiles("UserData/chara/female", "*.png");
            OCICharFemale c = AddObjectFemale.Add(cards[0]);
            yield return UndoSelfTest.Seconds(1.5f);
            got(c);
        }

        /// <summary>BetterSceneLoader's own loader component (null when the plugin is not installed).</summary>
        private static MonoBehaviour BetterSceneLoader()
        {
            foreach (MonoBehaviour m in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                if (m != null && m.GetType().FullName == "BetterSceneLoader.BetterSceneLoader")
                    return m;
            return null;
        }

        private static void CallPrivate(object target, string method, params object[] args)
        {
            System.Reflection.MethodInfo mi = null;
            foreach (System.Reflection.MethodInfo m in target.GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
                if (m.Name == method && m.GetParameters().Length == args.Length)
                    mi = m;
            mi.Invoke(target, args);
        }

        /// <param name="viaBsl">save and load through BetterSceneLoader's own SaveScene / LoadScene (its buttons)</param>
        internal static IEnumerator Run(MainWindow main, bool viaBsl)
        {
            Info("start" + (viaBsl ? " (BetterSceneLoader save + load)" : " (Studio save + load)"));
            while (Studio.Studio.Instance == null || Studio.Studio.Instance.treeNodeCtrl == null) yield return null;
            yield return UndoSelfTest.Seconds(4f);
            yield return UndoSelfTest.HideRecoveryModal();
            if (Directory.GetFiles("UserData/chara/female", "*.png").Length == 0) { Check(false, "setup: a female card"); UndoSelfTest.Info("done"); yield break; }

            OCICharFemale a = null, b = null;
            yield return Add(c => a = c);
            yield return Add(c => b = c);
            OCICharFemale cc = null;
            yield return Add(c => cc = c);
            PoseController pa = Pc(a), pb = Pc(b), pc = Pc(cc);
            Check(pa != null && pb != null && pc != null, "setup: three characters with HSPE");
            if (pa == null || pb == null || pc == null) { UndoSelfTest.Info("done"); yield break; }

            List<Probe> probes = new List<Probe>();

            // ---- A: blend shapes on head, teeth and tongue (the Blend Shapes page's SetWeightLinked)
            BlendShapesEditor bs = pa._blendShapesEditor;
            bs.KitRefresh();
            foreach (string mesh in new[] { "cf_O_head", "cf_O_ha", "cf_O_sita", "cf_O_mayuge", "cf_O_matuge" })
            {
                SkinnedMeshRenderer r = Renderer(a, mesh);
                if (r == null || r.sharedMesh == null || r.sharedMesh.blendShapeCount == 0) { Info("A has no blend shapes on " + mesh); continue; }
                int index = Mathf.Min(1, r.sharedMesh.blendShapeCount - 1);
                bs.SetWeightLinked(r, index, 73f);
                string m = mesh;
                int i = index;
                probes.Add(new Probe { Name = "A blend shape " + m + "[" + i + "] (" + r.sharedMesh.GetBlendShapeName(i) + ")", Read = c => { SkinnedMeshRenderer x = Renderer(c, m); return x != null ? F(x.GetBlendShapeWeight(i)) : null; } });
            }

            // ---- A: bones (position / rotation / scale through the Bones page)
            BonesEditor bones = pa._bonesEditor;
            Transform spine = UndoSelfTest.FindDeep(pa.transform, "cf_J_Spine02_s");
            Transform neck = UndoSelfTest.FindDeep(pa.transform, "cf_J_Neck_s");
            Transform hand = UndoSelfTest.FindDeep(pa.transform, "cf_J_Hand_s_L");
            if (spine != null) { bones.KitSelect(spine); bones.KitSetPosition(bones.KitGetPosition() + new Vector3(0.04f, 0f, 0f)); }
            if (neck != null) { bones.KitSelect(neck); bones.KitSetRotation(Quaternion.Euler(12f, 0f, 0f), true); }
            if (hand != null) { bones.KitSelect(hand); bones.KitSetScale(new Vector3(1.3f, 1.3f, 1.3f)); }
            foreach (string bn in new[] { "cf_J_Spine02_s", "cf_J_Neck_s", "cf_J_Hand_s_L" })
            {
                string n = bn;
                probes.Add(new Probe { Name = "A bone " + n, Read = c => { Transform t = UndoSelfTest.FindDeep(Pc(c).transform, n); return t != null ? V(t.localPosition) + " " + V(t.localEulerAngles) + " " + V(t.localScale) : null; } });
            }

            // ---- A: Dynamic Bones, colliders, boobs, IK (first of each)
            List<DynamicBone> dbs = new List<DynamicBone>();
            pa._dynamicBonesEditor.KitBones(dbs);
            if (dbs.Count != 0)
            {
                pa._dynamicBonesEditor.KitSetFloat(dbs[0], DynamicBonesEditor.DbField.Damping, 0.77f);
                pa._dynamicBonesEditor.KitSetGravity(dbs[0], new Vector3(0f, -0.03f, 0f));
                string root = (dbs[0].m_Root != null ? dbs[0].m_Root : dbs[0].transform).name;
                probes.Add(new Probe { Name = "A dynamic bone " + root, Read = c => { foreach (DynamicBone d in c.charInfo.gameObject.GetComponentsInChildren<DynamicBone>(true)) if ((d.m_Root != null ? d.m_Root : d.transform).name == root) return F(d.m_Damping) + " " + V(d.m_Gravity); return null; } });
            }
            else Info("A has no Dynamic Bones (hair without physics)");

            List<DynamicBoneCollider> cols = new List<DynamicBoneCollider>();
            pa._collidersEditor.KitColliders(cols);
            DynamicBoneCollider col = cols.Count != 0 ? cols[0] : null;
            if (col != null)
            {
                pa._collidersEditor.KitSetRadius(col, col.m_Radius + 0.021f);
                string cn = col.name;
                probes.Add(new Probe { Name = "A collider " + cn, Read = c => { foreach (DynamicBoneCollider d in c.charInfo.gameObject.GetComponentsInChildren<DynamicBoneCollider>(true)) if (d.name == cn) return F(d.m_Radius); return null; } });
            }
            else Info("A has no capsule colliders");

            DynamicBone_Ver02[] boobs = a.charInfo.gameObject.GetComponentsInChildren<DynamicBone_Ver02>(true);
            if (boobs.Length != 0)
            {
                ((CharaPoseController)pa)._boobsEditor.KitSetGravity(boobs[0], new Vector3(0f, -0.02f, 0.01f));
                string bn = boobs[0].name;
                probes.Add(new Probe { Name = "A boobs " + bn + " gravity", Read = c => { foreach (DynamicBone_Ver02 d in c.charInfo.gameObject.GetComponentsInChildren<DynamicBone_Ver02>(true)) if (d.name == bn) return V(d.Gravity); return null; } });
            }

            List<RootMotion.FinalIK.IK> iks = new List<RootMotion.FinalIK.IK>();
            pa._ikEditor.KitIKs(iks);
            if (iks.Count != 0)
            {
                pa._ikEditor.KitSetFloat(iks[0], IKEditor.IKField.PositionWeight, 0.42f);
                string ikName = iks[0].name;
                probes.Add(new Probe { Name = "A IK " + ikName + " position weight", Read = c => { foreach (RootMotion.FinalIK.IK k in c.charInfo.gameObject.GetComponentsInChildren<RootMotion.FinalIK.IK>(true)) if (k.name == ikName) return F(k.GetIKSolver().IKPositionWeight); return null; } });
            }
            else Info("A has no IK components listed");

            // ---- B: only the Face window's tongue point, fully out (a drag down on "Tongue")
            Face.FaceSession fs = Face.FaceSessions.Get(pb, true);
            if (fs != null)
            {
                fs.Input.Drags["Tongue"] = new float[] { 0f, -1f };
                fs.Apply();
                fs.Version++;
            }
            else Info("B: no Face session");
            probes.Add(new Probe { Name = "B Face tongue point: tongue mesh", Read = c => Weights(Renderer(c, "cf_O_sita")) });
            probes.Add(new Probe { Name = "B Face tongue point: head (mouth) mesh", Read = c => Weights(Renderer(c, "cf_O_head")) });
            probes.Add(new Probe { Name = "B Face tongue point: teeth mesh", Read = c => Weights(Renderer(c, "cf_O_ha")) });

            // ---- C: a Face window mood (Joy, full), then the tongue moved with the Blend Shapes page's slider on top
            Face.FaceSession fc = Face.FaceSessions.Get(pc, true);
            if (fc != null)
            {
                fc.Input.Angle = 0f;
                fc.Input.Radius = 1f;
                fc.Input.Vowel = "A"; // the mouth part (its meshes include the tongue) is the Face window's too
                fc.Apply();
                fc.Version++;
            }
            else Info("C: no Face session");
            yield return UndoSelfTest.Seconds(0.5f);
            SkinnedMeshRenderer tongueC = Renderer(cc, "cf_O_sita");
            if (tongueC != null && tongueC.sharedMesh != null && tongueC.sharedMesh.blendShapeCount > 1)
            {
                pc._blendShapesEditor.KitRefresh();
                pc._blendShapesEditor.SetWeightLinked(tongueC, 1, 73f);
                probes.Add(new Probe { Name = "C Face mood + tongue slider: tongue[1]", Read = c => { SkinnedMeshRenderer x = Renderer(c, "cf_O_sita"); return x != null ? F(x.GetBlendShapeWeight(1)) : null; } });
                probes.Add(new Probe { Name = "C Face mood + tongue slider: head (mood) mesh", Read = c => Weights(Renderer(c, "cf_O_head")) });
            }
            else Info("C: no tongue blend shapes");

            // the Face window open on C through save and load (it follows the selected character)
            yield return UndoSelfTest.Select(main, cc);
            if (Face.FaceWindow.Visible == false)
                Face.FaceWindow.Toggle();
            Info("Face window open: " + Face.FaceWindow.Visible);

            // let HSPE apply everything (LateUpdate) and Studio's face animation run over it a few times
            yield return UndoSelfTest.Seconds(1.5f);
            Dictionary<string, string> before = new Dictionary<string, string>();
            foreach (Probe p in probes)
            {
                OCIChar c = p.Name.StartsWith("B ") ? (OCIChar)b : p.Name.StartsWith("C ") ? (OCIChar)cc : a;
                before[p.Name] = p.Read(c);
                Info("before  " + p.Name + " = " + (before[p.Name] ?? "(not found)"));
            }

            // ---- save, then load (Studio's own, or BetterSceneLoader's)
            MonoBehaviour bsl = viaBsl ? BetterSceneLoader() : null;
            if (viaBsl)
            {
                Check(bsl != null, "BetterSceneLoader is installed and running");
                if (bsl == null) { UndoSelfTest.Info("done"); yield break; }
            }
            string dir = Path.GetFullPath("UserData/studioneo"); // Studio saves into scene\, BetterSceneLoader into BetterSceneLoader\<category>\
            DateTime t0 = DateTime.Now.AddSeconds(-1);
            if (viaBsl) CallPrivate(bsl, "SaveScene");
            else Studio.Studio.Instance.SaveScene();
            yield return UndoSelfTest.Seconds(1.5f);
            string saved = null;
            foreach (string f in Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories))
                if (File.GetLastWriteTime(f) >= t0 && (saved == null || File.GetLastWriteTime(f) > File.GetLastWriteTime(saved)))
                    saved = f;
            Check(saved != null, "scene file written", saved != null ? Path.GetFileName(saved) : "none");
            if (saved == null) { UndoSelfTest.Info("done"); yield break; }
            if (viaBsl) CallPrivate(bsl, "LoadScene", saved);
            else Studio.Studio.Instance.LoadScene(saved);
            yield return UndoSelfTest.Seconds(6f); // HSPE loads its data a few frames after the scene; BSL's extra-data step comes later still

            List<OCIChar> chars = new List<OCIChar>();
            foreach (KeyValuePair<int, ObjectCtrlInfo> kv in new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl))
                if (kv.Value is OCIChar) chars.Add((OCIChar)kv.Value);
            Check(chars.Count == 3, "three characters back after loading", chars.Count.ToString());
            if (chars.Count == 3)
            {
                // select C again with the Face window still open, then wait a moment
                yield return UndoSelfTest.Select(main, chars[2]);
                Info("after load: Face window open " + Face.FaceWindow.Visible + ", C selected");
                yield return UndoSelfTest.Seconds(1.5f);
                int lost = 0;
                foreach (Probe p in probes)
                {
                    string now = p.Read(p.Name.StartsWith("B ") ? chars[1] : p.Name.StartsWith("C ") ? chars[2] : chars[0]);
                    bool same = now == before[p.Name];
                    if (!same) lost++;
                    Check(same, p.Name + " survives save + load", same ? now : "before " + (before[p.Name] ?? "-") + "  after " + (now ?? "-"));
                }
                Info(lost == 0 ? "every measured value came back" : lost + " value(s) did not come back");
            }

            try { File.Delete(saved); } catch (Exception e) { Info("could not delete " + saved + ": " + e.Message); }
            UndoSelfTest.Info("result " + UndoSelfTest.Passed + " passed, " + UndoSelfTest.Failed + " failed");
            UndoSelfTest.Info("done");
        }
    }
}
