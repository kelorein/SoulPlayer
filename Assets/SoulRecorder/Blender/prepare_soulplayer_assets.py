"""Prepare SoulPlayer's approved third-party source assets in Blender.

Invoked by tools/Prepare-SoulRecorderAssets.ps1. This is original SoulPlayer
pipeline code and contains no implementation from either reference project.
"""

import argparse
import json
import math
import os
import shutil
import sys
from array import array

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector


CASSETTE_TARGET_DIMENSIONS = Vector((0.110, 0.070, 0.018))
CASSETTE_DIMENSION_TOLERANCE = Vector((0.002, 0.002, 0.001))
CASSETTE_SOURCE_OBJECTS = {
    "CassetteCase_Hero.001": "Shell",
    "Axle.001": "Axles",
    "tape.001": "TapeRibbon",
    "WheelCogs.001": "ReelLeft/ReelRight",
}
RECORDER_TEXTURE_SOURCES = {
    "cassette_player_body_diff_4k.png": "soulrecorder_body_basecolor.png",
    "cassette_player_body_nor_gl_4k.png": "soulrecorder_body_normal.png",
    "cassette_player_body_metallic_4k.png": "soulrecorder_body_metallic.png",
    "cassette_player_body_roughness_4k.png": "soulrecorder_body_roughness.png",
    "cassette_player_body_opacity_4k.png": "soulrecorder_body_opacity.png",
}
RECORDER_DERIVED_TEXTURES = {
    "soulrecorder_body_metallic_smoothness.png",
    "soulrecorder_flap_basecolor.png",
}
HANDS_TEXTURE_SOURCES = {
    "Arm_and_Hand_Arm_BaseColor.png": "soulrecorder_arms_basecolor.png",
    "Arm_and_Hand_Arm_Normal.png": "soulrecorder_arms_normal.png",
    "Arm_and_Hand_Arm_Roughness.png": "soulrecorder_arms_roughness.png",
    "Hand_Hand_BaseColor.png": "soulrecorder_hands_basecolor.png",
    "Hand_Hand_Normal.png": "soulrecorder_hands_normal.png",
    "Hand_Hand_Roughness.png": "soulrecorder_hands_roughness.png",
}
HANDS_DERIVED_TEXTURES = {
    "soulrecorder_arms_metallic_smoothness.png",
    "soulrecorder_hands_metallic_smoothness.png",
}
HANDS_REQUIRED_BONES = (
    "SoulRecorderHandsRoot",
    "Arm_1.L", "Arm_2.L", "Hand_1.L", "Hand_2.L",
    "Arm_1.R", "Arm_2.R", "Hand_1.R", "Hand_2.R",
    "Finger_1_1.L", "Finger_1_2.L", "Finger_1_3.L", "Finger_1_4.L",
    "Finger_2_1.L", "Finger_2_2.L", "Finger_2_3.L",
    "Finger_1_1.R", "Finger_1_2.R", "Finger_1_3.R", "Finger_1_4.R",
    "Finger_2_1.R", "Finger_2_2.R", "Finger_2_3.R",
    "RecorderGrip", "CassetteGrip", "CassetteContact",
    "SupportSleeveCutoff", "CassetteSleeveCutoff",
)
HANDS_CLIPS = {
    "SoulRecorder_Enter": (0, 25, False),
    "SoulRecorder_Insert": (26, 72, False),
    "SoulRecorder_StartExit": (73, 96, False),
    "SoulRecorder_Hold": (97, 109, True),
    "SoulRecorder_StopEnter": (110, 133, False),
    "SoulRecorder_Eject": (134, 175, False),
    "SoulRecorder_StopExit": (176, 199, False),
    "SoulRecorder_CancelInsert": (200, 218, False),
}
HANDS_ANIMATION_FPS = 60
FPS_ARM_FOREARM_CROP_FRACTION = 0.22
FPS_ARM_SLEEVE_REFERENCE_FRAME = 70
FPS_ARM_SLEEVE_EXTENSION_LENGTH = 0.57
FPS_ARM_CUTOFF_MARKERS = {
    "SupportSleeveCutoff": "Arm_2.L",
    "CassetteSleeveCutoff": "Arm_2.R",
}
HANDS_BASE_SHOULDER_LOCATIONS = {}
HANDS_LAST_SOLVED_ROTATIONS = {}
CASSETTE_GRIP_TARGET_KEYS = {}

# Cassette manipulation targets are explicit choreography beats, not samples on
# one carry-to-slot spline.  Coordinates are in the animated-hands rig space.
CASSETTE_ENTRY_TARGET = Vector((0.1750, 0.0000, 1.5000))
CASSETTE_CARRY_TARGET = Vector((0.1750, 0.0000, 1.5000))
CASSETTE_APPROACH_TARGET = Vector((0.1250, 0.0340, 1.5680))
CASSETTE_ALIGNMENT_TARGET = Vector((-0.0020, 0.0430, 1.6140))
CASSETTE_SEATED_TARGET = Vector((-0.0434, 0.0278, 1.5982))
CASSETTE_FIRST_CONTACT_TARGET = CASSETTE_SEATED_TARGET + Vector((0.0300, 0.0, 0.0))
CASSETTE_HALF_PUSH_TARGET = CASSETTE_SEATED_TARGET + Vector((0.0150, 0.0, 0.0))
# Keep the cassette path authoritative while placing the wrist/palm below and to
# the outside of it. CassetteGrip is keyed back to each target after the arm solve,
# producing a real edge pinch instead of placing the cassette at the heel of the
# palm. Coordinates are in animated-hands rig space.
CASSETTE_HAND_FROM_GRIP_OFFSET = Vector((0.0450, 0.0, -0.0520))


def arguments():
    values = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--kind",
        choices=(
            "soulrecorder_fp",
            "soultape_cassette",
            "soulrecorder_animated_hands",
        ),
        required=True,
    )
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--report", required=True)
    return parser.parse_args(values)


def rounded(values):
    return [round(float(value), 6) for value in values]


def scene_meshes():
    return [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]


def object_bounds(objects):
    points = [
        obj.matrix_world @ vertex.co
        for obj in objects
        for vertex in obj.data.vertices
    ]
    if not points:
        raise RuntimeError("No mesh bounds were available")
    minimum = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    maximum = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    return minimum, maximum


def mesh_audit(objects=None):
    meshes = list(objects) if objects is not None else scene_meshes()
    object_rows = []
    for obj in sorted(meshes, key=lambda value: value.name):
        obj.data.calc_loop_triangles()
        minimum, maximum = object_bounds([obj])
        object_rows.append({
            "name": obj.name,
            "vertices": len(obj.data.vertices),
            "polygons": len(obj.data.polygons),
            "triangles": len(obj.data.loop_triangles),
            "materials": [slot.material.name for slot in obj.material_slots if slot.material],
            "localScale": rounded(obj.scale),
            "dimensions": rounded(maximum - minimum),
        })

    audit = {
        "objects": object_rows,
        "objectNames": [row["name"] for row in object_rows],
        "meshObjectCount": len(meshes),
        "vertexCount": sum(row["vertices"] for row in object_rows),
        "polygonCount": sum(row["polygons"] for row in object_rows),
        "triangleCount": sum(row["triangles"] for row in object_rows),
        "materialCount": len({
            slot.material.name
            for obj in meshes
            for slot in obj.material_slots
            if slot.material
        }),
        "cameraCount": sum(obj.type == "CAMERA" for obj in bpy.context.scene.objects),
        "lightCount": sum(obj.type == "LIGHT" for obj in bpy.context.scene.objects),
    }
    if meshes:
        minimum, maximum = object_bounds(meshes)
        audit["overallBounds"] = {
            "minimum": rounded(minimum),
            "maximum": rounded(maximum),
            "dimensions": rounded(maximum - minimum),
        }
    return audit


def remove_scene_helpers():
    removed = []
    for obj in list(bpy.context.scene.objects):
        if obj.type in {"CAMERA", "LIGHT", "SPEAKER", "EMPTY", "FONT"}:
            removed.append(obj.name)
            bpy.data.objects.remove(obj, do_unlink=True)
    return removed


def remove_object(obj, removed):
    removed.append(obj.name)
    bpy.data.objects.remove(obj, do_unlink=True)


def clean_loose_vertices(obj):
    mesh = obj.data
    working = bmesh.new()
    working.from_mesh(mesh)
    loose = [vertex for vertex in working.verts if not vertex.link_edges and not vertex.link_faces]
    count = len(loose)
    if loose:
        bmesh.ops.delete(working, geom=loose, context="VERTS")
        working.to_mesh(mesh)
        mesh.update()
    working.free()
    return count


def vertex_influences(obj, vertex):
    return {
        obj.vertex_groups[group.group].name: float(group.weight)
        for group in vertex.groups
        if group.group < len(obj.vertex_groups)
    }


def arm_side_from_influences(influences, position):
    left = sum(weight for name, weight in influences.items() if name.endswith(".L"))
    right = sum(weight for name, weight in influences.items() if name.endswith(".R"))
    if abs(left - right) > 0.000001:
        return "L" if left > right else "R"
    return "L" if position.x < 0.0 else "R"


def upper_arm_projection(rig, mesh_object, vertex, side):
    rest_bone = rig.data.bones["Arm_1." + side]
    position = rig.matrix_world.inverted() @ mesh_object.matrix_world @ vertex.co
    axis = rest_bone.tail_local - rest_bone.head_local
    if axis.length_squared <= 0.00000001:
        raise RuntimeError("BAMEN upper-arm rest bone had zero length")
    fraction = (position - rest_bone.head_local).dot(axis) / axis.length_squared
    radial = ((position - rest_bone.head_local) - (axis * fraction)).length
    return fraction, radial


def forearm_projection(rig, mesh_object, vertex, side):
    rest_bone = rig.data.bones["Arm_2." + side]
    position = rig.matrix_world.inverted() @ mesh_object.matrix_world @ vertex.co
    axis = rest_bone.tail_local - rest_bone.head_local
    if axis.length_squared <= 0.00000001:
        raise RuntimeError("BAMEN forearm rest bone had zero length")
    fraction = (position - rest_bone.head_local).dot(axis) / axis.length_squared
    radial = ((position - rest_bone.head_local) - (axis * fraction)).length
    return fraction, radial


def hand_weight_region(name):
    if name in {"FPS Arms Root", "SoulRecorderHandsRoot"}:
        return "root/shoulder"
    if name.startswith("Arm_1."):
        return "upperArm"
    if name.startswith("Arm_2."):
        return "forearm"
    if name.startswith("Hand_") or name.startswith("Finger_"):
        return "wristHandFingers"
    return "other"


def audit_hand_mesh_weights(arm, rig):
    region_counts = {}
    dominant_bones = {}
    side_projection_bins = {
        "L": {"proximal0To25": 0, "proximal25To50": 0,
              "distal50To75": 0, "distal75To100": 0, "beyondElbow": 0},
        "R": {"proximal0To25": 0, "proximal25To50": 0,
              "distal50To75": 0, "distal75To100": 0, "beyondElbow": 0},
    }
    region_points = {}
    for vertex in arm.data.vertices:
        influences = vertex_influences(arm, vertex)
        dominant = max(influences, key=influences.get) if influences else "unweighted"
        region = hand_weight_region(dominant)
        dominant_bones[dominant] = dominant_bones.get(dominant, 0) + 1
        region_counts[region] = region_counts.get(region, 0) + 1
        region_points.setdefault(region, []).append(
            rig.matrix_world.inverted() @ arm.matrix_world @ vertex.co)
        side = arm_side_from_influences(influences, vertex.co)
        fraction, _ = upper_arm_projection(rig, arm, vertex, side)
        bins = side_projection_bins[side]
        if fraction < 0.25:
            bins["proximal0To25"] += 1
        elif fraction < 0.50:
            bins["proximal25To50"] += 1
        elif fraction < 0.75:
            bins["distal50To75"] += 1
        elif fraction <= 1.0:
            bins["distal75To100"] += 1
        else:
            bins["beyondElbow"] += 1

    bounds = {}
    for region, points in region_points.items():
        minimum = Vector(tuple(min(point[index] for point in points) for index in range(3)))
        maximum = Vector(tuple(max(point[index] for point in points) for index in range(3)))
        bounds[region] = {
            "minimum": rounded(minimum),
            "maximum": rounded(maximum),
            "dimensions": rounded(maximum - minimum),
        }
    return {
        "vertexCount": len(arm.data.vertices),
        "dominantRegionCounts": region_counts,
        "dominantBoneCounts": dict(sorted(dominant_bones.items())),
        "dominantRegionBoundsArmatureLocal": bounds,
        "upperArmProjectionBins": side_projection_bins,
    }


def derive_fps_cropped_arm_mesh(arm, rig, extension_vectors):
    """Build a skinned FPS sleeve whose termination remains below the camera.

    The source archive and source FBX remain untouched. Retained vertices keep
    their original groups and weights. The proximal source crop is continued
    backward with the same boundary weights/material before being capped; audit
    markers identify that final, deliberately off-screen termination.
    """
    source_vertex_count = len(arm.data.vertices)
    source_triangle_count = len(arm.data.loop_triangles)
    removed_indices = set()
    side_removed = {"L": 0, "R": 0}
    for vertex in arm.data.vertices:
        influences = vertex_influences(arm, vertex)
        side = arm_side_from_influences(influences, vertex.co)
        fraction, _ = forearm_projection(rig, arm, vertex, side)
        # Retain the hand, wrist and distal forearm only. The earlier upper-arm
        # crop still left the elbow and a long limb silhouette visible at runtime.
        # Cropping in forearm rest-bone space removes the entire shoulder/elbow
        # region while preserving the source weights on the useful FPS geometry.
        if fraction < FPS_ARM_FOREARM_CROP_FRACTION:
            removed_indices.add(vertex.index)
            side_removed[side] += 1

    removed_source_faces = sum(
        1 for polygon in arm.data.polygons
        if any(index in removed_indices for index in polygon.vertices))
    working = bmesh.new()
    working.from_mesh(arm.data)
    working.verts.ensure_lookup_table()
    bmesh.ops.delete(
        working,
        geom=[vertex for vertex in working.verts if vertex.index in removed_indices],
        context="VERTS",
    )
    working.verts.ensure_lookup_table()
    working.edges.ensure_lookup_table()
    cutoff_edges_by_side = {"L": [], "R": []}
    for edge in working.edges:
        if len(edge.link_faces) != 1:
            continue
        midpoint = (edge.verts[0].co + edge.verts[1].co) * 0.5
        side = "L" if midpoint.x < 0.0 else "R"
        rest_bone = rig.data.bones["Arm_2." + side]
        axis = rest_bone.tail_local - rest_bone.head_local
        fraction = (midpoint - rest_bone.head_local).dot(axis) / axis.length_squared
        if abs(fraction - FPS_ARM_FOREARM_CROP_FRACTION) <= 0.12:
            cutoff_edges_by_side[side].append(edge)

    extension_vertex_count = 0
    cap_face_count = 0
    for side, cutoff_edges in cutoff_edges_by_side.items():
        if not cutoff_edges:
            raise RuntimeError("FPS sleeve crop did not find the " + side + " boundary loop")
        extruded = bmesh.ops.extrude_edge_only(working, edges=cutoff_edges)
        extension_vertices = [
            value for value in extruded.get("geom", [])
            if isinstance(value, bmesh.types.BMVert)
        ]
        if not extension_vertices:
            raise RuntimeError("FPS sleeve extension did not create " + side + " vertices")
        deform_layer = working.verts.layers.deform.verify()
        root_group = arm.vertex_groups["SoulRecorderHandsRoot"].index
        for vertex in extension_vertices:
            weights = vertex[deform_layer]
            weights.clear()
            weights[root_group] = 1.0
        rest_bone = rig.data.bones["Arm_2." + side]
        axis = rest_bone.tail_local - rest_bone.head_local
        rig_to_mesh = (
            arm.matrix_world.inverted().to_3x3() @ rig.matrix_world.to_3x3()
        )
        # The extension was solved from the authored contact pose back into
        # rest space, so it exits through the appropriate lower screen corner
        # without changing the successful hand/recorder/cassette choreography.
        extension_armature = extension_vectors[side]
        extension_vector = rig_to_mesh @ extension_armature
        bmesh.ops.translate(
            working,
            verts=extension_vertices,
            vec=extension_vector,
        )
        extension_vertex_count += len(extension_vertices)
        extension_set = set(extension_vertices)
        working.edges.ensure_lookup_table()
        extension_end_edges = [
            edge for edge in working.edges
            if len(edge.link_faces) == 1 and
            edge.verts[0] in extension_set and edge.verts[1] in extension_set
        ]
        filled = bmesh.ops.holes_fill(working, edges=extension_end_edges, sides=0)
        for face in filled.get("faces", []):
            face.material_index = 0
        cap_face_count += len(filled.get("faces", []))
    working.to_mesh(arm.data)
    working.free()
    arm.data.update()
    arm.data.calc_loop_triangles()
    if len(arm.data.vertices) <= 0 or len(arm.data.vertices) >= source_vertex_count:
        raise RuntimeError("FPS crop did not produce a smaller usable arm mesh")
    return {
        "strategy": "distal-forearm FPS crop plus contact-pose-solved sleeve continuation",
        "sourceMeshUntouched": True,
        "cutoffFractionFromForearmStart": FPS_ARM_FOREARM_CROP_FRACTION,
        "sleeveExtensionReferenceFrame": FPS_ARM_SLEEVE_REFERENCE_FRAME,
        "sleeveExtensionLength": FPS_ARM_SLEEVE_EXTENSION_LENGTH,
        "sleeveExtensionArmatureRest": {
            side: rounded(value)
            for side, value in extension_vectors.items()
        },
        "extensionVertices": extension_vertex_count,
        "removedVertices": source_vertex_count - len(arm.data.vertices),
        "removedVerticesBySide": side_removed,
        "retainedVertices": len(arm.data.vertices),
        "removedSourcePolygons": removed_source_faces,
        "retainedPolygonsIncludingCaps": len(arm.data.polygons),
        "removedTrianglesNet": source_triangle_count - len(arm.data.loop_triangles),
        "capFaces": cap_face_count,
        "retainedWeights": "original BAMEN weights unchanged; generated off-screen sleeve endpoints follow the hands root",
        "visibleIntent": "hands, wrists, and distal forearms only; elbows and upper arms removed",
    }


def set_principled_color(material, rgba, roughness=0.58, metallic=0.0):
    material.diffuse_color = rgba
    if not material.node_tree:
        return
    for node in list(material.node_tree.nodes):
        if node.type == "TEX_IMAGE":
            material.node_tree.nodes.remove(node)
    principled = next(
        (node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"),
        None,
    )
    if principled and "Base Color" in principled.inputs:
        principled.inputs["Base Color"].default_value = rgba
    if principled and "Roughness" in principled.inputs:
        principled.inputs["Roughness"].default_value = roughness
    if principled and "Metallic" in principled.inputs:
        principled.inputs["Metallic"].default_value = metallic


def create_material(name, rgba, roughness=0.58, metallic=0.0):
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name=name)
    set_principled_color(material, rgba, roughness, metallic)
    return material


def assign_material(obj, material):
    obj.data.materials.clear()
    obj.data.materials.append(material)


def set_origin_to_bounds_center(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")


def split_reels(wheels):
    local_x = [vertex.co.x for vertex in wheels.data.vertices]
    midpoint = (min(local_x) + max(local_x)) * 0.5
    bpy.ops.object.select_all(action="DESELECT")
    wheels.select_set(True)
    bpy.context.view_layer.objects.active = wheels
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="DESELECT")
    bpy.ops.object.mode_set(mode="OBJECT")
    for vertex in wheels.data.vertices:
        vertex.select = vertex.co.x < midpoint
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")

    halves = [obj for obj in bpy.context.selected_objects if obj.type == "MESH"]
    if len(halves) != 2:
        raise RuntimeError("WheelCogs.001 did not split into two physical reel hubs")
    halves.sort(key=lambda obj: (object_bounds([obj])[0].x + object_bounds([obj])[1].x) * 0.5)
    halves[0].name = "ReelLeft"
    halves[1].name = "ReelRight"
    for reel in halves:
        set_origin_to_bounds_center(reel)
        if len(reel.data.vertices) != 510:
            raise RuntimeError(
                reel.name + " did not contain the expected six 85-vertex cog components")
    return halves


def cassette_identity():
    source = {obj.name: obj for obj in scene_meshes()}
    missing = [name for name in CASSETTE_SOURCE_OBJECTS if name not in source]
    if missing:
        raise RuntimeError("Cassette source assembly was missing: " + ", ".join(missing))

    removed = []
    retained_names = set(CASSETTE_SOURCE_OBJECTS)
    for obj in list(scene_meshes()):
        if obj.name not in retained_names:
            remove_object(obj, removed)

    shell = source["CassetteCase_Hero.001"]
    removed_loose_vertices = clean_loose_vertices(shell)
    if removed_loose_vertices != 4:
        raise RuntimeError(
            "Cassette shell cleanup expected four isolated presentation vertices but removed " +
            str(removed_loose_vertices))
    shell.name = "Shell"
    axles = source["Axle.001"]
    axles.name = "Axles"
    tape = source["tape.001"]
    tape.name = "TapeRibbon"
    reels = split_reels(source["WheelCogs.001"])

    dark_shell = create_material("SoulTape Dark Shell", (0.055, 0.065, 0.07, 1.0), 0.62)
    tape_material = create_material("SoulTape Ribbon", (0.018, 0.022, 0.024, 1.0), 0.48)
    amber = create_material("SoulTape Amber Detail", (0.42, 0.245, 0.075, 1.0), 0.46, 0.08)
    assign_material(shell, dark_shell)
    assign_material(tape, tape_material)
    assign_material(axles, amber)
    for reel in reels:
        assign_material(reel, amber)

    return {
        "retainedSourceObjects": [
            {"source": name, "derived": derived}
            for name, derived in CASSETTE_SOURCE_OBJECTS.items()
        ],
        "removedObjects": sorted(removed),
        "removedLooseShellVertices": removed_loose_vertices,
        "reelSourceMapping": {
            "ReelLeft": "negative-X six-cog half of WheelCogs.001",
            "ReelRight": "positive-X six-cog half of WheelCogs.001",
        },
    }


def image_source_basename(image):
    return os.path.basename(image.filepath.replace("\\", "/")) if image else ""


def remove_recorder_branding(image):
    """Clone adjacent unbranded panel pixels over the one real-world wordmark."""
    width = int(image.size[0])
    height = int(image.size[1])
    if width != 2048 or height != 2048:
        raise RuntimeError("Recorder branding cleanup requires the deterministic 2048 atlas")

    # Blender pixel rows count from the bottom. This is top-left atlas rectangle
    # x=740..1099, y=1563..1662 with a 100-pixel sample offset below it.
    x_start, x_end = 740, 1100
    y_start, y_end = 385, 485
    source_y_offset = -100
    feather = 12
    pixels = array("f", [0.0]) * (width * height * 4)
    image.pixels.foreach_get(pixels)
    source = array("f", pixels)
    for y_value in range(y_start, y_end):
        for x_value in range(x_start, x_end):
            edge_distance = min(
                x_value - x_start,
                x_end - 1 - x_value,
                y_value - y_start,
                y_end - 1 - y_value,
            )
            blend = min(1.0, max(0.0, edge_distance / float(feather)))
            destination_index = (y_value * width + x_value) * 4
            source_index = ((y_value + source_y_offset) * width + x_value) * 4
            for channel in range(4):
                original = source[destination_index + channel]
                replacement = source[source_index + channel]
                pixels[destination_index + channel] = (
                    original * (1.0 - blend) + replacement * blend
                )
    image.pixels.foreach_set(pixels)
    image.update()
    return {
        "method": "feathered clone from adjacent unbranded atlas panel",
        "topLeftPixelRectangle": [740, 1563, 360, 100],
        "removedIdentity": "Dixons TR12 Cassette Recorder",
    }


def derive_recorder_textures(input_path, output_folder):
    source_folder = os.path.join(os.path.dirname(input_path), "textures")
    texture_folder = os.path.join(output_folder, "textures")
    os.makedirs(texture_folder, exist_ok=True)
    expected_outputs = set(RECORDER_TEXTURE_SOURCES.values())
    for name in os.listdir(texture_folder):
        if name.startswith("soulrecorder_") and name not in expected_outputs:
            os.remove(os.path.join(texture_folder, name))

    outputs = []
    for source_name, output_name in RECORDER_TEXTURE_SOURCES.items():
        source_path = os.path.join(source_folder, source_name)
        if not os.path.isfile(source_path):
            raise RuntimeError("Required recorder texture was not found: " + source_path)
        image = bpy.data.images.load(source_path, check_existing=False)
        original_dimensions = [int(image.size[0]), int(image.size[1])]
        if image.size[0] > 2048 or image.size[1] > 2048:
            image.scale(2048, 2048)
        branding_cleanup = (
            remove_recorder_branding(image)
            if output_name == "soulrecorder_body_basecolor.png"
            else None
        )
        destination = os.path.join(texture_folder, output_name)
        image.filepath_raw = destination
        image.file_format = "PNG"
        image.save()

        semantic = os.path.splitext(source_name)[0]
        semantic = semantic.replace("cassette_player_body_", "").replace("_4k", "")
        for material in bpy.data.materials:
            if not material.node_tree:
                continue
            for node in material.node_tree.nodes:
                if node.type != "TEX_IMAGE" or node.image is None:
                    continue
                observed = (
                    image_source_basename(node.image) + " " + node.image.name
                ).lower()
                aliases = {
                    "diff": ("diff", "basecolor", "albedo"),
                    "nor_gl": ("nor", "normal"),
                    "metallic": ("metallic", "metal"),
                    "roughness": ("roughness", "rough"),
                    "opacity": ("opacity", "alpha"),
                }[semantic]
                if any(alias in observed for alias in aliases):
                    node.image = image

        outputs.append({
            "name": output_name,
            "relativePath": "textures/" + output_name,
            "source": source_name,
            "sourceDimensions": original_dimensions,
            "dimensions": [int(image.size[0]), int(image.size[1])],
            "format": "PNG",
            "sizeBytes": os.path.getsize(destination),
            "brandingCleanup": branding_cleanup,
        })
    outputs.extend(derive_recorder_runtime_textures(texture_folder))
    return outputs


def load_image_pixels(path):
    image = bpy.data.images.load(path, check_existing=False)
    pixels = array("f", [0.0]) * (int(image.size[0]) * int(image.size[1]) * 4)
    image.pixels.foreach_get(pixels)
    return image, pixels


def save_rgba_texture(texture_folder, name, pixels, width, height, sources, purpose):
    image = bpy.data.images.new(name, width=width, height=height, alpha=True)
    image.pixels.foreach_set(pixels)
    image.update()
    destination = os.path.join(texture_folder, name)
    image.filepath_raw = destination
    image.file_format = "PNG"
    image.save()
    return {
        "name": name,
        "relativePath": "textures/" + name,
        "source": " + ".join(sources),
        "sourceDimensions": [width, height],
        "dimensions": [width, height],
        "format": "PNG",
        "sizeBytes": os.path.getsize(destination),
        "purpose": purpose,
        "brandingCleanup": None,
    }


def derive_recorder_runtime_textures(texture_folder):
    base_path = os.path.join(texture_folder, "soulrecorder_body_basecolor.png")
    opacity_path = os.path.join(texture_folder, "soulrecorder_body_opacity.png")
    metallic_path = os.path.join(texture_folder, "soulrecorder_body_metallic.png")
    roughness_path = os.path.join(texture_folder, "soulrecorder_body_roughness.png")

    base_image, base_pixels = load_image_pixels(base_path)
    opacity_image, opacity_pixels = load_image_pixels(opacity_path)
    width, height = int(base_image.size[0]), int(base_image.size[1])
    if [int(opacity_image.size[0]), int(opacity_image.size[1])] != [width, height]:
        raise RuntimeError("Recorder base-color and opacity dimensions did not match")
    flap_pixels = array("f", base_pixels)
    for index in range(0, len(flap_pixels), 4):
        flap_pixels[index + 3] = opacity_pixels[index]
    flap = save_rgba_texture(
        texture_folder,
        "soulrecorder_flap_basecolor.png",
        flap_pixels,
        width,
        height,
        ["soulrecorder_body_basecolor.png", "soulrecorder_body_opacity.png"],
        "Standard transparent flap base color with authored opacity in alpha",
    )

    metallic_image, metallic_pixels = load_image_pixels(metallic_path)
    roughness_image, roughness_pixels = load_image_pixels(roughness_path)
    if ([int(metallic_image.size[0]), int(metallic_image.size[1])] != [width, height] or
            [int(roughness_image.size[0]), int(roughness_image.size[1])] != [width, height]):
        raise RuntimeError("Recorder metallic/roughness dimensions did not match base color")
    for index in range(0, len(metallic_pixels), 4):
        metallic_pixels[index + 3] = 1.0 - roughness_pixels[index]
    metallic_smoothness = save_rgba_texture(
        texture_folder,
        "soulrecorder_body_metallic_smoothness.png",
        metallic_pixels,
        width,
        height,
        ["soulrecorder_body_metallic.png", "soulrecorder_body_roughness.png"],
        "Unity Standard metallic RGB with inverted roughness in smoothness alpha",
    )
    return [flap, metallic_smoothness]


def recorder_identity(input_path, output_folder):
    removed = []
    for obj in list(scene_meshes()):
        if "cassette_player_tape" in obj.name.lower():
            remove_object(obj, removed)

    body = next((obj for obj in scene_meshes() if "cassette_player_body" in obj.name.lower()), None)
    if body is None:
        raise RuntimeError("Recorder source object cassette_player_body was not found")
    body.name = "SoulRecorderBody"
    return {
        "retainedSourceObjects": [
            {"source": "cassette_player_body", "derived": "SoulRecorderBody"}
        ],
        "removedObjects": sorted(removed),
        "textureOutputs": derive_recorder_textures(input_path, output_folder),
    }


def derive_hand_textures(input_path, output_folder):
    source_folder = os.path.join(os.path.dirname(os.path.dirname(input_path)), "textures")
    texture_folder = os.path.join(output_folder, "textures")
    os.makedirs(texture_folder, exist_ok=True)
    expected = set(HANDS_TEXTURE_SOURCES.values()) | HANDS_DERIVED_TEXTURES
    for name in os.listdir(texture_folder):
        if (name.startswith("soulrecorder_arms_") or
                name.startswith("soulrecorder_hands_")) and name not in expected:
            os.remove(os.path.join(texture_folder, name))

    outputs = []
    copied = {}
    for source_name, output_name in HANDS_TEXTURE_SOURCES.items():
        source_path = os.path.join(source_folder, source_name)
        if not os.path.isfile(source_path):
            raise RuntimeError("Required BAMEN texture was not found: " + source_path)
        destination = os.path.join(texture_folder, output_name)
        shutil.copy2(source_path, destination)
        image = bpy.data.images.load(destination, check_existing=False)
        copied[output_name] = destination
        outputs.append({
            "name": output_name,
            "relativePath": "textures/" + output_name,
            "source": source_name,
            "dimensions": [int(image.size[0]), int(image.size[1])],
            "format": "PNG",
            "sizeBytes": os.path.getsize(destination),
        })

    # The BAMEN arm atlas is bare skin. Painting that atlas olive in Unity made
    # the runtime forearms look brown rather than like tactical sleeves. Convert
    # only the derived SoulPlayer copy to a neutral woven-fabric palette; the
    # licensed source archive and hand/glove atlas remain untouched.
    arms_base_name = "soulrecorder_arms_basecolor.png"
    arms_base_image, arms_base_pixels = load_image_pixels(copied[arms_base_name])
    for index in range(0, len(arms_base_pixels), 4):
        luminance = (
            (arms_base_pixels[index] * 0.2126) +
            (arms_base_pixels[index + 1] * 0.7152) +
            (arms_base_pixels[index + 2] * 0.0722)
        )
        arms_base_pixels[index] = luminance * 0.46
        arms_base_pixels[index + 1] = luminance * 0.50
        arms_base_pixels[index + 2] = luminance * 0.48
    arms_base_image.pixels.foreach_set(arms_base_pixels)
    arms_base_image.update()
    arms_base_image.filepath_raw = copied[arms_base_name]
    arms_base_image.file_format = "PNG"
    arms_base_image.save()

    for prefix in ("arms", "hands"):
        roughness_name = "soulrecorder_" + prefix + "_roughness.png"
        roughness_image, roughness_pixels = load_image_pixels(copied[roughness_name])
        width, height = int(roughness_image.size[0]), int(roughness_image.size[1])
        metallic_pixels = array("f", [0.0]) * len(roughness_pixels)
        for index in range(0, len(metallic_pixels), 4):
            metallic_pixels[index + 3] = 1.0 - roughness_pixels[index]
        output_name = "soulrecorder_" + prefix + "_metallic_smoothness.png"
        outputs.append(save_rgba_texture(
            texture_folder,
            output_name,
            metallic_pixels,
            width,
            height,
            [roughness_name],
            "Unity Standard metallic RGB with inverted roughness in smoothness alpha",
        ))
    return outputs


def add_hand_socket(armature, name, parent_name, offset):
    parent = armature.data.edit_bones.get(parent_name)
    if parent is None:
        raise RuntimeError("BAMEN armature was missing socket parent " + parent_name)
    socket = armature.data.edit_bones.new(name)
    socket.head = parent.tail
    socket.tail = parent.tail + Vector(offset)
    socket.parent = parent
    socket.use_deform = False


def add_fps_cutoff_marker(armature, name, parent_name, extension_vector):
    source_parent = armature.data.edit_bones.get(parent_name)
    root_parent = armature.data.edit_bones.get("SoulRecorderHandsRoot")
    if source_parent is None or root_parent is None:
        raise RuntimeError("BAMEN armature was missing cutoff parent " + parent_name)
    marker = armature.data.edit_bones.new(name)
    axis = source_parent.tail - source_parent.head
    crop_point = source_parent.head + (axis * FPS_ARM_FOREARM_CROP_FRACTION)
    marker.head = crop_point + extension_vector
    marker.tail = marker.head + Vector((0.0, 0.0, 0.025))
    marker.parent = root_parent
    marker.use_deform = False


def key_pose_bone(bone, frame, rotation, location=None):
    bone.rotation_mode = "XYZ"
    bone.rotation_euler = Euler(tuple(math.radians(value) for value in rotation), "XYZ")
    if location is not None:
        bone.location = Vector(location)
        bone.keyframe_insert(data_path="location", frame=frame, group=bone.name)
    bone.keyframe_insert(data_path="rotation_euler", frame=frame, group=bone.name)


def key_cassette_grip_readability(rig, frame, face_roll_degrees):
    # CassetteGrip remains the sole cassette owner. These socket keys only roll
    # the cassette within the practiced pinch: broad face readable during carry,
    # then neutral again before physical slot alignment/contact.
    socket = rig.pose.bones["CassetteGrip"]
    amount = face_roll_degrees / 70.0
    key_pose_bone(socket, frame, (0.0, 0.0, 35.0 * amount))


def smooth_step(value):
    value = max(0.0, min(1.0, value))
    return value * value * (3.0 - (2.0 * value))


def solve_fps_sleeve_extension_vectors(rig):
    """Solve root-owned sleeve endpoints from the authored contact pose."""
    bpy.context.scene.frame_set(FPS_ARM_SLEEVE_REFERENCE_FRAME)
    bpy.context.view_layer.update()
    result = {}
    root_rest = rig.data.bones["SoulRecorderHandsRoot"]
    root_pose = rig.pose.bones["SoulRecorderHandsRoot"]
    root_deformation = (
        root_pose.matrix @ root_rest.matrix_local.inverted()
    )
    for side in ("L", "R"):
        rest_bone = rig.data.bones["Arm_2." + side]
        pose_bone = rig.pose.bones["Arm_2." + side]
        forearm_deformation = pose_bone.matrix @ rest_bone.matrix_local.inverted()
        axis = rest_bone.tail_local - rest_bone.head_local
        crop_rest = rest_bone.head_local + (axis * FPS_ARM_FOREARM_CROP_FRACTION)
        crop_pose = forearm_deformation @ crop_rest
        posed_axis = pose_bone.tail - pose_bone.head
        if posed_axis.length_squared <= 0.00000001:
            raise RuntimeError("BAMEN posed forearm bone had zero length")
        # Extend directly away from the wrist along the forearm. The old fixed
        # downward vector introduced a visible kink at the crop boundary that
        # looked exactly like a second elbow in first person.
        endpoint_pose = crop_pose - (
            posed_axis.normalized() * FPS_ARM_SLEEVE_EXTENSION_LENGTH)
        endpoint_rest = root_deformation.inverted() @ endpoint_pose
        result[side] = endpoint_rest - crop_rest
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    return result


def solve_arm_to_target(
        rig, frame, side, target_position, pole_position, pole_angle_degrees=0.0,
        shoulder_offset=(0.0, 0.0, 0.0)):
    # The original BAMEN hierarchy is retained. Temporary IK is used only as an
    # authoring aid; its evaluated shoulder/elbow/wrist rotations are baked into
    # the exported action and the constraint objects are immediately discarded.
    target = bpy.data.objects.new("SoulRecorder{0}HandIkTarget".format(side), None)
    pole = bpy.data.objects.new("SoulRecorder{0}ElbowIkPole".format(side), None)
    bpy.context.scene.collection.objects.link(target)
    bpy.context.scene.collection.objects.link(pole)
    target.matrix_world = rig.matrix_world @ Matrix.Translation(target_position)
    pole.matrix_world = rig.matrix_world @ Matrix.Translation(pole_position)
    hand = rig.pose.bones["Hand_1." + side]
    upper_arm = rig.pose.bones["Arm_1." + side]
    previous_location_locks = tuple(upper_arm.lock_location)
    # Every authored key starts from the source shoulder location. Otherwise
    # Blender's evaluated interpolation would accumulate this per-frame authoring
    # offset across the master action.
    upper_arm.location = HANDS_BASE_SHOULDER_LOCATIONS[side].copy()
    upper_arm.keyframe_insert(
        data_path="location",
        frame=frame,
        group=upper_arm.name,
    )
    bpy.context.view_layer.update()
    shifted_upper_arm_matrix = upper_arm.matrix.copy()
    shifted_upper_arm_matrix.translation += Vector(shoulder_offset)
    upper_arm.matrix = shifted_upper_arm_matrix
    upper_arm.lock_location = (True, True, True)
    constraint = hand.constraints.new("IK")
    constraint.name = "SoulRecorder{0}HandPoseIK".format(side)
    constraint.target = target
    constraint.pole_target = pole
    constraint.pole_angle = math.radians(pole_angle_degrees)
    constraint.chain_count = 3
    constraint.iterations = 64
    constraint.use_stretch = False
    bpy.context.view_layer.update()

    chain_names = (
        "Arm_1." + side,
        "Arm_2." + side,
        "Hand_1." + side,
    )
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.select_all(action="DESELECT")
    for name in chain_names:
        rig.pose.bones[name].select = True
    rig.data.bones.active = rig.data.bones["Hand_1." + side]
    bpy.ops.pose.visual_transform_apply()
    hand.constraints.remove(constraint)
    upper_arm.lock_location = previous_location_locks
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.data.objects.remove(target, do_unlink=True)
    bpy.data.objects.remove(pole, do_unlink=True)
    bpy.context.view_layer.update()
    for name in chain_names:
        bone = rig.pose.bones[name]
        bone.rotation_mode = "XYZ"
        solved_rotation = bone.rotation_euler.copy()
        previous_rotation = HANDS_LAST_SOLVED_ROTATIONS.get(name)
        if previous_rotation is not None:
            solved_rotation.make_compatible(previous_rotation)
        bone.rotation_euler = solved_rotation
        HANDS_LAST_SOLVED_ROTATIONS[name] = solved_rotation.copy()
        bone.keyframe_insert(
            data_path="rotation_euler",
            frame=frame,
            group=bone.name,
        )
        if name == "Arm_1." + side:
            bone.keyframe_insert(
                data_path="location",
                frame=frame,
                group=bone.name,
            )


def solve_support_arm(rig, frame, support_response):
    # The working hand is high enough to read while the shoulder root remains
    # below frame. The low/outside pole produces a long, restrained lower-forearm
    # silhouette instead of folding the elbow beside the recorder.
    counter_pressure = smooth_step(max(0.0, min(1.0, support_response)))
    # Bias the recorder to the player's right so the supporting forearm can
    # rise diagonally from the lower-left edge instead of forming a vertical
    # column below the recorder.
    working = Vector((0.000, 0.083, 1.600))
    working += Vector((-0.006, 0.003, 0.0045)) * counter_pressure
    solve_arm_to_target(
        rig,
        frame,
        "L",
        working,
        Vector((-0.82, -0.20, 0.86)),
        90.0,
        (-0.44, 0.0, -0.11),
    )


def solve_right_arm_to_cassette_target(
        rig, frame, target_position, interaction_visibility, right_motion):
    # Each manipulation beat supplies its own target.  Blender interpolates the
    # articulated arm between authored carry/approach/alignment/contact/push
    # poses, which gives the path a readable curve without moving an arm root.
    target_position = target_position.copy()
    # Anticipation is a folded arm below the frame, not a translated floating
    # limb. It lets the support hand establish the working pose before the
    # cassette hand enters from the opposite lower side.
    hidden = CASSETTE_ENTRY_TARGET
    target_position = hidden.lerp(
        target_position,
        smooth_step(interaction_visibility),
    )
    # Keep the elbow farther outside during carry/withdrawal, then ease toward
    # the stable contact pole for fine slot alignment. This produces a diagonal
    # lower-right forearm silhouette without moving the cassette targets.
    pole_x = 0.20 + (0.38 * smooth_step(right_motion))
    hand_target = target_position + CASSETTE_HAND_FROM_GRIP_OFFSET
    solve_arm_to_target(
        rig,
        frame,
        "R",
        hand_target,
        Vector((pole_x, 0.25, 0.65)),
        -120.0,
        (0.20, 0.0, -0.25),
    )
    # The hand solve deliberately offsets the palm from the tape. Move only the
    # non-deforming cassette socket back onto the authored target and key that
    # local translation. The visible cassette therefore keeps the exact existing
    # carry/alignment/contact path while the hand grips its outside edge.
    cassette_grip = rig.pose.bones["CassetteGrip"]
    socket_matrix = cassette_grip.matrix.copy()
    socket_matrix.translation = target_position
    cassette_grip.matrix = socket_matrix
    cassette_grip.keyframe_insert(
        data_path="location",
        frame=frame,
        group=cassette_grip.name,
    )
    return target_position.copy()


def interpolate_cassette_grip_target(frame):
    frames = sorted(CASSETTE_GRIP_TARGET_KEYS)
    if not frames:
        raise RuntimeError("Cassette grip target bake had no authored keys")
    if frame <= frames[0]:
        return CASSETTE_GRIP_TARGET_KEYS[frames[0]].copy()
    if frame >= frames[-1]:
        return CASSETTE_GRIP_TARGET_KEYS[frames[-1]].copy()
    for index in range(1, len(frames)):
        right = frames[index]
        if frame > right:
            continue
        left = frames[index - 1]
        if right == left:
            return CASSETTE_GRIP_TARGET_KEYS[right].copy()
        amount = smooth_step((frame - left) / float(right - left))
        return CASSETTE_GRIP_TARGET_KEYS[left].lerp(
            CASSETTE_GRIP_TARGET_KEYS[right], amount)
    raise RuntimeError("Cassette grip target interpolation failed")


def bake_cassette_grip_pose(rig):
    # Parent/hand animation and the compensating socket pose are both curved.
    # Sparse Bezier keys caused intermediate overshoot even though every authored
    # contact beat was exact. Cache the evaluated socket orientation first, then
    # bake one authoritative non-deforming socket pose per frame. The cassette
    # rolls from its readable carry presentation into the final slot orientation
    # before contact instead of inheriting a late IK wrist twist.
    socket = rig.pose.bones["CassetteGrip"]
    evaluated_rotations = {}
    for frame in range(bpy.context.scene.frame_start, bpy.context.scene.frame_end + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        evaluated_rotations[frame] = socket.matrix.to_quaternion().copy()

    aligned_rotation = evaluated_rotations[72]

    def blend_rotation(frame, start_frame, end_frame, start, end):
        amount = smooth_step((frame - start_frame) / float(end_frame - start_frame))
        return start.slerp(end, amount)

    def authored_rotation(frame):
        if 40 < frame < 57:
            return blend_rotation(
                frame, 40, 57, evaluated_rotations[40], aligned_rotation)
        if 57 <= frame <= 80:
            return aligned_rotation.copy()
        if 134 <= frame < 151:
            return blend_rotation(
                frame, 134, 151, evaluated_rotations[134], aligned_rotation)
        if 151 <= frame <= 157:
            return aligned_rotation.copy()
        if 157 < frame < 175:
            return blend_rotation(
                frame, 157, 175, aligned_rotation, evaluated_rotations[175])
        if 200 <= frame <= 208:
            return aligned_rotation.copy()
        if 208 < frame < 218:
            return blend_rotation(
                frame, 208, 218, aligned_rotation, evaluated_rotations[218])
        return evaluated_rotations[frame].copy()

    previous_rotation = None
    for frame in range(bpy.context.scene.frame_start, bpy.context.scene.frame_end + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        socket_matrix = authored_rotation(frame).to_matrix().to_4x4()
        socket_matrix.translation = interpolate_cassette_grip_target(frame)
        socket.matrix = socket_matrix
        socket.rotation_mode = "XYZ"
        socket_rotation = socket.rotation_euler.copy()
        if previous_rotation is not None:
            socket_rotation.make_compatible(previous_rotation)
        socket.rotation_euler = socket_rotation
        previous_rotation = socket_rotation.copy()
        socket.keyframe_insert(
            data_path="location",
            frame=frame,
            group=socket.name,
        )
        socket.keyframe_insert(
            data_path="rotation_euler",
            frame=frame,
            group=socket.name,
        )


def key_hands_pose(
        rig,
        frame,
        visibility,
        right_target=CASSETTE_CARRY_TARGET,
        right_motion=0.0,
        seated=False,
        interaction_visibility=1.0,
        cassette_grip=1.0,
        support_response=0.0):
    root = rig.pose.bones["SoulRecorderHandsRoot"]
    # The rig's shoulder stumps live below the virtual camera even at full
    # visibility. IK keeps the working hands at their authored targets while the
    # lower shoulder origin produces the expected bottom-up first-person entry.
    vertical = -0.10 - (0.50 * (1.0 - visibility))
    forward = -0.14 * (1.0 - visibility)
    key_pose_bone(root, frame, (0.0, 0.0, 0.0), (0.0, forward, vertical))

    # Asymmetric upper/forearm/wrist offsets turn the source rest pose into a
    # restrained two-handed first-person working pose. All values are authored
    # keyframes on the original articulated BAMEN skeleton.
    for side, sign in (("L", -1.0), ("R", 1.0)):
        side_motion = right_motion if side == "R" else 0.0
        support_reaction = support_response if side == "L" else 0.0
        upper_arm_swing = -34.0 if side == "L" else 42.0
        forearm_swing = (
            -38.0 - (2.5 * support_reaction)
            if side == "L"
            else 38.0 + (18.0 * side_motion)
        )
        key_pose_bone(
            rig.pose.bones["Arm_1." + side], frame,
            (10.0 + (4.0 * sign), -18.0 * sign, upper_arm_swing),
        )
        key_pose_bone(
            rig.pose.bones["Arm_2." + side], frame,
            (-18.0 + (7.0 * side_motion), 9.0 * sign, forearm_swing),
        )
        wrist_twist = (
            42.0 + (12.0 * side_motion)
            if side == "R"
            else -10.0 + (2.0 * support_reaction)
        )
        key_pose_bone(
            rig.pose.bones["Hand_1." + side], frame,
            (-8.0 + (6.0 * side_motion), wrist_twist, 12.0 * sign),
        )
        key_pose_bone(
            rig.pose.bones["Hand_2." + side], frame,
            (
                4.0 + (0.10 * support_reaction if side == "L" else 0.0),
                (-6.0 * sign) + (0.20 * support_reaction if side == "L" else 0.0),
                4.0 * sign,
            ),
        )

        # The recorder hand stays wrapped around the body.  The cassette-hand
        # grip is keyed continuously so the thumb/index/middle visibly close
        # before transfer and release only after the cassette is recorder-owned.
        if side == "L":
            finger_curls = {
                1: 0.72,
                2: 1.15,
                3: 1.30,
                4: 1.40,
                5: 1.48,
            }
        else:
            released = 1.0 - max(0.0, min(1.0, cassette_grip))
            finger_curls = {
                # Thumb/index form the actual edge pinch. The previous index and
                # middle curls were so strong that all three digits passed through
                # the cassette face as one claw.
                1: 0.64 - (0.34 * released),
                2: 1.10 - (0.58 * released),
                3: 0.48 - (0.23 * released),
                # Ring and little fingers stay folded into the palm, away from the
                # cassette body, instead of trying to grasp its broad face.
                4: 1.12,
                5: 1.24,
            }
        for finger in range(1, 6):
            segments = 4 if finger == 1 else 3
            for segment in range(1, segments + 1):
                name = "Finger_{0}_{1}.{2}".format(finger, segment, side)
                bone = rig.pose.bones.get(name)
                if bone is None:
                    raise RuntimeError("BAMEN finger bone was missing: " + name)
                amount = finger_curls[finger]
                bend = amount * (24.0 if segment == 1 else 34.0)
                if side == "R":
                    spread = {1: -18.0, 2: 5.0, 3: 2.0, 4: -3.0, 5: -6.0}[finger]
                else:
                    spread = -5.0 * sign if finger == 1 else 0.0
                key_pose_bone(bone, frame, (bend, 0.0, spread))

    solve_support_arm(rig, frame, support_response)
    actual_cassette_target = solve_right_arm_to_cassette_target(
        rig,
        frame,
        right_target,
        interaction_visibility,
        right_motion,
    )
    CASSETTE_GRIP_TARGET_KEYS[frame] = actual_cassette_target


def author_hands_animation(rig):
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    action = bpy.data.actions.new("SoulRecorder_Master")
    rig.animation_data_create()
    rig.animation_data.action = action
    HANDS_LAST_SOLVED_ROTATIONS.clear()
    CASSETTE_GRIP_TARGET_KEYS.clear()
    bpy.context.scene.render.fps = HANDS_ANIMATION_FPS
    bpy.context.scene.frame_start = 0
    bpy.context.scene.frame_end = max(end for _, end, _ in HANDS_CLIPS.values())

    # Enter then insert.  The final insert clip has explicit carry, curved
    # approach, 83ms alignment pause, first contact, 30mm push, and 67ms seat
    # settle.  CassetteGrip retains ownership through frame 72.
    key_hands_pose(rig, 0, 0.0, interaction_visibility=0.0)
    key_hands_pose(
        rig, 13, 0.72, CASSETTE_ENTRY_TARGET, 0.0,
        interaction_visibility=0.45)
    key_hands_pose(
        rig, 25, 1.0, CASSETTE_ENTRY_TARGET, 0.0,
        interaction_visibility=1.0)
    key_hands_pose(
        rig, 26, 1.0, CASSETTE_ENTRY_TARGET, 0.0,
        interaction_visibility=1.0)
    key_hands_pose(
        rig, 34, 1.0, CASSETTE_CARRY_TARGET, 0.12,
        interaction_visibility=1.0)
    key_hands_pose(
        rig, 40, 1.0, CASSETTE_CARRY_TARGET, 0.18,
        interaction_visibility=1.0)
    key_hands_pose(
        rig, 50, 1.0, CASSETTE_APPROACH_TARGET, 0.38,
        interaction_visibility=1.0)
    key_hands_pose(
        rig, 57, 1.0, CASSETTE_ALIGNMENT_TARGET, 1.0,
        interaction_visibility=1.0)
    key_hands_pose(
        rig, 62, 1.0, CASSETTE_FIRST_CONTACT_TARGET, 1.0,
        interaction_visibility=1.0)
    key_hands_pose(
        rig, 66, 1.0, CASSETTE_HALF_PUSH_TARGET, 1.0,
        interaction_visibility=1.0, support_response=0.60)
    key_hands_pose(
        rig, 68, 1.0, CASSETTE_SEATED_TARGET + Vector((0.005, 0.0, 0.0)), 1.0,
        interaction_visibility=1.0, support_response=1.0)
    key_hands_pose(
        rig, 72, 1.0, CASSETTE_SEATED_TARGET, 1.0,
        interaction_visibility=1.0, support_response=0.25)
    # Ownership moves to CassetteSlot only after Insert completes.  Keep the
    # pinch through the transfer frame, then relax it over the first 117ms of
    # StartExit so no release precedes the click/seat beat.
    key_hands_pose(
        rig, 73, 1.0, CASSETTE_SEATED_TARGET, 1.0,
        seated=True, cassette_grip=1.0, support_response=0.25)
    key_hands_pose(
        rig, 77, 0.92, CASSETTE_SEATED_TARGET, 1.0,
        seated=True, cassette_grip=0.55)
    key_hands_pose(
        rig, 80, 0.78, CASSETTE_FIRST_CONTACT_TARGET, 0.76,
        seated=True, cassette_grip=0.0)
    key_hands_pose(
        rig, 84, 0.64, CASSETTE_ALIGNMENT_TARGET, 0.58,
        seated=True, cassette_grip=0.0)
    key_hands_pose(
        rig, 96, 0.0, CASSETTE_CARRY_TARGET, 0.0,
        seated=True, cassette_grip=0.0)

    key_hands_pose(
        rig, 97, 1.0, CASSETTE_SEATED_TARGET, 1.0,
        seated=True, cassette_grip=0.0)
    key_hands_pose(
        rig, 103, 1.0, CASSETTE_SEATED_TARGET, 1.0,
        seated=True, cassette_grip=0.0)
    key_hands_pose(
        rig, 109, 1.0, CASSETTE_SEATED_TARGET, 1.0,
        seated=True, cassette_grip=0.0)

    # Stop/eject: the hand approaches open, finds the seated cassette, closes
    # before movement, draws it 8mm while CassetteSlot still owns it, then the
    # preserve-world transfer occurs at Eject frame 23 (master frame 157).
    key_hands_pose(
        rig, 110, 0.0, CASSETTE_SEATED_TARGET, 1.0,
        seated=True, interaction_visibility=0.0, cassette_grip=0.0)
    key_hands_pose(
        rig, 121, 0.68, CASSETTE_SEATED_TARGET, 1.0,
        seated=True, interaction_visibility=0.0, cassette_grip=0.0)
    key_hands_pose(
        rig, 133, 1.0, CASSETTE_SEATED_TARGET, 1.0,
        seated=True, interaction_visibility=0.0, cassette_grip=0.0)
    key_hands_pose(
        rig, 134, 1.0, CASSETTE_SEATED_TARGET, 1.0,
        seated=True, interaction_visibility=0.0, cassette_grip=0.0)
    key_hands_pose(
        rig, 141, 1.0, CASSETTE_APPROACH_TARGET, 0.58,
        seated=True, interaction_visibility=0.55, cassette_grip=0.0)
    key_hands_pose(
        rig, 147, 1.0, CASSETTE_ALIGNMENT_TARGET, 0.82,
        seated=True, interaction_visibility=1.0, cassette_grip=0.20)
    key_hands_pose(
        rig, 151, 1.0, CASSETTE_SEATED_TARGET, 1.0,
        seated=True, interaction_visibility=1.0, cassette_grip=0.72)
    key_hands_pose(
        rig, 153, 1.0, CASSETTE_SEATED_TARGET, 1.0,
        seated=True, interaction_visibility=1.0, cassette_grip=1.0,
        support_response=0.25)
    key_hands_pose(
        rig, 157, 1.0, CASSETTE_SEATED_TARGET + Vector((0.008, 0.0, 0.0)), 1.0,
        interaction_visibility=1.0, cassette_grip=1.0,
        support_response=1.0)
    key_hands_pose(
        rig, 163, 1.0, CASSETTE_APPROACH_TARGET, 0.55,
        interaction_visibility=1.0, cassette_grip=1.0,
        support_response=0.30)
    key_hands_pose(
        rig, 169, 1.0, CASSETTE_CARRY_TARGET, 0.20,
        interaction_visibility=1.0, cassette_grip=1.0)
    key_hands_pose(
        rig, 175, 1.0, CASSETTE_CARRY_TARGET, 0.0,
        interaction_visibility=1.0, cassette_grip=1.0)
    key_hands_pose(
        rig, 176, 1.0, CASSETTE_CARRY_TARGET, 0.0,
        interaction_visibility=1.0, cassette_grip=1.0)
    key_hands_pose(
        rig, 187, 0.61, CASSETTE_CARRY_TARGET, 0.0,
        interaction_visibility=1.0, cassette_grip=1.0)
    key_hands_pose(
        rig, 199, 0.0, CASSETTE_CARRY_TARGET, 0.0,
        interaction_visibility=0.0, cassette_grip=1.0)

    # Cancellation starts from first contact and reverses through the same
    # alignment/approach beats with hand ownership retained.
    key_hands_pose(
        rig, 200, 1.0, CASSETTE_FIRST_CONTACT_TARGET, 0.76,
        cassette_grip=1.0)
    key_hands_pose(
        rig, 208, 0.78, CASSETTE_ALIGNMENT_TARGET, 0.58,
        cassette_grip=1.0)
    key_hands_pose(
        rig, 218, 0.0, CASSETTE_CARRY_TARGET, 0.0,
        interaction_visibility=0.0, cassette_grip=1.0)

    # Present the broad cassette face at a readable three-quarter carry angle.
    # The socket eases back to its authored neutral orientation before alignment,
    # so contact and the preserve-world transfer into CassetteSlot are unchanged.
    for frame, roll in (
            (0, 70.0), (13, 70.0), (25, 70.0),
            (26, 70.0), (34, 70.0), (40, 70.0),
            (50, 38.0), (57, 0.0), (62, 0.0), (72, 0.0),
            (73, 0.0), (96, 0.0),
            (110, 0.0), (157, 0.0), (163, 38.0),
            (169, 70.0), (175, 70.0), (199, 70.0),
            (200, 0.0), (208, 0.0), (218, 70.0)):
        key_cassette_grip_readability(rig, frame, roll)

    bake_cassette_grip_pose(rig)

    # Blender 5.x stores action curves in layered channel bags; keyframe_insert
    # creates Bezier curves by default, which is the desired eased motion here.
    bpy.context.scene.frame_set(0)
    return [{
        "name": name,
        "firstFrame": first,
        "lastFrame": last,
        "durationSeconds": round((last - first) / HANDS_ANIMATION_FPS, 6),
        "loop": loop,
    } for name, (first, last, loop) in HANDS_CLIPS.items()]


def animation_pose_audit(rig):
    frames = (0, 13, 25, 40, 52, 70, 73, 84, 121, 133, 151, 175, 187, 199)
    names = (
        "Arm_1.L", "SupportSleeveCutoff", "Arm_2.L", "Hand_1.L", "Hand_2.L",
        "Arm_1.R", "CassetteSleeveCutoff", "Arm_2.R", "Hand_1.R", "Hand_2.R",
        "RecorderGrip", "CassetteGrip", "CassetteContact",
    )
    result = {}
    for frame in frames:
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        result[str(frame)] = {
            name: rounded(rig.pose.bones[name].matrix.translation)
            for name in names
        }
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    return result


def prepare_hands(input_path, output_folder):
    meshes = scene_meshes()
    rigs = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    if len(meshes) != 1 or len(rigs) != 1:
        raise RuntimeError("BAMEN source must contain exactly one mesh and one armature")
    arm = meshes[0]
    rig = rigs[0]
    if len(arm.data.vertices) != 6942:
        raise RuntimeError("BAMEN source vertex count changed unexpectedly")
    arm.data.calc_loop_triangles()
    if len(arm.data.loop_triangles) != 13728:
        raise RuntimeError("BAMEN source triangle count changed unexpectedly")
    if not any(modifier.type == "ARMATURE" and modifier.object == rig for modifier in arm.modifiers):
        raise RuntimeError("BAMEN FPS Arms mesh was not skinned to its armature")

    source_actions = [action.name for action in bpy.data.actions]
    source_root = rig.data.bones.get("FPS Arms Root")
    if source_root is None or source_root.parent is not None:
        raise RuntimeError("BAMEN source did not retain its single FPS Arms Root bone")
    source_root.name = "SoulRecorderHandsRoot"
    source_root_group = arm.vertex_groups.get("FPS Arms Root")
    if source_root_group is not None:
        source_root_group.name = "SoulRecorderHandsRoot"
    elif arm.vertex_groups.get("SoulRecorderHandsRoot") is None:
        arm.vertex_groups.new(name="SoulRecorderHandsRoot")
    rig.name = "SoulRecorderHandsRig"
    rig.data.name = "SoulRecorderHandsRig"
    arm.name = "SoulRecorderArms"

    authored_bones = {
        "RecorderGrip", "CassetteGrip", "CassetteContact",
        "SupportSleeveCutoff", "CassetteSleeveCutoff",
    }
    missing = [
        name for name in HANDS_REQUIRED_BONES
        if name not in authored_bones and rig.data.bones.get(name) is None
    ]
    if missing:
        raise RuntimeError("BAMEN articulated skeleton was incomplete: " + ", ".join(missing))

    HANDS_BASE_SHOULDER_LOCATIONS.clear()
    for side in ("L", "R"):
        HANDS_BASE_SHOULDER_LOCATIONS[side] = \
            rig.pose.bones["Arm_1." + side].location.copy()

    source_weight_audit = audit_hand_mesh_weights(arm, rig)

    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    add_hand_socket(rig, "RecorderGrip", "Hand_2.L", (0.0, 0.045, 0.0))
    add_hand_socket(rig, "CassetteGrip", "Hand_1.R", (0.0, 0.045, 0.0))
    add_hand_socket(rig, "CassetteContact", "Finger_2_2.R", (0.0, 0.025, 0.0))
    bpy.ops.object.mode_set(mode="OBJECT")

    texture_outputs = derive_hand_textures(input_path, output_folder)
    clips = author_hands_animation(rig)
    extension_vectors = solve_fps_sleeve_extension_vectors(rig)
    fps_crop = derive_fps_cropped_arm_mesh(arm, rig, extension_vectors)

    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for marker_name, parent_name in FPS_ARM_CUTOFF_MARKERS.items():
        side = "L" if parent_name.endswith(".L") else "R"
        add_fps_cutoff_marker(
            rig, marker_name, parent_name, extension_vectors[side])
    bpy.ops.object.mode_set(mode="OBJECT")

    pose_audit = animation_pose_audit(rig)

    for obj in list(bpy.context.scene.objects):
        if obj not in {rig, arm}:
            bpy.data.objects.remove(obj, do_unlink=True)

    return rig, {
        "retainedSourceObjects": [
            {"source": "FPS Arms", "derived": "SoulRecorderArms"},
            {"source": "FRPS Arms Armature", "derived": "SoulRecorderHandsRig"},
        ],
        "removedObjects": ["source scene animation take"],
        "sourceActions": source_actions,
        "textureOutputs": texture_outputs,
        "sourceWeightAudit": source_weight_audit,
        "fpsCrop": fps_crop,
        "runtimeBones": [bone.name for bone in rig.data.bones],
        "animationClips": clips,
        "animationPoseAuditArmatureLocal": pose_audit,
        "socketParents": {
            "RecorderGrip": "Hand_2.L",
            "CassetteGrip": "Hand_1.R",
            "CassetteContact": "Finger_2_2.R",
            "SupportSleeveCutoff": "SoulRecorderHandsRoot",
            "CassetteSleeveCutoff": "SoulRecorderHandsRoot",
        },
    }


def apply_rotation_and_scale(objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)


def normalize_asset(kind):
    meshes = scene_meshes()
    if not meshes:
        raise RuntimeError("No mesh objects remained after source cleanup")
    minimum, maximum = object_bounds(meshes)
    center = (minimum + maximum) * 0.5
    dimensions = maximum - minimum
    if min(dimensions) <= 0:
        raise RuntimeError("Prepared asset bounds were empty")

    if kind == "soultape_cassette":
        scale_values = Vector((
            CASSETTE_TARGET_DIMENSIONS.x / dimensions.x,
            CASSETTE_TARGET_DIMENSIONS.y / dimensions.y,
            CASSETTE_TARGET_DIMENSIONS.z / dimensions.z,
        ))
        axis_mapping = {
            "sourceX": "width",
            "sourceY": "height",
            "sourceZ": "depth",
        }
        root_name = "SoulTapeCassette"
    else:
        uniform_scale = 0.20 / max(dimensions)
        scale_values = Vector((uniform_scale, uniform_scale, uniform_scale))
        axis_mapping = {
            "sourceX": "recorder horizontal",
            "sourceY": "recorder longest dimension",
            "sourceZ": "recorder depth",
        }
        root_name = "SoulRecorderModel"

    normalization = Matrix.Diagonal((*scale_values, 1.0)) @ Matrix.Translation(-center)
    if kind == "soulrecorder_fp":
        # Bake the accepted source world orientation directly into the single
        # recorder mesh. This preserves its visible orientation while producing
        # identity object transforms for FBX.
        for obj in meshes:
            obj.data.transform(normalization @ obj.matrix_world)
            obj.matrix_world = Matrix.Identity(4)
            obj.data.update()
    else:
        for obj in meshes:
            obj.matrix_world = normalization @ obj.matrix_world
        apply_rotation_and_scale(meshes)

    root = bpy.data.objects.new(root_name, None)
    bpy.context.scene.collection.objects.link(root)
    for obj in meshes:
        obj.parent = root
    return root, {
        "axisMapping": axis_mapping,
        "sourceBoundsDimensions": rounded(dimensions),
        "appliedScale": rounded(scale_values),
    }


def add_cassette_label(root):
    shell = next(obj for obj in scene_meshes() if obj.name == "Shell")
    _, shell_maximum = object_bounds([shell])
    label_root = bpy.data.objects.new("Label", None)
    bpy.context.scene.collection.objects.link(label_root)
    label_root.parent = root
    material = create_material("SoulTape Aged Label", (0.64, 0.55, 0.37, 1.0), 0.72)
    panels = []
    for name, y_value in (("LabelUpper", 0.025), ("LabelLower", -0.022)):
        bpy.ops.mesh.primitive_cube_add(
            location=(0.0, y_value, shell_maximum.z + 0.0001),
            scale=(0.041, 0.006, 0.0001),
        )
        panel = bpy.context.active_object
        panel.name = name
        apply_rotation_and_scale([panel])
        assign_material(panel, material)
        panel.parent = label_root
        panels.append(panel)
    return label_root, panels


def descendants(root):
    result = []
    pending = list(root.children)
    while pending:
        current = pending.pop()
        result.append(current)
        pending.extend(current.children)
    return result


def required_transform_report(kind, root):
    if kind == "soultape_cassette":
        required = ["SoulTapeCassette", "Shell", "Label", "ReelLeft", "ReelRight"]
    elif kind == "soulrecorder_animated_hands":
        required = [
            "SoulRecorderHandsRig",
            "SoulRecorderHandsRoot",
            "SoulRecorderArms",
            "RecorderGrip",
            "CassetteGrip",
            "CassetteContact",
            "SupportSleeveCutoff",
            "CassetteSleeveCutoff",
        ]
    else:
        required = ["SoulRecorderModel", "SoulRecorderBody"]
    names = [root.name] + [obj.name for obj in descendants(root)]
    if kind == "soulrecorder_animated_hands" and root.type == "ARMATURE":
        names.extend(bone.name for bone in root.data.bones)
    counts = {name: names.count(name) for name in required}
    report = {
        "stage": "Blender FBX",
        "required": required,
        "counts": counts,
        "allUnique": all(value == 1 for value in counts.values()),
    }
    if kind == "soulrecorder_fp":
        report["addedByUnityPrefabBuilder"] = [
            "CassetteInsertionStart",
            "CassetteSlot",
            "CassetteWindow",
            "StatusLed",
            "ReelWindowLeft",
            "ReelWindowRight",
        ]
    return report


def reel_pivot_report():
    result = {}
    for name in ("ReelLeft", "ReelRight"):
        reel = next(obj for obj in scene_meshes() if obj.name == name)
        minimum, maximum = object_bounds([reel])
        bounds_center = (minimum + maximum) * 0.5
        pivot = reel.matrix_world.translation
        result[name] = {
            "pivot": rounded(pivot),
            "boundsCenter": rounded(bounds_center),
            "pivotOffsetMeters": round((pivot - bounds_center).length, 8),
            "rotationReady": (pivot - bounds_center).length <= 0.00001,
        }
    return result


def export_fbx(root, kind, output_folder):
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for obj in descendants(root):
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    output_name = (
        "soulrecorder_bamen_rig.fbx"
        if kind == "soulrecorder_animated_hands"
        else kind + ".fbx"
    )
    destination = os.path.join(output_folder, output_name)
    animated = kind == "soulrecorder_animated_hands"
    bpy.ops.export_scene.fbx(
        filepath=destination,
        use_selection=True,
        apply_unit_scale=True,
        # Store Blender's meter-to-FBX conversion in the file-level scale rather
        # than emitting 100x transforms on the armature and skinned mesh. Unity
        # then imports a physically identical rig with a unit-scale hierarchy,
        # which is essential for hand-bone sockets carrying recorder props.
        apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=False,
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        bake_anim=animated,
        bake_anim_use_all_bones=animated,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=animated,
        bake_anim_force_startend_keying=animated,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        path_mode="RELATIVE",
    )
    return destination


def validate_derived(kind, final_audit, transforms, texture_outputs, fbx_path):
    errors = []
    warnings = []
    if not transforms["allUnique"]:
        errors.append("Required Blender-stage transforms were missing or duplicated")

    for obj in final_audit["objects"]:
        if any(abs(value - 1.0) > 0.001 for value in obj["localScale"]):
            errors.append(obj["name"] + " has a non-unit final local scale")
        maximum_dimension = 2.20 if kind == "soulrecorder_animated_hands" else 0.25
        if any(value < 0 or value > maximum_dimension for value in obj["dimensions"]):
            errors.append(obj["name"] + " has an obviously invalid final dimension")

    if kind == "soultape_cassette":
        actual = Vector(final_audit["overallBounds"]["dimensions"])
        differences = Vector((
            abs(actual.x - CASSETTE_TARGET_DIMENSIONS.x),
            abs(actual.y - CASSETTE_TARGET_DIMENSIONS.y),
            abs(actual.z - CASSETTE_TARGET_DIMENSIONS.z),
        ))
        if any(differences[index] > CASSETTE_DIMENSION_TOLERANCE[index] for index in range(3)):
            errors.append(
                "Cassette bounds were outside 110x70x18 mm tolerance: " +
                str(rounded(actual)))
        if final_audit["vertexCount"] > 10000:
            warnings.append("Cassette exceeds the preferred 10,000-vertex ceiling")
        if final_audit["meshObjectCount"] > 8:
            warnings.append("Cassette contains more than eight mesh objects")
    elif kind == "soulrecorder_fp":
        expected = set(RECORDER_TEXTURE_SOURCES.values()) | RECORDER_DERIVED_TEXTURES
        actual = {entry["name"] for entry in texture_outputs}
        if actual != expected:
            errors.append("Recorder texture output set was incomplete")
        for texture in texture_outputs:
            if texture["dimensions"] != [2048, 2048] or texture["format"] != "PNG":
                errors.append(texture["name"] + " was not a 2048x2048 PNG")
    else:
        expected = set(HANDS_TEXTURE_SOURCES.values()) | HANDS_DERIVED_TEXTURES
        actual = {entry["name"] for entry in texture_outputs}
        if actual != expected:
            errors.append("Hands texture output set was incomplete")
        if final_audit["meshObjectCount"] != 1:
            errors.append("Hands runtime asset must contain one skinned mesh")
        # The conventional FPS staging removes substantially more proximal
        # upper-arm geometry than the first cropped pass. Keep this guard broad
        # enough for that reviewed crop while still catching a missing sleeve,
        # an uncropped source mesh, or accidental mesh duplication.
        if final_audit["vertexCount"] <= 5800 or final_audit["vertexCount"] >= 7500:
            errors.append(
                "FPS sleeve continuation vertex count was outside the reviewed range")

    if not os.path.isfile(fbx_path) or os.path.getsize(fbx_path) <= 0:
        errors.append("Derived FBX was missing or empty")
    return {
        "passed": not errors,
        "errors": errors,
        "warnings": warnings,
    }


def main():
    args = arguments()
    os.makedirs(args.output, exist_ok=True)
    if args.kind == "soulrecorder_animated_hands":
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=args.input, automatic_bone_orientation=False)
    else:
        bpy.ops.wm.open_mainfile(filepath=args.input)
    before = mesh_audit()
    if args.kind == "soulrecorder_animated_hands":
        root, preparation = prepare_hands(args.input, args.output)
        helper_objects = []
        normalization = {
            "axisMapping": {
                "sourceX": "screen horizontal",
                "sourceY": "camera-forward arm reach",
                "sourceZ": "camera-local vertical",
            },
            "appliedScale": [1.0, 1.0, 1.0],
            "holdingPose": "authored on the complete BAMEN skeleton with a derived FPS-cropped arm mesh",
        }
        after_cleanup = mesh_audit()
    else:
        helper_objects = remove_scene_helpers()
        if args.kind == "soulrecorder_fp":
            preparation = recorder_identity(args.input, args.output)
        else:
            preparation = cassette_identity()
            preparation["textureOutputs"] = []
        preparation["removedSceneHelpers"] = sorted(helper_objects)
        after_cleanup = mesh_audit()
        root, normalization = normalize_asset(args.kind)
        if args.kind == "soultape_cassette":
            add_cassette_label(root)
    final_audit = mesh_audit()
    transforms = required_transform_report(args.kind, root)
    reel_pivots = reel_pivot_report() if args.kind == "soultape_cassette" else {}
    output = export_fbx(root, args.kind, args.output)
    texture_outputs = preparation.get("textureOutputs", [])
    validation = validate_derived(
        args.kind,
        final_audit,
        transforms,
        texture_outputs,
        output,
    )
    report = {
        "logicalName": args.kind,
        "preparedWith": "Blender " + bpy.app.version_string,
        "sourceArchiveEntry": os.path.basename(args.input),
        "before": before,
        "afterCleanup": after_cleanup,
        "normalization": normalization,
        "finalDerived": final_audit,
        "requiredTransforms": transforms,
        "reelPivots": reel_pivots,
        "retainedSourceObjects": preparation.get("retainedSourceObjects", []),
        "removedObjects": preparation.get("removedObjects", []),
        "removedSceneHelpers": preparation.get("removedSceneHelpers", []),
        "removedLooseShellVertices": preparation.get("removedLooseShellVertices", 0),
        "reelSourceMapping": preparation.get("reelSourceMapping", {}),
        "textureOutputs": texture_outputs,
        "animationClips": preparation.get("animationClips", []),
        "animationPoseAuditArmatureLocal": preparation.get(
            "animationPoseAuditArmatureLocal", {}),
        "runtimeBones": preparation.get("runtimeBones", []),
        "socketParents": preparation.get("socketParents", {}),
        "sourceWeightAudit": preparation.get("sourceWeightAudit", {}),
        "fpsCrop": preparation.get("fpsCrop", {}),
        "derivedOutput": os.path.basename(output),
        "derivedFbxSizeBytes": os.path.getsize(output),
        "validation": validation,
    }
    with open(args.report, "w", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2)

    if not validation["passed"]:
        raise RuntimeError("Derived asset validation failed: " + "; ".join(validation["errors"]))


if __name__ == "__main__":
    main()
