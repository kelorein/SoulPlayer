"""Print viable local upper-arm/elbow axes for the prop-free FOV-70 test."""

import math
from pathlib import Path

import bpy
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Vector


root = Path(__file__).resolve().parents[1]
fbx = root / "Staging" / "DJMaesenFirstPersonArms" / "source" / "fpsarms.fbx"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(fbx))
armature = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")

camera_data = bpy.data.cameras.new("ProbeCamera")
camera = bpy.data.objects.new("ProbeCamera", camera_data)
bpy.context.collection.objects.link(camera)
camera.data.angle = math.radians(70.0)
camera.location = (0.0, 0.23, 0.025)
camera.rotation_euler = (
    Vector((0.0, -0.22, 0.0)) - camera.location
).to_track_quat("-Z", "Y").to_euler()
bpy.context.scene.camera = camera


def reset():
    for bone in armature.pose.bones:
        bone.rotation_mode = "XYZ"
        bone.rotation_euler = (0.0, 0.0, 0.0)
        bone.location = (0.0, 0.0, 0.0)
        bone.scale = (1.0, 1.0, 1.0)
    bpy.context.view_layer.update()


for side, target_x in (("L", 0.37), ("R", 0.63)):
    rows = []
    for upper_axis in range(3):
        for upper_sign in (-1, 1):
            for elbow_axis in range(3):
                for elbow_sign in (-1, 1):
                    reset()
                    armature.pose.bones[f"{side}_arm"].rotation_euler[
                        upper_axis
                    ] = math.radians(20.0 * upper_sign)
                    armature.pose.bones[f"{side}_elbow"].rotation_euler[
                        elbow_axis
                    ] = math.radians(60.0 * elbow_sign)
                    bpy.context.view_layer.update()
                    elbow = armature.matrix_world @ armature.pose.bones[
                        f"{side}_elbow"
                    ].head
                    wrist = armature.matrix_world @ armature.pose.bones[
                        f"{side}_wrist"
                    ].head
                    elbow_view = world_to_camera_view(
                        bpy.context.scene, camera, elbow
                    )
                    wrist_view = world_to_camera_view(
                        bpy.context.scene, camera, wrist
                    )
                    in_frame = (
                        0.02 < elbow_view.x < 0.98
                        and 0.02 < elbow_view.y < 0.98
                        and 0.02 < wrist_view.x < 0.98
                        and 0.02 < wrist_view.y < 0.98
                        and elbow_view.z > 0.0
                        and wrist_view.z > 0.0
                    )
                    score = (
                        abs(wrist_view.x - target_x)
                        + abs(wrist_view.y - 0.47)
                        + 0.35 * abs(elbow_view.y - 0.42)
                        + (0.0 if in_frame else 10.0)
                    )
                    rows.append(
                        (
                            score,
                            "XYZ"[upper_axis],
                            upper_sign,
                            "XYZ"[elbow_axis],
                            elbow_sign,
                            tuple(round(value, 4) for value in elbow_view),
                            tuple(round(value, 4) for value in wrist_view),
                        )
                    )
    print(f"\n{side} TOP AXIS COMBINATIONS")
    for row in sorted(rows)[:12]:
        print(row)
