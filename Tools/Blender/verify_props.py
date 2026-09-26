"""道具模型校验 + 一览渲染。

用法：
    python Tools/Blender/mcp_client.py exec Tools/Blender/verify_props.py

产出：
    Preview/props_v3.png
    Logs/prop_verify.txt
"""

import math
import os

import bpy

DIR = os.path.join("Assets", "Resources", "BombTavern", "Items")


def clear():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.actions, bpy.data.materials,
                 bpy.data.lights, bpy.data.cameras, bpy.data.worlds):
        for item in list(coll):
            try:
                item.use_fake_user = False
            except AttributeError:
                pass
            coll.remove(item)


def main():
    clear()

    files = sorted(f for f in os.listdir(DIR) if f.endswith(".fbx")) if os.path.isdir(DIR) else []
    report = []

    # 排成一排，间距按道具尺度给足，避免互相遮挡
    spacing = 0.30
    x = -spacing * (len(files) - 1) * 0.5

    for name in files:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(DIR, name))
        new = [o for o in bpy.data.objects if o not in before]

        roots = [o for o in new if o.parent is None] or \
                [o for o in new if o.type == "ARMATURE"] or new
        for root in roots:
            root.location = (x, 0, 0)

        mesh = next((o for o in new if o.type == "MESH"), None)
        report.append(f"{name}: 顶点={len(mesh.data.vertices) if mesh else 0}")
        x += spacing

    # ---- 相机（正对一排道具）
    cam_data = bpy.data.cameras.new("PropCam")
    cam = bpy.data.objects.new("PropCam", cam_data)
    bpy.context.collection.objects.link(cam)
    cam.location = (0.0, -1.9, 0.16)
    cam.rotation_euler = (math.radians(90), 0, 0)
    cam_data.lens = 45
    bpy.context.scene.camera = cam

    # ---- 灯光（配合 Standard 色彩变换，能量要低）
    for loc, energy, size in (((-1.2, -1.4, 1.0), 45, 1.8),
                              ((1.4, -1.1, 0.8), 32, 1.8),
                              ((0.0, 1.6, 0.9), 22, 2.4)):
        light_data = bpy.data.lights.new("PropLight", type="AREA")
        light_data.energy = energy
        light_data.size = size
        light = bpy.data.objects.new("PropLight", light_data)
        bpy.context.collection.objects.link(light)
        light.location = loc
        light.rotation_euler = (-light.location).to_track_quat("-Z", "Y").to_euler()

    world = bpy.data.worlds.new("PropWorld")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.07, 0.07, 0.09, 1.0)

    scene = bpy.context.scene
    bpy.context.scene.world = world
    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_WORKBENCH"):
        try:
            scene.render.engine = engine
            break
        except Exception:                                # noqa: BLE001
            continue
    try:
        scene.view_settings.view_transform = "Standard"
    except Exception:                                    # noqa: BLE001
        pass

    scene.render.resolution_x = 1400
    scene.render.resolution_y = 700
    scene.render.image_settings.file_format = "PNG"
    os.makedirs("Preview", exist_ok=True)
    scene.render.filepath = os.path.abspath(os.path.join("Preview", "props_v3.png"))
    bpy.ops.render.render(write_still=True)

    os.makedirs("Logs", exist_ok=True)
    with open(os.path.join("Logs", "prop_verify.txt"), "w", encoding="utf-8") as handle:
        handle.write("\n".join(report) + "\n")
    print("\n".join(report))


main()
