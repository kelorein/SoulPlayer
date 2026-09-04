"""Build the prop-free DJMaesen first-person arm validation scene.

This script never reads or modifies the BAMEN source, SoulRecorderGripPoses.json,
the recorder prefab, or the cassette prefab. It imports only the isolated CC BY
candidate FBX, audits its deform hierarchy, applies a deliberately simple pose,
and renders FOV-70 evidence before any SoulRecorder integration is allowed.
"""

from __future__ import annotations

import json
import math
from pathlib import Path

import bpy
from mathutils import Vector


SCRIPT_PATH = Path(__file__).resolve()
REPLACEMENT_ROOT = SCRIPT_PATH.parents[1]
SOURCE_ROOT = REPLACEMENT_ROOT / "Staging" / "DJMaesenFirstPersonArms"
FBX_PATH = SOURCE_ROOT / "source" / "fpsarms.fbx"
TEXTURE_ROOT = SOURCE_ROOT / "textures"
OUTPUT_ROOT = REPLACEMENT_ROOT / "ValidationOutput"
BLEND_PATH = OUTPUT_ROOT / "DJMaesenFirstPersonArmsRigValidation.blend"

REQUIRED_CHAINS = {
    "Left": ("L_arm", "L_elbow", "L_wrist"),
    "Right": ("R_arm", "R_elbow", "R_wrist"),
}

DIGITS = {
    "Thumb": "thumb",
    "Index": "point",
    "Middle": "middle",
    "Ring": "ring",
    "Pinky": "pink",
}


def reset_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_candidate() -> tuple[bpy.types.Object, bpy.types.Object]:
    if not FBX_PATH.exists():
        raise FileNotFoundError(FBX_PATH)
    bpy.ops.import_scene.fbx(filepath=str(FBX_PATH))
    armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    meshes = [
        obj
        for obj in bpy.context.scene.objects
        if obj.type == "MESH" and len(obj.data.vertices) > 100
    ]
    if len(armatures) != 1 or len(meshes) != 1:
        raise RuntimeError(
            f"Expected one armature and one arm mesh; found {len(armatures)} and {len(meshes)}"
        )
    armature = armatures[0]
    mesh = meshes[0]
    armature.name = "DJMaesenFPSArmsRig"
    mesh.name = "DJMaesenFPSArmsMesh"
    for obj in list(bpy.context.scene.objects):
        if obj not in {armature, mesh}:
            bpy.data.objects.remove(obj, do_unlink=True)
    return armature, mesh


def image_texture(nodes, name: str, filename: str, color_space: str):
    node = nodes.new("ShaderNodeTexImage")
    node.name = name
    node.label = name
    node.image = bpy.data.images.load(str(TEXTURE_ROOT / filename), check_existing=True)
    node.image.colorspace_settings.name = color_space
    return node


def configure_material(mesh: bpy.types.Object) -> None:
    material = bpy.data.materials.new("DJMaesenFPSArms_CC_BY_Staging")
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    color = image_texture(nodes, "Albedo", "armColor.png", "sRGB")
    ao = image_texture(nodes, "Ambient Occlusion", "armAO.png", "Non-Color")
    roughness = image_texture(nodes, "Roughness", "armRoughness.png", "Non-Color")
    normal = image_texture(nodes, "Normal", "armnormal.png", "Non-Color")
    multiply = nodes.new("ShaderNodeMixRGB")
    multiply.blend_type = "MULTIPLY"
    multiply.inputs[0].default_value = 1.0
    normal_map = nodes.new("ShaderNodeNormalMap")
    normal_map.inputs[0].default_value = 0.65
    links.new(color.outputs["Color"], multiply.inputs[1])
    links.new(ao.outputs["Color"], multiply.inputs[2])
    links.new(multiply.outputs["Color"], shader.inputs["Base Color"])
    links.new(roughness.outputs["Color"], shader.inputs["Roughness"])
    links.new(normal.outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], shader.inputs["Normal"])
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    mesh.data.materials.clear()
    mesh.data.materials.append(material)


def bone_world_head(armature: bpy.types.Object, name: str) -> Vector:
    return armature.matrix_world @ armature.data.bones[name].head_local


def hierarchy_path(armature: bpy.types.Object, name: str) -> list[str]:
    names: list[str] = []
    bone = armature.data.bones[name]
    while bone is not None:
        names.append(bone.name)
        bone = bone.parent
    return list(reversed(names))


def triangulated_count(mesh: bpy.types.Object) -> int:
    return sum(max(0, len(poly.vertices) - 2) for poly in mesh.data.polygons)


def weighted_vertex_counts(mesh: bpy.types.Object) -> dict[str, int]:
    counts: dict[str, int] = {}
    for group in mesh.vertex_groups:
        counts[group.name] = sum(
            1
            for vertex in mesh.data.vertices
            if any(
                assignment.group == group.index and assignment.weight > 0.001
                for assignment in vertex.groups
            )
        )
    return counts


def reset_pose(armature: bpy.types.Object) -> None:
    for pose_bone in armature.pose.bones:
        pose_bone.rotation_mode = "XYZ"
        pose_bone.location = (0.0, 0.0, 0.0)
        pose_bone.rotation_euler = (0.0, 0.0, 0.0)
        pose_bone.scale = (1.0, 1.0, 1.0)
    bpy.context.view_layer.update()


def evaluated_vertices(mesh: bpy.types.Object) -> list[Vector]:
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = mesh.evaluated_get(depsgraph)
    return [evaluated.matrix_world @ vertex.co for vertex in evaluated.data.vertices]


def group_vertex_indices(mesh: bpy.types.Object, prefix: str) -> set[int]:
    group_indices = {
        group.index for group in mesh.vertex_groups if group.name.startswith(prefix)
    }
    return {
        vertex.index
        for vertex in mesh.data.vertices
        if any(
            assignment.group in group_indices and assignment.weight > 0.05
            for assignment in vertex.groups
        )
    }


def movement_probe(
    armature: bpy.types.Object,
    mesh: bpy.types.Object,
    bone_name: str,
    axis: int,
    degrees: float,
) -> dict:
    reset_pose(armature)
    baseline = evaluated_vertices(mesh)
    pose_bone = armature.pose.bones[bone_name]
    pose_bone.rotation_euler[axis] = math.radians(degrees)
    bpy.context.view_layer.update()
    posed = evaluated_vertices(mesh)
    side_prefix = "L_" if bone_name.startswith("L_") else "R_"
    own = group_vertex_indices(mesh, side_prefix)
    other = group_vertex_indices(mesh, "R_" if side_prefix == "L_" else "L_")
    deltas = [(posed[index] - baseline[index]).length for index in range(len(posed))]
    result = {
        "bone": bone_name,
        "degrees": degrees,
        "axis": "XYZ"[axis],
        "ownVerticesMovedOver0.1mm": sum(deltas[i] > 0.0001 for i in own),
        "otherSideVerticesMovedOver0.1mm": sum(deltas[i] > 0.0001 for i in other),
        "maximumOwnDisplacementMeters": max((deltas[i] for i in own), default=0.0),
    }
    reset_pose(armature)
    return result


def apply_validation_pose(armature: bpy.types.Object) -> None:
    reset_pose(armature)
    # Deliberately simple articulation test, not a SoulRecorder grip pose.
    rotations = {
        "L_arm": (0.0, -20.0, 0.0),
        "R_arm": (0.0, 20.0, 0.0),
        # Negative local X folds both forearms down/inward in this skeleton;
        # positive X points them back into the camera and obscures the hinge.
        "L_elbow": (-60.0, 0.0, 0.0),
        "R_elbow": (-60.0, 0.0, 0.0),
        "L_wrist": (0.0, 0.0, 15.0),
        "R_wrist": (0.0, 0.0, -15.0),
    }
    for name, degrees in rotations.items():
        armature.pose.bones[name].rotation_euler = tuple(
            math.radians(value) for value in degrees
        )
    for side in ("L", "R"):
        sign = 1.0 if side == "L" else -1.0
        for stem in DIGITS.values():
            for joint, degrees in ((1, 18.0), (2, 32.0), (3, 42.0)):
                name = f"{side}_{stem}{joint}"
                if name in armature.pose.bones:
                    armature.pose.bones[name].rotation_euler[2] = math.radians(
                        sign * degrees
                    )
    bpy.context.view_layer.update()


def aim_camera(camera: bpy.types.Object, target: Vector) -> None:
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()


def configure_scene() -> bpy.types.Object:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    if scene.world is None:
        scene.world = bpy.data.worlds.new("RigValidationWorld")
    scene.world.color = (0.025, 0.03, 0.04)

    camera_data = bpy.data.cameras.new("FOV70_ValidationCamera")
    camera = bpy.data.objects.new("FOV70_ValidationCamera", camera_data)
    bpy.context.collection.objects.link(camera)
    camera.data.angle = math.radians(70.0)
    # Use conventional first-person framing for the acceptance renders.  A
    # separate wide overview below proves the shoulder/elbow articulation.
    camera.location = (0.0, 0.23, 0.025)
    aim_camera(camera, Vector((0.0, -0.18, 0.0)))
    scene.camera = camera

    key_data = bpy.data.lights.new("ValidationKey", type="AREA")
    key_data.energy = 700.0
    key_data.shape = "DISK"
    key_data.size = 2.0
    key = bpy.data.objects.new("ValidationKey", key_data)
    bpy.context.collection.objects.link(key)
    key.location = (0.35, 0.12, 0.45)
    aim_camera(key, Vector((0.0, -0.20, 0.0)))

    fill_data = bpy.data.lights.new("ValidationFill", type="AREA")
    fill_data.energy = 450.0
    fill_data.size = 1.5
    fill = bpy.data.objects.new("ValidationFill", fill_data)
    bpy.context.collection.objects.link(fill)
    fill.location = (-0.35, -0.05, 0.20)
    aim_camera(fill, Vector((0.0, -0.25, 0.0)))
    return camera


def render(scene_name: str) -> Path:
    path = OUTPUT_ROOT / f"{scene_name}.png"
    bpy.context.scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    return path


def main() -> None:
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    reset_scene()
    armature, mesh = import_candidate()
    configure_material(mesh)
    configure_scene()

    failures: list[str] = []
    hierarchy: dict[str, dict] = {}
    for side, (upper, forearm, wrist) in REQUIRED_CHAINS.items():
        missing = [name for name in (upper, forearm, wrist) if name not in armature.data.bones]
        if missing:
            failures.extend(f"{side} required bone missing: {name}" for name in missing)
            continue
        upper_bone = armature.data.bones[upper]
        forearm_bone = armature.data.bones[forearm]
        wrist_bone = armature.data.bones[wrist]
        if forearm_bone.parent != upper_bone:
            failures.append(f"{side} forearm is not parented to upper arm")
        if wrist_bone.parent != forearm_bone:
            failures.append(f"{side} wrist is not parented to forearm")
        shoulder = bone_world_head(armature, upper)
        elbow = bone_world_head(armature, forearm)
        wrist_point = bone_world_head(armature, wrist)
        upper_length = (elbow - shoulder).length
        forearm_length = (wrist_point - elbow).length
        shoulder_to_wrist = (wrist_point - shoulder).length
        if not 0.12 <= upper_length <= 0.35:
            failures.append(f"{side} upper-arm pivot span is implausible: {upper_length:.4f}m")
        if not 0.12 <= forearm_length <= 0.35:
            failures.append(f"{side} forearm pivot span is implausible: {forearm_length:.4f}m")
        if shoulder_to_wrist < 0.25:
            failures.append(f"{side} upper-arm pivot is too close to the hand: {shoulder_to_wrist:.4f}m")
        hierarchy[side] = {
            "upperArm": upper,
            "forearm": forearm,
            "wrist": wrist,
            "upperArmPath": hierarchy_path(armature, upper),
            "forearmPath": hierarchy_path(armature, forearm),
            "wristPath": hierarchy_path(armature, wrist),
            "upperArmLengthMeters": upper_length,
            "forearmLengthMeters": forearm_length,
            "shoulderToWristMeters": shoulder_to_wrist,
        }

    counts = weighted_vertex_counts(mesh)
    digit_chains: dict[str, dict] = {}
    for side in ("L", "R"):
        digit_chains[side] = {}
        for label, stem in DIGITS.items():
            names = [f"{side}_{stem}{joint}" for joint in range(1, 5)]
            existing = [name for name in names if name in armature.data.bones]
            if len(existing) != 4:
                failures.append(f"{side} {label} chain has {len(existing)}/4 bones")
            if existing and counts.get(existing[0], 0) == 0:
                failures.append(f"{side} {label} root has no skin weights")
            digit_chains[side][label] = {
                "bones": existing,
                "weightedVertices": {name: counts.get(name, 0) for name in existing},
            }

    movement = []
    for side, (upper, forearm, wrist) in REQUIRED_CHAINS.items():
        for name, axis, angle in (
            (upper, 2, 10.0),
            (forearm, 0, 10.0),
            (wrist, 2, 10.0),
        ):
            result = movement_probe(armature, mesh, name, axis, angle)
            movement.append(result)
            if result["ownVerticesMovedOver0.1mm"] < 100:
                failures.append(f"{name} does not visibly drive enough of its own mesh")
            if result["otherSideVerticesMovedOver0.1mm"] > 5:
                failures.append(f"{name} unexpectedly drives the opposite arm")

    for side in ("L", "R"):
        for stem in DIGITS.values():
            name = f"{side}_{stem}1"
            result = movement_probe(armature, mesh, name, 2, 15.0)
            movement.append(result)
            if result["ownVerticesMovedOver0.1mm"] < 20:
                failures.append(f"{name} does not independently deform its finger")

    reset_pose(armature)
    rest_path = render("01-rest-fov70")
    apply_validation_pose(armature)
    posed_path = render("02-articulation-test-fov70")
    camera = bpy.context.scene.camera
    camera.location = (0.0, 0.55, 0.035)
    aim_camera(camera, Vector((0.0, -0.18, 0.0)))
    overview_path = render("03-articulation-overview-fov70")

    report = {
        "source": str(FBX_PATH),
        "license": "Creative Commons Attribution (CC BY; version not stated in archive)",
        "mesh": {
            "vertices": len(mesh.data.vertices),
            "polygons": len(mesh.data.polygons),
            "triangles": triangulated_count(mesh),
            "dimensionsMeters": list(mesh.dimensions),
            "armatureModifier": any(
                modifier.type == "ARMATURE" and modifier.object == armature
                for modifier in mesh.modifiers
            ),
        },
        "armature": {
            "name": armature.name,
            "boneCount": len(armature.data.bones),
            "hierarchy": hierarchy,
            "digits": digit_chains,
        },
        "movementProbes": movement,
        "validationPose": {
            "upperArmDegrees": 20.0,
            "elbowDegrees": 60.0,
            "wristDegrees": 15.0,
            "eachFingerCurled": True,
            "fovDegrees": 70.0,
            "propsAttached": False,
        },
        "renders": [str(rest_path), str(posed_path), str(overview_path)],
        "failures": failures,
        "result": "PASS" if not failures else "FAIL",
    }
    (OUTPUT_ROOT / "rig-validation-report.json").write_text(
        json.dumps(report, indent=2), encoding="utf-8"
    )
    lines = [
        "DJMaesen First Person arms — prop-free rig validation",
        f"RESULT: {report['result']}",
        f"Mesh: {report['mesh']['vertices']} vertices / {report['mesh']['triangles']} triangles",
        f"Armature: {report['armature']['boneCount']} bones",
        "Props attached: NO",
    ]
    for side, values in hierarchy.items():
        lines.append(
            f"{side}: {values['upperArm']} -> {values['forearm']} -> {values['wrist']} "
            f"(upper={values['upperArmLengthMeters']:.4f}m, "
            f"forearm={values['forearmLengthMeters']:.4f}m, "
            f"shoulder-to-wrist={values['shoulderToWristMeters']:.4f}m)"
        )
    if failures:
        lines.append("Failures:")
        lines.extend(f"- {failure}" for failure in failures)
    else:
        lines.append("All hierarchy, pivot, side-isolation, and finger-deformation gates passed.")
    (OUTPUT_ROOT / "rig-validation-report.txt").write_text(
        "\n".join(lines) + "\n", encoding="utf-8"
    )
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
    print("\n".join(lines))


if __name__ == "__main__":
    main()
