"""
炸弹酒馆 —— 卡通炸弹角色生成器（Blender 5.x / 无头或经 MCP 执行）

运行方式（任选其一）：
    blender.exe --background --python Tools/Blender/build_bomb_characters.py
    python Tools/Blender/mcp_client.py exec Tools/Blender/build_bomb_characters.py

产出：
    Assets/LiarsBar/Game/Characters/Bomb_<Variant>.fbx

每份 FBX：合并网格 + 11 骨骼骨架 + 4 个动作（Idle / Aim / UseItem / Hit）

设计要点（相对第一版的改进）：
    1) 关节位置由 `joints()` 统一算出来，几何体和骨骼共用同一份数据 ——
       第一版是两边各写一套坐标，改体量时很容易错位，自动权重就会歪。
    2) 四肢加粗并在肩/肘/膝处补关节球，卡通感更强，也避免细杆在动画里看起来像铁丝。
    3) 手做成连指手套（球 + 拇指球），靴子做成圆头（方体 + 前端球）。
    4) 引信用环面绕出卷曲感，顶上补一颗火星球 —— 这是炸弹角色的标志性特征。
    5) 眼睛加大并补高光球，卡通角色「有神」主要靠这个。
"""

import math
import os
import sys

import bpy
from mathutils import Euler, Vector

# ============================================================ 变体
VARIANTS = [
    {
        "id": "black_sphere", "shape": "sphere",
        "body": (0.13, 0.13, 0.16), "band": (0.62, 0.62, 0.66),
        "glove": (0.88, 0.26, 0.25), "accent": (0.90, 0.30, 0.28),
    },
    {
        "id": "red_bundle", "shape": "bundle",
        "body": (0.72, 0.16, 0.14), "band": (0.24, 0.18, 0.12),
        "glove": (0.95, 0.76, 0.22), "accent": (0.98, 0.82, 0.30),
    },
    {
        "id": "brown_cracker", "shape": "cracker",
        "body": (0.44, 0.28, 0.16), "band": (0.80, 0.66, 0.34),
        "glove": (0.25, 0.75, 0.66), "accent": (0.30, 0.82, 0.72),
    },
    {
        "id": "gray_shell", "shape": "shell",
        "body": (0.46, 0.47, 0.50), "band": (0.70, 0.62, 0.30),
        "glove": (0.36, 0.55, 0.92), "accent": (0.42, 0.62, 0.98),
    },
]

BODY_SPEC = {
    "sphere":  {"radius": 0.46, "height": 0.92},
    "bundle":  {"radius": 0.34, "height": 0.72},
    "cracker": {"radius": 0.22, "height": 0.96},
    "shell":   {"radius": 0.30, "height": 1.02},
}

# 各造型「弹体顶端」相对 height 的系数，引信与帽子都按它定位
BODY_TOP = {"sphere": 1.00, "bundle": 0.98, "cracker": 0.96, "shell": 1.10}

# 直接产出到 Resources 下：运行时用 Resources.Load 拿，不用 AssetDatabase
# （AssetDatabase 只在编辑器可用，打进包里就取不到了）。
OUT_DIR = os.path.join("Assets", "Resources", "BombTavern", "Characters")


def joints(shape):
    """
    一处算出全部关节位置，几何体与骨骼都用它。
    返回：肩 / 肘 / 腕 / 髋 / 膝 的坐标，以及体量参数。
    """
    spec = BODY_SPEC[shape]
    r, h = spec["radius"], spec["height"]

    shoulder = r * 0.84
    shoulder_z = h * 0.56
    # 手臂别太长：第一版伸到 1.7r，看起来像长臂猿，收短到 ~1.35r 才像卡通手办
    upper_len = r * 0.36
    fore_len = r * 0.32

    elbow_x = shoulder + upper_len * 0.92
    elbow_z = shoulder_z - upper_len * 0.72
    wrist_x = elbow_x + fore_len * 0.86
    wrist_z = elbow_z - fore_len * 0.70

    # 髋关节必须贴在弹体**外表面**上，不能落在体量内部。
    #
    # 原来 hip_x = r*0.46、髋高 = h*0.22，两者叠加的结果是整条腿段都在肚子里，
    # 只有靴子尖从两侧顶出来一点 —— 渲染出来就是「有腿，但被身体吃了」。
    # 取 1.02r 是让腿贴着轮廓线外侧垂下：球体与圆柱体的轮廓半径都约等于 r，
    # 所以这个系数对四种造型都成立。
    hip_x = r * 1.02
    knee_z = h * 0.06

    return {
        "r": r, "h": h,
        "shoulder_x": shoulder, "shoulder_z": shoulder_z,
        "elbow_x": elbow_x, "elbow_z": elbow_z,
        "wrist_x": wrist_x, "wrist_z": wrist_z,
        "hip_x": hip_x, "knee_z": knee_z,
        "limb_radius": r * 0.20,
    }


# ============================================================ 基础工具
def reset_scene():
    """
    手动清空场景。

    这里**不能**用 bpy.ops.wm.read_factory_settings(use_empty=True)：
    它会连用户偏好一起重置，把正在运行中的 MCP 插件也关掉 ——
    经 MCP 执行时服务端会当场断开。手动删对象没有这个副作用。
    """
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)

    for collection in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions,
                       bpy.data.materials, bpy.data.curves):
        for item in list(collection):
            if item.users == 0:
                collection.remove(item)

    bpy.context.scene.frame_start = 1
    bpy.context.scene.render.fps = 30


def material(name, rgb, roughness=0.55, metallic=0.0, emission=0.0):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
        bsdf.inputs["Roughness"].default_value = roughness
        bsdf.inputs["Metallic"].default_value = metallic
        if "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = (
                rgb[0] * emission, rgb[1] * emission, rgb[2] * emission, 1.0)
        if "Emission Strength" in bsdf.inputs:
            bsdf.inputs["Emission Strength"].default_value = 1.0 if emission else 0.0
    return mat


def use(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    return obj


def sphere(loc, rad, scale=(1, 1, 1), rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=rad, segments=24, ring_count=16,
                                         location=loc, rotation=rot)
    o = bpy.context.active_object
    o.scale = scale
    return o


def cyl(loc, rad, depth, scale=(1, 1, 1), rot=(0, 0, 0), verts=20):
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
                                     major_segments=20, minor_segments=10,
                                     location=loc, rotation=rot)
    o = bpy.context.active_object
    o.scale = scale
    return o


def own(objs, bone):
    """把一组零件标记为「属于某根骨骼」。返回 (对象, 骨骼名) 列表。"""
    return [(o, bone) for o in objs if o]


def bind(obj, bone):
    """
    给零件打一个满权重顶点组。

    为什么不用自动权重（ARMATURE_AUTO）：
        这种「积木拼装」式网格零件之间没有共享拓扑，热力扩散权重会直接失败
        （Bone Heat Weighting: failed to find solution），结果是四肢不跟随骨骼。
        而这个角色本来就是一块块独立几何体，**刚性绑定**（整块跟一根骨骼）
        既符合卡通造型，又完全确定、不会失败。
    """
    group = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
    group.add([v.index for v in obj.data.vertices], 1.0, "REPLACE")
    return obj


def cap(start, end, radius, verts=16):
    """在两点之间架一根胶囊（圆柱 + 两端球）。卡通四肢靠它才不显得瘦。"""
    a, b = Vector(start), Vector(end)
    mid = (a + b) * 0.5
    vec = b - a
    length = vec.length

    bpy.ops.mesh.primitive_cylinder_add(radius=radius, depth=length, vertices=verts,
                                        location=mid)
    body = bpy.context.active_object
    body.rotation_mode = "QUATERNION"
    body.rotation_quaternion = vec.to_track_quat("Z", "Y")

    parts = [body]
    for point in (a, b):
        parts.append(sphere(point, radius))
    return parts


def bake_transform(obj):
    """
    把变换烘进顶点。

    location 必须一起烘 —— 每个零件都是用 ops 在各自位置创建的，
    不烘 location 的话，join 之后顶点坐标是相对「第一个零件的原点」的，
    跟原点在 (0,0,0) 的骨架对不上，按距离绑定就会全错。
    """
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def point_segment_distance(p, a, b):
    ab = b - a
    denom = ab.dot(ab)
    if denom < 1e-9:
        return (p - a).length
    t = max(0.0, min(1.0, (p - a).dot(ab) / denom))
    return (p - (a + ab * t)).length


def bind_by_nearest_bone(mesh_obj, arm_obj, max_influences=2, falloff=2.2):
    """
    按「顶点到骨骼线段的距离」绑定权重，取最近的 2 根骨骼做加权。

    为什么不用 ARMATURE_AUTO：
        自动权重靠热力扩散求解，在这种「一块块独立几何体拼起来」的网格上
        没有共享拓扑可以扩散，会直接失败（Bone Heat Weighting: failed to find
        solution），结果是四肢完全不跟随骨骼。
        这个算法是纯几何的，永远有解，而且对卡通角色的块状造型反而更合适。
    """
    mesh_obj.vertex_groups.clear()

    segments = [(b.name, b.head_local.copy(), b.tail_local.copy())
                for b in arm_obj.data.bones]
    groups = {name: mesh_obj.vertex_groups.new(name=name) for name, _, _ in segments}

    for vert in mesh_obj.data.vertices:
        scored = sorted(
            (point_segment_distance(vert.co, head, tail), name)
            for name, head, tail in segments
        )[:max_influences]

        weights = [(1.0 / (pow(dist, falloff) + 1e-4), name) for dist, name in scored]
        total = sum(w for w, _ in weights) or 1.0
        for weight, name in weights:
            groups[name].add([vert.index], weight / total, "REPLACE")


def join(objs, name):
    objs = [o for o in objs if o]
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    merged = bpy.context.active_object
    merged.name = name
    return merged


# ============================================================ 弹体
def build_body(shape, colors):
    j = joints(shape)
    r, h = j["r"], j["h"]
    body_mat = material(f"Body_{shape}", colors["body"], 0.45, 0.18)
    band_mat = material(f"Band_{shape}", colors["band"], 0.6)
    parts = []

    if shape == "sphere":
        parts.append(use(sphere((0, 0, h * 0.5), r, (1, 1, 0.97)), body_mat))
        for z in (h * 0.26, h * 0.50):
            parts.append(use(cyl((0, 0, z), r * 1.005, r * 0.11), band_mat))

    elif shape == "bundle":
        for dx in (-r * 0.64, 0.0, r * 0.64):
            parts.append(use(cyl((dx, 0, h * 0.5), r * 0.44, h * 0.94), body_mat))
        for z in (h * 0.26, h * 0.70):
            parts.append(use(cyl((0, 0, z), r * 1.16, h * 0.08, (1.0, 0.64, 1.0)), band_mat))

    elif shape == "cracker":
        for z, rr in ((h * 0.28, r * 1.0), (h * 0.74, r * 1.14)):
            parts.append(use(cyl((0, 0, z), rr, h * 0.42), body_mat))
            parts.append(use(cyl((0, 0, z - h * 0.21), rr * 1.03, h * 0.035), band_mat))

    else:  # shell
        parts.append(use(cyl((0, 0, h * 0.34), r, h * 0.64), body_mat))
        parts.append(use(sphere((0, 0, h * 0.70), r, (1, 1, 1.32)), body_mat))
        parts.append(use(cyl((0, 0, h * 0.05), r * 1.10, h * 0.07), band_mat))
        for z in (h * 0.28, h * 0.40):
            parts.append(use(cyl((0, 0, z), r * 1.05, h * 0.035), band_mat))

    # 引信：绕一圈半的卷曲 + 顶上的火星球。
    # 基准必须贴着「弹体真正的顶端」，不然引信会整段悬在空中。
    # 各造型的顶端不一样：圆雷/成捆差不多就是 h，二踢脚略矮，炮弹因为有卵形弹头更高。
    fuse_z = h * BODY_TOP[shape] + r * 0.14
    fuse_mat = material(f"Fuse_{shape}", (0.22, 0.18, 0.14), 0.7)
    spark_mat = material(f"Spark_{shape}", (1.0, 0.72, 0.18), 0.3, 0.0, emission=2.0)

    parts.append(use(torus((0, 0, fuse_z), r * 0.16, r * 0.035,
                           rot=(math.radians(78), 0, 0)), fuse_mat))
    parts.append(use(cyl((0, 0, fuse_z - r * 0.14), r * 0.045, r * 0.30), fuse_mat))
    parts.append(use(sphere((0, 0, fuse_z + r * 0.10), r * 0.11), spark_mat))

    return parts


# ============================================================ 五官
def build_face(shape, colors):
    j = joints(shape)
    r, h = j["r"], j["h"]
    face_y = -r * 0.84
    eye_z = h * 0.56

    white = material("Face_White", (0.97, 0.97, 0.98), 0.3)
    ink = material("Face_Ink", (0.06, 0.06, 0.08), 0.5)
    shine = material("Face_Shine", (1.0, 1.0, 1.0), 0.15, 0.0, emission=1.4)
    accent = material("Face_Accent", colors["accent"], 0.4, 0.0, emission=0.5)

    parts = []
    for sx in (-1, 1):
        # 眼白放大到体量的 22%，卡通脸靠大眼睛立住
        parts.append(use(sphere((sx * r * 0.36, face_y, eye_z), r * 0.22, (1, 1, 0.94)), white))
        parts.append(use(sphere((sx * r * 0.36, face_y - r * 0.15, eye_z - r * 0.02), r * 0.085), ink))
        parts.append(use(sphere((sx * r * 0.36 - sx * r * 0.05, face_y - r * 0.20,
                                 eye_z + r * 0.07), r * 0.035), shine))

        brow = cube((sx * r * 0.36, face_y - r * 0.06, eye_z + r * 0.30),
                    (r * 0.36, r * 0.05, r * 0.06),
                    rot=(0, 0, math.radians(-16 * sx)))
        parts.append(use(brow, ink))

    # 嘴：一条压扁的环面，比球体更像「咧嘴」
    parts.append(use(torus((0, face_y - r * 0.02, eye_z - r * 0.40), r * 0.14, r * 0.035,
                           rot=(math.radians(90), 0, 0), scale=(1.25, 0.75, 1.0)), ink))

    # 胸口徽记
    parts.append(use(cyl((0, -r * 0.90, h * 0.18), r * 0.19, r * 0.05,
                         rot=(math.radians(90), 0, 0)), accent))
    return parts


# ============================================================ 四肢
def build_limbs(shape, colors):
    j = joints(shape)
    r, h = j["r"], j["h"]
    lr = j["limb_radius"]

    shell = material(f"Limb_{shape}", colors["body"], 0.5, 0.12)
    glove = material(f"Glove_{shape}", colors["glove"], 0.55)
    boot = material(f"Boot_{shape}", (0.15, 0.13, 0.16), 0.6)

    parts = []
    for sx in (-1, 1):
        shoulder = (sx * j["shoulder_x"], 0, j["shoulder_z"])
        elbow = (sx * j["elbow_x"], 0, j["elbow_z"])
        wrist = (sx * j["wrist_x"], 0, j["wrist_z"])

        # 上臂 / 前臂：带两端关节球，弯肘时不会出现断裂
        parts += [use(o, shell) for o in cap(shoulder, elbow, lr)]
        parts += [use(o, shell) for o in cap(elbow, wrist, lr * 0.92)]

        # 连指手套：掌 + 拇指
        palm = sphere((sx * j["wrist_x"] + sx * r * 0.07, 0, j["wrist_z"] - r * 0.10),
                      lr * 1.18)
        parts.append(use(palm, glove))
        thumb = sphere((sx * (j["wrist_x"] + r * 0.02), -r * 0.12,
                        j["wrist_z"] - r * 0.14), lr * 0.52)
        parts.append(use(thumb, glove))

        # 腿 + 圆头靴。
        # 髋点抬到 h*0.30：配合上面的 1.02r，腿从弹体侧面垂下而不是从肚子里伸出来。
        hip = (sx * j["hip_x"], 0, h * 0.30)
        knee = (sx * j["hip_x"], 0, j["knee_z"])
        parts += [use(o, shell) for o in cap(hip, knee, lr * 0.94)]

        boot_body = sphere((sx * j["hip_x"], -r * 0.10, h * 0.035),
                           lr * 1.36, (1.0, 1.30, 0.82))
        parts.append(use(boot_body, boot))
        toe = sphere((sx * j["hip_x"], -r * 0.34, h * 0.028), lr * 0.80, (1.0, 1.05, 0.72))
        parts.append(use(toe, boot))

    return parts


def build_hat(shape, colors):
    """
    礼帽。

    第一版是把帽檐放在 h + r×1.24 处 —— 结果是浮在引信上方整整一个半径的空中，
    看起来像贴了个不相干的圆柱（渲染图里那道缝就是这么来的）。
    现在改成：帽檐贴着弹体顶面并略微嵌进去，同时整体偏到一侧、略微倾斜，
    这样正中的引信与它互不穿模，视觉上才是「戴在头上」而不是「悬在头顶」。
    """
    if shape not in ("sphere", "shell"):
        return []

    j = joints(shape)
    r, h = j["r"], j["h"]
    hat_mat = material(f"Hat_{shape}", colors["band"], 0.42, 0.35)

    brim_z = h * BODY_TOP[shape] - r * 0.05      # 略嵌进弹体，才像戴上去的
    hat_x = r * 0.30                             # 偏到一侧，给引信让位
    tilt = math.radians(13)                      # 歪戴一点更卡通

    crown_z = brim_z + r * 0.19
    crown_x = hat_x + math.sin(tilt) * r * 0.19

    return [
        use(cyl((hat_x, 0, brim_z), r * 0.60, r * 0.05, rot=(0, tilt, 0)), hat_mat),
        use(cyl((crown_x, 0, crown_z), r * 0.38, r * 0.32, rot=(0, tilt, 0)), hat_mat),
    ]


# ============================================================ 骨架（与 joints 同源）
def build_armature(shape):
    j = joints(shape)

    arm_data = bpy.data.armatures.new("BombRig")
    arm_obj = bpy.data.objects.new("BombRig", arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    arm_obj.select_set(True)

    def p(x, y, z):
        return Vector((x, y, z))

    spec = [
        ("Root", p(0, 0, 0), p(0, 0, j["h"] * 0.12), None),
        ("Spine", p(0, 0, j["h"] * 0.12), p(0, 0, j["h"] * 0.55), "Root"),
        ("Head", p(0, 0, j["h"] * 0.55), p(0, 0, j["h"] * 0.92), "Spine"),
        ("LegL", p(-j["hip_x"], 0, j["h"] * 0.22), p(-j["hip_x"], 0, 0), "Root"),
        ("LegR", p(j["hip_x"], 0, j["h"] * 0.22), p(j["hip_x"], 0, 0), "Root"),
    ]
    for side, sx in (("L", -1), ("R", 1)):
        spec += [
            (f"Arm{side}_Upper", p(sx * j["shoulder_x"], 0, j["shoulder_z"]),
             p(sx * j["elbow_x"], 0, j["elbow_z"]), "Spine"),
            (f"Arm{side}_Fore", p(sx * j["elbow_x"], 0, j["elbow_z"]),
             p(sx * j["wrist_x"], 0, j["wrist_z"]), f"Arm{side}_Upper"),
            (f"Arm{side}_Hand", p(sx * j["wrist_x"], 0, j["wrist_z"]),
             p(sx * j["wrist_x"] + sx * j["r"] * 0.10, 0, j["wrist_z"] - j["r"] * 0.18),
             f"Arm{side}_Fore"),
        ]

    bpy.ops.object.mode_set(mode="EDIT")
    made = {}
    for name, head, tail, parent in spec:
        bone = arm_data.edit_bones.new(name)
        bone.head, bone.tail = head, tail
        if parent:
            bone.parent = made[parent]
            bone.use_connect = False
        made[name] = bone
    bpy.ops.object.mode_set(mode="OBJECT")

    return arm_obj


# ============================================================ 动画
def d(deg):
    return math.radians(deg)


def key(arm, name, frame, loc=None, rot=None):
    pb = arm.pose.bones.get(name)
    if pb is None:
        return
    pb.rotation_mode = "XYZ"
    if loc is not None:
        pb.location = Vector(loc)
        pb.keyframe_insert(data_path="location", frame=frame)
    if rot is not None:
        pb.rotation_euler = Euler(rot, "XYZ")
        pb.keyframe_insert(data_path="rotation_euler", frame=frame)


def new_action(arm, name):
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = None
    key(arm, "Root", 1, loc=(0, 0, 0))       # 让 Blender 自动建 action（兼容 4.4+ slot）
    arm.animation_data.action.name = name


def anim_idle(arm):
    new_action(arm, "Idle")
    for frame, k in ((1, 0.0), (16, 1.0), (32, 0.0), (48, -1.0), (64, 0.0)):
        key(arm, "Spine", frame, loc=(0, 0.012 * k, 0))
        key(arm, "Head", frame, rot=(d(-3 * k), 0, d(4 * k)))
        key(arm, "ArmL_Upper", frame, rot=(0, 0, d(7 * k)))
        key(arm, "ArmR_Upper", frame, rot=(0, 0, d(-7 * k)))
        key(arm, "ArmL_Fore", frame, rot=(0, 0, d(4 * k)))
        key(arm, "ArmR_Fore", frame, rot=(0, 0, d(-4 * k)))


def anim_aim(arm):
    """指向面板上的某一格：右臂抬起、肘部微弯、头微低。"""
    new_action(arm, "Aim")
    for frame, k in ((1, 0.0), (10, 1.0), (44, 1.0), (60, 0.0)):
        key(arm, "ArmR_Upper", frame, rot=(0, d(-74 * k), d(-26 * k)))
        key(arm, "ArmR_Fore", frame, rot=(0, d(-30 * k), 0))
        key(arm, "ArmR_Hand", frame, rot=(0, d(-12 * k), 0))
        key(arm, "ArmL_Upper", frame, rot=(0, 0, d(10 * k)))
        key(arm, "Head", frame, rot=(d(9 * k), 0, 0))


def anim_use_item(arm):
    """
    使用道具：左臂抬到脸前「举起来看」，肘部真的弯曲；
    右臂同时前伸把它递出去。两级骨骼的价值就在这里。
    """
    new_action(arm, "UseItem")
    for frame, k in ((1, 0.0), (9, 0.85), (22, 1.0), (38, 1.0), (52, 0.25), (64, 0.0)):
        key(arm, "ArmL_Upper", frame, rot=(0, d(62 * k), d(-76 * k)))
        key(arm, "ArmL_Fore", frame, rot=(0, d(44 * k), d(-26 * k)))
        key(arm, "ArmL_Hand", frame, rot=(0, d(20 * k), 0))
        key(arm, "ArmR_Upper", frame, rot=(0, d(-36 * k), d(-18 * k)))
        key(arm, "ArmR_Fore", frame, rot=(0, d(-24 * k), 0))
        key(arm, "Head", frame, rot=(d(-11 * k), 0, d(7 * k)))
        key(arm, "Spine", frame, loc=(0, 0, 0), rot=(d(-4 * k), 0, 0))


def anim_hit(arm):
    """被炸：后仰 + 双臂上甩 + 头绕一圈。"""
    new_action(arm, "Hit")
    for frame, k, spin in ((1, 0.0, 0.0), (5, 1.0, 1.0), (14, 0.55, 0.75),
                           (26, 0.20, 0.35), (42, 0.0, 0.0)):
        key(arm, "Spine", frame, loc=(0, 0.05 * k, 0), rot=(d(-26 * k), 0, 0))
        key(arm, "Head", frame, rot=(d(-18 * k), 0, d(360 * spin)))
        key(arm, "ArmL_Upper", frame, rot=(0, d(-100 * k), d(-22 * k)))
        key(arm, "ArmR_Upper", frame, rot=(0, d(100 * k), d(22 * k)))
        key(arm, "LegL", frame, rot=(d(16 * k), 0, 0))
        key(arm, "LegR", frame, rot=(d(-16 * k), 0, 0))


# ============================================================ 主流程
def build_variant(variant):
    reset_scene()
    shape = variant["shape"]

    parts = []
    parts += build_body(shape, variant)
    parts += build_face(shape, variant)
    parts += build_limbs(shape, variant)
    parts += build_hat(shape, variant)

    for obj in parts:
        bake_transform(obj)

    mesh = join(parts, f"Bomb_{variant['id']}")
    bpy.ops.object.shade_smooth()

    arm = build_armature(shape)

    # 先按几何距离算好顶点组，再用 ARMATURE_NAME 按组绑定
    bind_by_nearest_bone(mesh, arm)

    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_NAME")

    anim_idle(arm)
    anim_aim(arm)
    anim_use_item(arm)
    anim_hit(arm)

    os.makedirs(OUT_DIR, exist_ok=True)
    out = os.path.join(OUT_DIR, f"Bomb_{variant['id']}.fbx")

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=out, use_selection=True, apply_unit_scale=True,
        object_types={"MESH", "ARMATURE"}, mesh_smooth_type="FACE",
        use_mesh_modifiers=True, add_leaf_bones=False,
        primary_bone_axis="Y", secondary_bone_axis="X", armature_nodetype="ROOT",
        bake_anim=True, bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True, path_mode="COPY", embed_textures=False,
    )
    print(f"[Blender] 已导出 {out}")


def main():
    print("[Blender] 开始生成卡通炸弹角色（v2）…")
    for variant in VARIANTS:
        try:
            build_variant(variant)
        except Exception as exc:                          # noqa: BLE001
            print(f"[Blender] 变体 {variant['id']} 失败：{exc}", file=sys.stderr)
    print("[Blender] 全部完成。")


# 无条件执行。
# 经 MCP 的 execute_code 跑时，命名空间里没有 __name__，
# 用 `if __name__ == "__main__"` 守卫会导致脚本「执行成功但什么都没做」。
main()
