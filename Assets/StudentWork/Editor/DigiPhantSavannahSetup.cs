using System;
using System.IO;
using System.Linq;
using DigiPhant;
using StudentStarter.Savannah;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Applies the documented Savannah menu action once after its first import.
// It is intentionally retained as a repeatable, explicit project setup command.
[InitializeOnLoad]
public static class DigiPhantSavannahSetup
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    static readonly string RequestPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
        "Library", "DigiPhantSavannahSetup.request");
    static readonly string ValidationRequestPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
        "Library", "DigiPhantSavannahValidation.request");
    static readonly string SoloProfileRequestPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
        "Library", "DigiPhantSoloUpperBody.request");

    static DigiPhantSavannahSetup()
    {
        EditorApplication.update += WaitForImportedAssets;
        EditorApplication.delayCall += ApplyAfterImport;
    }

    [MenuItem("DigiPhant/Setup/Apply Savannah to SampleScene")]
    public static void ApplyToSampleScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before applying the Savannah course.");

        EnsureNoDirtyOpenScenes();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (scene.isDirty)
            throw new InvalidOperationException("Save SampleScene before applying the Savannah course.");
        if (scene.GetRootGameObjects().Any(root => root.name == SavannahCourseGenerator.RootName))
        {
            Debug.Log("DIGIPHANT_SAVANNAH_ALREADY_APPLIED " + ScenePath);
            return;
        }

        var locomotion = UnityEngine.Object.FindAnyObjectByType<DigiPhantLocomotion>();
        if (!locomotion)
            throw new InvalidOperationException("SampleScene has no DigiPhant locomotion component.");
        locomotion.stageRadius = 30f;
        EditorUtility.SetDirty(locomotion);
        EditorSceneManager.SaveScene(scene);

        // This calls the same public method behind DigiPhant > Savannah > Inject Course into Open Scene.
        SavannahCourseGenerator.InjectIntoOpenScene();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("DIGIPHANT_SAVANNAH_APPLIED " + ScenePath + " | travel radius 30");
    }

    static void ApplyAfterImport()
    {
        if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode ||
            !AssetDatabase.IsValidFolder("Assets/SavannahCourse") ||
            !AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
            return;
        if (File.Exists(RequestPath))
        {
            try
            {
                ApplyToSampleScene();
                File.Delete(RequestPath);
            }
            catch (Exception exception) { Debug.LogError("DIGIPHANT_SAVANNAH_SETUP_FAILED: " + exception.Message); }
        }
        if (File.Exists(ValidationRequestPath))
        {
            try
            {
                ValidateSampleScene();
                File.Delete(ValidationRequestPath);
            }
            catch (Exception exception) { Debug.LogError("DIGIPHANT_SAVANNAH_VALIDATION_FAILED: " + exception.Message); }
        }
        if (File.Exists(SoloProfileRequestPath))
        {
            try
            {
                ConfigureSoloUpperBody();
                File.Delete(SoloProfileRequestPath);
            }
            catch (Exception exception) { Debug.LogError("DIGIPHANT_SOLO_PROFILE_FAILED: " + exception.Message); }
        }
    }

    [MenuItem("DigiPhant/Setup/Validate Savannah Integration")]
    public static void ValidateSampleScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before validating Savannah.");
        EnsureNoDirtyOpenScenes();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!SceneManager.GetActiveScene().GetRootGameObjects().Any(root => root.name == SavannahCourseGenerator.RootName))
            throw new InvalidOperationException("Inject the Savannah course before validating SampleScene.");
        SavannahCourseValidation.RunAll();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Debug.Log("DIGIPHANT_SAVANNAH_VALIDATED " + ScenePath);
    }

    [MenuItem("DigiPhant/Setup/Configure SampleScene for Solo Upper Body")]
    public static void ConfigureSoloUpperBody()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before changing the tracking profile.");
        EnsureNoDirtyOpenScenes();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var controller = UnityEngine.Object.FindAnyObjectByType<DigiPhantController>();
        var locomotion = UnityEngine.Object.FindAnyObjectByType<DigiPhantLocomotion>();
        if (!controller || !locomotion)
            throw new InvalidOperationException("SampleScene is missing DigiPhant controls or locomotion.");
        controller.SetPerformerCount(1);
        controller.SetUpperBodyOnly(true);
        // Persist the chosen live-control mode without opening a UDP receiver in Edit mode.
        controller.inputMode = InputMode.Camera;
        locomotion.stageRadius = 30f;
        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(locomotion);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("DIGIPHANT_SOLO_UPPER_BODY_CONFIGURED | performers 1 | upper body true | travel radius 30");
    }

    static void WaitForImportedAssets()
    {
        if (!File.Exists(RequestPath) && !File.Exists(ValidationRequestPath) && !File.Exists(SoloProfileRequestPath))
        {
            EditorApplication.update -= WaitForImportedAssets;
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || Application.isBatchMode ||
            EditorApplication.isPlayingOrWillChangePlaymode || !AssetDatabase.IsValidFolder("Assets/SavannahCourse") ||
            !AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
            return;
        EditorApplication.update -= WaitForImportedAssets;
        EditorApplication.delayCall += ApplyAfterImport;
    }

    static void EnsureNoDirtyOpenScenes()
    {
        var dirty = Enumerable.Range(0, SceneManager.sceneCount)
            .Select(SceneManager.GetSceneAt)
            .Where(scene => scene.isDirty)
            .Select(scene => string.IsNullOrEmpty(scene.path) ? scene.name : scene.path)
            .ToArray();
        if (dirty.Length > 0)
            throw new InvalidOperationException("Save open scene changes before setup: " + string.Join(", ", dirty));
    }
}
