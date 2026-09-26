"""
验证 + 预览渲染：把生成的四个角色 FBX 逐个导回来，检查骨架/动画/权重，并各渲一张图。

用法：
    python Tools/Blender/mcp_client.py exec Tools/Blender/verify_characters.py

产出：
    Preview/char_<name>.png   （每个角色一张，避免互相遮挡）
    Logs/character_verify.txt （UTF-8 报告；MCP 回传的 stdout 在 Windows 上中文会乱码）
"""

import math
import os

import bpy

CHARS = ["black_sphere", "red_bundle", "brown_cracker", "gray_shell"]
FBX_DIR = os.path.join("Assets", "Resources", "BombTavern", "Characters")


def clear():
    """
    彻底清场。

    注意：FBX 导入器会给动作/网格等数据加上 fake user（为了不让它们被自动回收），
    所以「users == 0 才删」是不够的 —— 经 MCP 连续跑多个脚本时它们会一直累积，
    报告里的动作数就会 3、4、5、6 一路涨。这里强制清掉 fake user 再删。
    """
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)

    for coll in (bpy.data.actions, bpy.data.meshes, bpy.data.armatures,
                 bpy.data.materials, bpy.data.lights, bpy.data.cameras,
                 bpy.data.worlds):
        for item in list(coll):
            try:
                item.use_fake_user = False
            except AttributeError:
                pass
            coll.remove(item)


def pick_engine():
    for name in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_WORKBENCH"):
        try:
            bpy.context.scene.render.engine = name
            return name
        except Exception:                                # noqa: BLE001
            continue
    return "?"


def setup_stage():
    """摆相机、三点光、世界背景。每个角色单独渲染，所以每轮都重建。"""
    cam_data = bpy.data.cameras.new("VerifyCam")
    cam = bpy.data.objects.new("VerifyCam", cam_data)
    bpy.context.collection.objects.link(cam)
    # 站远一点，保证帽子和靴子都在画面里（角色总高约 1.7 米）
    cam.location = (0.0, -3.35, 0.82)
    cam.rotation_euler = (math.radians(90), 0, 0)
    cam_data.lens = 50
    bpy.context.scene.camera = cam

    # 必须用 Standard 色彩变换。
    # Blender 4.x/5.x 默认是 AgX，会把亮部大幅去饱和 ——
    # 红色会渲成粉色、深色会渲成灰色，看起来像「材质丢了」，其实是色调映射的锅。
    try:
        bpy.context.scene.view_settings.view_transform = "Standard"
    except Exception:                                    # noqa: BLE001
        pass

    # 光强要和 Standard 色彩变换配套。
    # 原来按 AgX 调的 300+ 瓦在 Standard 下会整片过曝成白色，
    # 看起来像「材质颜色没导进 FBX」，其实只是曝光问题。
    for loc, energy, size in (((-1.5, -1.8, 1.7), 55, 2.2),
                              ((1.7, -1.4, 1.3), 38, 2.2),
                              ((0.0, 2.0, 1.4), 26, 3.0)):
        light_data = bpy.data.lights.new("VerifyLight", type="AREA")
        light_data.energy = energy
        light_data.size = size
        light = bpy.data.objects.new("VerifyLight", light_data)
        bpy.context.collection.objects.link(light)
        light.location = loc
        light.rotation_euler = (-light.location).to_track_quat("-Z", "Y").to_euler()

    world = bpy.data.worlds.new("VerifyWorld")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.07, 0.07, 0.09, 1.0)
        bg.inputs[1].default_value = 1.0
    bpy.context.scene.world = world


def main():
    report = []
    os.makedirs("Preview", exist_ok=True)

    for name in CHARS:
        path = os.path.join(FBX_DIR, f"Bomb_{name}.fbx")
        if not os.path.exists(path):
            report.append(f"{name}: 文件缺失")
            continue

        # 每个角色都在干净场景里单独渲染。
        # 四个一起摆必然互相遮挡，间距也很难调，逐个出图才看得清轮廓。
        clear()
        bpy.ops.import_scene.fbx(filepath=path)

        arm = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
        mesh = next((o for o in bpy.data.objects if o.type == "MESH"), None)

        bones = len(arm.data.bones) if arm else 0
        groups = len(mesh.vertex_groups) if mesh else 0
        verts = len(mesh.data.vertices) if mesh else 0
        weighted = sum(1 for v in mesh.data.vertices if len(v.groups) > 0) if mesh else 0
        acts = sorted({a.name.split("|")[-1] for a in bpy.data.actions})

        report.append(f"{name}: 骨骼={bones} 顶点组={groups} 顶点={verts} "
                      f"已加权={weighted} 动作={acts}")

        setup_stage()
        scene = bpy.context.scene
        scene.render.resolution_x = 900
        scene.render.resolution_y = 900
        scene.render.image_settings.file_format = "PNG"
        scene.render.filepath = os.path.abspath(os.path.join("Preview", f"char_{name}.png"))
        bpy.ops.render.render(write_still=True)

    lines = ["=== 角色校验 ==="] + report + [f"渲染引擎：{pick_engine()}"]
    os.makedirs("Logs", exist_ok=True)
    with open(os.path.join("Logs", "character_verify.txt"), "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")

    print("\n".join(lines))


main()
