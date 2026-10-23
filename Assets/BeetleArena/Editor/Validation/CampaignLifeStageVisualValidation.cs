using System;
using System.Linq;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using UnityEngine;
using CampaignSpecimen = BeetleArena.Campaign.CampaignSpecimen;

/// <summary>Read-only checks for a selected individual after its scene/camera has settled in Play mode.</summary>
public static class CampaignLifeStageVisualValidation
{
    public static string ValidateCurrent(bool checkCameraFraming = true)
    {
        Require(Application.isPlaying, "Life-stage scene validation requires Play mode.");
        var app = CampaignApp.Instance;
        var selected = app.Session.State.beetles.Find(b => b.id == app.Session.State.selectedId);
        Require(selected != null, "Select an individual before checking its stage scene.");
        var specimen = UnityEngine.Object.FindObjectsByType<CampaignSpecimen>(FindObjectsSortMode.None).First(s => s.selected);
        var display = specimen.LifeStageDisplay;
        Require(display && display.CurrentStage == selected.stage, "Displayed life stage does not match selected saved individual.");
        var adults = specimen.GetComponentsInChildren<Renderer>().Where(r => r.gameObject.activeInHierarchy).ToArray();
        bool adultBody = selected.stage == CampaignLifeStage.ActiveAdult || selected.stage == CampaignLifeStage.DormantAdult;
        Require(adults.Length > 0, "Original adult model is missing.");
        Require(adults.All(r => r.enabled == adultBody), "A hidden adult mesh leaked into an immature scene, or the reusable adult model is hidden.");
        Require(display.HasStageEnvironment == (selected.stage != CampaignLifeStage.ActiveAdult), "Stage scenery was not restored/switched correctly.");
        Require(specimen.enclosureFloor.enabled == (selected.stage == CampaignLifeStage.ActiveAdult), "Original enclosure overlaps a stage-specific container.");
        Bounds bounds;
        if (display.HasStageEnvironment)
        {
            bounds = display.ObservationBounds;
            Require(bounds.size.x > .1f && bounds.size.y > .04f && bounds.size.z > .1f, "Stage animal has empty or flat visual bounds.");
            var environment = display.EnvironmentBounds;
            Require(environment.size.x > bounds.size.x && environment.size.z > bounds.size.z, "The animal is larger than its observation container.");
            Require(bounds.min.y >= display.GroundHeight - .08f, "The stage animal is buried below its substrate surface.");
            Require(display.StageRoot.GetComponentsInChildren<Collider>().All(c => !c.enabled), "Procedural presentation added an active physics collider.");
        }
        else
        {
            bounds = adults[0].bounds; foreach (var renderer in adults) bounds.Encapsulate(renderer.bounds);
        }
        if (checkCameraFraming)
        {
            var backdrop = UnityEngine.Object.FindObjectsByType<CampaignBackdrop>(FindObjectsSortMode.None)
                .First(b => b.gameObject.scene == specimen.gameObject.scene);
            var camera = backdrop.sceneCamera;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        var point = camera.WorldToViewportPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
                        Require(point.z > camera.nearClipPlane && point.x >= -.02f && point.x <= 1.02f && point.y >= -.02f && point.y <= 1.02f,
                            "A stage-animal bound lies outside the observation camera. Reset view and let camera settle before checking.");
                    }
        }
        return $"PASS {selected.stage}: adultModel={adultBody}, container={display.HasStageEnvironment}, body={bounds.size}, revision={display.Revision}";
    }

    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
