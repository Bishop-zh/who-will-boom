"""
炸弹酒馆 —— 道具模型生成器（Blender 5.x，经 MCP 或 --background 执行）

用法：
    python Tools/Blender/mcp_client.py exec Tools/Blender/build_item_props.py

产出：
    Assets/LiarsBar/Game/Items/Prop_<Effect>.fbx   （8 个）

约定：
    * 原点设在**握持点**（手柄底部 / 掌心处），这样挂到角色手骨上时
      只需要一个很小的偏移，不用为每个道具单独调位置。
    * +Z 是「朝外」方向（镜片正面、手电前端），+Y 朝上，与角色朝向一致。
    * 尺度统一在 0.06~0.14 米之间 —— 角色身高约 0.9 米，道具太大就像举着棒槌。

为什么按「效果类型」而不是「道具条目」建模：
    道具目录是可配置的，策划可以加十几种道具，但效果类型是有限的枚举。
    按效果类型建模，新增道具只要复用已有效果就自动有模型。
"""

import math
import os

import bpy

# 与角色一致：产出到 Resources 下，运行时用 Resources.Load 取
OUT_DIR = os.path.join("Assets", "Resources", "BombTavern", "Items")

# 与 Unity 侧 ItemEffectType 枚举同名
EFFECTS = [
    "ProbeCell", "ScanRow", "ScanArea", "DecreaseRisk",
    "IncreaseRisk", "Shield", "FreezeDrift", "RevealNeighbors", "SkipTurn",
]


# ============================================================ 工具
def clear():
    """手动清场。不能用 read_factory_settings —— 它会重置用户偏好，把 MCP 插件一起关掉。"""
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.actions, bpy.data.materials,
                 bpy.data.curves):
        for item in list(coll):
            try:
                item.use_fake_user = False
            except AttributeError:
                pass
            coll.remove(item)


def mat(name, rgb, rough=0.5, metal=0.0, emit=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
        if "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = (
                rgb[0] * emit, rgb[1] * emit, rgb[2] * emit, 1.0)
        if "Emission Strength" in bsdf.inputs:
            bsdf.inputs["Emission Strength"].default_value = 1.0 if emit else 0.0
    return m


def use(obj, m):
    obj.data.materials.clear()
    obj.data.materials.append(m)
    return obj


def sphere(loc, rad, scale=(1, 1, 1), rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=rad, segments=20, ring_count=12,
                                         location=loc, rotation=rot)
    o = bpy.context.active_object
    o.scale = scale
    return o


def cyl(loc, rad, depth, scale=(1, 1, 1), rot=(0, 0, 0), verts=18):
    bpy.ops.mesh.primitive_cylinder_add(radius=rad, depth=depth, vertices=verts,
                                        location=loc, rotation=rot)
    o = bpy.context.active_object
    o.scale = scale
    return o


def cube(loc, scale, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.scale = scale
    return o


def torus(loc, major, minor, rot=(0, 0, 0), scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor,
                                     major_segments=18, minor_segments=8,
                                     location=loc, rotation=rot)
    o = bpy.context.active_object
    o.scale = scale
    return o


def d(deg):
    return math.radians(deg)


def bake(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def export(name, objs):
    bake(objs)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()

    merged = bpy.context.active_object
    merged.name = name
    bpy.ops.object.shade_smooth()

    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, f"{name}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True,
        object_types={"MESH"}, mesh_smooth_type="FACE", add_leaf_bones=False,
        primary_bone_axis="Y", secondary_bone_axis="X",
        bake_anim=False, path_mode="COPY", embed_textures=False,
    )
    print(f"[Blender] 已导出 {path}")


# ============================================================ 各道具
GLASS = (0.42, 0.92, 1.00)
FRAME = (0.20, 0.22, 0.27)
BRASS = (0.82, 0.68, 0.28)
RED = (0.90, 0.24, 0.20)
CYAN = (0.42, 0.92, 1.00)
STEEL = (0.66, 0.70, 0.76)
DARK = (0.14, 0.14, 0.17)
CREAM = (0.96, 0.94, 0.86)


def prop_probe_cell():
    """探查单格 —— 眼镜。原点在两镜片中缝。"""
    parts = []
    m_glass = mat("Glass", GLASS, 0.15, 0.0, 0.4)
    m_frame = mat("GlassesFrame", FRAME, 0.45)

    for sx in (-1, 1):
        # Cylinder 长轴是 Y，绕 X 转 90° 后镜片面朝向使用者
        parts.append(use(cyl((sx * 0.030, 0, 0), 0.026, 0.005, rot=(d(90), 0, 0)), m_glass))
        parts.append(use(torus((sx * 0.030, 0, 0), 0.026, 0.005, rot=(d(90), 0, 0)), m_frame))
        # 镜腿
        parts.append(use(cube((sx * 0.062, 0, -0.026), (0.005, 0.005, 0.058)), m_frame))

    parts.append(use(cube((0, 0, 0), (0.012, 0.005, 0.005)), m_frame))
    return parts


def prop_magnifier():
    """放大镜：手柄在原点，镜圈在上方。"""
    parts = []
    m_glass = mat("Glass", GLASS, 0.15, 0.0, 0.4)
    m_brass = mat("Brass", BRASS, 0.35, 0.6)

    parts.append(use(cyl((0, 0, 0.030), 0.010, 0.060), m_brass))            # 手柄
    parts.append(use(torus((0, 0, 0.090), 0.042, 0.008, rot=(d(90), 0, 0)), m_brass))
    parts.append(use(cyl((0, 0, 0.090), 0.042, 0.004, rot=(d(90), 0, 0)), m_glass))
    parts.append(use(sphere((0, 0, 0.0), 0.014), m_brass))
    return parts


def prop_coolant():
    """冷却剂罐：罐体在原点，喷嘴朝 +Z。"""
    parts = []
    m_steel = mat("Steel", STEEL, 0.35, 0.7)
    m_cyan = mat("CoolantGlow", CYAN, 0.3, 0.0, 0.8)
    m_dark = mat("Dark", DARK, 0.5)

    parts.append(use(cyl((0, 0, 0.038), 0.026, 0.076), m_steel))
    parts.append(use(cyl((0, 0, 0.076), 0.020, 0.016), m_cyan))
    parts.append(use(cube((0, 0.020, 0.082), (0.010, 0.026, 0.012)), m_dark))
    parts.append(use(cyl((0, 0, 0.014), 0.029, 0.014), m_cyan))
    return parts


def prop_vial():
    """危险试剂瓶：瓶底在原点。"""
    parts = []
    m_red = mat("VialRed", RED, 0.25, 0.0, 0.5)
    m_glass = mat("Glass", GLASS, 0.15, 0.0, 0.4)
    m_dark = mat("Dark", DARK, 0.5)
    m_brass = mat("Brass", BRASS, 0.35, 0.6)

    parts.append(use(cyl((0, 0, 0.030), 0.024, 0.060), m_red))
    parts.append(use(cyl((0, 0, 0.068), 0.012, 0.020), m_glass))
    parts.append(use(cube((0, 0, 0.082), (0.020, 0.020, 0.014)), m_dark))
    parts.append(use(sphere((0, 0, 0.094), 0.012), m_brass))
    return parts


def prop_shield():
    """护盾：握把在原点，盾面向 +Z 立在身前。"""
    parts = []
    m_steel = mat("ShieldSteel", STEEL, 0.3, 0.75)
    m_dark = mat("Dark", DARK, 0.5)
    m_cyan = mat("ShieldBoss", CYAN, 0.25, 0.0, 0.7)

    parts.append(use(cube((0, 0, 0), (0.020, 0.020, 0.060)), m_dark))        # 握把
    parts.append(use(cube((0, 0.030, 0), (0.140, 0.014, 0.170)), m_steel))   # 盾面
    parts.append(use(cube((0, 0.030, 0.085), (0.140, 0.018, 0.028)), m_dark))
    parts.append(use(cube((0, 0.030, -0.085), (0.090, 0.018, 0.028)), m_dark))
    parts.append(use(cyl((0, 0.042, 0), 0.034, 0.010, rot=(d(90), 0, 0)), m_cyan))
    return parts


def prop_ice():
    """冰晶：中心在原点，六向细杆。"""
    parts = []
    m_cyan = mat("IceGlow", CYAN, 0.15, 0.0, 1.2)
    m_glass = mat("Glass", GLASS, 0.1, 0.0, 0.6)

    for angle in (0, 60, 120):
        parts.append(use(cube((0, 0, 0), (0.010, 0.010, 0.150), rot=(0, 0, d(angle))), m_cyan))
    parts.append(use(sphere((0, 0, 0), 0.026), m_glass))
    return parts


def prop_flashlight():
    """手电筒：握把在原点，灯头朝 +Z。"""
    parts = []
    m_dark = mat("Dark", DARK, 0.5)
    m_steel = mat("Steel", STEEL, 0.35, 0.7)
    m_lamp = mat("Lamp", (1.0, 0.94, 0.62), 0.1, 0.0, 2.0)

    parts.append(use(cyl((0, 0, 0.020), 0.022, 0.060, rot=(d(90), 0, 0)), m_dark))
    parts.append(use(cyl((0, 0, 0.058), 0.038, 0.028, rot=(d(90), 0, 0)), m_steel))
    parts.append(use(cyl((0, 0, 0.074), 0.032, 0.006, rot=(d(90), 0, 0)), m_lamp))
    return parts


def prop_pocket_watch():
    """怀表：表链环在原点，表盘在下方。"""
    parts = []
    m_brass = mat("Brass", BRASS, 0.3, 0.7)
    m_face = mat("WatchFace", CREAM, 0.25)
    m_dark = mat("Dark", DARK, 0.5)
    m_red = mat("WatchHand", RED, 0.4)

    parts.append(use(torus((0, 0, 0), 0.011, 0.0035, rot=(d(90), 0, 0)), m_brass))  # 挂环
    parts.append(use(cyl((0, 0, -0.036), 0.048, 0.014, rot=(d(90), 0, 0)), m_brass))
    parts.append(use(cyl((0, 0.008, -0.036), 0.040, 0.005, rot=(d(90), 0, 0)), m_face))
    parts.append(use(cube((0, 0.012, -0.030), (0.004, 0.003, 0.030)), m_dark))
    parts.append(use(cube((0, 0.012, -0.036), (0.024, 0.003, 0.004)), m_red))
    return parts


BUILDERS = {
    "ProbeCell": prop_probe_cell,
    "ScanRow": prop_magnifier,
    "ScanArea": prop_magnifier,
    "DecreaseRisk": prop_coolant,
    "IncreaseRisk": prop_vial,
    "Shield": prop_shield,
    "FreezeDrift": prop_ice,
    "RevealNeighbors": prop_flashlight,
    "SkipTurn": prop_pocket_watch,
}


def main():
    print("[Blender] 开始生成道具模型…")
    for effect in EFFECTS:
        builder = BUILDERS.get(effect)
        if builder is None:
            print(f"[Blender] 跳过 {effect}（没有对应造型）")
            continue
        clear()
        export(f"Prop_{effect}", builder())
    print("[Blender] 道具全部完成。")


main()
