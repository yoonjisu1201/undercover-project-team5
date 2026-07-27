# FogSample Scene Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expand the existing `Assets/Scenes/Sample/FogSample.unity` scene into a manually switchable fog comparison scene with three Volume-based examples and one world-space particle example.

**Architecture:** Keep the existing camera and directional light. Add four root GameObjects with descriptive names, each disabled by default so the user can enable one directly from the Hierarchy. Use scene-owned Volume Profiles for the three post-processing examples and the existing imported fog particle prefab for the non-Volume example.

**Tech Stack:** Unity 6, URP 17.3, scene YAML assets, URP Volume overrides, imported particle fog prefab.

## Global Constraints

- Do not create a new scene; modify the existing `FogSample.unity`.
- Do not add an automatic controller, player movement, trigger, or keyboard toggle.
- Keep all four sample roots independently toggleable from the Hierarchy and disabled by default.
- Use separate profile assets so each example can be tuned without changing the others.

## Task 1: Add the three Volume sample profiles

- [ ] Add `Assets/Scenes/Sample/Fog/ScreenHazeProfile.asset` with a global Volume profile containing Color Adjustments and Vignette overrides for a soft screen-wide haze.
- [ ] Add `Assets/Scenes/Sample/Fog/NoiseFogProfile.asset` with Color Adjustments, Vignette, and Film Grain overrides for a noisy screen fog presentation.
- [ ] Add `Assets/Scenes/Sample/Fog/WorldFogProfile.asset` with Depth of Field and Color Adjustments overrides for a depth-dependent spatial haze presentation.
- [ ] Add matching `.meta` files with stable GUIDs and reference the profiles from the scene Volume components.

## Task 2: Add the manually switchable hierarchy

- [ ] Add `Volume_ScreenHaze`, `Volume_NoiseFog`, and `Volume_WorldFog` root GameObjects with disabled global Volume components and their matching profiles.
- [ ] Add `ParticleFog` as a disabled root GameObject containing an instance of `Assets/Imported/Fog Particles/Prefabs/Whitish Fog.prefab`, positioned in front of the camera as a world-space effect.
- [ ] Preserve the existing camera and directional light, and keep built-in scene fog disabled by default so the comparisons are isolated.

## Task 3: Verify the scene asset

- [ ] Confirm the scene contains exactly the four sample roots and that each root is disabled in serialized scene data.
- [ ] Confirm every Volume points to its intended profile and the particle sample points to the imported prefab.
- [ ] Check `git diff --check` and inspect the final diff for unrelated changes.

---

Plan complete and ready for inline execution in this workspace.
